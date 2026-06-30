using Game;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.GridWarfare.Systems;
using CivicSurvival.Domains.GridWarfare.UI;

namespace CivicSurvival.Domains.GridWarfare
{
    /// <summary>
    /// GridWarfare domain - enemy simulation, player attacks, city stability.
    /// Priority 2800 = Gameplay tier (after Refugees).
    /// </summary>
    public class GridWarfareDomain : IFeatureModule, IUiFeatureModule, IDependentFeatureModule
    {
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Notifications" };

        private static readonly LogContext Log = new("GridWarfareDomain");
        private const int PRIORITY = 2800;

        public string Name => "GridWarfare";
        public int Priority => PRIORITY;
        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<GridWarfareUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Enemy simulation - stance rotation, pressure regeneration
            updateSystem.RegisterAt<EnemySimulationSystem>(SystemUpdatePhase.GameSimulation);

            // Counter-attack arsenal: owns the munition-stock singleton + the paid
            // procurement pipeline (budget-gated, mirrors AAResupplyPipelineSystem).
            updateSystem.RegisterAt<CounterAttackArsenalSystem>(SystemUpdatePhase.GameSimulation);

            // Player attack reads the damage-stats marker output.
            updateSystem.RegisterAfter<PlayerAttackSystem, global::CivicSurvival.Core.Systems.Scheduling.DamageStatsReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Pause-safe operation clicks enqueue combat effects; this system applies
            // EnemyState axis writes from an ECS phase instead of the UI callback.
            updateSystem.RegisterAt<EnemyOperationEffectSystem>(SystemUpdatePhase.ModificationEnd);

            // City stability consumes player-attack discount state, so it must run after attacks.
            updateSystem.RegisterAfter<CityStabilitySystem, global::CivicSurvival.Domains.GridWarfare.Systems.PlayerAttackSystem>(SystemUpdatePhase.GameSimulation);

            // NOTE: ArenaLeaderboardSystem and ArenaUISystem live in Arena-owned modules.

            Log.Info("Systems registered");
        }
    }
}
