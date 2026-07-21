using Game.Common;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    public static class AirDefenseLifecycle
    {
        public static bool TryGetActiveInstallation(
            Entity aaEntity,
            ComponentLookup<AirDefenseInstallation> aaLookup,
            EntityStorageInfoLookup storageInfoLookup,
            ComponentLookup<Simulate> simulateLookup,
            ComponentLookup<Deleted> deletedLookup,
            ComponentLookup<Destroyed> destroyedLookup,
            out AirDefenseInstallation aa)
        {
            aa = default;
            if (!storageInfoLookup.Exists(aaEntity))
                return false;
            if (!aaLookup.TryGetComponent(aaEntity, out aa))
                return false;
            if (!simulateLookup.HasComponent(aaEntity) || !simulateLookup.IsComponentEnabled(aaEntity))
                return false;
            if (deletedLookup.HasComponent(aaEntity) || destroyedLookup.HasComponent(aaEntity))
                return false;

            return IsLiveLinkedBuilding(aa.GetBuildingEntity(), storageInfoLookup, deletedLookup, destroyedLookup);
        }

        public static bool IsLiveLinkedBuilding(
            Entity buildingEntity,
            EntityStorageInfoLookup storageInfoLookup,
            ComponentLookup<Deleted> deletedLookup,
            ComponentLookup<Destroyed> destroyedLookup)
        {
            return TargetLiveness.IsLiveTarget(buildingEntity, storageInfoLookup, deletedLookup, destroyedLookup);
        }
    }
}
