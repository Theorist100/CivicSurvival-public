using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that WorldShockState (Attention domain) is ready for consumption.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies (Axiom 5).
    /// - WorldShockSystem RegisterBefore(WorldShockReadyMarker)] guarantees state is written
    /// - Consumer systems run RegisterAfter(typeof(WorldShockReadyMarker))]
    ///   without importing CivicSurvival.Domains.Attention.Systems.
    ///
    /// Consumers: VIPProtectionRacketSystem (Corruption), DonorConferenceSystem (Diplomacy).
    /// </summary>
    public partial class WorldShockReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
