using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Written exclusively by PlantWearSimulation, PlantRepairService, and PlantExplosionService.
    /// Present on regular power plant entities (NOT OutsideConnection).
    /// </summary>
    public struct EquipmentWearModifier : IComponentData
    {
        /// <summary>Plant is offline for scheduled repair (capacity = 0).</summary>
        public bool IsUnderRepair;

        /// <summary>Equipment explosion damage (0.0 - 0.8).</summary>
        public float ExplosionDamagePercent;
    }
}
