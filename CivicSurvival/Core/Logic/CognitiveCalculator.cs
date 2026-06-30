using Unity.Burst;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure calculation: cognitive infection from propaganda exposure.
    /// Extracted from MentalHealthJobs.UpdateCognitiveStateJob.
    ///
    /// Implements: attack/defense/skepticism + CDI-7 blackout vulnerability.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public static class CognitiveCalculator
    {
        private const float NET_IMPACT_DEADBAND = 0.0001f;

        /// <summary>
        /// Calculate new infection level from exposure and state.
        /// </summary>
        /// <param name="currentInfection">Current infection level (0-1)</param>
        /// <param name="enemyInternet">Enemy internet exposure (0-1)</param>
        /// <param name="enemyIPSO">Enemy IPSO exposure (0-1)</param>
        /// <param name="stateMedia">State media defense (0-1)</param>
        /// <param name="counterOps">Counter-ops defense (0-1)</param>
        /// <param name="resistance">Household resistance (0-0.8)</param>
        /// <param name="blackoutHours">Current blackout hours (for vulnerability)</param>
        /// <param name="deltaTime">Time delta in hours (via GameRate.HoursDelta)</param>
        /// <param name="enemyInternetWeight">Config: weight for internet propaganda</param>
        /// <param name="enemyIpsoWeight">Config: weight for IPSO propaganda</param>
        /// <param name="counterOpsMultiplier">Config: multiplier for counter-ops</param>
        /// <param name="skepticismFactor">Config: how much educated doubt state media</param>
        /// <param name="infectionRate">Config: base infection rate per hour (multiplied by deltaTime in hours)</param>
        /// <param name="recoveryRate">Config: base recovery rate per hour (multiplied by deltaTime in hours)</param>
        /// <param name="blackoutVulnThreshold">Config: hours before vulnerability starts</param>
        /// <param name="blackoutVulnMaxHours">Config: hours for max vulnerability</param>
        /// <param name="blackoutVulnMaxBonus">Config: max vulnerability bonus ratio (0..1)</param>
        /// <returns>New infection level (0-1)</returns>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        public static float Calculate(
            float currentInfection,
            float enemyInternet,
            float enemyIPSO,
            float stateMedia,
            float counterOps,
            float resistance,
            float blackoutHours,
            float deltaTime,
            float enemyInternetWeight,
            float enemyIpsoWeight,
            float counterOpsMultiplier,
            float skepticismFactor,
            float infectionRate,
            float recoveryRate,
            float blackoutVulnThreshold,
            float blackoutVulnMaxHours,
            float blackoutVulnMaxBonus)
        {
            currentInfection = FiniteSaturate(currentInfection);
            resistance = FiniteSaturate(resistance);
            skepticismFactor = FiniteSaturate(skepticismFactor);

            // Early out if nothing happening
            bool hasExposure = enemyInternet > 0f || enemyIPSO > 0f ||
                               stateMedia > 0f || counterOps > 0f;
            if (!hasExposure && currentInfection <= 0f)
                return currentInfection;

            // --- ATTACK POWER ---
            // Education strongly cuts crude spam (IPSO), but less effective against quality content
            float attackPower = (enemyInternet * enemyInternetWeight) +
                               (enemyIPSO * enemyIpsoWeight);
            float effectiveAttack = attackPower * (1.0f - resistance);

            // --- CDI-7: BLACKOUT VULNERABILITY ---
            // Households under extended blackout are more susceptible to propaganda.
            // Stressed, scared people are easier to manipulate.
            float excessHours = math.max(0f, blackoutHours - blackoutVulnThreshold);
            float safeMaxHours = math.max(blackoutVulnMaxHours, 0.001f);
            float maxVulnBonus = math.clamp(blackoutVulnMaxBonus, 0f, 1f);
            float vulnProgress = math.saturate(excessHours / safeMaxHours);
            float vulnBonus = vulnProgress * maxVulnBonus;
            effectiveAttack *= (1.0f + vulnBonus);

            // --- DEFENSE POWER ---
            // State media + counter-ops (Greta > TV)
            float defensePower = stateMedia + (counterOps * counterOpsMultiplier);

            // NUANCE: Educated people are skeptical of State TV too!
            float skepticism = resistance * skepticismFactor;
            float effectiveDefense = defensePower * (1.0f - skepticism);

            // --- NET IMPACT ---
            float netImpact = effectiveAttack - effectiveDefense;

            // --- APPLY ---
            float newInfection;
            if (netImpact > NET_IMPACT_DEADBAND)
            {
                // Infection grows
                newInfection = currentInfection + netImpact * infectionRate * deltaTime;
            }
            else if (netImpact < -NET_IMPACT_DEADBAND)
            {
                // Recovery: base decay + defense effectiveness bonus.
                // Formula: recoveryRate * (1 + |netImpact|) → max = 2x recoveryRate when defense wins fully.
                // Intentional asymmetry with infection path: strong defense accelerates deradicalization.
                float recovery = recoveryRate + (math.abs(netImpact) * recoveryRate);
                newInfection = currentInfection - recovery * deltaTime;
            }
            else
            {
                newInfection = currentInfection;
            }

            return math.saturate(newInfection);
        }

        private static float FiniteSaturate(float value)
        {
            return math.isfinite(value) ? math.saturate(value) : 0f;
        }
    }
}
