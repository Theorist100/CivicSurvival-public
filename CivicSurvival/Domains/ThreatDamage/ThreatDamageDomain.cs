using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.ThreatDamage.Systems;

namespace CivicSurvival.Domains.ThreatDamage
{
    /// <summary>
    /// ThreatDamage domain — arrival detection, debris, damage application, operational damage.
    /// Priority 2502 = Gameplay tier (registered separately via Mod.cs in Phase 6).
    ///
    /// Decoupled from ThreatFlight via 1-frame latency at boundary.
    /// </summary>
    public class ThreatDamageDomain : IFeatureModule
    {
        private static readonly LogContext Log = new("ThreatDamageDomain");

        private const int PRIORITY = 2502;

        public string Name => "ThreatDamage";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Arrival detection — detects drone/ballistic arrivals and queues terminal outcomes.
            // Anchors on WaveExecutorReadyMarker (Core marker, attached to WaveExecutor in
            // SystemRegistrar) instead of WaveExecutor directly to preserve Axiom 5.
            updateSystem.RegisterAfter<ThreatArrivalSystem, global::CivicSurvival.Core.Systems.Scheduling.WaveExecutorReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Single terminalizer: consumes intercept + arrival outcomes, deletes rendered
            // threat entities, emits immediate impacts, and creates renderless debris timers.
            updateSystem.RegisterAfter<ThreatTerminalizationSystem, global::CivicSurvival.Domains.ThreatDamage.Systems.ThreatArrivalSystem>(SystemUpdatePhase.GameSimulation);

            // Debris system — renderless debris timer and impact detection.
            updateSystem.RegisterAfter<DebrisSystem, global::CivicSurvival.Domains.ThreatDamage.Systems.ThreatTerminalizationSystem>(SystemUpdatePhase.GameSimulation);

            // Impact intake — preserves the wave/arrival/debris chain and fills the
            // apply queue. If the upkeep chain ran earlier this tick, the queue is
            // applied on the next tick; this keeps both systems single-anchored.
            updateSystem.RegisterAfter<ThreatDamageIntakeSystem, global::CivicSurvival.Domains.ThreatDamage.Systems.DebrisSystem>(SystemUpdatePhase.GameSimulation);

            // Progressive civilian building damage — hit count persistence.
            // RegisterAt (root in m_Systems) instead of RegisterAfter<CDS, BuildingUpkeepSystem>.
            // Reason: anchoring to a vanilla system is brittle (CS2 may reorder phases between
            // versions). Natural ordering vanilla→mod (vanilla systems get smaller m_AddIndex
            // since they register first via SystemOrder.Initialize) keeps CDS running after
            // BuildingUpkeepSystem within GameSimulation without an explicit anchor.
            updateSystem.RegisterAt<CivilianDamageSystem>(SystemUpdatePhase.GameSimulation);

            // Threat damage — applies the intake queue after civilian hit persistence.
            // RegisterAt root: an earlier probe (research-tds-registration branch, commit
            // 460f3a726 / 3e220c74b) found that RegisterAfter<TDS, CDS> silently skipped TDS
            // from m_Updates while CDS itself was anchored on vanilla BuildingUpkeepSystem.
            // Root cause unconfirmed (see RESEARCH_TDS_REGISTRATION.md "Часть 1").
            // Registration order in this file (CDS above TDS) gives CDS a smaller m_AddIndex
            // so CDS runs before TDS within GameSimulation; TDS reads CDS state via direct
            // method call (RecordHit), not ECB, so same-tick ordering matters here.
            updateSystem.RegisterAt<ThreatDamageSystem>(SystemUpdatePhase.GameSimulation);

            // Operational damage — power plant efficiency loss from hits
            updateSystem.RegisterAt<OperationalDamageSystem>(SystemUpdatePhase.GameSimulation);

            // Cross-domain ordering anchor for OperationalDamageSystem (Axiom 5).
            // Engineering's PlantWearSimulation orders itself after this marker
            // instead of importing OperationalDamageSystem directly.
            updateSystem.RegisterAfter<OperationalDamageReadyMarker, OperationalDamageSystem>(SystemUpdatePhase.GameSimulation);

            // Fire apply — consumer half of the mod fire producer/consumer split. Producers
            // (ThreatDamage/Corruption/PowerBackup/Engineering) create ModFireIntent in
            // GameSimulation; this system does the OnFire+BatchesUpdated archetype migration
            // in ModificationEnd, in phase with the vanilla render batch pipeline (mirror of
            // FireSimulationSystem->IgniteSystem). RegisterAt: ModificationEndBarrier playback
            // already runs before RequiredBatchesSystem within the phase.
            updateSystem.RegisterAt<ModFireApplySystem>(SystemUpdatePhase.ModificationEnd);

            // Drone deletion apply — consumer half of the drone-Deleted producer/consumer split.
            // Producers (ThreatTerminalizationSystem, ThreatDebugSystem) flip the enableable
            // PendingThreatDeletion bit in GameSimulation; this system does the structural
            // AddComponent<Deleted> on the vanilla render drone in Modification4 +
            // ModificationBarrier4 (mirror of vanilla IgniteSystem / DestroySystem, and the
            // mod's BlackoutStateSetupSystem). The add migrates the drone render chunk BEFORE
            // RequiredBatchesSystem/PreCulling/BatchManager of the same MainLoop → in phase with
            // the render pass. Done in GameSimulation the add landed out of phase → zeroed
            // render chunk-cache crash class. RegisterAt: ModificationBarrier4 playback runs
            // before RequiredBatchesSystem within the frame (cross-phase, deterministic).
            updateSystem.RegisterAt<ThreatDeletionApplySystem>(SystemUpdatePhase.Modification4);

            // DroneRenderWriteJob's handle is folded into system.Dependency at its schedule site
            // (ThreatMovementSystem, Branch B), so ECS fences vanilla readers (ObjectInterpolateSystem,
            // culling/BRG, the torn-read job lib_burst+0x9525c0) against it on the producing frame AND the
            // next — no manual Modification4 drain (removed ThreatRenderDrainSystem). The spawn/delete
            // Consume in ThreatSpawnApplySystem / ThreatDeletionApplySystem stays for main-thread
            // structural ordering.

            // Pause-safe civilian repair — request -> payment -> commit in ModificationEnd.
            updateSystem.RegisterAt<CivilianRepairDetectorSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterAfter<CivilianRepairPaymentSystem, CivilianRepairDetectorSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterAfter<CivilianRepairCommitSystem, CivilianRepairPaymentSystem>(SystemUpdatePhase.ModificationEnd);

            Log.Info("Systems registered");
        }
    }
}
