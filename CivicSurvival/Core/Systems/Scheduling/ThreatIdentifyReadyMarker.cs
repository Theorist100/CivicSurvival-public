using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Scheduling anchor for the threat identification pipeline.
    /// Producer: ThreatIdentifySystem writes IdentifiedTarget before this marker.
    /// Consumers that snapshot threat targeting state order after this marker.
    /// </summary>
    public partial class ThreatIdentifyReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
