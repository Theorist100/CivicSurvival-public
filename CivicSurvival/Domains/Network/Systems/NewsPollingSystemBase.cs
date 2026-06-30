using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Network.Data;
using Newtonsoft.Json.Linq;

namespace CivicSurvival.Domains.Network.Systems
{
    /// <summary>
    /// Shared poll-loop scaffolding for the two server-news systems
    /// (<see cref="GlobalNewsSystem"/>, <see cref="PersonalChronicleSystem"/>).
    ///
    /// Owns the parts that were byte-for-byte mirrored between them:
    /// throttle timer + ±10s jitter, the circuit-breaker availability gate, the
    /// async epochs (<c>m_AsyncEpoch</c>/<c>m_NetworkAsyncEpoch</c>) with
    /// <see cref="IsAsyncEpochCurrent"/>/<see cref="IsNetworkAsyncEpochCurrent"/>,
    /// server-URL resolution, the JSON read helpers, mood mapping and body
    /// composition, and the destroy/load epoch-bump bookkeeping. (Each subclass
    /// owns its own private main-thread handoff lock, since it locks only its own
    /// queues — the base never locks.)
    ///
    /// Subclasses supply only their specifics via the abstract/virtual hooks:
    /// <see cref="ShouldPoll"/> (the on/off gate — GlobalNews uses the network
    /// toggle, PersonalChronicle uses network+registered),
    /// <see cref="PollIntervalSeconds"/>, <see cref="Fetch"/> (URL + GET-anon vs
    /// POST-auth + parse + queue), and <see cref="OnPumpCompletions"/> (drain the
    /// background queue into the feed on the main thread).
    ///
    /// PAUSE-SAFETY (AXIOM 14): poll throttling runs in this <see cref="OnUpdateImpl"/>,
    /// which the domain registers under GameSimulation (frozen while paused — polling
    /// must not run paused). Delivery is drained by <see cref="PumpAsyncCompletions"/>,
    /// which <see cref="NewsCompletionPumpSystem"/> calls from UIUpdate (ticks while
    /// paused). The base does not alter this split — subclasses keep their
    /// GameSimulation registration and their <c>internal</c> pump entry point.
    /// </summary>
    public abstract partial class NewsPollingSystemBase : CivicSystemBase
    {
        protected const float CIRCUIT_BREAKER_COOLDOWN_SECONDS = 300f;
        protected const double POLL_JITTER_RANGE = 20.0;
        private const double POLL_JITTER_OFFSET = 10.0;

        protected string m_ServerUrl = string.Empty;
        protected ModSettings? m_Settings;
        protected TelemetryConfig m_TelemetryConfig = null!;
        protected TelemetryAuth m_Auth = null!;

        protected volatile bool m_Enabled;
        protected volatile bool m_Destroyed;
        protected int m_AsyncEpoch;
        protected int m_NetworkAsyncEpoch;

        protected readonly CircuitBreakerState m_CircuitBreaker =
            new(failureThreshold: 3, cooldownSeconds: CIRCUIT_BREAKER_COOLDOWN_SECONDS);

        protected readonly HashSet<string> m_UnknownMoodWarnings = new(StringComparer.OrdinalIgnoreCase);

        private float m_TimeSinceLastPoll;

        // ── Specifics supplied by subclasses ────────────────────────────────────

        /// <summary>Per-subclass log context (so messages keep their original prefix).</summary>
        protected abstract LogContext Log { get; }

        /// <summary>
        /// The on/off gate. The base never polls when this is false. GlobalNews →
        /// network-connection toggle; PersonalChronicle → network &amp;&amp; registered.
        /// Toggle semantics are preserved 1-for-1.
        /// </summary>
        protected abstract bool ShouldPoll();

        /// <summary>Primary poll cadence (GlobalNews 60s, PersonalChronicle 180s).</summary>
        protected abstract float PollIntervalSeconds { get; }

        /// <summary>
        /// Issue the primary background fetch (specific URL, GET-anon vs POST-auth,
        /// parse, and queue into the main-thread handoff). Fire-and-forget.
        /// </summary>
        protected abstract void Fetch();

