using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Network.Data;
using CivicSurvival.Domains.Network.Events;
using CivicSurvival.Localization;
using Newtonsoft.Json.Linq;

namespace CivicSurvival.Domains.Network.Systems
{
    /// <summary>
    /// Polls the server for a player's Personal Chronicle digest (Mode A — "газета")
    /// and publishes the items into the existing NewsFeedService with Scope="personal".
    ///
    /// Shares the poll-loop scaffolding (throttle/jitter, circuit-breaker gate, epochs,
    /// pending-lock, mood/JSON helpers, destroy bookkeeping) with
    /// <see cref="GlobalNewsSystem"/> via <see cref="NewsPollingSystemBase"/>. This
    /// system supplies only the personal specifics:
    /// - Transport: <see cref="HttpUtils"/>.Post + <see cref="BackgroundTask"/> (fire-and-forget).
    /// - The on/off gate is <see cref="IsPersonalChronicleAvailable"/> (network +
    ///   registered), as opposed to GlobalNews's plain network toggle. Identity (the
    ///   auth_token behind "registered") is issued when Online is on — diagnostics-independent.
    /// - Single poll stream; durable since-cursor (survives load); ON-401/403 token drop.
    /// - Polls in GameSimulation (frozen while paused); delivery is drained by
    ///   <see cref="NewsCompletionPumpSystem"/> in UIUpdate → pause-safe (AXIOM 14).
    ///
    /// Identity is the durable player_id + auth_token from <see cref="TelemetryAuth"/>
    /// (NOT the ephemeral telemetry session id).
    /// </summary>
    [ActIndependent]
    public partial class PersonalChronicleSystem : NewsPollingSystemBase
    {
        private static readonly LogContext s_Log = new("PersonalChronicle");
        protected override LogContext Log => s_Log;

        private const float POLL_INTERVAL_SECONDS = 180f;
        private const int MAX_CHRONICLE_ITEMS = 5;
        private const int MAX_SEEN_CHRONICLE_IDS = 256;

        private const int STATUS_UNAUTHORIZED = 401;
        private const int STATUS_FORBIDDEN = 403;

        protected override float PollIntervalSeconds => POLL_INTERVAL_SECONDS;

        private long m_SinceTicks;            // atomic since-cursor (UTC ticks of newest seen item)

        private readonly BoundedNewsIdDeduper m_SeenChronicleIds = new(MAX_SEEN_CHRONICLE_IDS);

        // Main-thread handoff lock: background completions queue under it; the pump
        // and post-load/destroy resets take it too. Private — locks only this
        // system's own queues (MA0064: never a publicly-reachable instance).
        private readonly object m_PendingLock = new();

        // Main-thread handoff: background completions queue here; pumped on main thread.
        private readonly List<ChronicleFetchCompletion> m_PendingCompletions = new();
        private readonly List<ChronicleFetchCompletion> m_CompletionsScratch = new();

        // GC-friendly swap-buffer for ready feed items.
        private List<GlobalNewsItem> m_PendingChronicleItems = new();
        private List<GlobalNewsItem> m_ProcessingBuffer = new();

        // Single in-memory source of the Online state for this system. Seeded at cold
        // start from the persisted setting (the single writer's durable output) and
        // updated from OnlineConnectionStateChangedEvent at a live toggle — never
        // re-derived from ModSettings during a dispatch, which would race the writer.
        private bool m_OnlineEnabled;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Subscribe BEFORE LoadConfig (OnStartRunning) so runtime re-enable works.
            // React to the authoritative post-write Online signal (carries the final value
            // after the writer persisted it) — NOT the raw toggle command, whose dispatch
            // order against the writer is unspecified.
            SubscribeRequired<OnlineConnectionStateChangedEvent>(OnOnlineStateChanged);
            SubscribeRequired<GameLoadedEvent>(OnGameLoaded);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            if (m_Settings != null) return;

            LoadSharedConfig();
            // Cold start: no toggle event yet, so the authoritative Online value is the
            // writer's persisted output (settings, seeded from ConsentStore at boot/load).
            // Reading it here is not a race — the writer is not running concurrently.
            m_OnlineEnabled = m_Settings!.NetworkConnectionEnabled;
            m_Enabled = IsPersonalChronicleAvailable(m_OnlineEnabled);

