using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Features.Efficiency
{
    /// <summary>
    /// AlwaysOpen coordinator feature (D8, locked).
    /// Owns the generator-efficiency clear step before per-domain efficiency writers:
    ///
    ///   Clear  -> [efficiency writers in producer features]
    ///
    /// AlwaysOpen because stale sources must be cleared even when individual
    /// producer features are closed.
    /// </summary>
    public sealed class EfficiencyFeature : IFeatureModule
    {
        private static readonly LogContext Log = new("EfficiencyFeature");

        private const int PRIORITY = 2215;

        public string Name => "Efficiency";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            updateSystem.RegisterAt<GeneratorEfficiencyClearSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<GeneratorEfficiencyClearReadyMarker, GeneratorEfficiencyClearSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
