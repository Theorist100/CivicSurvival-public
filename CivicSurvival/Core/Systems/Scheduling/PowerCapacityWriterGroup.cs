using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker for sidecar lifecycle systems that feed the Engineering
    /// PowerCapacityPipeline. PowerCapacityResolverSystem runs AFTER this group
    /// to hydrate split modifiers and apply final capacity.
    ///
    /// Field ownership:
    /// - ConstructionDelaySystem  → UnderConstruction sidecar lifecycle
    /// - PlantWearSimulation      → EquipmentWear sidecar lifecycle
    /// - GridStressSystem         → GridStressModifier.IsCollapsed
    /// - OperationalDamageSystem  → OperationalDamageModifier.OperationalDamagePercent
    /// - PowerPlantDisasterSystem → DisabledByDisaster sidecar lifecycle
    /// - IsolatedGridPatch        → ImportCapModifier.ImportCapLimitKW (OutsideConnection only)
    /// - PowerCapacityPipeline    → PlantBaseCapacity + split modifier hydration/resolution
    ///                              (incl. SaturationModifier surplus-saturation inertia)
    ///
    /// Execution order: After PowerPlantAISystem (vanilla recalculates capacity).
    /// </summary>
    public partial class PowerCapacityWriterGroup : SystemBase
    {
        protected override void OnUpdate() { }
    }

    /// <summary>
    /// Marker system for scheduling. Systems that READ resolved capacity
    /// should be registered after PowerCapacityReadyMarker.
    /// </summary>
    public partial class PowerCapacityReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
