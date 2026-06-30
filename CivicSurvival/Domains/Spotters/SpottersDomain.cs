using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Spotters.Systems;
using CivicSurvival.Domains.Spotters.UI;

namespace CivicSurvival.Domains.Spotters
{
    /// <summary>
    /// Spotters domain - OSINT spotter network spawn, simulation, SBU countermeasures.
    /// Priority 2514 = after AirDefense core (2510).
    /// Q1 option C: no IDependentFeatureModule.Dependencies edge to Intel;
    /// Intel data may enrich OpSec, but Spotters owns the core controls.
    /// </summary>
    public class SpottersDomain : IFeatureModule, IUiFeatureModule
    {
        private const int PRIORITY = 2514;

        private static readonly LogContext Log = new("SpottersDomain");
        public string Name => "Spotters";
        public int Priority => PRIORITY;

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<SpotterUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // 5-system pipeline (registration order = OnCreate order):
            // 1. Spawn — creates new spotters (ECB)
            updateSystem.RegisterAfter<SpotterSpawnSystem, global::CivicSurvival.Core.Systems.Scheduling.CognitiveStateReadyMarker>(SystemUpdatePhase.GameSimulation);
            // 2. Aggregate — drains commands, lifecycle, detection, penalty, stats (sole writer)
            //    Registered BEFORE ingress systems so they can GetOrCreate it in OnCreate()
            updateSystem.RegisterBefore<SpotterAggregateSystem, global::CivicSurvival.Core.Systems.Scheduling.SpotterReadyMarker>(SystemUpdatePhase.GameSimulation);
            // 3. CommandIngress — validates player requests, enqueues commands
            updateSystem.RegisterBefore<SpotterCommandIngressSystem, global::CivicSurvival.Domains.Spotters.Systems.SpotterAggregateSystem>(SystemUpdatePhase.GameSimulation);
            // 4. BudgetIngress — budget event handler, enqueues commands (event-driven)
            updateSystem.RegisterBefore<SpotterBudgetIngressSystem, global::CivicSurvival.Domains.Spotters.Systems.SpotterAggregateSystem>(SystemUpdatePhase.GameSimulation);
            // 5. Requests — processes Narrative requests (ECB, on-demand)
            updateSystem.RegisterBefore<SpotterRequestSystem, global::CivicSurvival.Domains.Spotters.Systems.SpotterAggregateSystem>(SystemUpdatePhase.GameSimulation);
            // Pause-safe district internet toggle — ModEnd request consumer, mutation delegated to aggregate owner.
            updateSystem.RegisterAt<DistrictInternetToggleSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