            if (!m_Enabled)
            {
                Log.Info("[PersonalChronicle] Disabled (opt-in required: network + registered)");
                return;
            }

            Log.Info($"[PersonalChronicle] Enabled — polling {m_ServerUrl}/chronicle/personal");
            FetchPersonalChronicle();
        }

        /// <summary>The on/off gate: network + registered (re-checked on toggle).</summary>
        protected override bool ShouldPoll() => m_Enabled;

#if DEBUG
        /// <summary>
        /// DEBUG: pull the personal feed immediately, bypassing the 180 s throttle.
        /// Used by the dev "Generate My Chronicle Now" button so the freshly
        /// generated digest shows up without waiting for the next scheduled poll.
        /// Main-thread only (called from the DevTools UIUpdate system). No-op when
        /// the feed is disabled (opt-out / unregistered).
        /// </summary>
        internal void DebugRefetchNow()
        {
            if (!m_Enabled) return;
            ResetPollTimer();
            m_CircuitBreaker.Reset();
            FetchPersonalChronicle();
        }
#endif

        /// <summary>Primary (and only) stream: the personal digest.</summary>
        protected override void Fetch() => FetchPersonalChronicle();

        /// <summary>
        /// Drains async completions so personal items reach the feed even while
        /// simulation is paused. Called from <see cref="NewsCompletionPumpSystem"/>
        /// (UIUpdate) in addition to this system's own GameSimulation OnUpdateImpl.
        /// </summary>
        protected override void OnPumpCompletions()
        {
            ProcessPendingCompletions();
            ProcessPendingChronicleItems();
        }

        private void ProcessPendingCompletions()
        {
#pragma warning disable CIVIC114 // Scratch list is main-thread-only; lock protects m_PendingCompletions not scratch
            m_CompletionsScratch.Clear();
#pragma warning restore CIVIC114

            lock (m_PendingLock)
            {
                if (m_PendingCompletions.Count > 0)
                {
                    m_CompletionsScratch.AddRange(m_PendingCompletions);
                    m_PendingCompletions.Clear();
                }
            }

            foreach (var completion in m_CompletionsScratch)
                ApplyChronicleCompletion(completion);
        }

        private void ProcessPendingChronicleItems()
        {
            lock (m_PendingLock)
            {
                if (m_PendingChronicleItems.Count > 0)
                {
                    // Swap buffers instead of copying (avoids allocation).
                    (m_PendingChronicleItems, m_ProcessingBuffer) = (m_ProcessingBuffer, m_PendingChronicleItems);
                }
            }

            if (m_ProcessingBuffer.Count == 0) return;

            foreach (var item in m_ProcessingBuffer)
            {
                var mood = MapMoodToSocialMood(item.Mood);
                string body = ComposeBody(item);
                var post = NewsFeedPost.FromPersonalChronicle(item, mood, body);
                EventBus?.SafePublish(new OfficialNewsReceivedEvent(post), "PersonalChronicleSystem");
            }

            m_ProcessingBuffer.Clear();
        }

        /// <summary>
        /// The Online connection plus a durable registered identity must be present.
        /// <paramref name="onlineEnabled"/> is the authoritative Online state (from the
        /// post-write event at a live toggle, or the persisted setting at cold start) —
        /// the mod may pull results; registered → there is an auth_token to bind the
        /// chronicle to (issued when Online is on, independent of diagnostics).
        /// </summary>
        private bool IsPersonalChronicleAvailable(bool onlineEnabled)
        {
            if (m_Auth == null) return false;
            return onlineEnabled && m_Auth.IsRegistered;
        }

        private void OnOnlineStateChanged(OnlineConnectionStateChangedEvent evt)
        {
            Interlocked.Increment(ref m_NetworkAsyncEpoch);

            // Re-resolve auth — registration may have changed; but the Online decision
            // comes from the event (authoritative, post-write), not from re-reading the
            // setting that the writer just patched in the same dispatch.
            LoadSharedConfig();
            m_OnlineEnabled = evt.Enabled;
            m_Enabled = IsPersonalChronicleAvailable(m_OnlineEnabled);

            if (m_Enabled)
            {
                Log.Info("[PersonalChronicle] Enabled by user");
                ClearCompletionQueues();
                ResetPollTimer();
                m_CircuitBreaker.Reset();
                FetchPersonalChronicle();
            }
            else
            {
                Log.Info("[PersonalChronicle] Disabled by user");
                ClearCompletionQueues();
            }
        }

