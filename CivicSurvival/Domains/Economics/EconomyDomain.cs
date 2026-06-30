using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Economics.Systems;

namespace CivicSurvival.Domains.Economics
{
    /// <summary>
    /// Economy domain - crisis economics and donor fund handling.
    /// Priority 2200 controls registration order; runtime ordering lives on systems.
    /// </summary>
    public class EconomyDomain : IFeatureModule
    {
        private static readonly LogContext Log = new("EconomyDomain");

        private const int PRIORITY = 2200;

        public string Name => "Economy";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Crisis economics - applies economic effects based on act.
            // Orders after ScenarioStateReadyMarker (Core marker) instead of
            // ScenarioStateMachine (Scenario) to preserve Axiom 5 — no cross-domain
            // type reference.
            updateSystem.RegisterAfter<CrisisEconomicsSystem, global::CivicSurvival.Core.Systems.Scheduling.ScenarioStateReadyMarker>(SystemUpdatePhase.GameSimulation);

            // EDA: Donor funds handler (decoupled from Diplomacy)
            updateSystem.RegisterAt<DonorFundsHandlerSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
