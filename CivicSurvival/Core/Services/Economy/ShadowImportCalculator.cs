using Unity.Mathematics;
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Services.Economy
{
    /// <summary>
    /// Pure static service for Shadow Import calculations.
    /// No dependencies on ECS or state - fully testable.
    ///
    /// Shadow Import Mechanics:
    /// - Max capacity = consumption × ShadowImport.MaxImportPercent × corruption gate multiplier
    /// - Gate multiplier unlocks higher capacity from ShadowImport gate thresholds
    /// - Risk escalates by ShadowImport.RiskDay* config values
    /// - Discovery applies ShadowImport.SanctionDurationDays
    /// </summary>
    public static class ShadowImportCalculator
    {
        /// <summary>
        /// Calculate corruption gate multiplier.
        /// Higher corruption = more black market "connections" = higher import capacity.
        /// </summary>
        /// <param name="corruptionScore">Current corruption score (0-100)</param>
        /// <returns>Configured gate multiplier for the current corruption score.</returns>
        public static float GetGateMultiplier(float corruptionScore)
        {
            var si = BalanceConfig.Current.ShadowImport;

            // Level 1: limited access below GateLevel1Threshold
            if (corruptionScore < si.GateLevel1Threshold)
            {
                return si.GateLevel1Multiplier;
            }

            // Level 2: normal access below GateLevel2Threshold
            if (corruptionScore < si.GateLevel2Threshold)
            {
                return si.GateLevel2Multiplier;
            }

            // Level 3: full configured access
            return si.GateLevel3Multiplier;
        }

        /// <summary>
        /// Calculate maximum import capacity in MW.
        /// </summary>
        /// <param name="consumption">Current grid consumption in MW</param>
        /// <param name="corruptionScore">Current corruption score (0-100)</param>
        /// <returns>Maximum allowed import MW; 0 when the grid has no demand.</returns>
        public static int CalculateMaxImportMW(int consumption, float corruptionScore)
        {
            var si = BalanceConfig.Current.ShadowImport;

            if (consumption <= 0)
                return 0;

            // Get corruption gate multiplier
            float gateMultiplier = GetGateMultiplier(corruptionScore);

            // Calculate max: consumption × 30% × gate multiplier
            int maxMW = (int)System.Math.Round(consumption * si.MaxImportPercent * gateMultiplier);

            // Clamp to absolute bounds
            return math.clamp(maxMW, si.MinImportMw, si.AbsoluteMaxMw);
        }

        public static int CalculateImportMWForPercent(int maxImportMW, int percent)
        {
            int clampedMax = math.max(0, maxImportMW);
            int clampedPercent = math.clamp(percent, 0, 100);
            return clampedMax > 0
                ? (int)math.round(clampedMax * clampedPercent / 100f)
                : 0;
        }

        /// <summary>
        /// Calculate discovery risk based on days active.
        /// Risk escalates by ShadowImport.RiskDay1/RiskDay2/RiskDay3/RiskDay4Plus.
        /// </summary>
        /// <param name="daysActive">Number of consecutive days import has been active</param>
        /// <returns>Discovery risk (0.0 to 1.0)</returns>
        public static float GetRiskForDay(int daysActive)
        {
            var si = BalanceConfig.Current.ShadowImport;
            // FIX S26_RAG3:113: Guard daysActive <= 0 (no risk before import starts)
            return daysActive switch
            {
                <= 0 => 0f,
                1 => si.RiskDay1,
                2 => si.RiskDay2,
                3 => si.RiskDay3,
                _ => si.RiskDay4Plus
            };
        }

        /// <summary>
        /// Calculate daily cost for importing given MW at specified price.
        /// </summary>
        /// <param name="importMW">Current import level in MW</param>
        /// <param name="pricePerMW">Price per MW (varies by difficulty: $300/$600/$1200)</param>
        /// <returns>Daily cost in dollars</returns>
        public static int CalculateDailyCost(int importMW, float pricePerMW)
        {
            long cost = CalculateDailyCostLong(importMW, pricePerMW);
            if (cost >= int.MaxValue)
                return int.MaxValue;
            return checked((int)cost);
        }

        public static long CalculateDailyCostLong(int importMW, float pricePerMW)
        {
            if (importMW <= 0 || pricePerMW <= 0f)
                return 0L;

            double raw = (double)importMW * pricePerMW;
            if (double.IsNaN(raw) || raw <= 0.0)
                return 0L;
            if (raw >= long.MaxValue)
                return long.MaxValue;

            return (long)System.Math.Round(raw);
        }
    }
}
