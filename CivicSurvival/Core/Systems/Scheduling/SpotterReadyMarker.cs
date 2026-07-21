using Unity.Entities;
using Game.Simulation;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that spotter-domain producers have completed their update.
    ///
    /// Purpose: Decouples cross-domain scheduling dependencies.
    /// - Producer systems order themselves before this marker
    /// - Consumers: MentalHealthResolverSystem, CognitiveStateSystem, IPSOCampaignSystem, CognitiveUISystem
    /// </summary>
    public partial class SpotterReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
