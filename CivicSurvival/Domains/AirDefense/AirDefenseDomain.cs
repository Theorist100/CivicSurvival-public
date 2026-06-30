using System.Collections.Generic;
using Game;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Systems;
using CivicSurvival.Domains.AirDefense.UI;

namespace CivicSurvival.Domains.AirDefense
{
    /// <summary>
    /// AirDefense domain - AA installations, interception, ammo, tracers.
    /// Priority 2510 = Gameplay tier (after Threats core systems).
    /// Systems self-wire in OnStartRunning (no wiring system needed).
    /// </summary>
    public class AirDefenseDomain : IFeatureModule, IUiFeatureModule, IDependentFeatureModule
    {
        // D11 (locked): Mobilization is a hard dependency. Without crew assignment
        // AA cannot fire — base loop fails. Phase 5 acceptance.
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Mobilization" };

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<AirDefenseUISystem>(SystemUpdatePhase.UIUpdate);
        }

        private const int PRIORITY = 2510;

        private static readonly LogContext Log = new("AirDefenseDomain");
        public string Name => "AirDefense";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // AA prefab marker (AirDefensePrefabData) is owned by CivicPrefabInitSystem
            // (Core/Systems/Bootstrap/CivicPrefabInitSystem.cs), registered by
            // SystemRegistrar.RegisterCoreSystems. It runs in IInitializable.OnInitialize
            // (PostLoadValidationSystem post-load pass) before MarkGameplayReady, so
            // the marker is present before any AA consumer can read it.

            // AA Installation detector - detects Created instances via prefab marker
            // Pattern: prefab has AirDefensePrefabData → creates AAPlacementIntent entity
            // NOTE: Must run in ModificationEnd where Created tag still exists (before CleanUpSystem)
            // Pause-safe placement command host owns PrefabSystem/ToolSystem and activates
            // the vanilla placement tool synchronously from the UI trigger callback.
            updateSystem.RegisterBefore<AAPlacementCommandSystem, global::CivicSurvival.Domains.AirDefense.Systems.AAInstallationDetectorSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterAt<AAInstallationDetectorSystem>(SystemUpdatePhase.ModificationEnd);

            // Pause-safe placement payment/commit. Vanilla tool placement applies while
            // selectedSpeed==0; these systems close AA placement in ModificationEnd too.
            updateSystem.RegisterAfter<AAPlacementPaymentSystem, global::CivicSurvival.Domains.AirDefense.Systems.AAInstallationDetectorSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterAfter<AAPlacementCommitSystem, global::CivicSurvival.Domains.AirDefense.Systems.AAPlacementPaymentSystem>(SystemUpdatePhase.ModificationEnd);

            // AA placement lifecycle - owns cancel / timeout / post-load cleanup.
            // Runs after commit so Created -> Intent -> Apply wins over tool-default cancellation.
            updateSystem.RegisterAfter<AAPlacementLifecycleSystem, global::CivicSurvival.Domains.AirDefense.Systems.AAPlacementCommitSystem>(SystemUpdatePhase.ModificationEnd);

            // Player demolition (bulldozer) - pause-safe full crew return + synchronous cash refund.
            // ModificationEnd so it works on pause, symmetric with placement (vanilla bulldoze also
            // refunds in the pause-safe tool phase). Combat loss stays in AACrewReleaseSystem.
            updateSystem.RegisterAt<AAPlayerDemolitionSystem>(SystemUpdatePhase.ModificationEnd);

            // Request processor - handles Resupply/SetCrew/ForceCrewRelease requests.
            // Ordered before ADO so this frame's ammo/crew writes are visible to targeting.
            updateSystem.RegisterBefore<AARequestProcessorSystem, global::CivicSurvival.Domains.AirDefense.Systems.AirDefenseOrchestrator>(SystemUpdatePhase.GameSimulation);

            // UI-action request processor — file in Core/Systems/Requests for shared
            // request infrastructure, owned by AirDefense lifecycle (Phase 2).
            updateSystem.RegisterAt<AirDefenseActionRequestSystem>(SystemUpdatePhase.GameSimulation);

