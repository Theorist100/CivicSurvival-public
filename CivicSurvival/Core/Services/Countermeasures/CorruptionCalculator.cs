using System;
#if !TEST
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
#endif
using CivicSurvival.Core.Config;

namespace CivicSurvival.Core.Services.Countermeasures
{
    /// <summary>
    /// Pure service for calculating corruption score.
    /// No dependencies on ECS or static systems.
    ///
    /// Corruption Score calculation (rebalanced):
    /// - Export% × 0.25 (max 25 points) - less punishing
    /// - VIP districts × 5 (max ~25 points)
    /// - VIP Bypass × 3 (max ~15 points)
    /// - Offshore > $100k: +15 points
    /// - Offshore > $300k: +15 more points
    /// - Offshore > $600k: +15 more points
    ///
    /// Inertia: corruption changes max ±8 points per game day
    /// </summary>
    public static class CorruptionCalculator
    {
        private const double EMERGENCY_FUND_DIVISOR = 100000.0;

        // Access BalanceConfig.Current directly for hot-reload support

#if !TEST
        /// <summary>
        /// Calculate corruption score from CorruptionSingleton.
        /// ECS-pure approach - reads directly from singleton.
        /// </summary>
        public static float Calculate(in CorruptionSingleton data)
        {
            return Calculate(
                data.ExportPercentage,
                data.VIPDistrictCount,
                data.VIPBypassCount,
                data.OffshoreBalance,
                data.EmergencyFundWithdrawn,
                data.FuelSiphonPercent,
                data.AccumulatedExposure,
                data.ShadyContractCount
            );
        }
#endif

        /// <summary>
        /// Calculate corruption score from raw values.
        /// Pure function - no side effects.
        /// </summary>
        public static float Calculate(
            int exportPercentage,
            int vipCount,
            int vipBypassCount,
            double offshoreAccount,
            double emergencyFundWithdrawn = 0,
            int fuelSiphonPercent = 0,
            float accumulatedExposure = 0f,
            int shadyContractCount = 0)
        {
            float score = 0f;
            var corruption = BalanceConfig.Current.Corruption;

            // Export percentage (0-100) → 0-25 points
            score += exportPercentage * corruption.ExportWeight;

            // VIP districts: 5 points each
            score += vipCount * corruption.VipWeight;

            // VIP Bypass: 3 points each
            score += vipBypassCount * corruption.VipBypassWeight;

            // Offshore account tiers
            if (offshoreAccount > corruption.OffshoreTier1) score += corruption.OffshorePointsPerTier;
            if (offshoreAccount > corruption.OffshoreTier2) score += corruption.OffshorePointsPerTier;
            if (offshoreAccount > corruption.OffshoreTier3) score += corruption.OffshorePointsPerTier;

            // === Corruption Schemes ===

            var balance = BalanceConfig.Current;
            // Shady procurement contracts have their own corruption weight; procurement weight is for contract economics.
            score += shadyContractCount * corruption.ShadyContractWeight;

            // Emergency Fund Raid: +1 per $100k withdrawn
            score += (float)(emergencyFundWithdrawn / EMERGENCY_FUND_DIVISOR) * balance.EmergencyFund.CorruptionPer100k;

            // Fuel Siphoning: +0.5 per % siphoned
            score += fuelSiphonPercent * balance.FuelSiphoning.CorruptionPerPercent;

            // Accumulated exposure from kickbacks, shadow trade, disasters
            score += accumulatedExposure * corruption.ExposureWeight;

            // Clamp to valid range [0, 100]
            return Math.Max(0f, Math.Min(100f, score));
        }

        /// <summary>
        /// Get corruption level description.
        /// Thresholds defined in BalanceConfig.Current.Corruption.Level*
        /// </summary>
        public static string GetCorruptionLevel(float score)
        {
            var corruption = BalanceConfig.Current.Corruption;
            return score switch
            {
                _ when score < corruption.LevelClean => "Clean",
                _ when score < corruption.LevelMinor => "Minor Irregularities",
                _ when score < corruption.LevelSuspicious => "Suspicious",
                _ when score < corruption.LevelCorrupt => "Corrupt",
                _ => "Criminal Enterprise"
            };
        }

        /// <summary>
        /// Check if corruption triggers suspicion (threshold from BalanceConfig).
        /// </summary>
        public static bool TriggersSuspicion(float score) => score >= BalanceConfig.Current.Countermeasures.SuspicionThreshold;

        /// <summary>
        /// Check if corruption triggers investigation (threshold from BalanceConfig).
        /// </summary>
        public static bool TriggersInvestigation(float score) => score >= BalanceConfig.Current.Countermeasures.InvestigationThreshold;

        /// <summary>
        /// Calculate investigation start chance based on corruption.
        /// At threshold: base chance, scaling up with corruption.
        /// </summary>
        public static float GetInvestigationChance(float score)
        {
            var cm = BalanceConfig.Current.Countermeasures;
            if (score < cm.InvestigationThreshold) return 0f;
            return cm.InvestigationBaseChance +
                   (score - cm.InvestigationThreshold) *
                   cm.InvestigationChancePerPoint;
        }

        /// <summary>
        /// Calculate protest chance based on corruption.
        /// Range: base to base + max bonus.
        /// </summary>
        public static float GetProtestChance(float score)
        {
            var cm = BalanceConfig.Current.Countermeasures;
            return cm.ProtestBaseChance + (score / 100f) * cm.ProtestMaxBonus;
        }

        /// <summary>
        /// Apply inertia to corruption change.
        /// Corruption moves toward target but limited by MAX_CHANGE_PER_DAY.
        /// </summary>
        /// <param name="currentScore">Current displayed corruption</param>
        /// <param name="targetScore">Calculated target corruption</param>
        /// <param name="dayFraction">Fraction of game day elapsed (0-1)</param>
        /// <returns>New corruption score after inertia</returns>
        public static float ApplyInertia(float currentScore, float targetScore, float dayFraction)
        {
            currentScore = IsFinite(currentScore) ? ClampScore(currentScore) : 0f;
            if (!IsFinite(targetScore))
                return currentScore;
            targetScore = ClampScore(targetScore);

            float safeDayFraction = IsFinite(dayFraction) ? Math.Max(0f, dayFraction) : 0f;
            float changePerDay = BalanceConfig.Current.Corruption.MaxChangePerDay;
            changePerDay = IsFinite(changePerDay) ? Math.Max(0f, changePerDay) : 0f;
            float maxChange = changePerDay * safeDayFraction;
            float diff = targetScore - currentScore;

            float result = Math.Abs(diff) <= maxChange
                ? targetScore
                : currentScore + Math.Sign(diff) * maxChange;

            return ClampScore(result);
        }

        private static float ClampScore(float value) => Math.Max(0f, Math.Min(100f, value));

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    }
}
