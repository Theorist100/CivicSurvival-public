using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;

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
        /// <param name="enemyInternet">Output: enemy internet exposure (0-1)</param>
        /// <param name="enemyIPSO">Output: enemy IPSO exposure (0-1)</param>
        /// <param name="counterOps">Output: counter-ops defense (0-1)</param>
        /// <param name="stateMedia">Output: state media defense (0-1)</param>
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
            out float enemyInternet,
            out float enemyIPSO,
            out float counterOps,
            out float stateMedia)
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

            // 2. ENEMY IPSO (pre-calculated per-district by IPSOCampaignSystem)
            // S17b-7 FIX: Zero IPSO during Blackout (leaflet residual should not feed CognitiveCalculator)
            enemyIPSO = 0f;
            if (!isGlobalBlackout && hasIpsoData && districtIndex >= 0 &&
                ipsoDistrictExposure.TryGetValue(districtIndex, out float ipsoValue))
            {
                enemyIPSO = ipsoValue;
            }

            // 3. COUNTER OPS (hero status) - city-wide
            counterOps = heroStatus switch
            {
                HeroStatus.Inactive => 0f,
                HeroStatus.Deployed => 1.0f,
                HeroStatus.Lecturing => 0.5f,
                _ => 0f // S27-H2 FIX: throw crashes Burst job worker thread
            };

            // 4. STATE MEDIA (telemarathon) - city-wide
            if (!telemarathonActive)
            {
                stateMedia = 0f;
            }
            else
            {
                float baseValue = math.saturate(telemarathonTrust) * math.max(0f, telemarathonEffectiveness);
                stateMedia = math.max(0f, baseValue * math.max(0f, telemarathonModeBonus));
                // S17a-9 FIX: Shock reduces state media defense to 50% instead of zeroing it.
                // Compound stacking (trust penalty + trust freeze + zero defense + fatigue) was too punishing.
                if (telemarathonInShock) stateMedia *= 0.5f;
            }
        }

    }
}
