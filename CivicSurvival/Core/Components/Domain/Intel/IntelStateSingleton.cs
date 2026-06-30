using CivicSurvival.Core.Components.Lifecycle;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Components.Domain.Intel
{
    /// <summary>
    /// Global intel state as ECS singleton.
    /// Single Source of Truth for intel predictions and upgrades.
    ///
    /// Access: SystemAPI.GetSingleton&lt;IntelStateSingleton&gt;()
    /// Modify: SystemAPI.GetSingletonRW&lt;IntelStateSingleton&gt;()
    ///
    /// Writer: IntelStateSystem
    /// Readers: AirDefenseUIPanel, GridWarfareUIPanel
    ///
    /// NOTE: Intentionally no ISerializable - predictions are recalculated
    /// from wave state. Upgrade level persisted via IntelStateSystem save.
    /// </summary>
    public struct IntelStateSingleton : IComponentData
    {
        // ===== Default Intel Values =====
        private const int DEFAULT_ENERGY_FOCUS_MIN = 30;
        private const int DEFAULT_ENERGY_FOCUS_MAX = 90;
        private const int DEFAULT_INFRA_FOCUS_MAX = 60;
        private const int DEFAULT_RESIDENTIAL_FOCUS_MAX = 40;
        private const float DEFAULT_TIME_ESTIMATE_MAX_HOURS = 12f;
        private const int DEFAULT_INSIDER_COST = 50000;
        // S3-05 FIX: moved to RemoteBalanceConfig.IntelConfig.IntelUpgradeCostPerLevel

        // ============ TENSION LEVEL ============

        /// <summary>Tension level 0-100 based on wave phase and time.</summary>
        public int TensionLevel;

        /// <summary>Text status: LOW, ELEVATED, HIGH, CRITICAL.</summary>
        public FixedString32Bytes TensionStatus;

        // ============ WAVE TYPE PREDICTION ============

        /// <summary>Predicted wave type string.</summary>
        public FixedString64Bytes WaveTypePrediction;

        /// <summary>True if next wave is likely MassiveStrike.</summary>
        public bool IsMassiveStrikePredicted;

        // ============ TARGET FOCUS (with noise) ============

        /// <summary>Energy focus range min%.</summary>
        public int EnergyFocusMin;
        /// <summary>Energy focus range max%.</summary>
        public int EnergyFocusMax;

        /// <summary>Infrastructure focus range min%.</summary>
        public int InfraFocusMin;
        /// <summary>Infrastructure focus range max%.</summary>
        public int InfraFocusMax;

        /// <summary>Residential focus range min%.</summary>
        public int ResidentialFocusMin;
        /// <summary>Residential focus range max%.</summary>
        public int ResidentialFocusMax;

        // ============ TIME ESTIMATE ============

        /// <summary>Time until attack (min hours).</summary>
        public float TimeEstimateMinHours;
        /// <summary>Time until attack (max hours).</summary>
        public float TimeEstimateMaxHours;
        /// <summary>ETA availability status: unknown, in-attack, in-recovery, available.</summary>
        public FixedString32Bytes TimeEstimateStatus;

        // ============ THREAT COUNT ============

        /// <summary>Estimated Shahed count (-1 = unknown).</summary>
        public int EstimatedShaheds;
        /// <summary>Estimated Ballistic count (-1 = unknown).</summary>
        public int EstimatedBallistics;

        /// <summary>Human-readable composition string.</summary>
        public FixedString512Bytes ThreatComposition; // HIGH-16: 128 could overflow with many threat types

        // ============ INSIDER STATE ============

        /// <summary>Whether insider info has been purchased for current wave.</summary>
        public bool HasInsider;

        /// <summary>
        /// Base cost to purchase insider info (pre-sanctions markup).
        /// Use AirDefenseDto.InsiderCost for the final cost with markup applied.
        /// </summary>
        public long InsiderCost; // HIGH-15: long for wallet API compatibility (GetIntelUpgradeCost also returns long)

        // ============ ECONOMY IMPACT ============

        /// <summary>Price multiplier for shadow import based on tension.</summary>
#pragma warning disable CIVIC167 // Multiplier (0.0-2.0), not monetary amount
        public float PriceMultiplier;
#pragma warning restore CIVIC167

        /// <summary>Price modifier percentage (0, 15, 35, 100).</summary>
        public int PriceModifierPercent;

        // ============ GRIDWARFARE INTEL UPGRADES ============

        /// <summary>Current intel upgrade level (0-2). S16b-5 FIX.</summary>
        public int IntelUpgradeLevel;

        /// <summary>Whether player can see next enemy stance (Intel Lv1+).</summary>
        public bool CanSeeNextStance;

        /// <summary>Cost to purchase next intel upgrade. 0 when IsMaxIntelUpgrade=true.</summary>
        public long IntelUpgradeCost;

        /// <summary>True when IntelUpgradeLevel has reached maximum — use instead of sentinel -1.</summary>
        public bool IsMaxIntelUpgrade;

        /// <summary>
        /// Default state for initialization.
        /// LOW-19: IntelUpgradeCost reads BalanceConfig — transient stale (up to 10 frames) if config
        /// loads after this struct. IntelStateSystem.UpdateSingleton() corrects it on first throttled update.
        /// </summary>
        public static IntelStateSingleton Default => new IntelStateSingleton
        {
            TensionLevel = 0,
            TensionStatus = "LOW",
            WaveTypePrediction = "Unknown Activity",
            IsMassiveStrikePredicted = false,
            EnergyFocusMin = DEFAULT_ENERGY_FOCUS_MIN,
            EnergyFocusMax = DEFAULT_ENERGY_FOCUS_MAX,
            InfraFocusMin = 0,
            InfraFocusMax = DEFAULT_INFRA_FOCUS_MAX,
            ResidentialFocusMin = 0,
            ResidentialFocusMax = DEFAULT_RESIDENTIAL_FOCUS_MAX,
            TimeEstimateMinHours = 4f,
            TimeEstimateMaxHours = DEFAULT_TIME_ESTIMATE_MAX_HOURS,
            TimeEstimateStatus = "unknown",
            EstimatedShaheds = -1,
            EstimatedBallistics = -1,
            ThreatComposition = "Unknown swarm size",
            HasInsider = false,
            InsiderCost = DEFAULT_INSIDER_COST,
            PriceMultiplier = 1.0f,
            PriceModifierPercent = 0,
            IntelUpgradeLevel = 0,
            CanSeeNextStance = false,
            IntelUpgradeCost = BalanceConfig.Current.Intel.IntelUpgradeCostPerLevel,
            IsMaxIntelUpgrade = false
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