            // Defense policy - manages defense policy and creates singleton (MUST run before AAAmmoSystem)
            updateSystem.RegisterAt<AirDefensePolicySystem>(SystemUpdatePhase.GameSimulation);

            // Residential cache — owns NativeArray<float3> for ResidentialCheckJob
            // RegisterBefore(AirDefenseOrchestrator)] — populated before scheduling
            updateSystem.RegisterBefore<ResidentialCacheSystem, global::CivicSurvival.Domains.AirDefense.Systems.AirDefenseOrchestrator>(SystemUpdatePhase.GameSimulation);

            // Shared live-AA cache — order-version-gated snapshot of all live AA. The ONLY
            // main-thread Simulate/Transform touch in the targeting path; rebuilt only when the
            // AA set changes. Must run before both readers (ADO and BDS); ADO is already before
            // BDS at the registration below, so before-ADO is transitively before both.
            updateSystem.RegisterBefore<LiveAACacheSystem, global::CivicSurvival.Domains.AirDefense.Systems.AirDefenseOrchestrator>(SystemUpdatePhase.GameSimulation);

            // Air defense orchestrator - async job pipeline for Shahed interception
            updateSystem.RegisterBefore<AirDefenseOrchestrator, global::CivicSurvival.Domains.AirDefense.Systems.BallisticDefenseSystem>(SystemUpdatePhase.GameSimulation);

            // Ballistic defense - intercepts ballistic missiles
            updateSystem.RegisterBefore<BallisticDefenseSystem, global::CivicSurvival.Domains.AirDefense.Systems.AirDefenseShotStatsFlushSystem>(SystemUpdatePhase.GameSimulation);

            // S004: single writer of DebriefingShotStats — drains AA + ballistic shot counters
            // RegisterAfter(ADO, BDS)] + RegisterBefore(WaveExecutor via WaveExecutor's UpdateAfter]
            updateSystem.RegisterBefore<AirDefenseShotStatsFlushSystem, global::CivicSurvival.Core.Systems.Scheduling.InterceptBarrier>(SystemUpdatePhase.GameSimulation);

            // NOTE: InterceptBarrier lives in CoreKernel (Core/Systems/Scheduling/) as a shared
            // scheduling anchor; AirDefense producers (ADO, BDS) reach it via World.GetOrCreateSystemManaged.

            // AA ammo system - ammo resupply and Patriot upgrades
            updateSystem.RegisterAt<AAAmmoSystem>(SystemUpdatePhase.GameSimulation);

            // S002b + S003b: gates paid resupply on one confirmed budget result per frozen batch.
            // Ordered before ADO so accepted resupply lines are visible to same-frame targeting.
            updateSystem.RegisterBefore<AAResupplyPipelineSystem, global::CivicSurvival.Domains.AirDefense.Systems.AirDefenseOrchestrator>(SystemUpdatePhase.GameSimulation);

            // AA crew release - releases manpower when AA is destroyed
            updateSystem.RegisterAfter<AACrewReleaseSystem, global::CivicSurvival.Domains.AirDefense.Systems.AARequestProcessorSystem>(SystemUpdatePhase.GameSimulation);

            // Heritage grant publishes a same-tick HeritageGrantedEvent. Keep it in
            // GameSimulation before the credit owner so no cross-frame ECB request is
            // load-bearing for the initial AA credit grant.
            updateSystem.RegisterAfter<HeritageGrantSystem, global::CivicSurvival.Domains.AirDefense.Systems.AirDefenseOrchestrator>(SystemUpdatePhase.GameSimulation);

            // Air defense state - SINGLE WRITER for AirDefenseCreditsSingleton
            // Applies heritage grant events and owns credit mutations exposed to
            // pause-safe placement payment through explicit methods.
            // Also owns the pause-safe UI stats read model for AA counts/ammo.
            updateSystem.RegisterAfter<AirDefenseStateSystem, global::CivicSurvival.Domains.AirDefense.Systems.HeritageGrantSystem>(SystemUpdatePhase.GameSimulation);

