using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Cognitive.Threats.Systems
{
    /// <summary>
    /// Generates bot messages in Social Feed when IPSO is active.
    /// 5 bot personas post propaganda with varying moods.
    /// Frequency scales with IPSO intensity: low = 1 post/120s, high = 1 post/30s.
    ///
    /// Posts via SocialPostEvent → SocialFeedService (event bus pattern).
    ///
    /// Writer: None (read-only, publishes events)
    /// Reads: IPSOState singleton
    ///
    /// S17b-4 ACCEPTED: m_TimeSinceLastPost resets to 0 on load — system waits full interval before first post; harmless.
    /// </summary>
    [ActIndependent]
    public partial class IPSOBotMessageSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("IPSOBotMessageSystem");

        /// <summary>Base interval at minimum intensity (120 seconds worth of frames).</summary>
        private const float MAX_INTERVAL_SECONDS = 120f;

        /// <summary>Minimum interval at maximum intensity (30 seconds).</summary>
        private const float MIN_INTERVAL_SECONDS = 30f;

        /// <summary>Minimum IPSO intensity to start posting (10%).</summary>
        private const float POSTING_THRESHOLD = 0.1f;

        // Use 2s throttle for checking; actual post timing uses accumulator
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND * 2;

        [System.NonSerialized] private float m_TimeSinceLastPost;
        [System.NonSerialized] private Random m_Random;
        [System.NonSerialized] private bool m_WasActive;

        // EntityQuery finds the singleton entity; ComponentLookup performs the precise RO dependency fence.
        private EntityQuery m_IPSOQuery;
        private EntityQuery m_CogQuery;
        private ComponentLookup<IPSOState> m_IPSOReadLookup;

        // ════════════════════════════════════════════════════════════════
        // BOT PERSONAS
        // ════════════════════════════════════════════════════════════════

        private static readonly BotPersona[] s_Bots = new[]
        {
            new BotPersona("@TruthPatriot_UA", SocialMood.Angry, new[]
            {
                "Why is the government silent about the casualties?",
                "They're hiding the real damage numbers!",
                "Don't believe the official reports. We know the truth.",
                "City hall is covering up the infrastructure collapse!",
            }),
            new BotPersona("@RealNews24", SocialMood.Paranoid, new[]
            {
                "SHOCKING: Mayor hiding the scale of destruction!",
                "Insider reveals funds diverted from reconstruction!",
                "Why is nobody investigating the defense failures?",
                "Sources confirm: air defense is a scam!",
            }),
            new BotPersona("@FreedomVoice_Kyiv", SocialMood.Suffering, new[]
            {
                "How much more can we endure? Surrender while we're alive.",
                "Our children deserve peace, not endless sirens.",
                "Every wave takes more lives. When does it stop?",
                "The defenders can't protect us. Accept reality.",
            }),
            new BotPersona("@LocalInsider", SocialMood.Suspicious, new[]
            {
                "Neighbor saw city hall loading gold into trucks...",
                "They're evacuating the elite while we suffer!",
                "Saw strange vehicles near the mayor's office at night.",
                "Why do officials' families leave but we can't?",
            }),
            new BotPersona("@NarodnaGazeta", SocialMood.Warning, new[]
            {
                "Official statistics are lies — casualties are 3x higher!",
                "Reconstruction fund audit reveals missing millions.",
                "Air defense effectiveness is overstated by 40%!",
                "Your taxes fund corruption, not protection.",
            }),
        };

        // Managed type (string[]) — intentionally not Burst-compatible (main-thread only, string interpolation needed)
        private struct BotPersona
        {
            public string Author;
            public SocialMood Mood;
            public string[] Messages;

            public BotPersona(string author, SocialMood mood, string[] messages)
            {
                Author = author;
                Mood = mood;
                Messages = messages;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Random = CreateRandom();

            // FIX W2-M2: EntityQuery for ShouldSkipUpdate (avoid SystemAPI per-frame sync points)
            m_IPSOQuery = GetEntityQuery(ComponentType.ReadOnly<IPSOState>());
            m_CogQuery = GetEntityQuery(ComponentType.ReadOnly<CognitiveState>());
            m_IPSOReadLookup = GetComponentLookup<IPSOState>(true);

            Log.Info("Created");
        }

        // FIX W2-H4: Reset reactivation guard when system deactivates via ShouldSkipUpdate.
        // Without this, m_WasActive stays true → guard bypassed → instant bot post on reactivation.
        protected override void OnBecameDisabled()
        {
            m_WasActive = false;
            m_TimeSinceLastPost = 0f;
        }

        protected override bool ShouldSkipUpdate()
        {
            if (!TryReadIPSOState(out var state))
                return true;

            // S17b-1 FIX: Bot messages are social media posts — require internet
            if (m_CogQuery.TryGetSingleton<CognitiveState>(out var cogState)
                && cogState.InternetMode == GlobalInternetMode.Blackout)
                return true;

            return !state.IsActive || state.GlobalExposure < POSTING_THRESHOLD;
        }

        protected override void OnThrottledUpdate()
        {
            if (!TryReadIPSOState(out var ipso))
                return;

            // Reset accumulator on reactivation to prevent instant post
            bool isActive = ipso.IsActive && ipso.GlobalExposure >= POSTING_THRESHOLD;
            if (isActive && !m_WasActive)
            {
                m_TimeSinceLastPost = 0f;
                m_WasActive = isActive;
                return; // FIX M11: Skip accumulation in reactivation frame — start fresh next tick
            }
            m_WasActive = isActive;

            float dt = ThrottledDeltaSeconds;
            m_TimeSinceLastPost += dt;

            // Calculate interval based on intensity: high intensity = shorter interval
            float t = math.saturate(ipso.GlobalExposure);
#pragma warning disable CIVIC073 // t already saturated on previous line
            float nextPostInterval = math.lerp(MAX_INTERVAL_SECONDS, MIN_INTERVAL_SECONDS, t);
#pragma warning restore CIVIC073

            if (m_TimeSinceLastPost < nextPostInterval)
                return;

            m_TimeSinceLastPost = 0f;

            // Pick random bot and message
            if (s_Bots.Length == 0) return;
            int botIndex = m_Random.NextInt(0, s_Bots.Length);
            ref var bot = ref s_Bots[botIndex];
            if (bot.Messages.Length == 0) return;
            int msgIndex = m_Random.NextInt(0, bot.Messages.Length);

            string author = bot.Author;
            string message = bot.Messages[msgIndex];
            SocialMood mood = bot.Mood;

            // Publish via EventBus → SocialFeedService picks it up
            EventBus?.SafePublish(
                new SocialPostEvent(author, message, mood),
                "IPSOBotMessageSystem");
        }

        private bool TryReadIPSOState(out IPSOState state)
        {
            state = default;
            m_IPSOReadLookup.Update(this);
            if (!m_IPSOQuery.TryGetSingletonEntity<IPSOState>(out var entity))
                return false;
            if (!m_IPSOReadLookup.HasComponent(entity))
                return false;

            state = m_IPSOReadLookup[entity];
            return true;
        }

        private static Random CreateRandom()
        {
            uint seed = ((uint)System.Environment.TickCount ^ 0x49505350u) | 1u;
            return new Random(seed);
        }
    }
}
