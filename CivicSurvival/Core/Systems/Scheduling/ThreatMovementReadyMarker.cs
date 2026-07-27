using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that threat positions are settled for the frame.
    ///
    /// Producer: ThreatMovementSystem (Domains/ThreatFlight) orders itself
    /// before this marker via RegisterBefore(ThreatMovementReadyMarker)].
    /// Consumers (ThreatRadarSystem, future cross-feature readers) order
    /// themselves after this marker — without referencing the producer's
    /// type directly (preserves Axiom 5 when consumers live in Core or
    /// other features).
    ///
    /// D5 (locked).
    /// </summary>
    public partial class ThreatMovementReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