            // Tracer VFX (visual-only, no gameplay impact). Drawn as camera-facing emissive streaks
            // via Graphics.RenderMesh — NOT a BRG mesh entity and NOT the OverlayRenderSystem buffer.
            // The mesh+OIS path interpolated a fast vertical tracer ~0.5s in the past (render position
            // dropped underground); the overlay path sized/gated the quad by horizontal length so a
            // near-vertical streak flickered as a square. A directly-submitted world-space quad,
            // stretched by the true 3D length, has neither failure mode.
            //
            // Producer: listens to AAFireEvent (a GameSimulation-tick event; AA does not fire in
            // pause, so no tracers ever appear in pause). Creates one lightweight Tracer-only entity
            // per round via EndFrameBarrier — not a render chunk, so the create is safe from
            // GameSimulation (no off-barrier split / render-completion gate needed).
            updateSystem.RegisterAt<TracerSpawnSystem>(SystemUpdatePhase.GameSimulation);

            // Renderer: per-frame billboard-quad submit via Graphics.RenderMesh + lifetime decrement
            // + expiry destroy. RenderMesh registers a one-frame intermediate renderer that HDRP draws
            // regardless of in-frame system order, so no ordering relative to OverlayRenderSystem is
            // needed. Rendering ticks during pause, but the draw delta comes from Time.time (frozen in
            // pause), so a live tracer freezes instead of advancing. No persistent render state ⇒
            // save/load is trivial and there is no restored-entity crash to purge.
            updateSystem.RegisterAt<TracerRenderSystem>(SystemUpdatePhase.Rendering);

            // Visible Patriot interceptor missile (render-only layer over the intercept formula).
            // Producer records spawn intent in GameSimulation (pause-safe — AA does not fire in pause);
            // the consumer does the render-archetype CreateEntity in Modification4 (render-safe phase,
            // mirror of the threat spawn split); cleanup despawns by lifetime, also in Modification4.
            updateSystem.RegisterAt<InterceptorSpawnSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<InterceptorMovementSystem>(SystemUpdatePhase.GameSimulation);
            // Exhaust BEFORE Movement: its collect-foreach enumerates the interceptor chunks, which would
            // force main-thread completion of Movement's in-flight InterceptorRenderWriteJob (Transform/
            // Moving/TF RW on those chunks, handle out of Dependency → dragging Movement's whole input
            // chain) — measured 20-39ms spikes on SP:InterceptorExhaust.Collect. Running before Movement,
            // Exhaust sees the PREVIOUS Movement tick's render job, already drained by the Modification4
            // RenderWriteBarrier.Consume → completion is free. The 1-tick seed lag is invisible (the VFX
            // seed is overwritten by the owner's InterpolatedTransform within a frame).
            updateSystem.RegisterBefore<InterceptorExhaustSystem, global::CivicSurvival.Domains.AirDefense.Systems.InterceptorMovementSystem>(SystemUpdatePhase.GameSimulation);
            // InterceptorRenderWriteJob's handle is folded into system.Dependency at its schedule site
            // (InterceptorMovementSystem, Branch B), so ECS fences vanilla readers AND orders the next
            // frame's InterceptorExhaustSystem after it via the job graph — no manual Modification4 drain,
            // no main-thread force-complete (removed InterceptorRenderDrainSystem). The spawn/despawn
            // Consume in InterceptorSpawnApplySystem / InterceptorCleanupSystem stays for main-thread
            // structural ordering.
            updateSystem.RegisterAt<InterceptorSpawnApplySystem>(SystemUpdatePhase.Modification4);
            updateSystem.RegisterAfter<InterceptorCleanupSystem, global::CivicSurvival.Domains.AirDefense.Systems.InterceptorSpawnApplySystem>(SystemUpdatePhase.Modification4);
            // One-shot: purge restored missile shells on first ModificationEnd of a load, before PreCulling.
            updateSystem.RegisterAt<InterceptorLoadPurgeSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
