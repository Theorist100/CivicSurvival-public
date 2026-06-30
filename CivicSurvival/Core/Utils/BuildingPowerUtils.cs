using Game.Areas;
using Game.Buildings;
using Unity.Entities;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Utility methods for querying building power and district state.
    /// Extracted from NarrativeSystem for reusability.
    /// </summary>
    public static class BuildingPowerUtils
    {
        /// <summary>
        /// Get district index for a building entity.
        /// Returns the canonical unzoned bucket if building has no district.
        /// </summary>
        public static int GetBuildingDistrict(Entity building, ComponentLookup<CurrentDistrict> districtLookup)
        {
            if (!districtLookup.HasComponent(building))
                return DistrictUtils.UNZONED_AREA_INDEX;

            var currentDistrict = districtLookup[building];
            return currentDistrict.m_District.Index;
        }

        /// <summary>
        /// Check if building is currently without power.
        /// Returns true if building wants power but has none fulfilled.
        /// </summary>
        public static bool IsBuildingWithoutPower(Entity building, ComponentLookup<ElectricityConsumer> consumerLookup)
        {
            if (!consumerLookup.HasComponent(building))
                return false;

            var consumer = consumerLookup[building];
            // If fulfilled is 0 but wanted > 0, building has no power
            return consumer.m_WantedConsumption > 0 && consumer.m_FulfilledConsumption == 0;
        }
    }
}
