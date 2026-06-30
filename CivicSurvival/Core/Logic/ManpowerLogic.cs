using System;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure static functions for manpower calculations.
    /// No side effects, no dependencies on ECS or services.
    ///
    /// NOTE: Constants are in BalanceConfig.Current.Mobilization for cross-domain access and server sync.
    /// </summary>
    public static class ManpowerLogic
    {
        // Fallbacks when balance config holds a non-positive (misconfigured) value.
        // Mirror the contract defaults for Mobilization.ManpowerCoeff / ManpowerExponent
        // (cube-root curve anchored so the City tier keeps its ~28 pool).
        private const float DEFAULT_MANPOWER_COEFF = 0.6f;
        private const float DEFAULT_MANPOWER_EXPONENT = 0.33f;

        /// <summary>
        /// Calculate base manpower pool from population via a compressing power curve:
        /// <c>basePool = ManpowerCoeff * pop^ManpowerExponent</c>.
        ///
        /// A linear "pop / N" pool cannot serve all city tiers: population spans ~×375
        /// from a village (800) to a metropolis (300k), while a viable defence needs only
        /// ~×15 the gunners (1 → ~18). Any divider that arms the village floods the
        /// metropolis, and any that bounds the metropolis floors the village to zero
        /// (integer division below N → 0, which left every town under ~14k defenceless).
        /// A sub-linear power curve (default cube-root, exponent 0.33) compresses the
        /// ×375 population span into the ×7 manpower span the gameplay actually wants,
        /// so a village fields one gun while the metropolis stays bounded. The coefficient
        /// is anchored so the City tier (~100k) keeps its previous ~28 pool.
        /// Both knobs live in balance config for tuning without a recompile.
        /// </summary>
        public static int CalculateBasePool(int population)
        {
            if (population <= 0) return 0;

            var mob = BalanceConfig.Current.Mobilization;
            float coeff = mob.ManpowerCoeff > 0f ? mob.ManpowerCoeff : DEFAULT_MANPOWER_COEFF;
            float exponent = mob.ManpowerExponent > 0f ? mob.ManpowerExponent : DEFAULT_MANPOWER_EXPONENT;

            double pool = coeff * Math.Pow(population, exponent);
            if (pool >= int.MaxValue) return int.MaxValue;
            return (int)Math.Round(pool);
        }

        /// <summary>
        /// Defence potential = how many AA guns the population COULD crew, the population-derived
        /// signal the wave-density surcharge scales by (mirror of the surplus-power surcharge). It is
        /// the same crew arithmetic that turns manpower into operational AA
        /// (<c>AirDefenseForecast.OperationalAa</c> — <c>pool / crewPerGun</c>), so the wave and the
        /// defence it scales against both key off population and stay in step (a player cannot hide a
        /// wave by not building AA; it is the population, not the built fleet, that raises it).
        /// <paramref name="crewPerGun"/> is floored at 1 (a non-positive config value would divide by
        /// zero / inflate the signal). Population ≤ 0 → 0 pool → 0 (no signal → no surcharge, the
        /// fail-safe direction: never escalate blind).
        /// </summary>
        public static float DefensePotential(int population, int crewPerGun)
        {
            int pool = CalculateBasePool(population);
            int crew = Math.Max(1, crewPerGun);
            return (float)pool / crew;
        }

        /// <summary>
        /// Calculate patriot factor from corruption.
        /// Range: 0.5 (100% corruption) to 1.0 (0% corruption)
        /// </summary>
        /// <param name="exportPercentage">0-100 from ICorruptionDataProvider</param>
        public static float CalculatePatriotismFactor(int exportPercentage)
        {
            float corruptionNorm = exportPercentage / 100f;
            float impact = BalanceConfig.Current.Mobilization.CorruptionImpact;
            return Math.Max(0f, 1.0f - (corruptionNorm * impact));
        }

        /// <summary>
        /// Calculate morale factor from happiness penalty.
        /// Range: 0.5 (max penalty) to 1.0 (no penalty)
        /// </summary>
        /// <param name="happinessPenalty">0.0-1.0 from DistrictPenaltySystem</param>
        public static float CalculateMoraleFactor(float happinessPenalty)
        {
            float impact = BalanceConfig.Current.Mobilization.HappinessImpact;
            float boundedPenalty = Math.Clamp(happinessPenalty, 0f, PenaltyConfig.MAX_HAPPINESS_PENALTY);
            return Math.Max(0f, 1.0f - (boundedPenalty * impact));
        }

        /// <summary>
        /// Calculate fatigue factor based on war duration.
        /// Two-tier step function (S14a-7):
        ///   Day 0-30:   1.0  (no fatigue)
        ///   Day 31-180: 0.85 (-15%, early fatigue)
        ///   Day 181+:   0.70 (-30%, deep fatigue / "chronic exhaustion")
        /// </summary>
        /// <param name="warDay">-1 if war not started, 0+ otherwise</param>
        public static float CalculateFatigueFactor(int warDay)
        {
            if (warDay < 0) return 1.0f;

            var balance = BalanceConfig.Current;
            var mob = balance.Mobilization;
            int deepFatigueDay = balance.Scenario.WarFatigueDay;

            if (warDay > deepFatigueDay)
                return 1.0f - mob.DeepFatiguePenalty;   // 0.70

            if (warDay > mob.WarFatigueDay)
                return 1.0f - mob.FatiguePenalty;        // 0.85

            return 1.0f;
        }

        /// <summary>
        /// Calculate total manpower capacity.
        /// </summary>
        public static int CalculateTotalManpower(
            int basePool,
            float patriotismFactor,
            float moraleFactor,
            float fatigueFactor,
            bool isConscription)
        {
            float modifier = patriotismFactor * moraleFactor * fatigueFactor;
            float total = basePool * modifier;

            if (isConscription)
            {
                total *= (1.0f + BalanceConfig.Current.Mobilization.ConscriptionBonus);
            }

            return (int)total;
        }

        /// <summary>
        /// Effective manpower cap (total minus casualties, clamped to ≥0) for the
        /// given conscription state. Mirrors the rawTotal − casualties step used by
        /// <see cref="BuildBreakdown"/> and the debug-morale override path so a
        /// "what would the cap be without conscription" prediction matches the
        /// authoritative breakdown.
        /// </summary>
        public static int EffectiveTotal(
            int basePool,
            float patriotismFactor,
            float moraleFactor,
            float fatigueFactor,
            int casualties,
            bool isConscription)
        {
            int rawTotal = CalculateTotalManpower(
                basePool, patriotismFactor, moraleFactor, fatigueFactor, isConscription);
            return Math.Max(0, rawTotal - casualties);
        }

        /// <summary>
        /// Build complete breakdown for UI.
        /// </summary>
        public static ManpowerBreakdown BuildBreakdown(
            int population,
            int exportPercentage,
            float happinessPenalty,
            int warDay,
            int usedManpower,
            int casualties,
            bool isConscription)
        {
            int basePool = CalculateBasePool(population);
            float patriotismFactor = CalculatePatriotismFactor(exportPercentage);
            float moraleFactor = CalculateMoraleFactor(happinessPenalty);
            float fatigueFactor = CalculateFatigueFactor(warDay);
            var mobCfg = BalanceConfig.Current.Mobilization;
            float conscriptionBonus = isConscription ? mobCfg.ConscriptionBonus : 0f;

            int rawTotal = CalculateTotalManpower(
                basePool, patriotismFactor, moraleFactor, fatigueFactor, isConscription);

            // Casualties reduce effective manpower cap
            int total = rawTotal - casualties;
            if (total < 0) total = 0;

            int available = total - usedManpower;
            if (available < 0) available = 0;

            bool isFatigued = warDay > mobCfg.WarFatigueDay;

            return new ManpowerBreakdown(
                population, basePool,
                patriotismFactor, moraleFactor, fatigueFactor, conscriptionBonus,
                total, usedManpower, available, casualties,
                warDay, isFatigued, isConscription
            );
        }

        /// <summary>
        /// Crew force-release quantum: how much already-committed manpower must come
        /// off the guns when the effective cap drops below what is in use. The single
        /// home for the <c>max(0, used − totalWithout)</c> clamp — runtime over-commit
        /// release (<c>MobilizationSystem.ForceReleaseExcess</c> → <c>CrewMath.Release</c>)
        /// and the "what would deactivating conscription release right now" prediction
        /// share this one form so they cannot drift.
        /// </summary>
        public static int PredictedForceRelease(int used, int totalWithout)
        {
            return Math.Max(0, used - totalWithout);
        }

        /// <summary>
        /// Check if manpower is critically low (less than 20%).
        /// Returns true if total is 0 or negative (no manpower = critical).
        /// </summary>
        public static bool IsCritical(int available, int total)
        {
            // No manpower at all = critical situation
            if (total <= 0) return true;
            // Double division (matching the runtime's original CheckCritical predicate):
            // (float)available / total drifts in the low bits versus (double), shifting the
            // critical boundary on edge populations. Both the event predicate and the UI
            // flag read this one rule, so the more-precise double keeps them consistent.
            return (double)available / total < BalanceConfig.Current.Mobilization.CriticalThreshold;
        }
    }
}
