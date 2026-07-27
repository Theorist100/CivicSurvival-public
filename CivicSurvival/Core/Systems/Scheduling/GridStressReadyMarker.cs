using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that GridStress data is ready for consumption.
    ///
    /// Purpose: Decouples domain scheduling dependencies.
    /// - GridStressSystem (Engineering) feeds PowerCapacityWriterGroup inputs
    /// - This marker runs after PowerCapacityWriterGroup completes
    /// - PowerCapacityResolverSystem runs AFTER this marker (reads fresh TotalDemandKW snapshot)
    /// - AutoDispatchSystem (PowerGrid) runs AFTER PowerCapacityReadyMarker
    /// - No direct cross-domain reference needed
    ///
    /// Ordering chain: PowerCapacityWriterGroup -> GridStressReadyMarker -> PowerCapacityResolverSystem -> PowerCapacityReadyMarker -> AutoDispatchSystem
    /// </summary>
    public partial class GridStressReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