        /// <summary>
        /// Drain background completions into the feed on the main thread. Called from
        /// both this system's GameSimulation <see cref="OnUpdateImpl"/> and the UIUpdate
        /// pump. Must be safe to call when disabled / destroyed.
        /// </summary>
        protected abstract void OnPumpCompletions();

        /// <summary>
        /// Optional second poll stream driven on the same OnUpdate beat (GlobalNews
        /// online-stats). Default no-op. Called only while enabled and the breaker
        /// allows proceeding.
        /// </summary>
        protected virtual void OnAdditionalPoll(float deltaTime, float currentTime) { }

        // ── Shared scaffolding ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves the server URL (env override or PROD) and loads settings/auth.
        /// Bumps both epochs so any in-flight background callback from a prior config
        /// is discarded. Subclasses call this from their OnStartRunning/toggle paths.
        /// </summary>
        protected void LoadSharedConfig()
        {
            Interlocked.Increment(ref m_AsyncEpoch);
            Interlocked.Increment(ref m_NetworkAsyncEpoch);

            var settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_Settings = settings;
            m_TelemetryConfig = TelemetryConfig.Load(settings);
            m_Auth = ServiceRegistry.Instance.Require<TelemetryIdentityService>().GetAuth(m_TelemetryConfig);

            // Route the env override through the single TLS-enforcing resolver: a
            // non-https CIVIC_TELEMETRY_URL must fall back to PROD, never downgrade the
            // transport that carries player_id + auth_token.
            var urlOverride = Environment.GetEnvironmentVariable("CIVIC_TELEMETRY_URL");
            m_ServerUrl = TelemetryConfig.NormalizeServerUrl(urlOverride, TelemetryConfig.ProductionServerUrl);
        }

        protected sealed override void OnUpdateImpl()
        {
            PumpAsyncCompletions();

            // The on/off gate. Subclasses define what "should poll" means (GlobalNews →
            // network toggle; PersonalChronicle → network + registered).
            if (!ShouldPoll()) return;

            float deltaTime = UnityEngine.Time.deltaTime;
            float currentTime = UnityEngine.Time.realtimeSinceStartup;

            // Circuit breaker: skip polling during outages.
            if (!m_CircuitBreaker.CanProceed(currentTime)) return;

#pragma warning disable CIVIC056 // Timer resets every poll interval — never accumulates unbounded
            m_TimeSinceLastPoll += deltaTime;
#pragma warning restore CIVIC056
            if (m_TimeSinceLastPoll >= PollIntervalSeconds)
            {
                Fetch();
                m_TimeSinceLastPoll = NextJitter();
            }

            OnAdditionalPoll(deltaTime, currentTime);
        }

        /// <summary>
        /// Drains async completions so items reach the feed even while simulation is
        /// paused. Called from <see cref="NewsCompletionPumpSystem"/> (UIUpdate) in
        /// addition to this system's own GameSimulation OnUpdateImpl. Pause-safety
        /// invariant (AXIOM 14) lives here.
        /// </summary>
        internal void PumpAsyncCompletions()
        {
            if (m_Destroyed) return;
            OnPumpCompletions();
        }

        /// <summary>
        /// Resets the primary throttle timer to fire on the next eligible tick.
        /// Used by toggle/load paths that want an immediate refetch.
        /// </summary>
        protected void ResetPollTimer() => m_TimeSinceLastPoll = 0f;

        /// <summary>±10s jitter to avoid a thundering herd with other pollers.</summary>
        protected static float NextJitter()
            => (float)(ThreadSafeRandom.NextDouble() * POLL_JITTER_RANGE - POLL_JITTER_OFFSET);

        /// <summary>Bumps the network epoch and stamps destroy state. Call from OnDestroy.</summary>
        protected void MarkDestroyed()
        {
            m_Destroyed = true;
            Interlocked.Increment(ref m_AsyncEpoch);
            Interlocked.Increment(ref m_NetworkAsyncEpoch);
            m_UnknownMoodWarnings.Clear();
        }

        protected bool IsAsyncEpochCurrent(int asyncEpoch)
            => !m_Destroyed && Volatile.Read(ref m_AsyncEpoch) == asyncEpoch;

