using CivicSurvival.Core.Components.Lifecycle;
using Unity.Entities;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Singleton component tracking grid stress state.
    /// Stress accumulates when grid is in deficit (consumption > production).
    /// When stress reaches threshold, triggers Grid Collapse (full shutdown).
    /// </summary>
    public struct GridStressData : IComponentData
    {
        // Default values (fallback if config not loaded)
        private const float DEFAULT_NORMAL_FREQUENCY = 50.0f;
        private const float DEFAULT_COLLAPSE_THRESHOLD_HOURS = 2f;
        public const float MIN_STRESS_HOURS = -2f;
        public const float MAX_STRESS_HOURS = 1000f;

        /// <summary>
        /// Accumulated stress hours while grid is in deficit.
        /// Allowed range [MIN_STRESS_HOURS, MAX_STRESS_HOURS]; negative values represent post-recovery grace.
        /// UI clamps display to zero through StressPercent.
        /// </summary>
        public float StressHours;

        /// <summary>
        /// Current grid frequency (display value, Hz).
        /// 50.0 = normal, decreases with stress.
        /// </summary>
        public float CurrentFrequency;

        /// <summary>
        /// True when grid has collapsed and is in recovery.
        /// All power plants disabled during collapse.
        /// </summary>
        public bool IsCollapsed;

        /// <summary>
        /// Current stress zone for UI display.
        /// </summary>
        public GridStressZone Zone;

        /// <summary>
        /// Hours remaining until recovery (only during collapse).
        /// </summary>
        public float RecoveryHoursRemaining;

        /// <summary>
        /// Collapse threshold from config (cached at creation to avoid BalanceConfig access in IComponentData).
        /// </summary>
        public float CollapseThresholdHours;

        /// <summary>
        /// Calculate stress percentage (0-1) relative to collapse threshold.
        /// </summary>
        // FIX S16_CODE2:109: Clamp to 0 — StressHours goes negative during post-recovery grace period
        public readonly float StressPercent
        {
            get
            {
                if (CollapseThresholdHours <= 0f)
                    return 0f;

                float ratio = StressHours / CollapseThresholdHours;
                return Unity.Mathematics.math.isfinite(ratio) ? Unity.Mathematics.math.max(0f, ratio) : 0f;
            }
        }

        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, CreateDefault());
        }

        /// <summary>
        /// Create default (healthy) grid state.
        /// </summary>
        public static GridStressData CreateDefault()
        {
            var gs = BalanceConfig.Current?.GridStress;
            return new()
            {
                StressHours = 0f,
                CurrentFrequency = gs?.NormalFrequency ?? DEFAULT_NORMAL_FREQUENCY,
                IsCollapsed = false,
                Zone = GridStressZone.Normal,
                RecoveryHoursRemaining = 0f,
                CollapseThresholdHours = gs?.CollapseThresholdHours ?? DEFAULT_COLLAPSE_THRESHOLD_HOURS
            };
        }

        public void SetDefaults() => this = CreateDefault();
    }

    /// <summary>
    /// Visual zone for UI indication.
    /// </summary>
    public enum GridStressZone : byte
    {
        /// <summary>Green zone - grid healthy, frequency 50.0 Hz</summary>
        Normal = 0,

        /// <summary>Yellow zone - deficit detected, threshold active, 49.5-49.9 Hz</summary>
        Yellow = 1,

        /// <summary>Red zone - collapse imminent, 49.0-49.5 Hz</summary>
        Red = 2,

        /// <summary>Black zone - collapsed, full shutdown, < 49.0 Hz</summary>
        Collapsed = 3
    }
}


