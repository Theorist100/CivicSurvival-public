using Unity.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Casualty calculation utilities.
    /// Pure calculation, no side effects - caller decides what to do with results.
    /// </summary>
    public static class CasualtyHelper
    {
        /// <summary>
        /// Calculate casualties for a destroyed building.
        /// </summary>
        /// <param name="buildingType">Type of building destroyed</param>
        /// <param name="random">Random for variable casualties</param>
        /// <param name="casualtyType">Output: type of casualties for shock calculation</param>
        /// <returns>Number of casualties (0 if none)</returns>
        public static int CalculateCasualties(
            BuildingType buildingType,
            ref SerializableRandom random,
            out CasualtyType casualtyType)
        {
            casualtyType = BuildingClassifier.GetCasualtyType(buildingType);
            return BuildingClassifier.GetBaseCasualties(buildingType, ref random);
        }

        /// <summary>
        /// Check if building destruction should trigger a scandal event.
        /// "The Hard Choice" - hospital hit while prioritizing grid.
        /// </summary>
        /// <param name="buildingType">Type of building destroyed</param>
        /// <param name="currentPolicy">Current defense policy</param>
        /// <returns>True if scandal should be triggered</returns>
        public static bool ShouldTriggerScandal(
            BuildingType buildingType,
            DefensePolicy currentPolicy)
        {
            // Hospital hit while prioritizing grid, or while the policy owner is
            // unavailable, is fail-closed as a scandal.
            return buildingType == BuildingType.Hospital &&
                   (currentPolicy == DefensePolicy.GridIntegrity ||
                    currentPolicy == DefensePolicy.Unavailable);
        }
    }
}

