using Game;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.GridWarfare.Placement;
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

            // Mirror-city STRIKE-view snapshot publisher. Read-only consumer of MirrorCitySystem's
            // MirrorCityState + EnemyTarget buffer; quantizes the city to the effective intel level and
            // publishes it as its own raw-string binding. Same UI phase as GridWarfareUISystem.
            updateSystem.RegisterAt<MirrorCityUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Drone launcher — the buildable outbound-strike origin. Placed through the generic
            // Core building-placement pipeline; this domain registers the launcher handler (prefab
            // marker check, shadow-only payment, multi-instance, installation commit). The launch
            // origin (ThreatSpawnSystem) reads the committed DroneLauncherInstallation set directly.
            BuildingPlacementHandlerRegistry.Register(new DroneLauncherPlacementHandler());

            // Enemy simulation - stance rotation, pressure regeneration
            updateSystem.RegisterAt<EnemySimulationSystem>(SystemUpdatePhase.GameSimulation);

            // Mirror city model: owns the EnemyTarget buffer + MirrorCityState, ticks target
            // repair/rebuild, and recomputes the derived enemy axes from the targets. Runs after
            // EnemySimulationSystem so the target-derived recompute (when the buffer changes) is the
            // last axis write of the frame. TRANSITIONAL: phase B consolidates axis ownership here.
            updateSystem.RegisterAfter<MirrorCitySystem, global::CivicSurvival.Domains.GridWarfare.Systems.EnemySimulationSystem>(SystemUpdatePhase.GameSimulation);

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
