using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that intel state data is ready for consumption.
    ///
    /// Purpose: Decouples domain scheduling dependencies.
    /// - IntelStateSystem (AirDefense domain) writes IntelStateSingleton
    /// - This marker runs in SimulationSystemGroup (after default scheduling)
    /// - Consumer systems run RegisterAfter(typeof(IntelStateReadyMarker))]
    ///
    /// Consumers: IntelPurchaseSystem
    /// </summary>
    public partial class IntelStateReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
