using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that threat statistics data is ready for consumption.
    ///
    /// Purpose: Decouples domain scheduling dependencies.
    /// - ThreatTargetSystem (Waves domain) writes ThreatStatsSingleton
    /// - This marker runs in SimulationSystemGroup (after default scheduling)
    /// - Consumer systems run RegisterAfter(typeof(ThreatStatsReadyMarker))]
    ///
    /// Consumers: TelemarathonSystem
    /// </summary>
    public partial class ThreatStatsReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
