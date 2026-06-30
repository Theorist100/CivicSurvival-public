using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Features.Wellbeing
{
    /// <summary>
    /// AlwaysOpen coordinator feature (D1, locked).
    /// Owns the single mod-writer pipeline that resolves <c>Citizen.m_WellBeing</c>:
    ///
    ///   PsyPressureWriterGroup -> [pressure source features] -> WellbeingResolverSystem
    ///
    /// Plus district-level penalty pipeline that feeds wellbeing.
    ///
    /// AlwaysOpen because this is the bridge between mod gameplay and vanilla
    /// citizens — without it, mod-side pressure sources have no path to the
    /// vanilla wellbeing field. Reads optional inputs via TryGetSingleton and
    /// is no-op-correct under empty input (closed-feature semantics §2.3).
    /// </summary>
    public sealed class WellbeingFeature : IFeatureModule
    {
        private static readonly LogContext Log = new("WellbeingFeature");

        private const int PRIORITY = 2140;

        public string Name => "Wellbeing";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // District-level penalty pipeline (feeds wellbeing aggregator).
            // Anchors on MentalHealthResolverReadyMarker (Core marker, attached to
            // MentalHealthResolverSystem in CognitiveDomain) to preserve Axiom 5.
            updateSystem.RegisterAfter<DistrictPenaltySystem, global::CivicSurvival.Core.Systems.Scheduling.MentalHealthResolverReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Prepare wellbeing inputs after mod pressure/penalty producers.
            updateSystem.RegisterAfter<WellbeingResolverSystem, global::CivicSurvival.Core.Features.Wellbeing.DistrictPenaltySystem>(SystemUpdatePhase.GameSimulation);

            // Apply the deferred Citizen.m_WellBeing write after Population's marker.
            // The marker is ordered after ResidentPopulationModelSystem, which is ordered
            // after vanilla CitizenHappinessSystem; this avoids forcing Population to wait
            // on WCAS when it clears and rebuilds its eligibility buffers.
            // Separate system keeps the one-anchor invariant intact:
            // WRS stays after MHR/DPS, this apply system stays after the Core marker.
            updateSystem.RegisterAfter<WellbeingCitizenApplySystem, global::CivicSurvival.Core.Systems.Scheduling.ResidentHouseholdReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
