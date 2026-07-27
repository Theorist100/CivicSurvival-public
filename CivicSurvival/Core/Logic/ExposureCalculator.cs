using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure calculation: cognitive exposure from propaganda sources.
    /// Extracted from CognitiveExposureSystem.UpdateExposureJob.
    /// </summary>
    #if ENABLE_BURST
    [BurstCompile]
    #endif
    public static class ExposureCalculator
    {
        /// <summary>
        /// Calculate all exposure values for a household.
        /// </summary>
        /// <param name="districtIndex">District index from CurrentDistrict, or virtual unzoned index 0</param>
        /// <param name="internetDisabledDistricts">Set of districts with internet disabled</param>
        /// <param name="hasInternetData">True if internet data is available</param>
        /// <param name="heroStatus">Current hero deployment status</param>
        /// <param name="telemarathonActive">True if telemarathon is active</param>
        /// <param name="telemarathonInShock">True if population is in shock (not watching)</param>
        /// <param name="telemarathonTrust">Trust level (0-1)</param>
        /// <param name="telemarathonEffectiveness">Effectiveness multiplier (reduced by fatigue)</param>
        /// <param name="telemarathonModeBonus">Mode-specific bonus</param>
        /// <param name="ipsoDistrictExposure">Pre-calculated IPSO exposure per district</param>
        /// <param name="hasIpsoData">True if IPSO campaign data is available</param>
        /// <param name="isGlobalBlackout">True if global Internet Blackout mode is active</param>
        /// <param name="stratum">Household social stratum — selects tool stratum-effectiveness.</param>
        /// <param name="archetype">Active hero archetype — selects the hero's per-stratum tilt.</param>
        /// <param name="telemarathonStratumStrong">Telemarathon effectiveness on its strong stratum (middle).</param>
        /// <param name="telemarathonStratumFloor">Telemarathon blanket floor on the other strata.</param>
        /// <param name="patriotWealthyBackfire">Patriot tilt on the wealthy (negative — capital flight backfire).</param>
        /// <param name="patriotPoorPress">Patriot tilt on the poor (negative — "presses the poor").</param>
        /// <param name="inCoverage">Household sits inside telecom coverage. Our counter-propaganda
        /// broadcast (counter-ops + state media) reaches ONLY inside coverage — a hole = undefended.</param>
        /// <param name="enemyInternet">Output: enemy internet exposure (0-1)</param>
        /// <param name="enemyIPSO">Output: enemy IPSO exposure (0-1)</param>
        /// <param name="counterOps">Output: counter-ops defense (0-1)</param>
        /// <param name="stateMedia">Output: state media defense (0-1)</param>
        /// <param name="heroDefenseTilt">Output: signed hero per-stratum tilt [-1..1] folded into defence (negative = added pressure / backfire).</param>
        #if ENABLE_BURST
        [BurstCompile]
        #endif
        public static void Calculate(
            int districtIndex,
            in NativeParallelHashSet<int> internetDisabledDistricts,
            bool hasInternetData,
            in NativeHashMap<int, float> ipsoDistrictExposure,
            bool hasIpsoData,
            bool isGlobalBlackout,
            HeroStatus heroStatus,
            bool telemarathonActive,
            bool telemarathonInShock,
            float telemarathonTrust,
            float telemarathonEffectiveness,
            float telemarathonModeBonus,
            SocialStratum stratum,
            HeroArchetype archetype,
            float telemarathonStratumStrong,
            float telemarathonStratumFloor,
            float patriotWealthyBackfire,
            float patriotPoorPress,
            bool inCoverage,
            out float enemyInternet,
            out float enemyIPSO,
            out float counterOps,
            out float stateMedia,
            out float heroDefenseTilt)
        {
            // 1. ENEMY INTERNET
            // T4-7 FIX: Global Blackout blocks ALL internet exposure (not just per-district ISOLATE).
            // Previously only checked per-district internetDisabledDistricts, ignoring global mode.
            if (isGlobalBlackout)
            {
                enemyInternet = 0f;
            }
            else if (hasInternetData)
            {
                bool hasInternet = districtIndex >= 0 &&
                                   !internetDisabledDistricts.Contains(districtIndex);
                enemyInternet = hasInternet ? 1.0f : 0f;
            }
            else
            {
                // No internet data yet (first frame after load) — skip exposure, matching IPSO default
                enemyInternet = 0f;
            }

            // 2. ENEMY IPSO (pre-calculated per-district by IPSOCampaignSystem).
            // The per-district value already carries the enemy word-of-mouth FLOOR
            // (enemyExposure = floor + (1-floor)*networkTerm, folded in the single-writer IPSO path).
            // Under BLACKOUT that value collapses to the floor — NOT to zero — so the leaflet/rumor
            // residual must reach CognitiveCalculator (reverses the old S17b-7 hard-zero, which made
            // the global switch a free off-switch). enemyInternet stays the pure network channel (0 at
            // blackout); the floor rides here.
            enemyIPSO = 0f;
            if (hasIpsoData && districtIndex >= 0 &&
                ipsoDistrictExposure.TryGetValue(districtIndex, out float ipsoValue))
            {
                enemyIPSO = ipsoValue;
            }

            // 3. COUNTER OPS (hero status) - city-wide standing field defence (archetype-flat).
            // The archetype's DISTINCTIVE power is its per-TYPE counter, applied on the discrete
            // PSYOPS impact channel in ResolveHouseholdPsyJob — not here on the ambient field.
            counterOps = heroStatus switch
            {
                HeroStatus.Inactive => 0f,
                HeroStatus.Deployed => 1.0f,
                HeroStatus.Lecturing => 0.5f,
                _ => 0f // S27-H2 FIX: throw crashes Burst job worker thread
            };

            // 4. STATE MEDIA (telemarathon) — now stratum-specialised: strongest on the
            // middle stratum (TV audience), reduced blanket floor elsewhere.
            if (!telemarathonActive)
            {
                stateMedia = 0f;
            }
            else
            {
                float baseValue = math.saturate(telemarathonTrust) * math.max(0f, telemarathonEffectiveness);
                stateMedia = math.max(0f, baseValue * math.max(0f, telemarathonModeBonus));
                stateMedia *= StratumDefense.TelemarathonStratumDefense(stratum, telemarathonStratumStrong, telemarathonStratumFloor);
                // S17a-9 FIX: Shock reduces state media defense to 50% instead of zeroing it.
                // Compound stacking (trust penalty + trust freeze + zero defense + fatigue) was too punishing.
                if (telemarathonInShock) stateMedia *= 0.5f;
            }

            // 5. HERO PER-STRATUM TILT — signed [-1..1] side effect distinct from the
            // type counter. Patriot drives the wealthy to flee (negative — capital flight) and
            // presses the poor; the calculator folds a negative tilt into added pressure (backfire).
            bool heroActive = heroStatus != HeroStatus.Inactive;
            heroDefenseTilt = StratumDefense.HeroStratumTilt(archetype, stratum, heroActive, patriotWealthyBackfire, patriotPoorPress);

            // 6. TELECOM COVERAGE GATE — counter-ops + state media are BROADCAST defences,
            // delivered over the network; they reach a household ONLY where telecom covers it. In a
            // coverage hole both drop to 0 (undefended zone, mirror AA). Asymmetry (Decision 3): the
            // enemy floor (enemyIPSO above) is NOT gated — it lands regardless — so a hole is the
            // household's worst zone: floor lands, counter-reach ~0. The hero per-stratum tilt is a
            // socio-economic side effect (capital flight), not broadcast reach → left ungated.
            if (!inCoverage)
            {
                counterOps = 0f;
                stateMedia = 0f;
            }
        }

    }
}
