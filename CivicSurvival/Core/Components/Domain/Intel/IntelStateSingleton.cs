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

        /// <summary>Locale id of the predicted wave type — resolved by the UI, never shown raw.</summary>
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

        /// <summary>Cost to purchase next intel upgrade. 0 when IsMaxIntelUpgrade=true.</summary>
        public long IntelUpgradeCost;

        /// <summary>True when IntelUpgradeLevel has reached maximum — use instead of sentinel -1.</summary>
        public bool IsMaxIntelUpgrade;

        /// <summary>Highest intel precision level (mirror of <c>IntelStateSystem.MAX_INTEL_UPGRADE_LEVEL</c>; lives here so Core/cross-domain readers need no Domain import).</summary>
        public const int MaxIntelLevel = 2;

        /// <summary>
        /// The ONE effective intel precision (plan decision 7): the insider is a temporary
        /// <see cref="MaxIntelLevel"/> window, otherwise the persistent upgrade level clamped to
        /// [0, <see cref="MaxIntelLevel"/>]. Every intel-gated producer (mirror-city snapshot
        /// quantization, GridWarfare enemy readout, target naming) reads THIS — two producers each
        /// folding the insider on their own is exactly how the War Room once showed an L2 radar
        /// next to an anonymised L0 target card.
        /// </summary>
        public readonly int EffectiveIntelLevel => HasInsider ? MaxIntelLevel : System.Math.Clamp(IntelUpgradeLevel, 0, MaxIntelLevel);

        /// <summary>
        /// Default state for initialization.
        /// LOW-19: IntelUpgradeCost reads BalanceConfig — transient stale (up to 10 frames) if config
        /// loads after this struct. IntelStateSystem.UpdateSingleton() corrects it on first throttled update.
        /// </summary>
        public static IntelStateSingleton Default => new IntelStateSingleton
        {
            TensionLevel = 0,
            TensionStatus = "LOW",
            WaveTypePrediction = "INTEL_UNKNOWN",
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
            HasInsider = false,
            InsiderCost = DEFAULT_INSIDER_COST,
            PriceMultiplier = 1.0f,
            PriceModifierPercent = 0,
            IntelUpgradeLevel = 0,
            IntelUpgradeCost = BalanceConfig.Current.Intel.IntelUpgradeCostPerLevel,
            IsMaxIntelUpgrade = false
        };

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, Default);
        }
    }
}
