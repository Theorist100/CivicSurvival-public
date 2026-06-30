using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Written exclusively by <c>PowerCapacityResolverSystem.ApplyWearAndRepair</c>
    /// — the resolver aggregates damage from active <c>PowerPlantDamage</c>
    /// entries and writes the per-plant canonical value via the modifier
    /// lookup. Component is added once when a plant is first indexed (by
    /// <c>PowerCapacityIndexSystem.EnsurePlantModifiers</c>); subsequent writes
    /// mutate the existing component, never re-add it.
    /// Damage from missile strikes (0.0 - 1.0, -35% per hit).
    /// Present on regular power plant entities (NOT OutsideConnection).
    /// </summary>
    public struct OperationalDamageModifier : IComponentData
    {
        /// <summary>Operational damage from missile strikes (0.0 - 1.0).</summary>
        public float OperationalDamagePercent;
    }
}
