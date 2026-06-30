using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker — fires after UI request systems finish applying frame-local settings.
    /// Consumers can order themselves after this marker without referencing
    /// producer system types directly.
    ///
    /// D5 (locked).
    /// </summary>
    public partial class CorruptionSchemesReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
