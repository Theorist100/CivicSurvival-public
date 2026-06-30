using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Features.Population
{
    public sealed class PopulationFeature : IFeatureModule
    {
        private static readonly LogContext Log = new("PopulationFeature");

        private const int PRIORITY = 2480;

        public string Name => "Population";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");
            updateSystem.RegisterAfter<ResidentPopulationModelSystem, global::Game.Simulation.CitizenHappinessSystem>(SystemUpdatePhase.GameSimulation);

            // Cross-domain ordering anchor for Population producers (Axiom 5).
            // Attention and Diplomacy consumers order after this marker instead
            // of importing the Population model system directly.
            updateSystem.RegisterAfter<ResidentHouseholdReadyMarker, ResidentPopulationModelSystem>(SystemUpdatePhase.GameSimulation);
            Log.Info("Systems registered");
        }
    }
}
