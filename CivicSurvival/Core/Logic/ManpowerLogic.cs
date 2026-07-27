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
        // Fallbacks when balance config holds a non-positive (misconfigured) value. These MIRROR
        // the contract defaults and must be updated with them: the coefficient sat at 0.6 here long
        // after the contract moved to 1.0, then 1.4, then 1.3, so a city that fell back to it fielded
        // less than half the crew the balance promised — a divergence nothing in the build could see.
        private const float DEFAULT_MANPOWER_COEFF = 1.3f;
        private const float DEFAULT_MANPOWER_EXPONENT = 0.33f;
        private const float DEFAULT_MANPOWER_LINEAR_DIVISOR = 10000f;

        // PERF-LOCK: the wave-density surcharge (anti-trivialisation — a well-defended populous
        // city must not obsolete small waves) scales FULLY with population, but through the WAVE's
        // own curve, never the mobilization one. Every live drone or missile is linear render cost,
        // so the crew pool must not be able to inflate the threat count by being buffed. This
        // replaced a half-coupling that leaked half of every ManpowerCoeff buff into wave density;
        // the knob defaults reproduce that coupling's output exactly, so the split moved no number.
        // Do NOT re-derive these from Mobilization knobs. Fallbacks mirror the contract defaults.
        private const float DEFAULT_DENSITY_COEFF = 1.15f;
        private const float DEFAULT_DENSITY_EXPONENT = 0.33f;

        /// <summary>
        /// Calculate base manpower pool from population:
        /// <c>basePool = ManpowerCoeff * pop^ManpowerExponent + pop / ManpowerLinearDivisor</c>.
        ///
        /// A plain "pop / N" pool cannot serve all city tiers: population spans ~×375 from a village
        /// (800) to a metropolis (300k), while a viable defence needs only a few times the gunners.
        /// Any divider that arms the village floods the metropolis, and any that bounds the
        /// metropolis floors the village to zero (integer division below N → 0, which left every
        /// town under ~14k defenceless). Hence the sub-linear curve: it compresses that span so a
        /// village still fields a gun.
        ///
        /// But compression alone made size stop mattering: with the cube root, tripling a city added
        /// barely a fifth to its pool, and a player who doubled his population saw almost nothing for
        /// it. The linear term restores that reward without taking anything back from small cities —
        /// at 1k it contributes 0.1 of a crewman and vanishes into the rounding, at 300k it adds 30.
        /// The curve holds the floor, the line pays for growth.
        ///
        /// The divisor reads as "one extra crewman per N residents" and is the knob to turn when the
        /// question is how much a bigger city should be worth. All three live in balance config.
        /// </summary>
        public static int CalculateBasePool(int population)
        {
            if (population <= 0) return 0;

            var mob = BalanceConfig.Current.Mobilization;
            float coeff = mob.ManpowerCoeff > 0f ? mob.ManpowerCoeff : DEFAULT_MANPOWER_COEFF;
            float exponent = mob.ManpowerExponent > 0f ? mob.ManpowerExponent : DEFAULT_MANPOWER_EXPONENT;
            float configured = mob.ManpowerLinearDivisor > 0f
                ? mob.ManpowerLinearDivisor
                : DEFAULT_MANPOWER_LINEAR_DIVISOR;
            // Floored at 1, not merely checked for zero: the contract clamps this knob, but a
            // hand-edited local config with 0.5 would turn "residents per extra crewman" into a
            // multiplier and hand a metropolis six hundred thousand gunners.
            double divisor = Math.Max(1.0, configured);

            double pool = coeff * Math.Pow(population, exponent) + population / divisor;
            if (pool >= int.MaxValue) return int.MaxValue;
            return (int)Math.Round(pool);
        }

        /// <summary>
        /// Defence potential the wave-density surcharge scales by: how many AA guns the population
        /// COULD crew, keyed off RAW population. A high-population city draws denser waves so its
        /// defence does not trivialise the threat (a player cannot hide a wave by not building AA —
        /// it is the population, not the built fleet, that raises it).
        ///
        /// PERF-LOCK: population scaling is FULL, but the curve is the wave's own
        /// (<c>Waves.DensityCoeff</c> / <c>Waves.DensityExponent</c>) and is never derived from the
        /// mobilization knobs. Every live drone or missile is linear render cost, so a buff to the
        /// player's crew pool must not raise the threat count as a side effect — which is exactly
        /// what the previous half-coupling did with half of every ManpowerCoeff change. The defaults
        /// (1.15 / 0.33) reproduce that coupling's output at ManpowerCoeff 1.3 to the digit, so the
        /// split changed no wave. NOT the raw CalculateBasePool — the surcharge answers "how many
        /// guns COULD this population crew", not "how many can it crew after casualties".
        /// <paramref name="crewPerGun"/> floored at 1 (a non-positive value would divide by zero /
        /// inflate the signal). Population ≤ 0 → 0 (no signal → no surcharge, fail-safe).
        /// </summary>
        public static float DefensePotentialForDensity(int population, int crewPerGun)
        {
            if (population <= 0) return 0f;

            var waves = BalanceConfig.Current.Waves;
            float coeff = waves.DensityCoeff > 0f ? waves.DensityCoeff : DEFAULT_DENSITY_COEFF;
            float exponent = waves.DensityExponent > 0f ? waves.DensityExponent : DEFAULT_DENSITY_EXPONENT;

            int crew = Math.Max(1, crewPerGun);
            double pool = coeff * Math.Pow(population, exponent);
            return (float)(pool / crew);
        }

        /// <summary>
        /// Willingness to serve, from how corrupt the administration looks TO THE CITY.
        /// Range: 1 − CorruptionImpact (fully corrupt) to 1.0 (clean).
        ///
        /// Reads the aggregate from <c>CorruptionCalculator.CalculateVisibleToCity</c> — NOT the
        /// shadow-export slider it read historically. The knob was always called CorruptionImpact
        /// and the local was always corruptionNorm: the intent was "a corrupt administration
        /// depresses the draft", but only one of the seven indicators was ever wired in. A city
        /// could siphon fuel, rig contracts and spare its VIP districts at "100% patriotism",
        /// while a city whose only sin was the export slider took the entire cut — which is why
        /// the readout read as broken to players.
        /// </summary>
        /// <param name="visibleCorruptionScore">0-100 from CorruptionCalculator.CalculateVisibleToCity</param>
        public static float CalculatePatriotismFactor(float visibleCorruptionScore)
        {
            float corruptionNorm = Math.Clamp(visibleCorruptionScore, 0f, 100f) / 100f;
            float impact = BalanceConfig.Current.Mobilization.CorruptionImpact;
            return Math.Max(0f, 1.0f - (corruptionNorm * impact));
        }

        /// <summary>
        /// Floor under the effective pool: the free heritage grant must always be crewable.
        /// The modifiers MULTIPLY (corruption × morale × fatigue × evasion bottoms out near 0.17)
        /// and casualties, sold deferrals and disabilities subtract on top of that, so a city can
        /// reach literally zero crew and lose even the guns the game handed it for nothing —
        /// leaving nothing to shoot back with and no way out of the hole. Floored at
        /// HeritageBaseCount × HeritageCrewRequired. A zero <paramref name="basePool"/> means there
        /// is no city to draft from at all, and that still crews nothing.
        /// </summary>
        public static int ApplyDefenceFloor(int total, int basePool)
        {
            total = Math.Max(0, total);
            if (basePool <= 0) return total;

            var aa = BalanceConfig.Current.AAUnits;
            int floor = Math.Max(0, aa.HeritageBaseCount) * Math.Max(0, aa.HeritageCrewRequired);
            return Math.Max(total, floor);
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
        /// Draft-evasion factor from the accumulated evasion level (0-1): the share of the pool
        /// that dodges the draft under enemy Propaganda pressure ("ухилянти"). Level 0 → 1.0
        /// (no evasion), level 1 → 1 − DodgerMaxPenalty. The level itself is owned and
        /// integrated by MobilizationSystem from the Core draft-fear lever.
        /// </summary>
        public static float CalculateDodgerFactor(float dodgerLevel)
        {
            float level = Math.Clamp(dodgerLevel, 0f, 1f);
            float maxPenalty = Math.Clamp(BalanceConfig.Current.Mobilization.DodgerMaxPenalty, 0f, 1f);
            return 1.0f - level * maxPenalty;
        }

        /// <summary>
        /// Calculate total manpower capacity.
        /// </summary>
        public static int CalculateTotalManpower(
            int basePool,
            float patriotismFactor,
            float moraleFactor,
            float fatigueFactor,
            float dodgerFactor,
            bool isConscription)
        {
            float modifier = Math.Max(0f, patriotismFactor * moraleFactor * fatigueFactor * dodgerFactor);
            float total = basePool * modifier;

            if (isConscription)
            {
                total *= (1.0f + BalanceConfig.Current.Mobilization.ConscriptionBonus);
            }

            return (int)total;
        }

        /// <summary>
        /// Heads removed from the drafted pool by the sold-deferral rate (the
        /// Draft-Exemption corruption scheme, reversible layer). The rate bites a
        /// proportional slice of the CURRENT post-modifier pool, so dropping the
        /// rate returns the men in full.
        /// </summary>
        public static int CalculateDeferralHeads(int rawTotal, int deferralRatePercent)
        {
            if (rawTotal <= 0 || deferralRatePercent <= 0) return 0;
            float fraction = Math.Clamp(
                deferralRatePercent * BalanceConfig.Current.DraftExemption.RateManpowerFractionPerPercent,
                0f, 1f);
            return (int)Math.Round(rawTotal * fraction);
        }

        /// <summary>
        /// Effective manpower cap (total minus casualties and sold exemptions,
        /// clamped to ≥0) for the given conscription state. Mirrors the
        /// rawTotal − casualties − deferral − disability step used by
        /// <see cref="BuildBreakdown"/> and the debug-morale override path so a
        /// "what would the cap be without conscription" prediction matches the
        /// authoritative breakdown.
        /// </summary>
        public static int EffectiveTotal(
            int basePool,
            float patriotismFactor,
            float moraleFactor,
            float fatigueFactor,
            float dodgerFactor,
            int casualties,
            int deferralRatePercent,
            int disabilityHeads,
            bool isConscription)
        {
            int rawTotal = CalculateTotalManpower(
                basePool, patriotismFactor, moraleFactor, fatigueFactor, dodgerFactor, isConscription);
            int deferralHeads = CalculateDeferralHeads(rawTotal, deferralRatePercent);
            // Same defence floor as BuildBreakdown — the two must not drift, or the "what would
            // deactivating conscription release" prediction disagrees with the authoritative cap.
            return ApplyDefenceFloor(
                rawTotal - casualties - deferralHeads - Math.Max(0, disabilityHeads), basePool);
        }

        /// <summary>
        /// Build complete breakdown for UI.
        /// </summary>
        public static ManpowerBreakdown BuildBreakdown(
            int population,
            float visibleCorruptionScore,
            int exportLoadPercent,
            float happinessPenalty,
            float dodgerLevel,
            int warDay,
            int usedManpower,
            int casualties,
            int deferralRatePercent,
            int disabilityHeads,
            bool isConscription)
        {
            int basePool = CalculateBasePool(population);
            float patriotismFactor = CalculatePatriotismFactor(visibleCorruptionScore);
            float moraleFactor = CalculateMoraleFactor(happinessPenalty);
            float fatigueFactor = CalculateFatigueFactor(warDay);
            float dodgerFactor = CalculateDodgerFactor(dodgerLevel);
            var mobCfg = BalanceConfig.Current.Mobilization;
            float conscriptionBonus = isConscription ? mobCfg.ConscriptionBonus : 0f;

            int rawTotal = CalculateTotalManpower(
                basePool, patriotismFactor, moraleFactor, fatigueFactor, dodgerFactor, isConscription);

            // Draft-dodger READOUT counts MEN, not pool heads: the pool is a compressed crew
            // abstraction (0.6×pop^0.33 ≈ dozens on a metropolis), so a pool-head difference
            // reads "1 dodger per 300k city". The displayed number is the same evasion bite
            // (1 − dodgerFactor) applied to the draft-age share of the population; the
            // MECHANICAL bite stays dodgerFactor on the pool above.
            float populationShare = Math.Clamp(mobCfg.DodgerPopulationShare, 0f, 1f);
            int dodgerCount = (int)Math.Round(population * populationShare * (1.0f - dodgerFactor));
            if (dodgerCount < 0) dodgerCount = 0;

            // Draft-Exemption scheme: reversible deferral slice + permanent disability heads.
            int deferralHeads = CalculateDeferralHeads(rawTotal, deferralRatePercent);
            int clampedDisability = Math.Max(0, disabilityHeads);

            // Casualties and sold exemptions reduce effective manpower cap, then the defence floor
            // guarantees the free heritage guns stay crewable (see ApplyDefenceFloor).
            int beforeFloor = rawTotal - casualties - deferralHeads - clampedDisability;
            int total = ApplyDefenceFloor(beforeFloor, basePool);
            bool floorEngaged = total > Math.Max(0, beforeFloor);

            int available = total - usedManpower;
            if (available < 0) available = 0;

            bool isFatigued = warDay > mobCfg.WarFatigueDay;

            return new ManpowerBreakdown(
                population, basePool,
                patriotismFactor, moraleFactor, fatigueFactor, dodgerFactor, conscriptionBonus,
                total, usedManpower, available, casualties, dodgerCount,
                deferralHeads, clampedDisability,
                warDay, isFatigued, isConscription,
                visibleCorruptionScore, exportLoadPercent, floorEngaged
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
        /// Is the manpower pool critically low? Entry is <c>CriticalThreshold</c> (0.2); once
        /// critical, clearing requires <c>CriticalThreshold + CriticalRecoveryBand</c> (0.25).
        /// Pass the previous verdict as <paramref name="wasCritical"/>: outside the band the live
        /// pool decides regardless of it, inside the band the previous verdict holds. Without the
        /// band a pool parked on the threshold flips the alarm and the UI badge on every rebuild.
        ///
        /// <c>total &lt;= 0</c> is critical unconditionally — a pool that can crew nothing is the
        /// worst case, not an undefined one. Callers building an event payload must guard the
        /// percentage division themselves (total 0 → report 0) and must NOT skip the case:
        /// skipping it left the collapse-to-zero pool as the one state that never raised the alarm.
        /// </summary>
        public static bool IsCritical(int available, int total, bool wasCritical)
        {
            // No manpower at all = critical situation
            if (total <= 0) return true;

            var mobCfg = BalanceConfig.Current.Mobilization;
            // Clamp the exit to 1.0, same guard as CognitiveStateSystem's compromise band: a
            // configured band that pushes the exit above 1.0 could never be cleared by a ratio
            // that is at most 1.0, locking the alarm on for the rest of the save.
            double threshold = wasCritical
                ? Math.Min(1.0, mobCfg.CriticalThreshold + Math.Max(0f, mobCfg.CriticalRecoveryBand))
                : mobCfg.CriticalThreshold;

            // Double division: (float)available / total drifts in the low bits, shifting the
            // critical boundary on edge populations. The event predicate and the UI flag both
            // resolve through this one rule, so the more-precise double keeps them consistent.
            return (double)available / total < threshold;
        }
    }
}
