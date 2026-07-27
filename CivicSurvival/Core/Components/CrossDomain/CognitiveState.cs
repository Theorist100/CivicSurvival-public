using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Buffer element tracking cognitive integrity per district.
    /// Attached to CognitiveState singleton entity.
    ///
    /// Integrity decreases when internet is ON (propaganda exposure).
    /// Integrity increases when internet is OFF (recovery from manipulation).
    ///
    /// When integrity drops below threshold (50%), district is "compromised"
    /// and receives happiness/commerce penalties via DistrictPenaltySystem.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct CognitiveIntegrityBuffer : IBufferElementData
    {
        /// <summary>District entity index.</summary>
#pragma warning disable CIVIC262 // Session-local raw district entity.Index (runtime key convention); persisted via DistrictKeyCodec (engine entity remap), never as raw int
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>Cognitive integrity 0.0 - 1.0 (1.0 = fully intact).</summary>
        public float Integrity;

        /// <summary>Whether this district is currently marked as compromised (below threshold).</summary>
        public bool IsCompromised;
    }

    /// <summary>
    /// Hero unit status.
    /// "The Climate Prophet" - she who dares to speak uncomfortable truths.
    /// </summary>
    public enum HeroStatus : byte
    {
        Inactive = 0,   // Not hired
        Deployed = 1,   // Active, countering propaganda (Lightning Rod effect)
        Lecturing = 2   // At university, boosting recovery (How Dare You!)
    }

    /// <summary>
    /// Protest risk level (derived from avg integrity).
    /// </summary>
    public enum ProtestRisk : byte
    {
        Low = 0,      // Integrity > 70%
        Medium = 1,   // 50-70%
        High = 2,     // 30-50%
        Critical = 3  // < 30%
    }

    /// <summary>
    /// Cognitive warfare persistent state (ECS singleton).
    /// Models information warfare where enemy propaganda degrades cognitive integrity.
    ///
    /// Access: SystemAPI.GetSingleton&lt;CognitiveState&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;CognitiveState&gt;()
    ///
    /// Writer: CognitiveStateSystem (sole ICivicSingletonOwner&lt;CognitiveState&gt;)
    /// Readers: UI panels
    ///
    /// Buffers attached: CognitiveIntegrityBuffer
    /// </summary>
    public struct CognitiveState : IComponentData
    {
        /// <summary>System active (activated when war starts).</summary>
        public bool IsActive;

        /// <summary>Base rate at which integrity decreases per game hour when internet is ON (0.02 = 2%/hour).</summary>
        public float InfectionRate;

        /// <summary>Base rate at which integrity recovers per game hour when internet is OFF (0.01 = 1%/hour).</summary>
        public float RecoveryRate;

        /// <summary>Threshold below which district is considered compromised (0.5 = 50%).</summary>
        public float CompromiseThreshold;

        /// <summary>Critical threshold for bonus recovery effect (0.3 = 30%).</summary>
        public float CriticalThreshold;

        /// <summary>Multiplier for recovery when below critical threshold.</summary>
        public float CriticalRecoveryMultiplier;

        // Hero fields moved to HeroDeploymentState (separate singleton, separate writer).
        // EffectiveInfectionRate / EffectiveRecoveryRate live in CognitiveRates because
        // the formula now spans two singletons.

        // ============ Global Internet Mode ============

        /// <summary>Current global internet mode for the city.</summary>
        public GlobalInternetMode InternetMode;

        /// <summary>Infection rate multiplier in Firewall mode (0.3 = 30% of normal).</summary>
        public float FirewallInfectionMultiplier;

        /// <summary>Recovery rate multiplier in Firewall mode (0.5 = 50% of normal recovery).</summary>
        public float FirewallRecoveryMultiplier;

        /// <summary>Commerce penalty in Firewall mode (0.10 = -10%).</summary>
        public float FirewallCommercePenalty;

        /// <summary>Commerce penalty in Blackout mode (0.25 = -25%).</summary>
        public float BlackoutCommercePenalty;

        // ===== Fallback Defaults =====
        private const float FALLBACK_INFECTION_RATE = 0.02f;
        private const float FALLBACK_CRITICAL_THRESHOLD = 0.3f;
        private const float FALLBACK_CRITICAL_RECOVERY_MULT = 1.5f;
        private const float FALLBACK_FIREWALL_INFECTION_MULT = 0.3f;
        private const float FALLBACK_BLACKOUT_COMMERCE_PENALTY = 0.25f;

        public static CognitiveState Default
        {
            get
            {
                var cfg = BalanceConfig.Current?.Cognitive;
                return new()
                {
                    IsActive = false,
                    InfectionRate = cfg?.InfectionRateBase ?? FALLBACK_INFECTION_RATE,
                    RecoveryRate = cfg?.RecoveryRateBase ?? 0.01f,
                    CompromiseThreshold = cfg?.CompromiseThreshold ?? 0.5f,
                    CriticalThreshold = cfg?.CriticalThreshold ?? FALLBACK_CRITICAL_THRESHOLD,
                    CriticalRecoveryMultiplier = cfg?.CriticalRecoveryMultiplier ?? FALLBACK_CRITICAL_RECOVERY_MULT,
                    InternetMode = GlobalInternetMode.Open,
                    FirewallInfectionMultiplier = cfg?.FirewallInfectionMultiplier ?? FALLBACK_FIREWALL_INFECTION_MULT,
                    FirewallRecoveryMultiplier = cfg?.FirewallRecoveryMultiplier ?? 0.5f,
                    FirewallCommercePenalty = cfg?.FirewallCommercePenalty ?? 0.10f,
                    BlackoutCommercePenalty = cfg?.BlackoutCommercePenalty ?? FALLBACK_BLACKOUT_COMMERCE_PENALTY
                };
            }
        }

        /// <summary>
        /// Get current commerce penalty based on internet mode.
        /// </summary>
        public readonly float CurrentCommercePenalty => InternetMode switch
        {
            GlobalInternetMode.Open => 0f,
            GlobalInternetMode.Firewall => FirewallCommercePenalty,
            GlobalInternetMode.Blackout => BlackoutCommercePenalty,
            _ => 0f
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default, new EnsureSingletonPolicy<CognitiveState>
            {
                EnsureShape = EnsureShape
            });
        }

        private static void EnsureShape(EntityManager em, Entity entity)
        {
            if (!em.HasBuffer<CognitiveIntegrityBuffer>(entity))
                em.AddBuffer<CognitiveIntegrityBuffer>(entity);
        }
    }
}
