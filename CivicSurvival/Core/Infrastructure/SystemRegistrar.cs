using System;
using CivicSurvival.Core.Features.Wellbeing;
using Game;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Bootstrap;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Features.CrossDomain.DamageAccounting;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.UI.Toast;
using CivicSurvival.Services.Arena;
using CivicSurvival.Services.City;
using CivicSurvival.Services.Telemetry;
using CivicSurvival.Services.UI;

namespace CivicSurvival.Services.Bootstrap
{
    /// <summary>
    /// ECS system registration entry point.
    ///
    /// Structure:
    /// 1. Core systems (singletons, time, adapters) - registered here
    /// 2. Cross-domain systems (aggregators, request processors) - Core gameplay logic
    ///    that spans multiple domains, cannot live in any single Domain per Axiom 5
    /// 3. Domain systems - registered via FeatureRegistry self-registration
    ///
    /// Core systems MUST run first as they provide foundational services.
    /// </summary>
    public static class SystemRegistrar
    {
        private static readonly LogContext Log = new("SystemRegistrar");

        /// <summary>
        /// Register ALL ECS systems. Single entry point from Mod.OnLoad().
        /// </summary>
        public static void RegisterAll(UpdateSystem updateSystem, FeatureManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            Log.Info("════════════════════════════════════════");
            Log.Info("Registering ECS systems...");

            // M75 FIX: Clear stale registrations (guards against hypothetical re-registration)
            RegistrationValidator.Clear();

            // Core systems - foundational, must run first
            RegisterCoreSystems(updateSystem);

            // Cross-domain systems - aggregators and request processors
            RegisterSharedInfrastructureSystems(updateSystem);

            // Feature systems - gate-aware self-registration via FeatureRegistry
            if (FeatureRegistry.IsInitialized)
                FeatureRegistry.Instance.RegisterOpenFeatures(updateSystem, manifest);
            else
                Log.Error("FeatureRegistry not initialized — ALL feature systems skipped!");

            // Validate no orphaned CivicSystemBase subclasses
            RegistrationValidator.Validate();

            Log.Info("All systems registered");
            Log.Info("════════════════════════════════════════");
        }