        private void OnGameLoaded(GameLoadedEvent evt)
        {
            ResetAfterLoad();

            // Re-evaluate availability against the in-memory Online state (seeded at start
            // from the persisted setting / updated by the toggle event) and current
            // registration — registration may have completed since start.
            m_Enabled = IsPersonalChronicleAvailable(m_OnlineEnabled);

            if (!m_Enabled) return;

            ResetPollTimer();
            m_CircuitBreaker.Reset();
            FetchPersonalChronicle();
        }

        private void ResetAfterLoad()
        {
            Interlocked.Increment(ref m_NetworkAsyncEpoch);

            // Reset the since-cursor to zero so the post-load fetch re-pulls the
            // last-N personal posts from cold. This must happen because the feed
            // is NOT guaranteed durable across load: an in-game load reuses the
            // same NewsFeedService (ClearGlobalScope keeps personal posts), but a
            // main-menu→load tears the Network domain down and RegisterContent
            // builds a FRESH, empty NewsFeedService. A durable cursor pointing past
            // every already-seen post would then leave that fresh feed empty until
            // the next digest (≤30 min). Re-pulling from zero repopulates it.
            //
            // No duplicate burst: the re-pulled items are deduped at the feed by
            // NewsFeedService.m_SeenIds. After an in-game load those ids are still
            // present (ClearGlobalScope kept them), so TryAddPost rejects the
            // re-pull → feed unchanged. After a main-menu→load the fresh feed has
            // no ids, so the re-pull legitimately repopulates. This mirrors
            // GlobalNewsSystem.ResetNewsProducerAfterLoad, which likewise zeroes
            // its fetch cursor on load.
            Interlocked.Exchange(ref m_SinceTicks, 0L);

            lock (m_PendingLock)
            {
                m_PendingCompletions.Clear();
                m_PendingChronicleItems.Clear();
                // Guarded by m_PendingLock: MarkSeen runs under the same lock from the
                // completion path, so the reset must hold it to avoid a mid-call race.
                m_SeenChronicleIds.Clear();
            }

            m_ProcessingBuffer.Clear();
            m_UnknownMoodWarnings.Clear();
        }

        private void ClearCompletionQueues()
        {
            lock (m_PendingLock)
            {
                m_PendingCompletions.Clear();
                m_PendingChronicleItems.Clear();
            }

            m_ProcessingBuffer.Clear();
        }

