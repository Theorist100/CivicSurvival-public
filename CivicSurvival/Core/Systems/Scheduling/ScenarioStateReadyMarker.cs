using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system — signals ScenarioStateMachine (Scenario domain) has updated.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - Scenario domain RegisterAfter&lt;ScenarioStateReadyMarker, ScenarioStateMachine&gt;
    ///   so the marker runs after the producer.
    /// - Consumer domains RegisterAfter&lt;TheirSystem, ScenarioStateReadyMarker&gt;
    ///   without importing CivicSurvival.Domains.Scenario.Systems.
    ///
    /// Consumers: CrisisEconomicsSystem (Economy).
    /// </summary>
    public partial class ScenarioStateReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
