using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Domains.Engineering.Logic
{
    /// <summary>
    /// Pure static service for calculating grid stress zone and frequency.
    /// No dependencies on ECS or state - fully testable.
    ///
    /// Frequency Model:
    /// - 50.0 Hz (Normal) = balance >= 0, stress = 0%
    /// - 50.0-49.9 Hz (Yellow early) = stress 0-25%
    /// - 49.9-49.5 Hz (Yellow) = stress 25-50%
    /// - 49.5-49.0 Hz (Red) = stress 50-100%
    /// - 48.5 Hz (Collapsed) = grid collapse triggered
    /// </summary>
    public static class GridZoneCalculator
    {
        private const float MIN_YELLOW_THRESHOLD = 0.01f;
        private const float MAX_YELLOW_THRESHOLD = 0.99f;

        /// <summary>
        /// Calculate grid zone and frequency from stress percentage.
        /// Pure function - no side effects.
        /// </summary>
        /// <param name="stressPercent">Stress percentage (0.0 to 1.0, where 1.0 = 100%)</param>
        /// <param name="isCollapsed">True if grid has collapsed</param>
        /// <returns>Tuple of (zone, frequency in Hz)</returns>
        public static (GridStressZone zone, float frequency) CalculateZoneAndFrequency(
            float stressPercent,
            bool isCollapsed)
        {
            var gsCfg = BalanceConfig.Current.GridStress;
            if (!math.isfinite(stressPercent))
                stressPercent = 0f;

            float yellowThreshold = math.clamp(gsCfg.WarningThresholdYellow, MIN_YELLOW_THRESHOLD, MAX_YELLOW_THRESHOLD);
            float redThreshold = math.clamp(gsCfg.WarningThresholdRed, yellowThreshold + 0.01f, 1f);

            // Collapsed state overrides everything
            if (isCollapsed)
            {
                return (GridStressZone.Collapsed, gsCfg.CollapseFrequency);
            }

            // Red zone (50-100% stress): 49.0 → 48.5 Hz (interpolates toward Collapse)
            if (stressPercent >= redThreshold)
            {
                // Interpolation factor: 0 at threshold, 1 at 100% stress
                float t = math.saturate((stressPercent - redThreshold) / math.max(0.01f, 1f - redThreshold));
#pragma warning disable CIVIC073 // t is math.saturate() on previous line
                float frequency = math.lerp(
                    gsCfg.RedZoneFrequency,
                    gsCfg.CollapseFrequency,
                    t);
#pragma warning restore CIVIC073
                return (GridStressZone.Red, frequency);
            }

            // Yellow zone (25-50% stress): 49.5 - 49.9 Hz
            if (stressPercent >= yellowThreshold)
            {
                // Interpolation factor: 0 at yellow threshold, 1 at red threshold
                float t = math.saturate((stressPercent - yellowThreshold) / math.max(0.01f, redThreshold - yellowThreshold));
#pragma warning disable CIVIC073 // t is math.saturate() on previous line
                float frequency = math.lerp(
                    gsCfg.YellowZoneFrequency,
                    gsCfg.RedZoneFrequency,
                    t);
#pragma warning restore CIVIC073
                return (GridStressZone.Yellow, frequency);
            }

            // Early warning (0-yellowThreshold stress): frequency dips slightly but zone stays Normal.
            // Must not return Yellow here — that would trigger GridStressWarning at any nonzero stress,
            // bypassing WarningThresholdYellow config.
            if (stressPercent > 0f)
            {
                // Interpolation factor: 0 at 0% stress, 1 at yellow threshold
                float t = math.saturate(stressPercent / math.max(0.01f, yellowThreshold));
#pragma warning disable CIVIC073 // t is math.saturate() on previous line
                float frequency = math.lerp(
                    gsCfg.NormalFrequency,
                    gsCfg.YellowZoneFrequency,
                    t);
#pragma warning restore CIVIC073
                return (GridStressZone.Normal, frequency);
            }

            // Normal zone (0% stress): 50.0 Hz
            return (GridStressZone.Normal, gsCfg.NormalFrequency);
        }

        /// <summary>
        /// Get human-readable zone description.
        /// </summary>
        public static string GetZoneDescription(GridStressZone zone)
        {
            return zone switch
            {
                GridStressZone.Normal => "Normal Operation",
                GridStressZone.Yellow => "Stress Warning",
                GridStressZone.Red => "Critical Stress",
                GridStressZone.Collapsed => "Grid Collapsed",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get color code for UI visualization.
        /// </summary>
        public static string GetZoneColor(GridStressZone zone)
        {
            return zone switch
            {
                GridStressZone.Normal => "#00FF00",   // Green
                GridStressZone.Yellow => "#FFFF00",   // Yellow
                GridStressZone.Red => "#FF0000",      // Red
                GridStressZone.Collapsed => "#000000", // Black
                _ => "#808080"                         // Gray (unknown)
            };
        }
    }
}
