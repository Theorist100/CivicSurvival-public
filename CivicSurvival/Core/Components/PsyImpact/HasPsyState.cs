using Unity.Entities;

namespace CivicSurvival.Core.Components.PsyImpact
{
    /// <summary>
    /// Tag on vanilla Household entities marking that a corresponding
    /// HouseholdPsyState mod entity has been created.
    /// NOT ISerializable — lives on vanilla entities, re-tagged on each load
    /// by PsyImpactLifecycleSystem Phase 1 (same pattern as PowerCapacityModifiers).
    /// </summary>
    public struct HasPsyState : IComponentData { }
}
