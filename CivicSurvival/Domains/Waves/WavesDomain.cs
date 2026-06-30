using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Waves.Systems;

namespace CivicSurvival.Domains.Waves
{
    /// <summary>
    /// Waves domain — wave execution, spawn, targeting, cleanup.
    /// Priority 2520 = Gameplay tier (registered separately via Mod.cs in Phase 6).
    ///
    /// Decoupled from ThreatDamage via 1-frame latency at boundary.
    /// </summary>
    public class WavesDomain : IFeatureModule, IDependentFeatureModule
    {
        private static readonly LogContext Log = new("WavesDomain");

        // Above ThreatsAirDefense (2511) so dependency evaluation sees it first.
        private const int PRIORITY = 2520;

        public string Name => "Waves";
        public int Priority => PRIORITY;
        public IReadOnlyList<string> Dependencies => new[] { "ThreatsAirDefense" };

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Wave executor — phase transitions, single writer for WaveStateSingleton.
            // Anchors on InterceptProcessingReadyMarker (Core marker, attached to
            // InterceptProcessingSystem in ThreatsAirDefenseFeature) to preserve Axiom 5.
            updateSystem.RegisterAfter<WaveExecutor, global::CivicSurvival.Core.Systems.Scheduling.InterceptProcessingReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Building target cache — throttled producer for IThreatTargetSource.
            // Owns the 7 building queries off ThreatSpawnSystem so its per-tick
            // CompleteDependencyBeforeRO<Transform> wait is gone (Phase 5 pattern).
            updateSystem.RegisterAt<ThreatTargetCacheSystem>(SystemUpdatePhase.GameSimulation);

            // Threat spawn — resolves targets/CEP/positions/RNG and records ThreatSpawnIntent.
            updateSystem.RegisterAfter<ThreatSpawnSystem, global::CivicSurvival.Domains.Waves.Systems.WaveExecutor>(SystemUpdatePhase.GameSimulation);

            // Threat spawn apply — consumer half (mirror of ThreatDeletionApplySystem). Runs in
            // Modification4: drains the render writer (render-completion gate) then does the actual
            // drone/ballistic CreateEntity in the render-safe phase. The intent host is owned here.
            updateSystem.RegisterAt<ThreatSpawnApplySystem>(SystemUpdatePhase.Modification4);

            // Threat target — collects target data, implements IThreatTargetReader
            updateSystem.RegisterBefore<ThreatTargetSystem, global::CivicSurvival.Domains.Waves.Systems.WaveExecutor>(SystemUpdatePhase.GameSimulation);

            // Load-side render reinit (C1): threats persist across save/load, but a restored
            // drone comes back with empty render buffers (MeshColor/TransformFrame) and without
            // its lifecycle tags (ActiveThreat/PendingDestruction/Created/Updated — no serializer,
            // stripped on save). A length-0 MeshColor buffer crashes vanilla BatchDataSystem (OOB
            // read in the render Burst job). Reinit restored threats one-shot on load in
            // ModificationEnd — pause-safe (ticks under pausedAfterLoading) and ordered before
            // PreCulling, unlike the old GameSimulation cleanup that the crash window outran.
            // Already-intercepted/arrived threats are destroyed here; only live in-flight
            // threats are rehydrated before vanilla PreCulling.
            updateSystem.RegisterAt<ThreatLoadRenderReinitSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
