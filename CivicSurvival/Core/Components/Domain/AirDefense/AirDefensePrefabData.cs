using Unity.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// Marker + config on AA prefab entities.
    /// Added by CivicPrefabInitSystem at game start.
    /// Read by AAInstallationDetectorSystem when instance is created.
    /// </summary>
    public struct AirDefensePrefabData : IComponentData
    {
        public AAType Type;
        public float Range;
        public float InterceptChanceShahed;
        public float InterceptChanceBallistic;
        public int MaxAmmo;
        public float CooldownDuration;
        public int CrewRequired;
        public int Price;
        public bool IsHeritage;  // true = can use heritage credits
    }
}
