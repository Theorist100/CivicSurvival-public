using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that blackout-domain producers have completed their update.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies.
    /// - Producer systems order themselves before this marker
    /// - ThresholdOperationSystem (Engineering) and other consumers use RegisterAfter(BlackoutReadyMarker)]
    ///   instead of directly referencing blackout-domain systems
    /// </summary>
    public partial class BlackoutReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
