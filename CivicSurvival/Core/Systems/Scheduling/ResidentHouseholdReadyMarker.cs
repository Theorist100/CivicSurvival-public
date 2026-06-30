using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system — signals the Population model has had its ordering opportunity.
    /// This is an Axiom 5 firewall for feature registration, not a synchronization device.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - Population feature registers this marker after ResidentPopulationModelSystem.
    /// - Consumer domains RegisterAfter&lt;TheirSystem, ResidentHouseholdReadyMarker&gt;
    ///   without importing CivicSurvival.Core.Features.Population.
    ///
    /// The throttled producer may skip a frame; this marker is an ordering boundary,
    /// not a strict fresh-publish guarantee.
    ///
    /// Consumers: ExodusSystem (Attention), CrisisMonitorSystem (Diplomacy).
    /// </summary>
    public partial class ResidentHouseholdReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
