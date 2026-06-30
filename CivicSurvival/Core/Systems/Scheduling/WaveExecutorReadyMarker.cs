using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that WaveStateSingleton (Waves domain) is ready for consumption.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - WaveExecutor RegisterBefore(WaveExecutorReadyMarker)] guarantees state is written
    /// - Consumer systems run RegisterAfter(typeof(WaveExecutorReadyMarker))]
    ///   without importing CivicSurvival.Domains.Waves.Systems.
    ///
    /// Consumers: IntelPurchaseSystem (Intel).
    /// </summary>
    public partial class WaveExecutorReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
