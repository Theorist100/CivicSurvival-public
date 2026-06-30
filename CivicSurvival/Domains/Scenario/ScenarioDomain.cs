using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Scenario.Systems;
using CivicSurvival.Domains.Scenario.UI;

namespace CivicSurvival.Domains.Scenario
{
    /// <summary>
    /// Scenario domain - state machine, intro, crisis, milestones.
    /// Priority 2300 = Gameplay tier (after Economy).
    /// Systems self-wire in OnStartRunning (no wiring system needed).
    /// Note: Tutorial systems are registered in TutorialDomain.
    /// </summary>
    public class ScenarioDomain : IFeatureModule, IContentFeatureModule
    {
        public void RegisterContent() => SatireRegistry.Register(new ScenarioSatireProvider());

        private static readonly LogContext Log = new("ScenarioDomain");

        private const int PRIORITY = 2300;

        public string Name => "Scenario";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Scenario state machine - manages game acts and progression
            updateSystem.RegisterAfter<ScenarioStateMachine, global::CivicSurvival.Domains.Scenario.Systems.CrisisActCoordinator>(SystemUpdatePhase.GameSimulation);

            // Cross-domain ordering anchor for ScenarioStateMachine (Axiom 5).
            // Economy's CrisisEconomicsSystem orders itself after this marker
            // instead of importing ScenarioStateMachine directly.
            updateSystem.RegisterAfter<ScenarioStateReadyMarker, ScenarioStateMachine>(SystemUpdatePhase.GameSimulation);

            // Online consent gate - one-time GLOBAL GRID agreement, all scenarios,
            // decoupled from the narrative cold-open (event-driven; phase irrelevant).
            updateSystem.RegisterAt<OnlineConsentGateSystem>(SystemUpdatePhase.GameSimulation);

            // Intro scenario - "04:57 AM" cold open sequence
            updateSystem.RegisterAt<IntroScenarioSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<IntroLoadRestoreUISystem>(SystemUpdatePhase.UIUpdate);

            // Crisis act coordinator - crisis lifecycle management
            updateSystem.RegisterAfter<CrisisActCoordinator, global::CivicSurvival.Domains.Scenario.Systems.IntroScenarioSystem>(SystemUpdatePhase.GameSimulation);

            // Ominous signs - pre-war atmosphere (Village scenario)
            updateSystem.RegisterAfter<OminousSignsSystem, global::CivicSurvival.Core.Systems.GameTimeSystem>(SystemUpdatePhase.GameSimulation);

            // Scenario statistics - wave/damage stats aggregation
            updateSystem.RegisterAfter<ScenarioStatisticsSystem, ScenarioStateMachine>(SystemUpdatePhase.GameSimulation);

            // Scenario milestones - victory/fatigue modals
            updateSystem.RegisterAfter<ScenarioMilestonesSystem, ScenarioStatisticsSystem>(SystemUpdatePhase.GameSimulation);

            // Wave scheduler - timing logic, decides WHEN to attack
            // WaveExecutor (Threats domain) handles execution
            updateSystem.RegisterAt<WaveScheduler>(SystemUpdatePhase.GameSimulation);

            // Defeat detection - checks loss conditions
            updateSystem.RegisterAfter<DefeatCheckSystem, global::CivicSurvival.Domains.Scenario.Systems.ScenarioMilestonesSystem>(SystemUpdatePhase.GameSimulation);

            // Scenario UI - binds scenario data for UI panels
            updateSystem.RegisterAfter<ScenarioUISystem, ScenarioMilestonesSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
