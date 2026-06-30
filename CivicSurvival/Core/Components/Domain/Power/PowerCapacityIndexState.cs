using CivicSurvival.Core.Attributes;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    public struct PowerCapacityIndexState : IComponentData
    {
        [NonEntityIndex]
        public int PrefabIndex;
        public int PrefabVersion;
        public int UpgradeHash;
        public int HydroShapeHash;
        public CapacityChannel Channel;
    }
}
