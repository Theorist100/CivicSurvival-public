using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Features.Efficiency
{
    /// <summary>
    /// Owns generator-efficiency aggregation after all producer features have written.
    /// </summary>
    public sealed class EfficiencyFinalizeFeature : IFeatureModule
    {
        private static readonly LogContext Log = new("EfficiencyFinalizeFeature");

        private const int PRIORITY = 2960;

        public string Name => "EfficiencyFinalize";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            updateSystem.RegisterAt<GeneratorEfficiencyAggregateSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<GeneratorEfficiencyReadyMarker>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered");
        }
    }
}
