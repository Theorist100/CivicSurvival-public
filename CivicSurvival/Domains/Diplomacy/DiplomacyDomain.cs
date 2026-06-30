using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Diplomacy.Systems;
using CivicSurvival.Domains.Diplomacy.UI;

namespace CivicSurvival.Domains.Diplomacy
{
    /// <summary>
    /// Diplomacy domain - donor conferences, crisis monitoring, international scandals.
    /// Priority 2270 = Gameplay tier (after HumanitarianAid).
    /// Systems self-wire in OnStartRunning (no wiring system needed).
    /// Hard dependency on Countermeasures: DonorConferenceSystem reads
    /// CountermeasuresCoreFsm singleton for trust source. When Countermeasures
    /// is dep-skipped (Corruption closed) or failed, Diplomacy dep-skips too —
    /// transitively closing donor/scandal/crisis surfaces.
    /// </summary>
    public class DiplomacyDomain : IFeatureModule, IUiFeatureModule, IDependentFeatureModule
    {
        private const int PRIORITY = 2270;

        private static readonly LogContext Log = new("DiplomacyDomain");

        public string Name => "Diplomacy";
        public int Priority => PRIORITY;
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Countermeasures" };

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<ShadowReputationUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<DonorConferenceUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Crisis monitoring - tracks city crisis level.
            // Anchors on ResidentHouseholdReadyMarker (Core marker, attached to
            // ResidentPopulationModelSystem in PopulationFeature) to preserve Axiom 5.
            updateSystem.RegisterAfter<CrisisMonitorSystem, global::CivicSurvival.Core.Systems.Scheduling.ResidentHouseholdReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Scandal system - reacts to corruption exposure
            updateSystem.RegisterAfter<ScandalSystem, global::CivicSurvival.Core.Systems.Scheduling.CountermeasuresReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Donor conference - international aid negotiations
            updateSystem.RegisterAfter<DonorConferenceSystem, global::CivicSurvival.Domains.Diplomacy.Systems.ScandalSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