        // ════════════════════════════════════════════════════════════════════
        // CORE SYSTEMS
        // Singletons, time, adapters - MUST run first
        // ════════════════════════════════════════════════════════════════════
        private static void RegisterCoreSystems(UpdateSystem updateSystem)
        {
            Log.Info("→ Core systems");

            // Lifecycle oracle - must exist before PLVS and gated base systems
            // observe load/menu transitions.
            updateSystem.RegisterBefore<CivicGameLifecycleSystem>(SystemUpdatePhase.GameSimulation);

            // Post-load validation - one-shot system that runs after all Deserialize() completes
            // Early-band registration; must run before other systems that call Register()
            updateSystem.RegisterBefore<PostLoadValidationSystem>(SystemUpdatePhase.GameSimulation);

            // Mod-wide prefab init — resolves mod .cok via IInitializable.OnInitialize
            // (invoked by PLVS after validators / singleton restore / building-ref rebind,
            // before MarkGameplayReady). Owns marker setup for AA prefabs
            // (AirDefensePrefabData) and cached PrefabBase + Entity for AttackDrone / Tracer
            // consumers. OnUpdateImpl runs only until the async tail of a slow (server-
            // delivered) load arrives: it incrementally scans newly-appended prefabs and
            // binds the ballistic exhaust VFX, then disables its own tick (Enabled = false);
            // OnInitialize re-arms it on the next load.
            //
            // ModificationEnd (not GameSimulation): the .cok tail lands on the async
            // ParadoxMods path (batched, WaitXFrames between AddPrefab batches) and an
            // intro / pausedAfterLoading load sits at selectedSpeed=0 the whole time.
            // GameSimulation does NOT tick while paused (decompile SimulationSystem.OnUpdate:
            // selectedSpeed==0 → phases skipped), so a GameSimulation-bound tail re-scan +
            // FinalizeMissing would be dead exactly in the window the tail arrives.
            // ModificationEnd lives in MainLoop outside the pause gate (Axiom 14), so the
            // tail drain and the missing-asset finalize run every frame including pause.
            // The Setup*/TryBind* structural changes (EntityManager.AddComponentData/AddBuffer
            // on a single prefab entity) are main-thread structural ops — safe in this
            // structural-friendly phase, and moving them out of GameSimulation also drops a
            // per-frame sync point from the sim group while the tick is still draining.
            updateSystem.RegisterAt<CivicPrefabInitSystem>(SystemUpdatePhase.ModificationEnd);

            // Save metadata - tracks mod version in saves for compatibility checks
            updateSystem.RegisterAt<SaveMetadataSystem>(SystemUpdatePhase.GameSimulation);

            // Time system - detects day changes and publishes DayChangedEvent
            // Must run BEFORE all systems that subscribe to DayChangedEvent
            updateSystem.RegisterBefore<GameTimeSystem>(SystemUpdatePhase.GameSimulation);

            // District lifecycle - tracks district creation/destruction
            // Publishes DistrictLifecycleEvent for state cleanup (fixes Entity.Index reuse bug)
            updateSystem.RegisterAt<DistrictLifecycleSystem>(SystemUpdatePhase.GameSimulation);

            // Vanilla Climate Adapter - isolates mod from Game.dll ClimateSystem dependencies
            updateSystem.RegisterAfter<VanillaClimateAdapter, global::Game.Simulation.ClimateSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<VanillaTerrainAdapter>(SystemUpdatePhase.GameSimulation);
            // Coastline contour producer — one-shot terrain/water sample per loaded city.
            updateSystem.RegisterAt<VanillaMapContourAdapter>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<VanillaPlanetaryClockAdapter>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<VanillaLightingAdapter>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<VanillaAreasAdapter, global::Game.Areas.UpdateCollectSystem>(SystemUpdatePhase.GameSimulation);

            // Façade hosts — per-World owners of vanilla refs; matching facades live
            // in ServiceRegistry (registered in Mod.OnLoad).
            updateSystem.RegisterAt<NameSystemHost>(SystemUpdatePhase.GameSimulation);
            // Camera focus is a UI/navigation side effect; drain it in Rendering so
            // queued focus requests still apply while GameSimulation is paused.
            updateSystem.RegisterAt<CameraFocusApplierSystem>(SystemUpdatePhase.Rendering);
            updateSystem.RegisterAt<MotionBlurHandlerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<TaxPatchHandlerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<CommercePatchHandlerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<IsolatedGridHandlerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<CityBudgetHost>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<EntityCountProbeHost>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<PopulationCountHost>(SystemUpdatePhase.GameSimulation);

            // NOTE: DistrictPenaltySystem moved to WellbeingFeature (Phase 5, D1).
            //       WellbeingResolverSystem moved to WellbeingFeature (Phase 5, D1).
            //       GeneratorEfficiency*Systems moved to EfficiencyFeature (Phase 5, D8).

            // Scheduling markers - ordering bridge between Core and feature domains.
            // Must be registered before consumers and before domain systems sort.
            // Markers used as cross-feature RegisterAfter/Before] anchors stay in CoreKernel
            // so depending systems schedule correctly even when the producer feature is closed.
            // Without registration, Unity ECS silently ignores RegisterAfter(typeof(Marker))]
            // and the systems schedule by other constraints or by accident.

            // Cognitive / mental-health pipeline
            updateSystem.RegisterAfter<PsyPressureWriterGroup, global::CivicSurvival.Core.Systems.Scheduling.ThreatStatsReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<CognitiveStateReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.SpotterReadyMarker>(SystemUpdatePhase.GameSimulation);
            // MentalHealthReadyMarker now carries the upper "resolution complete" anchor directly off
            // MentalHealthResolverSystem (Services→Domains is allowed), so MHR keeps one registration
            // and can take its lower anchor (CognitiveStateReadyMarker) in CognitiveDomain. The marker
            // stays after CognitiveStateReadyMarker transitively (MHR is registered after it).
            updateSystem.RegisterAfter<MentalHealthReadyMarker, global::CivicSurvival.Domains.Cognitive.Core.Systems.MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Anti-corruption / countermeasures pipeline
            updateSystem.RegisterAfter<CountermeasuresReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.CorruptionStateReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<ShadowTradeReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.CorruptionSchemesReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<CorruptionStateReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.MaintenanceContractReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<CorruptionSchemesReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.CorruptionStateReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Power capacity pipeline:
            // PowerCapacityWriterGroup -> GridStressReadyMarker -> PowerCapacityResolverSystem
            //   -> PowerCapacityReadyMarker -> AutoDispatchSystem
            updateSystem.RegisterAfter<PowerCapacityWriterGroup, global::CivicSurvival.Core.Systems.Scheduling.WaveExecutorReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<GridStressReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.PowerCapacityWriterGroup>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAt<PowerDataReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Blackout pipeline. Marker anchored on vanilla DispatchElectricitySystem so the chain
            // DES -> BlackoutSystem -> BlackoutReadyMarker -> ThresholdOperationSystem prevents DES's
            // m_FulfilledConsumption write (interval 128, offset 126) from overwriting our blackout zero.
            updateSystem.RegisterAfter<BlackoutReadyMarker, global::Game.Simulation.DispatchElectricitySystem>(SystemUpdatePhase.GameSimulation);

            // Spotter / intel pipeline
            updateSystem.RegisterAfter<SpotterReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.PsyPressureWriterGroup>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<IntelStateReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.WaveExecutorReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Threat stats pipeline
            updateSystem.RegisterAfter<ThreatStatsReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.CorruptionSchemesReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Threat identification/movement pipeline (identification settled before movement snapshots)
            updateSystem.RegisterAfter<ThreatIdentifyReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.WaveExecutorReadyMarker>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<ThreatMovementReadyMarker, global::CivicSurvival.Core.Systems.Scheduling.ThreatIdentifyReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Attention / world-shock pipeline (WorldShockSystem -> Corruption/Diplomacy consumers).
            // Marker carries no OnUpdate logic and consumes nothing from an ECB drain; the real
            // ordering chain is RegisterBefore<WorldShockSystem, WorldShockReadyMarker> in
            // AttentionDomain plus RegisterAfter<VIPProtectionRacketSystem, WorldShockReadyMarker>
            // in CorruptionDomain. Anchoring it on the barrier added a spurious dependency that
            // would put marker in the SafeCommandBufferSystem closed-gate window post-migration.
            updateSystem.RegisterAt<WorldShockReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Wave execution pipeline (WaveExecutor -> Intel consumers)
            updateSystem.RegisterAfter<WaveExecutorReadyMarker, global::CivicSurvival.Domains.Waves.Systems.WaveExecutor>(SystemUpdatePhase.GameSimulation);

            // Intercept barrier - shared ECB for AirDefense + Ballistic intercept commands,
            // consumed by InterceptProcessingSystem. Lives in Core (Rule 4: scheduling
            // anchors stay in CoreKernel) so AirDefense-domain producers can reach it via
            // World.GetOrCreateSystemManaged without an implicit runtime dependency on the
            // ThreatsAirDefense coordinator.
            updateSystem.RegisterAfter<InterceptBarrier, global::CivicSurvival.Core.Systems.PostLoadValidationSystem>(SystemUpdatePhase.GameSimulation);

            // GameSimulation ECB barrier with paired AllowBarrier wired as a RefMap child so
            // AllowBarrier lands IMMEDIATELY after the barrier in m_Updates, not in the At-band
            // far below the vanilla cluster. UpdateSystem.AddSystemUpdate walks the parent's
            // RefMap right after the parent (UpdateSystem.cs:425-440); RegisterAfter<Child, Parent>
            // sorts those children by AddIndex among themselves, so AllowBarrier — registered
            // first — sits first in the chain.
            //
            // Phase iteration becomes: ... barrier drain (gate closes) → AllowBarrier (gate reopens)
            // → everything else with gate=true regardless of where it physically sorts (vanilla
            // GameSim cluster, our At-band, anchored chains rooted in vanilla). Without this
            // RefMap wiring, AllowBarrier registered with plain RegisterAt sorts into the At-band
            // (AddIndex ≈ 1031+) which lands AFTER vanilla cluster (AddIndex ≈ 1..600) — leaving
            // the entire vanilla cluster in the closed-gate window and crashing any of our
            // systems anchored RegisterAfter<X, VanillaSystem> with "EntityCommandBuffer when
            // it's not allowed!" (observed on PowerCapacityIndexSystem → PowerPlantAISystem 2026-05-25).
            updateSystem.RegisterBefore<GameSimulationEndBarrier>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<AllowBarrier<GameSimulationEndBarrier>, GameSimulationEndBarrier>(SystemUpdatePhase.GameSimulation);

            // ThreatLifecycleBarrier — narrow SafeCommandBufferSystem barrier for threat-entity
            // structural changes. Same RefMap-child wiring as GameSimulationEndBarrier above so
            // AllowBarrier<ThreatLifecycleBarrier> lands immediately after the barrier in m_Updates,
            // not in the At-band.
            //
            // Hosts: ThreatArrivalSystem, InterceptProcessingSystem,
            // WaveExecutor.CleanupOrphanThreats, ThreatDebugSystem — enable-bit / renderless ECB
            // producers only. The DroneRenderWriteJob handle is NO LONGER registered here (the
            // DroneRenderAsync refactor moved render-job completion to render→render self-sync plus
            // the Modification4 RenderWriteBarrier.Consume gate). Enable-bit flips played back here
            // do not race the render job because DroneRenderWriteJob queries only ThreatPosition,
            // not the enableable threat tags (RACE-SAFETY INVARIANT, CIVIC508).
            //
            // Purpose: TMS DroneRenderWriteJob handle was once piggy-backed on
            // GameSimulationEndBarrier to gate threat-destructive flushes, forcing ~50 unrelated
            // ECB producers to wait on the 60-72ms render-job Complete every playback. Narrowing
            // the gating zone to producers that actually mutate the threat archetype eliminated
            // that spike; the render handle then left the barrier entirely (see header above).
            //
            // Ordering: runs AFTER GameSimulationEndBarrier (so threat-destructive ops see state
            // committed by the broader sim ECB), still inside SystemUpdatePhase.GameSimulation.
            updateSystem.RegisterAfter<ThreatLifecycleBarrier, GameSimulationEndBarrier>(SystemUpdatePhase.GameSimulation);
            updateSystem.RegisterAfter<AllowBarrier<ThreatLifecycleBarrier>, ThreatLifecycleBarrier>(SystemUpdatePhase.GameSimulation);

            // PostSimulation cleanup ECB barrier with terminal safe ordering:
            // AllowBarrier opens the gate, approved cleanup/request producers write,
            // ModCleanupBarrier plays back at the end of the PostSimulation group.
            updateSystem.RegisterBefore<AllowBarrier<ModCleanupBarrier>, SettingsRequestSystem>(SystemUpdatePhase.PostSimulation);
            updateSystem.RegisterAfter<ModCleanupBarrier>(SystemUpdatePhase.PostSimulation);

            // Telemetry - collects anonymous gameplay metrics via EventBus subscriptions
            updateSystem.RegisterAfter<TelemetryService>(SystemUpdatePhase.GameSimulation);

            // Diagnostic reporting - logs memory, state, errors every 60s
            // Gated by IsDebugEnabled — zero cost when debug is off
            updateSystem.RegisterAfter<DiagnosticReportSystem>(SystemUpdatePhase.GameSimulation);

            // Performance profiler - triggers PERF.log reports (OrderLast to avoid self-measurement)
            updateSystem.RegisterAfter<PerformanceProfilerSystem>(SystemUpdatePhase.GameSimulation);

            // System-order self-audit - one-shot, reads the RESOLVED post-Refresh order and flags
            // any vanilla ECB producer mis-phased into a simulation phase (e.g. UpdateGroupSystem in
            // GameSimulation → "EntityCommandBuffer when it's not allowed!"). Silent unless a
            // violation is found; disables its own tick after one read (zero recurring cost).
            updateSystem.RegisterAt<global::CivicSurvival.Core.Systems.SystemOrderAuditSystem>(SystemUpdatePhase.GameSimulation);

            // Mod entity cleanup - destroys entities marked with Deleted flag.
            // Runs in PostSimulation so its 13-job orphan sweep no longer extends
            // the GameSimulationEndBarrier producer chain. It still sits inside a
            // terminal SafeCommandBufferSystem window:
            // AllowBarrier<ModCleanupBarrier> -> cleanup producers -> ModCleanupBarrier.
            updateSystem.RegisterBefore<ModEntityCleanupSystem, global::CivicSurvival.Core.Systems.Scheduling.FrameMutationDedupClearSystem>(SystemUpdatePhase.PostSimulation);

            // Frame-mutation dedup clear: empties the shared IFrameMutationDedup map
            // after gameplay producers have queued intents and after cleanup has had
            // a chance to read current liveness. Keep the core chain rooted without
            // depending on feature-gated domains; feature producers may attach after
            // this marker and still run before ModCleanupBarrier playback.
            updateSystem.RegisterBefore<FrameMutationDedupClearSystem, global::CivicSurvival.Core.Systems.Scheduling.ModCleanupBarrier>(SystemUpdatePhase.PostSimulation);

            // Mod settings serialization - persists player settings across saves
            updateSystem.RegisterAt<ModSettingsSerializationSystem>(SystemUpdatePhase.GameSimulation);

            // Budget resolution - central queue for all budget operations (OrderLast)
            updateSystem.RegisterAfter<BudgetResolutionSystem, global::CivicSurvival.Core.Systems.PostLoadValidationSystem>(SystemUpdatePhase.GameSimulation);

            // Toast UI - cross-feature notification surface (D6: physical home Core/UI/Toast/)
            updateSystem.RegisterAt<ToastUISystem>(SystemUpdatePhase.UIUpdate);

#if ENABLE_BURST
            // Unity.Logging appends its own pump to PlayerLoop, but CS2 can rebuild
            // the loop while entering a city. Keep the logging memory manager reclaim
            // alive from our registered UI phase instead.
            updateSystem.RegisterAt<UnityLoggingPumpSystem>(SystemUpdatePhase.UIUpdate);
#endif
        }

