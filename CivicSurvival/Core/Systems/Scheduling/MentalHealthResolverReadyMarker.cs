using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system — signals MentalHealthResolverSystem (Cognitive domain) has updated.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - Cognitive domain RegisterAfter&lt;MentalHealthResolverReadyMarker, MentalHealthResolverSystem&gt;
    ///   so the marker runs after the producer.
    /// - Consumer domains RegisterAfter&lt;TheirSystem, MentalHealthResolverReadyMarker&gt;
    ///   without importing CivicSurvival.Domains.Cognitive.Core.Systems.
    ///
    /// Consumers: DistrictPenaltySystem (Wellbeing).
    /// </summary>
    public partial class MentalHealthResolverReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
