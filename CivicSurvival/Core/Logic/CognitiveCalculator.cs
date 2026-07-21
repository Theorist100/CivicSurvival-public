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
        /// <param name="impactAttack">Matched live landed-raid pressure for this household's stratum, ALREADY damped by the hero's by-type shield (0 = none)</param>
        /// <param name="impactInfectionWeight">Config: weight of the discrete-raid channel (Cognitive.ImpactInfectionWeight)</param>
        /// <param name="raidInfectionRate">Config: infection per hour per unit of matched raid pressure (Cognitive.RaidInfectionRate, perHour). Integrated over deltaTime like the ambient field — a landed raid is standing pressure for its landed window, so height × duration is the dose.</param>
        /// <param name="defenseTilt">Signed per-stratum defence tilt (hero stratum tilt + Buckwheat aid effect). The positive part (aid on the ground) adds ambient defence and damps the raid term; the negative part (backfire) amplifies the ambient ENEMY attack rather than adding standalone defence, so it can never fabricate infection when there is no attack to exploit.</param>
        /// <returns>New infection level (0-1)</returns>
        /// <remarks>
        /// TWO CHANNELS, deliberately separate.
        ///
        /// AMBIENT — the reactive field (enemy internet / IPSO) against the broadcast defence
        /// (telemarathon + counter-ops). Net-positive grows infection, net-negative heals it.
        ///
        /// RAID — a discrete PSYOPS contact that has LANDED. It does NOT enter the ambient sum:
        /// a landed raid must always leave a mark, and the thing that blunts it is the hero's
        /// by-type counter (already folded into <paramref name="impactAttack"/>) plus aid on the
        /// ground (defenseTilt) — NOT a broadcast that happens to be on air. Folding it into the
        /// ambient sum made a running telemarathon (state-media defence ~0.34 for the poor, ~0.73
        /// for the middle) outweigh a clean hit (~0.18), so raids landed for exactly zero and the
        /// hero's whole rock-paper-scissors identity was decorative.
        ///
        /// Both channels INTEGRATE over deltaTime. The raid input is standing pressure from the
        /// live landed attacks (rebuilt each fire), so applying it un-scaled per cycle made the
        /// dose proportional to fire count instead of game time — any raid saturated its stratum
        /// to 1.0 within minutes and intensity/type stopped mattering (2026-07-13 log: a 0.22
        /// Propaganda and a 0.80 FakeVideo both parked their strata at ~0.95).
        /// </remarks>
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
            float blackoutVulnMaxBonus,
            float impactAttack,
            float impactInfectionWeight,
            float raidInfectionRate,
            float defenseTilt)
        {
            currentInfection = FiniteSaturate(currentInfection);
            resistance = FiniteSaturate(resistance);
            skepticismFactor = FiniteSaturate(skepticismFactor);

            // Finitize the runtime-derived inputs at entry: a NaN/Inf produced by an upstream exposure or
            // impact calc must not ride the formula into the persisted InfectionLevel (which would freeze
            // a household's save with a stuck value). math.max/math.saturate do NOT filter NaN, so the
            // output saturate is not a reliable barrier — the guard belongs on the inputs. Config
            // weights/rates are already finite-guarded at their source (balance nonNegativeFinite ops),
            // so they are not re-guarded here.
            enemyInternet = Finite(enemyInternet);
            enemyIPSO = Finite(enemyIPSO);
            stateMedia = Finite(stateMedia);
            counterOps = Finite(counterOps);
            impactAttack = Finite(impactAttack);
            defenseTilt = Finite(defenseTilt);

            // Split the signed defence tilt: aid (positive) adds ground defence; backfire (negative) is a
            // grievance the wrong tool/posture created. Backfire does NOT generate its own attack — it
            // makes the enemy's ambient attack land harder — so with no hostile signal it produces
            // nothing (this is what stops a negative tilt from fabricating infection in a zero-exposure
            // field such as a Blackout).
            float aid = math.max(0f, defenseTilt);
            float backfire = math.max(0f, -defenseTilt);

            // --- RAID CHANNEL (discrete, landed contact) ---
            // Already damped by the hero's by-type shield upstream; damped further by aid on the
            // ground (a positive defenceTilt), but NEVER by the broadcast defence. Education still
            // resists a smear/deepfake, so it rides the same resistance cut as the ambient attack.
            float raidTerm = math.max(0f, impactAttack) * math.max(0f, impactInfectionWeight);
            raidTerm *= 1.0f - math.saturate(defenseTilt);
            raidTerm *= 1.0f - resistance;

            // Early out if nothing happening
            bool hasExposure = enemyInternet > 0f || enemyIPSO > 0f ||
                               stateMedia > 0f || counterOps > 0f || raidTerm > 0f;
            if (!hasExposure && currentInfection <= 0f)
                return currentInfection;

            // --- ATTACK POWER (ambient only) ---
            // Education strongly cuts crude spam (IPSO), but less effective against quality content.
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

            // Backfire amplifies the ambient attack (an exploited grievance) instead of subtracting from
            // defence: it can only add pressure when there is a hostile signal to exploit, never on its
            // own. A household with backfire but zero enemy exposure stays at effectiveAttack == 0.
            effectiveAttack *= (1.0f + backfire);

            // --- DEFENSE POWER ---
            // State media + counter-ops (Greta > TV) + aid on the ground (Buckwheat→poor / hero stratum
            // tilt). Only the positive (aid) part of the tilt adds defence; the negative (backfire) part
            // is folded into the attack above, so defencePower stays non-negative.
            float defensePower = stateMedia + (counterOps * counterOpsMultiplier) + aid;

            // NUANCE: Educated people are skeptical of State TV too! defencePower is non-negative here,
            // so scaling by (1 - skepticism) ∈ [0,1] only ever reduces defence, never flips its sign.
            float skepticism = resistance * skepticismFactor;
            float effectiveDefense = defensePower * (1.0f - skepticism);

            // --- NET AMBIENT ---
            float netAmbient = effectiveAttack - effectiveDefense;

            // --- APPLY AMBIENT ---
            float newInfection;
            if (netAmbient > NET_IMPACT_DEADBAND)
            {
                // Infection grows
                newInfection = currentInfection + netAmbient * infectionRate * deltaTime;
            }
            else if (netAmbient < -NET_IMPACT_DEADBAND)
            {
                // Recovery: base decay + defense effectiveness bonus.
                // Formula: recoveryRate * (1 + |netAmbient|) → max = 2x recoveryRate when defense wins fully.
                // Intentional asymmetry with infection path: strong defense accelerates deradicalization.
                float recovery = recoveryRate + (math.abs(netAmbient) * recoveryRate);
                newInfection = currentInfection - recovery * deltaTime;
            }
            else
            {
                newInfection = currentInfection;
            }

            // --- APPLY RAID (on top of the ambient outcome) ---
            // Standing pressure × perHour rate × dt — the same integration as the ambient field, so a
            // landed window delivers its height × duration dose regardless of how many resolve cycles
            // it spans (dt = 0 on a slot's first post-load fire adds nothing, exactly like ambient).
            // Non-negative by construction, so it can only ever push infection up; the counter-play is
            // the hero's type shield and aid, both already folded into raidTerm above.
            float raidGain = raidTerm * math.max(0f, raidInfectionRate) * deltaTime;

            return math.saturate(newInfection + raidGain);
        }

        private static float FiniteSaturate(float value)
        {
            return math.isfinite(value) ? math.saturate(value) : 0f;
        }

        // Range-preserving finite guard for signed/unbounded inputs (tilt, weights, raw exposure):
        // replaces NaN/Inf with a neutral 0 without clamping legitimate finite values into [0,1].
        private static float Finite(float value)
        {
            return math.isfinite(value) ? value : 0f;
        }
    }
}