        // ════════════════════════════════════════════════════════════════════
        // SHARED INFRASTRUCTURE SYSTEMS
        // Generic, mod-wide systems with no single feature owner. Anything that
        // would otherwise belong to one feature has been moved out (Phases 2/5).
        // ════════════════════════════════════════════════════════════════════
        private static void RegisterSharedInfrastructureSystems(UpdateSystem updateSystem)
        {
            Log.Info("→ Shared infrastructure systems");

            // Mod-wide request processors (cross-feature, no single owner).
            // Settings requests are UI-side bookkeeping and use ModCleanupBarrier.
            updateSystem.RegisterBefore<SettingsRequestSystem, global::CivicSurvival.Core.Systems.Requests.CommandRequestCleanupSystem>(SystemUpdatePhase.PostSimulation);

            // Crisis sweep — in-game balance/diagnostics tool (SettingsRequestSystem twin).
            // PostSimulation so the panel button works while paused (Axiom 14); RegisterBefore
            // the generic cleanup so it reads the request entity before it is reaped.
            updateSystem.RegisterBefore<CrisisSweepSystem, global::CivicSurvival.Core.Systems.Requests.CommandRequestCleanupSystem>(SystemUpdatePhase.PostSimulation);

            // Generic request lifecycle infrastructure
            updateSystem.RegisterBefore<CommandRequestCleanupSystem, global::CivicSurvival.Core.Systems.RequestResultCollectorSystem>(SystemUpdatePhase.PostSimulation);
            updateSystem.RegisterBefore<RequestResultCollectorSystem, global::CivicSurvival.Core.Systems.RequestResultCleanupSystem>(SystemUpdatePhase.PostSimulation);
            updateSystem.RegisterBefore<RequestResultCleanupSystem, global::CivicSurvival.Core.Systems.ModEntityCleanupSystem>(SystemUpdatePhase.PostSimulation);
#if DEBUG
            updateSystem.RegisterBefore<RequestDebugSystem>(SystemUpdatePhase.GameSimulation);
            // W2 row 421: GameSystemBase + class-level UpdateInGroup attribute does NOT auto-create a
            // mod system in CS2 — the lint never ran. Register it like every other
            // DEBUG system so the IDefaultSerializable-without-IResettable safety
            // net actually fires.
            updateSystem.RegisterAfter<CivicSurvival.Core.Systems.ResetStateValidatorSystem>(SystemUpdatePhase.GameSimulation);
#endif
        }
    }
}