        /// <summary>
        /// Fetch the personal chronicle on a background thread (fire-and-forget).
        /// POST body carries durable auth (player_id + auth_token), lang, since, limit.
        /// </summary>
        private void FetchPersonalChronicle()
        {
            float requestTime = UnityEngine.Time.realtimeSinceStartup;
            if (!m_CircuitBreaker.TryBeginProbe(requestTime, out var probe))
                return;

            int asyncEpoch = Volatile.Read(ref m_AsyncEpoch);
            int networkEpoch = Volatile.Read(ref m_NetworkAsyncEpoch);

            // Capture thread-safe identity snapshots before leaving the main thread.
            string playerId = m_Auth.PlayerId;
            string authToken = m_Auth.AuthToken;
            string lang = LocalizationManager.CurrentLocale;
            string url = $"{m_ServerUrl}/chronicle/personal";

            var terminalDelivered = new int[1];
            BackgroundTask.Run(() =>
            {
                if (!IsNetworkAsyncEpochCurrent(asyncEpoch, networkEpoch))
                {
                    EnqueueCompletionOnce(terminalDelivered,
                        ChronicleFetchCompletion.Cancel(asyncEpoch, networkEpoch, probe));
                    return;
                }

                try
                {
                    long ticks = Interlocked.Read(ref m_SinceTicks);
                    var body = JsonBuilder.Object()
                        .Add("player_id", playerId)
                        .Add("lang", lang);
                    if (ticks != 0)
                    {
                        var since = new DateTime(ticks, DateTimeKind.Utc);
                        // Sub-second precision (7 fractional digits): the cursor stores
                        // the newest item's full-tick created_at, and the server filters
                        // strictly `created_at > $2`. Truncating to whole seconds would
                        // make the strict `>` re-return the cursor item itself (its
                        // microsecond fraction exceeds the truncated bound), forcing the
                        // client deduper to absorb a duplicate every poll. Full precision
                        // makes the cursor exact.
                        body.Add("since", since.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
                    }
                    body.Add("limit", MAX_CHRONICLE_ITEMS);

                    var result = HttpUtils.Post(url, body.Build(), authToken);
                    if (!result.Success)
                    {
                        // Expired/invalid token → drop it so the player re-registers.
                        if (result.StatusCode is STATUS_UNAUTHORIZED or STATUS_FORBIDDEN)
                            m_Auth.InvalidateToken();

                        EnqueueCompletionOnce(terminalDelivered,
                            ChronicleFetchCompletion.Failure(asyncEpoch, networkEpoch, probe,
                                $"[PersonalChronicle] Fetch failed: {result.ErrorMessage}"));
                        return;
                    }

                    var items = ParseChronicleResponse(result.Response);
                    long latestTicks = ticks;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Timestamp.Ticks > latestTicks)
                            latestTicks = items[i].Timestamp.Ticks;
                    }

                    EnqueueCompletionOnce(terminalDelivered,
                        ChronicleFetchCompletion.Success(asyncEpoch, networkEpoch, probe, items, latestTicks));
                }
                catch (Exception ex)
                {
                    // Log here (background thread); pass no message to the completion so
                    // the main-thread apply only records the breaker failure (no double-log).
                    Log.Error($"[PersonalChronicle] Fetch/parse failed: {ex}");
                    EnqueueCompletionOnce(terminalDelivered,
                        ChronicleFetchCompletion.Failure(asyncEpoch, networkEpoch, probe, string.Empty));
                }
            }, () => EnqueueCompletionOnce(terminalDelivered,
                ChronicleFetchCompletion.Cancel(asyncEpoch, networkEpoch, probe)));
        }

        private void EnqueueCompletionOnce(int[] terminalDelivered, ChronicleFetchCompletion completion)
        {
            if (Interlocked.Exchange(ref terminalDelivered[0], 1) != 0)
                return;

            if (!IsNetworkAsyncEpochCurrent(completion.AsyncEpoch, completion.NetworkEpoch))
            {
                m_CircuitBreaker.CancelProbe(completion.Probe);
                return;
            }

            lock (m_PendingLock)
            {
                m_PendingCompletions.Add(completion);
            }
        }

        private void ApplyChronicleCompletion(ChronicleFetchCompletion completion)
        {
            if (!IsNetworkAsyncEpochCurrent(completion.AsyncEpoch, completion.NetworkEpoch))
                return;

            switch (completion.Status)
            {
                case FetchStatus.Success:
                    m_CircuitBreaker.RecordSuccess(completion.Probe);
                    if (completion.Items.Count > 0)
                    {
                        Interlocked.Exchange(ref m_SinceTicks, completion.LatestTicks);
                        QueueChronicleItems(completion.Items);

                        if (Log.IsDebugEnabled)
                            Log.Debug($"[PersonalChronicle] Received {completion.Items.Count} item(s)");
                    }
                    break;

                case FetchStatus.Failure:
                    if (!string.IsNullOrEmpty(completion.LogMessage))
                    {
                        if (completion.LogAsError) Log.Error(completion.LogMessage);
                        else Log.Warn(completion.LogMessage);
                    }
                    m_CircuitBreaker.RecordFailure(completion.Probe);
                    break;

                case FetchStatus.Cancel:
                    m_CircuitBreaker.CancelProbe(completion.Probe);
                    break;

                default:
                    Log.Warn($"[PersonalChronicle] Unknown completion status: {completion.Status}");
                    m_CircuitBreaker.CancelProbe(completion.Probe);
                    break;
            }
        }

        private void QueueChronicleItems(IReadOnlyList<GlobalNewsItem> items)
        {
            lock (m_PendingLock)
            {
                foreach (var item in items)
                {
                    if (m_SeenChronicleIds.MarkSeen(item.Id))
                        m_PendingChronicleItems.Add(item);
                }
            }
        }

        /// <summary>
        /// Parse the { "chronicle": [...] } response. Reuses the same item shape the
        /// global chronicle path parses; an empty array is normal (opt-out/cold-start).
        /// No try/catch here — parse exceptions propagate so the circuit breaker sees them.
        /// </summary>
        private List<GlobalNewsItem> ParseChronicleResponse(string json)
        {
#pragma warning disable CIVIC050 // returned/stored, not per-frame
            var items = new List<GlobalNewsItem>();
#pragma warning restore CIVIC050

            var root = JObject.Parse(json);
            if (root["chronicle"] is not JArray chronicle)
                return items;

            foreach (var token in chronicle)
            {
                if (token is not JObject itemObj) continue;
                var id = ReadString(itemObj, "id");
                if (string.IsNullOrEmpty(id)) continue;
                // Skip items with an unparseable created_at instead of stamping
                // DateTime.MinValue: the timestamp anchors the since-cursor and
                // dedup, so a malformed date must not enter the feed.
                if (!TryReadDateTime(itemObj, "created_at", out var createdAt))
                    continue;
                items.Add(new GlobalNewsItem(
                    id: id,
                    headline: ReadString(itemObj, "headline"),
                    category: "personal",
                    nickname: null,
                    timestamp: createdAt,
                    isChronicle: true,
                    body: ReadString(itemObj, "body"),
                    breakingFlash: ReadString(itemObj, "breaking_flash"),
                    mood: ReadString(itemObj, "mood")));
            }

            return items;
        }

        protected override void OnDestroy()
        {
            MarkDestroyed();
            Interlocked.Exchange(ref m_SinceTicks, 0L);
            ClearCompletionQueues();
            m_SeenChronicleIds.Clear();
            UnsubscribeSafe<OnlineConnectionStateChangedEvent>(OnOnlineStateChanged);
            UnsubscribeSafe<GameLoadedEvent>(OnGameLoaded);
            base.OnDestroy();
        }

        private enum FetchStatus
        {
            None = 0,
            Success = 1,
            Failure = 2,
            Cancel = 3
        }

        private readonly struct ChronicleFetchCompletion
        {
            public readonly int AsyncEpoch;
            public readonly int NetworkEpoch;
            public readonly CircuitBreakerState.BreakerProbe Probe;
            public readonly FetchStatus Status;
            public readonly IReadOnlyList<GlobalNewsItem> Items;
            public readonly long LatestTicks;
            public readonly string LogMessage;
            public readonly bool LogAsError;

            private ChronicleFetchCompletion(
                int asyncEpoch,
                int networkEpoch,
                CircuitBreakerState.BreakerProbe probe,
                FetchStatus status,
                IReadOnlyList<GlobalNewsItem> items,
                long latestTicks,
                string logMessage,
                bool logAsError)
            {
                AsyncEpoch = asyncEpoch;
                NetworkEpoch = networkEpoch;
                Probe = probe;
                Status = status;
                Items = items;
                LatestTicks = latestTicks;
                LogMessage = logMessage ?? string.Empty;
                LogAsError = logAsError;
            }

            public static ChronicleFetchCompletion Success(
                int asyncEpoch,
                int networkEpoch,
                CircuitBreakerState.BreakerProbe probe,
                IReadOnlyList<GlobalNewsItem> items,
                long latestTicks)
                => new(asyncEpoch, networkEpoch, probe, FetchStatus.Success, items, latestTicks, string.Empty, false);

            public static ChronicleFetchCompletion Failure(
                int asyncEpoch,
                int networkEpoch,
                CircuitBreakerState.BreakerProbe probe,
                string logMessage,
                bool logAsError = false)
                => new(asyncEpoch, networkEpoch, probe, FetchStatus.Failure, Array.Empty<GlobalNewsItem>(), 0L, logMessage, logAsError);

            public static ChronicleFetchCompletion Cancel(
                int asyncEpoch,
                int networkEpoch,
                CircuitBreakerState.BreakerProbe probe)
                => new(asyncEpoch, networkEpoch, probe, FetchStatus.Cancel, Array.Empty<GlobalNewsItem>(), 0L, string.Empty, false);
        }
    }
}
