using Unity.Entities;
using Game.Simulation;

using CivicSurvival.Core.Features.Wellbeing;
namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Marker system - signals that mental health resolution is complete.
    ///
    /// Purpose: Decouples Core/Domain scheduling dependencies.
    /// - MentalHealthResolverSystem (Cognitive) runs RegisterBefore] this marker
    /// - WellbeingResolverSystem (Core) runs RegisterAfter] this marker
    /// - No Core → Domain import needed
    ///
    /// Ordering chain: PsyPressureWriterGroup → MentalHealthResolverSystem → MentalHealthReadyMarker → WellbeingResolverSystem
    /// </summary>
    public partial class MentalHealthReadyMarker : SystemBase
    {
        protected override void OnUpdate() { }
    }
}
