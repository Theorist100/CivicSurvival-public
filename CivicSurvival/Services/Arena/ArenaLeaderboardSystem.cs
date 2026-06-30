using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Game;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Base;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.Arena
{
    /// <summary>
    /// Fetches arena leaderboard from server periodically.
    /// Exposes data for UI binding.
    /// </summary>
    [ActIndependent]
    public partial class ArenaLeaderboardSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ArenaLeaderboard");
        private const float FETCH_INTERVAL_SECONDS = 60f;
        private const float CIRCUIT_BREAKER_COOLDOWN_SECONDS = 300f;
        private const int RANK_ENTROPY_LORD_MIN_HITS = 500;
        private const int RANK_GRID_TYCOON_MIN_HITS = 25;
        private const double FETCH_JITTER_RANGE = 20.0;

        private TelemetryConfig m_Config = null!;
        private TelemetryAuth m_Auth = null!;
        private ModSettings? m_Settings;
        private RealtimeSeconds m_TimeSinceLastFetch;
        private bool m_Enabled;

        // Cached data
        private string m_CachedLeaderboardJson = "[]";
        private string m_CachedWeeklyJson = "[]";
        private string m_CachedRankTiersJson = "[]";
        private int? m_YourPosition;
        private int? m_YourWeeklyPosition;
        private readonly object m_Lock = new();

        // Pre-allocated builders for the leaderboard and weekly arrays. Both
        // are written only under m_Lock; never touched outside that critical
        // section. CIVIC050: avoids per-call StringBuilder allocations in
        // BuildLeaderboardArrayJson / BuildWeeklyLeaderboardArrayJson
        // (transitively reachable from OnUpdateImpl via the background-task
        // continuation).
        private readonly StringBuilder m_LeaderboardSb = new StringBuilder(512);
        private readonly StringBuilder m_WeeklySb = new StringBuilder(512);

        private readonly CircuitBreakerState m_LeaderboardBreaker = new(failureThreshold: 3, cooldownSeconds: CIRCUIT_BREAKER_COOLDOWN_SECONDS);
        private readonly CircuitBreakerState m_WeeklyBreaker = new(failureThreshold: 3, cooldownSeconds: CIRCUIT_BREAKER_COOLDOWN_SECONDS);
        private readonly List<ArenaFetchCompletion> m_PendingRefreshCompletions = new();
        private readonly List<ArenaFetchCompletion> m_RefreshCompletionsScratch = new();
        private SchedulerToken m_ActiveRefreshToken;
        private bool m_ActiveRefreshAllTimeDone;
        private bool m_ActiveRefreshAllTimeSuccess;
        private bool m_ActiveRefreshWeeklyDone;
        private bool m_ActiveRefreshWeeklySuccess;
        private int m_RefreshEpoch;
        private bool m_AcceptRefreshCallbacks;

        public bool IsRefreshInFlight => m_ActiveRefreshToken.IsValid;

        protected override void OnCreate()
        {
            base.OnCreate();

            // React to the authoritative post-write Online signal so the leaderboard comes
            // up the moment the player enables Online — without needing them to open the
            // arena panel and press refresh. Symmetric with PersonalChronicleSystem /
            // TelemetryService; the Online value is carried in the event (already written by
            // the single writer), so there is no dispatch-order race on the raw command.
            SubscribeRequired<OnlineConnectionStateChangedEvent>(OnOnlineStateChanged);
        }

        /// <summary>
        /// Apply the authoritative Online state from the post-write event. Reloads the
        /// durable config fields (server URL, auth) but takes the online gate from the event
        /// (single source, not a settings re-read racing the writer). On enable, kicks an
        /// immediate fetch so the board populates at once.
        /// </summary>
        private void OnOnlineStateChanged(OnlineConnectionStateChangedEvent evt)
        {
            if (!ServiceRegistry.IsInitialized) return;

            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            m_Config = TelemetryConfig.Load(m_Settings).WithOnlineEnabled(evt.Enabled);
            m_Auth = ServiceRegistry.Instance.Require<TelemetryIdentityService>().GetAuth(m_Config);

            bool wasEnabled = m_Enabled;
            m_Enabled = evt.Enabled;
            if (m_Enabled == wasEnabled) return;

            if (m_Enabled)
            {
                Log.Info(" Online enabled — fetching leaderboard");
                FetchLeaderboard();
                FetchWeekly();
                m_TimeSinceLastFetch = RealtimeSeconds.Zero;
            }
            else
            {
                Log.Info(" Online disabled — leaderboard polling stopped");
            }
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            Interlocked.Increment(ref m_RefreshEpoch);
            Volatile.Write(ref m_AcceptRefreshCallbacks, true);
            lock (m_Lock)
            {
                m_PendingRefreshCompletions.Clear();
            }

#pragma warning disable CIVIC114 // Scratch list is main-thread-only; OnStartRunning runs on the owner thread.
            m_RefreshCompletionsScratch.Clear();
#pragma warning restore CIVIC114
            ResetActiveRefreshState();
            if (m_Settings != null) return;

            m_Settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_Config = TelemetryConfig.Load(m_Settings);
            m_Auth = ServiceRegistry.Instance.Require<TelemetryIdentityService>().GetAuth(m_Config);
            m_Enabled = m_Config.OnlineEnabled;

            if (!m_Enabled)
            {
                Log.Info(" Disabled (online off)");
                // Still provide mock data for UI
                BuildRankTiersJson();
                return;
            }

            Log.Info(" Initialized — fetching from server");
            BuildRankTiersJson();

            // Initial fetch
            FetchLeaderboard();
            FetchWeekly();
        }

        protected override void OnUpdateImpl()
        {
            PumpRefreshCompletions();
            if (!m_Enabled) return;

            if (RealtimeSeconds.TryCreate(UnityEngine.Time.deltaTime, out var frameDelta, clampNegative: true))
            {
                m_TimeSinceLastFetch = RealtimeSeconds.TryCreate(
                    m_TimeSinceLastFetch.Value + frameDelta.Value,
                    out var elapsed,
                    clampNegative: true)
                        ? elapsed
                        : RealtimeSeconds.Zero;
            }

            if (m_TimeSinceLastFetch.Value >= FETCH_INTERVAL_SECONDS)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                bool attempted = false;
                if (m_LeaderboardBreaker.CanProceed(now))
                {
                    FetchLeaderboard();
                    attempted = true;
                }
                if (m_WeeklyBreaker.CanProceed(now))
                {
                    FetchWeekly();
                    attempted = true;
                }
                if (attempted)
                    m_TimeSinceLastFetch = RealtimeSeconds.TryCreate(
                        (float)(ThreadSafeRandom.NextDouble() * FETCH_JITTER_RANGE - 10.0),
                        out var jitter,
                        clampNegative: true)
                            ? jitter
                            : RealtimeSeconds.Zero;
            }
        }

        /// <summary>
        /// Force immediate leaderboard refresh on next update cycle.
        /// Called from ArenaUISystem on manual refresh button.
        /// </summary>
        public bool ForceRefresh()
        {
            RefreshTelemetryConfig();
            if (!m_Enabled)
            {
                Log.Info(" Refresh ignored: online disabled");
                return false;
            }
            m_TimeSinceLastFetch = RealtimeSeconds.TryCreate(FETCH_INTERVAL_SECONDS, out var immediateRefreshDelay)
                ? immediateRefreshDelay
                : RealtimeSeconds.Zero;
            return true;
        }

        public bool ForceRefresh(int requestId)
        {
            RefreshTelemetryConfig();
            if (!m_Enabled || requestId <= 0)
            {
                Log.Info(" Refresh ignored: online disabled or invalid request");
                return false;
            }

            var requestToken = new SchedulerToken(requestId);
            if (m_ActiveRefreshToken.IsValid && !m_ActiveRefreshToken.Equals(requestToken))
                return false;

            m_ActiveRefreshToken = requestToken;
            m_ActiveRefreshAllTimeDone = false;
            m_ActiveRefreshAllTimeSuccess = false;
            m_ActiveRefreshWeeklyDone = false;
            m_ActiveRefreshWeeklySuccess = false;
            FetchLeaderboard(requestId);
            FetchWeekly(requestId);
            m_TimeSinceLastFetch = RealtimeSeconds.Zero;
            return true;
        }

        public bool CanRefresh
        {
            get
            {
                RefreshTelemetryConfig();
                return m_Enabled;
            }
        }

        private void RefreshTelemetryConfig()
        {
            var nextConfig = TelemetryConfig.Load(m_Settings);
            var nextEnabled = nextConfig.OnlineEnabled;
            var needsAuthReload =
                m_Config == null ||
                !string.Equals(m_Config.ServerUrl, nextConfig.ServerUrl, StringComparison.Ordinal) ||
                m_Config.OnlineEnabled != nextConfig.OnlineEnabled;

            m_Config = nextConfig;
            if (needsAuthReload)
            {
                m_Auth = ServiceRegistry.Instance.Require<TelemetryIdentityService>().GetAuth(m_Config);
            }

            if (m_Enabled != nextEnabled)
            {
                m_Enabled = nextEnabled;
                Log.Info(m_Enabled
                    ? " Runtime online enabled for leaderboard refresh"
                    : " Runtime online disabled for leaderboard refresh");
            }
        }

        #region HTTP Fetch

        /// <summary>
        /// Fetch all-time leaderboard on background thread (fire-and-forget by design).
        /// </summary>
        private void FetchLeaderboard(int refreshRequestId = 0)
        {
            int refreshEpoch = Volatile.Read(ref m_RefreshEpoch);
            if (!IsRefreshEpochCurrent(refreshEpoch))
                return;

            var playerId = GetPlayerId();
            var url = $"{m_Config.ServerUrl}/arena/leaderboard";
            if (!string.IsNullOrEmpty(playerId))
            {
                url += $"?player_id={Uri.EscapeDataString(playerId)}";
            }

            // FIX H79: Cache main-thread time — realtimeSinceStartup is not safe from ThreadPool
            float cachedTime = UnityEngine.Time.realtimeSinceStartup;
            if (!m_LeaderboardBreaker.TryBeginProbe(cachedTime, out var probe))
            {
                QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: true, success: false);
                return;
            }
            var terminalDelivered = new int[1];
            bool KeepRefreshEpochOrCancelProbe()
            {
                if (IsRefreshEpochCurrent(refreshEpoch))
                    return true;

                CompleteProbeOnce(terminalDelivered, () => m_LeaderboardBreaker.CancelProbe(probe));
                return false;
            }

            BackgroundTask.Run(() =>
            {
                if (!KeepRefreshEpochOrCancelProbe())
                    return;

                var result = HttpUtils.Get(url);
                if (!KeepRefreshEpochOrCancelProbe())
                    return;

                if (!result.Success)
                {
                    Log.Warn($" Fetch failed: {result.ErrorMessage}");
                    CompleteProbeOnce(terminalDelivered, () => m_LeaderboardBreaker.RecordFailure(probe));
                    QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: true, success: false);
                    return;
                }

                if (!KeepRefreshEpochOrCancelProbe()) return;
                try
                {
                    var response = ArenaWireReader.ParseLeaderboardResponse(result.Response);
                    if (!KeepRefreshEpochOrCancelProbe())
                        return;

                    lock (m_Lock)
                    {
                        if (!KeepRefreshEpochOrCancelProbe())
                            return;

                        m_CachedLeaderboardJson = BuildLeaderboardArrayJson(response.Leaderboard);
                        m_YourPosition = response.YourPosition;
                    }

                    if (Log.IsDebugEnabled) Log.Debug($" Fetched all-time leaderboard, your pos={m_YourPosition}");
                    if (!KeepRefreshEpochOrCancelProbe())
                        return;

                    CompleteProbeOnce(terminalDelivered, () => m_LeaderboardBreaker.RecordSuccess(probe));
                }
                catch (Exception ex)
                {
                    if (!KeepRefreshEpochOrCancelProbe())
                        return;

                    Log.Warn($" Parse failed: {ex}");
                    CompleteProbeOnce(terminalDelivered, () => m_LeaderboardBreaker.RecordFailure(probe));
                    QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: true, success: false);
                    return;
                }

                QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: true, success: true);
            }, () => CompleteProbeOnce(terminalDelivered, () => m_LeaderboardBreaker.CancelProbe(probe)));
        }

        /// <summary>
        /// Fetch weekly leaderboard on background thread (fire-and-forget by design).
        /// </summary>
        private void FetchWeekly(int refreshRequestId = 0)
        {
            int refreshEpoch = Volatile.Read(ref m_RefreshEpoch);
            if (!IsRefreshEpochCurrent(refreshEpoch))
                return;

            var playerId = GetPlayerId();
            var url = $"{m_Config.ServerUrl}/arena/leaderboard/weekly";
            if (!string.IsNullOrEmpty(playerId))
            {
                url += $"?player_id={Uri.EscapeDataString(playerId)}";
            }

            // FIX H79: Cache main-thread time — realtimeSinceStartup is not safe from ThreadPool
            float cachedTimeWeekly = UnityEngine.Time.realtimeSinceStartup;
            if (!m_WeeklyBreaker.TryBeginProbe(cachedTimeWeekly, out var probe))
            {
                QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: false, success: false);
                return;
            }
            var terminalDelivered = new int[1];
            bool KeepRefreshEpochOrCancelProbe()
            {
                if (IsRefreshEpochCurrent(refreshEpoch))
                    return true;

                CompleteProbeOnce(terminalDelivered, () => m_WeeklyBreaker.CancelProbe(probe));
                return false;
            }

            BackgroundTask.Run(() =>
            {
                if (!KeepRefreshEpochOrCancelProbe())
                    return;

                var result = HttpUtils.Get(url);
                if (!KeepRefreshEpochOrCancelProbe())
                    return;

                if (!result.Success)
                {
                    Log.Warn($" Weekly fetch failed: {result.ErrorMessage}");
                    CompleteProbeOnce(terminalDelivered, () => m_WeeklyBreaker.RecordFailure(probe));
                    QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: false, success: false);
                    return;
                }

                if (!KeepRefreshEpochOrCancelProbe()) return;
                try
                {
                    var response = ArenaWireReader.ParseWeeklyLeaderboardResponse(result.Response);
                    if (!KeepRefreshEpochOrCancelProbe())
                        return;

                    lock (m_Lock)
                    {
                        if (!KeepRefreshEpochOrCancelProbe())
                            return;

                        m_CachedWeeklyJson = BuildWeeklyLeaderboardArrayJson(response.Leaderboard);
                        m_YourWeeklyPosition = response.YourPosition;
                    }

                    if (Log.IsDebugEnabled) Log.Debug($" Fetched weekly leaderboard, your pos={m_YourWeeklyPosition}");
                    if (!KeepRefreshEpochOrCancelProbe())
                        return;

                    CompleteProbeOnce(terminalDelivered, () => m_WeeklyBreaker.RecordSuccess(probe));
                }
                catch (Exception ex)
                {
                    if (!KeepRefreshEpochOrCancelProbe())
                        return;

                    Log.Warn($" Weekly parse failed: {ex}");
                    CompleteProbeOnce(terminalDelivered, () => m_WeeklyBreaker.RecordFailure(probe));
                    QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: false, success: false);
                    return;
                }

                QueueRefreshCompletion(refreshRequestId, refreshEpoch, allTime: false, success: true);
            }, () => CompleteProbeOnce(terminalDelivered, () => m_WeeklyBreaker.CancelProbe(probe)));
        }

        private bool IsRefreshEpochCurrent(int refreshEpoch)
        {
            return Volatile.Read(ref m_AcceptRefreshCallbacks) &&
                Volatile.Read(ref m_RefreshEpoch) == refreshEpoch &&
                !Mod.IsUnloading;
        }

        private static void CompleteProbeOnce(int[] terminalDelivered, Action terminal)
        {
            if (Interlocked.Exchange(ref terminalDelivered[0], 1) != 0)
                return;

            terminal();
        }

        private readonly struct ArenaFetchCompletion
        {
            public readonly SchedulerToken RequestToken;
            public readonly int RefreshEpoch;
            public readonly bool AllTime;
            public readonly bool Success;

            public ArenaFetchCompletion(int requestId, int refreshEpoch, bool allTime, bool success)
            {
                RequestToken = new SchedulerToken(requestId);
                RefreshEpoch = refreshEpoch;
                AllTime = allTime;
                Success = success;
            }
        }

        private void QueueRefreshCompletion(int requestId, int refreshEpoch, bool allTime, bool success)
        {
            if (requestId <= 0 || !IsRefreshEpochCurrent(refreshEpoch))
                return;

            lock (m_Lock)
            {
                if (!IsRefreshEpochCurrent(refreshEpoch))
                    return;

                m_PendingRefreshCompletions.Add(new ArenaFetchCompletion(requestId, refreshEpoch, allTime, success));
            }
        }

        internal void PumpRefreshCompletions()
        {
            int refreshEpoch = Volatile.Read(ref m_RefreshEpoch);
#pragma warning disable CIVIC114 // Scratch list is main-thread-only; lock protects only the producer queue.
            m_RefreshCompletionsScratch.Clear();
            lock (m_Lock)
            {
                if (m_PendingRefreshCompletions.Count > 0)
                {
                    m_RefreshCompletionsScratch.AddRange(m_PendingRefreshCompletions);
                    m_PendingRefreshCompletions.Clear();
                }
            }
#pragma warning restore CIVIC114

            foreach (var completion in m_RefreshCompletionsScratch)
            {
                if (completion.RefreshEpoch != refreshEpoch)
                    continue;

                if (!completion.RequestToken.Equals(m_ActiveRefreshToken))
                    continue;

                if (completion.AllTime)
                {
                    m_ActiveRefreshAllTimeDone = true;
                    m_ActiveRefreshAllTimeSuccess = completion.Success;
                }
                else
                {
                    m_ActiveRefreshWeeklyDone = true;
                    m_ActiveRefreshWeeklySuccess = completion.Success;
                }
            }

            if (!m_ActiveRefreshToken.IsValid ||
                !m_ActiveRefreshAllTimeDone ||
                !m_ActiveRefreshWeeklyDone ||
                !IsRefreshEpochCurrent(refreshEpoch))
            {
                return;
            }

            bool success = m_ActiveRefreshAllTimeSuccess && m_ActiveRefreshWeeklySuccess;
            RequestResultBridge.PublishTerminalForBegun(
                RequestResultBridge.ArenaRefresh,
                m_ActiveRefreshToken.Value,
                success ? RequestStatus.Success : RequestStatus.Failed,
                success ? ReasonId.None : ReasonIds.ArenaPartialRefresh);

            ResetActiveRefreshState();
        }

        private void ResetActiveRefreshState()
        {
            m_ActiveRefreshToken = default;
            m_ActiveRefreshAllTimeDone = false;
            m_ActiveRefreshWeeklyDone = false;
            m_ActiveRefreshAllTimeSuccess = false;
            m_ActiveRefreshWeeklySuccess = false;
        }

        private string? GetPlayerId()
        {
            var playerId = m_Auth?.PlayerId;
            return string.IsNullOrWhiteSpace(playerId) ? null : playerId;
        }

        #endregion

        #region Rank Tiers (static)

        private static readonly RankTierDto[] s_RankTiers = new[]
        {
            new RankTierDto { Name = "Entropy Lord", MinFloorHits = RANK_ENTROPY_LORD_MIN_HITS, Icon = "rank5" },
            new RankTierDto { Name = "Chaos Broker", MinFloorHits = 100, Icon = "rank4" },
            new RankTierDto { Name = "Grid Tycoon", MinFloorHits = RANK_GRID_TYCOON_MIN_HITS, Icon = "rank3" },
            new RankTierDto { Name = "System Operator", MinFloorHits = 5, Icon = "rank2" },
            new RankTierDto { Name = "Blackout Survivor", MinFloorHits = 0, Icon = "rank1" },
        };

        private void BuildRankTiersJson()
        {
            // Static rank tiers with requirements
            var sb = new StringBuilder(256);
            sb.Append('[');
            for (int i = 0; i < s_RankTiers.Length; i++)
            {
                if (i > 0) sb.Append(',');
                s_RankTiers[i].WriteTo(sb);
            }
            sb.Append(']');
            m_CachedRankTiersJson = sb.ToString();
        }

        // Caller holds m_Lock; reuses m_LeaderboardSb (CIVIC050).
        private string BuildLeaderboardArrayJson(List<LeaderboardEntry> entries)
        {
            if (entries == null || entries.Count == 0) return JsonBuilder.EmptyArray;
            var sb = m_LeaderboardSb;
            sb.Clear();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var e = entries[i];
                var dto = new LeaderboardEntryDto
                {
                    Position = e.Position,
                    Nickname = e.Nickname ?? string.Empty,
                    FloorHits = e.FloorHits,
                    TotalDamage = e.TotalDamage,
                    BestStreak = e.BestStreak,
                    RankTier = e.RankTier ?? string.Empty,
                };
                dto.WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Caller holds m_Lock; reuses m_WeeklySb (CIVIC050).
        private string BuildWeeklyLeaderboardArrayJson(List<WeeklyLeaderboardEntry> entries)
        {
            // WeekStart is intentionally not emitted — UI does not consume it.
            if (entries == null || entries.Count == 0) return JsonBuilder.EmptyArray;
            var sb = m_WeeklySb;
            sb.Clear();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var e = entries[i];
                var dto = new WeeklyLeaderboardEntryDto
                {
                    Position = e.Position,
                    Nickname = e.Nickname ?? string.Empty,
                    FloorHits = e.FloorHits,
                    DamageDealt = e.DamageDealt,
                };
                dto.WriteTo(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }

        #endregion


        #region Public API

        /// <summary>
        /// Get all-time leaderboard as JSON array.
        /// </summary>
        public string GetLeaderboardJson()
        {
            lock (m_Lock)
            {
                return m_CachedLeaderboardJson;
            }
        }

        /// <summary>
        /// Get weekly leaderboard as JSON array.
        /// </summary>
        public string GetWeeklyJson()
        {
            lock (m_Lock)
            {
                return m_CachedWeeklyJson;
            }
        }

        /// <summary>
        /// Get rank tiers as JSON array.
        /// </summary>
        public string GetRankTiersJson()
        {
            return m_CachedRankTiersJson;
        }

        /// <summary>
        /// Your position in all-time leaderboard.
        /// </summary>
        public int? YourPosition
        {
            get { lock (m_Lock) { return m_YourPosition; } }
        }

        /// <summary>
        /// Your position in weekly leaderboard.
        /// </summary>
        public int? YourWeeklyPosition
        {
            get { lock (m_Lock) { return m_YourWeeklyPosition; } }
        }

        #endregion

        protected override void OnDestroy()
        {
            UnsubscribeSafe<OnlineConnectionStateChangedEvent>(OnOnlineStateChanged);
            Volatile.Write(ref m_AcceptRefreshCallbacks, false);
            Interlocked.Increment(ref m_RefreshEpoch);
            lock (m_Lock)
            {
                m_PendingRefreshCompletions.Clear();
            }

#pragma warning disable CIVIC114 // Scratch list is main-thread-only; OnDestroy runs on the owner thread.
            m_RefreshCompletionsScratch.Clear();
#pragma warning restore CIVIC114
            ResetActiveRefreshState();
            base.OnDestroy();
        }
    }
}
