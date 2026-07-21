using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// Construction state mirrored onto a power plant for capacity resolution.
    /// Hydrated by PowerCapacityResolverSystem.ApplyConstructionDelay from the
    /// UnderConstruction sidecar (owned by ConstructionDelaySystem). Present on regular
    /// power plant entities (NOT OutsideConnection).
    /// </summary>
    public struct ConstructionModifier : IComponentData
    {
        /// <summary>Plant is currently within its construction window.</summary>
        public bool IsUnderConstruction;

        /// <summary>
        /// Construction progress 0..1 — drives a linear capacity ramp while building
        /// (effective capacity = base + delta * Progress) instead of a hard zero. Only
        /// meaningful while <see cref="IsUnderConstruction"/> is true.
        /// </summary>
        public float Progress;

        /// <summary>
        /// Pre-upgrade base capacity in kW that is NOT ramped — it produces full MW for the
        /// whole window. Mirrored from the UnderConstruction sidecar's BaseCapacityKW by
        /// PowerCapacityResolverSystem.ApplyConstructionDelay. 0 for a brand-new plant (the whole
        /// nameplate ramps), which makes the resolver's delta-aware ramp collapse to the legacy
        /// progress-only behaviour. Only meaningful while <see cref="IsUnderConstruction"/> is true.
        /// </summary>
        public int BaseCapacityKW;

        /// <summary>
        /// Target nameplate in kW the ramp converges to — mirrored from the UnderConstruction
        /// sidecar's OriginalCapacity by PowerCapacityResolverSystem.ApplyConstructionDelay. Used as
        /// the ramp divisor (served / target) so it stays self-consistent with BaseCapacityKW even
        /// when the index system has not yet re-published PlantBaseCapacity after an upgrade. 0 when
        /// not under construction (the resolver then falls back to PlantBaseCapacity.OriginalCapacity).
        /// Only meaningful while <see cref="IsUnderConstruction"/> is true.
        /// </summary>
        public int TargetNameplateKW;
    }
}
