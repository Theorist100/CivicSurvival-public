using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker — fires after CorruptionStateUpdateSystem (Corruption) settles
    /// CorruptionSingleton for the frame. Consumers in other features order
    /// themselves after this marker without referencing the producer's type.
    ///
    /// D5 (locked).
    /// </summary>
    public partial class CorruptionStateReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