        protected bool IsNetworkAsyncEpochCurrent(int asyncEpoch, int networkEpoch)
            => m_Enabled
               && IsAsyncEpochCurrent(asyncEpoch)
               && Volatile.Read(ref m_NetworkAsyncEpoch) == networkEpoch;

        // ── Shared parsing / formatting helpers ─────────────────────────────────

        protected static string ComposeBody(GlobalNewsItem item)
        {
            if (!string.IsNullOrEmpty(item.BreakingFlash))
                return item.BreakingFlash;
            if (!string.IsNullOrEmpty(item.Body))
                return item.Body;
            return string.Empty;
        }

        protected SocialMood MapMoodToSocialMood(string mood)
        {
#pragma warning disable CIVIC135 // JSON mood string → enum: string input by design
            if (string.IsNullOrWhiteSpace(mood))
                return SocialMood.Neutral;

            string normalized = mood.Trim().ToUpperInvariant();
            var mapped = normalized switch
            {
                "NEUTRAL" => SocialMood.Neutral,
                "DESPAIR" => SocialMood.Suffering,
                "SUFFERING" => SocialMood.Suffering,
                "HEROIC" => SocialMood.Smug,
                "SMUG" => SocialMood.Smug,
                "CYNICAL" => SocialMood.Suspicious,
                "SUSPICIOUS" => SocialMood.Suspicious,
                "HOPEFUL" => SocialMood.Neutral,
                "TENSE" => SocialMood.Warning,
                "WARNING" => SocialMood.Warning,
                "ANGRY" => SocialMood.Angry,
                "PARANOID" => SocialMood.Paranoid,
                _ => SocialMood.Neutral
            };

            if (mapped == SocialMood.Neutral
                && normalized != "NEUTRAL"
                && normalized != "HOPEFUL"
                && m_UnknownMoodWarnings.Add(normalized))
            {
                Log.Warn($"Unknown news mood '{mood}' — rendering as Neutral");
            }

            return mapped;
#pragma warning restore CIVIC135
        }

        protected static string? ReadString(JObject obj, string key)
            => obj[key]?.Type == JTokenType.String ? obj[key]!.Value<string>() : null;

        protected static int ReadInt(JObject obj, string key)
            => obj[key]?.Type is JTokenType.Integer or JTokenType.Float ? obj[key]!.Value<int>() : 0;

        protected static DateTime ReadDateTime(JObject obj, string key)
        {
            var text = ReadString(obj, key);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
                ? value
                : DateTime.MinValue;
        }

        protected static bool TryReadDateTime(JObject obj, string key, out DateTime value)
        {
            var text = ReadString(obj, key);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
        }
    }

    /// <summary>
    /// Bounded LRU set of seen news ids (dedup). Shared by both news systems.
    /// </summary>
    internal sealed class BoundedNewsIdDeduper
    {
        private readonly int m_Capacity;
        private readonly LinkedList<string> m_SeenOrder = new();
        private readonly Dictionary<string, LinkedListNode<string>> m_SeenIndex;

        public BoundedNewsIdDeduper(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

            m_Capacity = capacity;
            m_SeenIndex = new Dictionary<string, LinkedListNode<string>>(capacity, StringComparer.Ordinal);
        }

        public int Count => m_SeenIndex.Count;

        public bool MarkSeen(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            if (m_SeenIndex.ContainsKey(id))
                return false;

            var node = m_SeenOrder.AddLast(id);
            m_SeenIndex[id] = node;

            if (m_SeenIndex.Count > m_Capacity)
                EvictOldest();

            return true;
        }

        public bool Contains(string id)
            => !string.IsNullOrEmpty(id) && m_SeenIndex.ContainsKey(id);

        public void Clear()
        {
            m_SeenOrder.Clear();
            m_SeenIndex.Clear();
        }

        private void EvictOldest()
        {
            var oldest = m_SeenOrder.First;
            if (oldest == null)
                return;

            m_SeenOrder.RemoveFirst();
            m_SeenIndex.Remove(oldest.Value);
        }
    }
}
