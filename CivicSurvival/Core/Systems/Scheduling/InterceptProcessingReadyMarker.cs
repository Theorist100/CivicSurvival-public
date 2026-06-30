using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system — signals InterceptProcessingSystem (ThreatsAirDefense cross-domain feature)
    /// has updated.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - ThreatsAirDefenseFeature RegisterAfter&lt;InterceptProcessingReadyMarker, InterceptProcessingSystem&gt;
    ///   so the marker runs after the producer.
    /// - Consumer domains RegisterAfter&lt;TheirSystem, InterceptProcessingReadyMarker&gt;
    ///   without importing CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense.
    ///
    /// Consumers: WaveExecutor (Waves).
    /// </summary>
    public partial class InterceptProcessingReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
