using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system — signals OperationalDamageSystem (ThreatDamage domain) has updated.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - ThreatDamage domain RegisterAfter&lt;OperationalDamageReadyMarker, OperationalDamageSystem&gt;
    ///   so the marker runs after the producer.
    /// - Consumer domains RegisterAfter&lt;TheirSystem, OperationalDamageReadyMarker&gt;
    ///   without importing CivicSurvival.Domains.ThreatDamage.Systems.
    ///
    /// Consumers: PlantWearSimulation (Engineering).
    /// </summary>
    public partial class OperationalDamageReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
