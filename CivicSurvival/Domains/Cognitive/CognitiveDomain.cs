using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Domains.Cognitive.Placement;
using CivicSurvival.Domains.Cognitive.Ops.Countermeasures;
using CivicSurvival.Domains.Cognitive.Ops.Systems;
using CivicSurvival.Domains.Cognitive.Threats.Systems;
using CivicSurvival.Domains.Cognitive.UI;

namespace CivicSurvival.Domains.Cognitive
{
    /// <summary>
    /// Cognitive domain - mental health and propaganda systems.
    /// Priority 2550 = Gameplay tier (after Threats, before Narrative).
    ///
    /// Architecture: Core / Threats / Ops
    /// - Core: State aggregation (MentalHealthResolver, StatsAggregator, CognitiveState)
    /// - Threats: Enemy actions (CognitiveExposure, PsyImpactLifecycle)
    /// - Ops: Player countermeasures (Telemarathon, Buckwheat, CounterPropaganda)
    ///
    /// Mechanics:
    /// - Internet ON: Integrity decreases (propaganda exposure)
    /// - Internet OFF: Integrity recovers
    /// - Below 50%: District compromised (happiness/commerce penalties)
    /// </summary>
    public class CognitiveDomain : IFeatureModule, IContentFeatureModule, IUiFeatureModule
    {
        private static readonly LogContext Log = new("CognitiveDomain");

        private const int PRIORITY = 2550;

        public string Name => "Cognitive";
        public int Priority => PRIORITY;
        public void RegisterContent()
        {
            SatireRegistry.Register(new BuckwheatSatireProvider());
            // The hero's lines in the feed — the enemy already speaks there, he did not.
            SatireRegistry.Register(new HeroSatireProvider());
        }

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<CognitiveUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<BuckwheatUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

            // Propaganda Center — the buildable defence spine. Placed through the generic
            // Core building-placement pipeline; this domain registers the Center handler (prefab
            // marker check, budget-only payment, one-HQ guard, installation commit) and the tracker
            // that derives the PropagandaCenterState posture (built/online, capacity, telecom, reach).
            BuildingPlacementHandlerRegistry.Register(new PropagandaCenterPlacementHandler());

            // Center tracker BEFORE the coverage projection so the broadcast tools read a current-tick
            // reach (the coverage cache scales Telemarathon + hero by CenterReach). Single writer of
            // PropagandaCenterState; reads installations + vanilla TelecomCoverage; writes no vanilla state.
            updateSystem.RegisterBefore<PropagandaCenterSystem, CognitiveCoverageCacheSystem>(SystemUpdatePhase.GameSimulation);

            // Installation lifecycle (single killer of PropagandaCenterInstallation) — the
            // ModificationEnd phase ticks while paused (Axiom 14; AA demolition symmetry), so a
            // bulldozed Center refunds + re-locks the layer immediately and an enemy-destroyed one
            // re-locks with no refund, instead of both deferring to the next unpaused sim tick.
            updateSystem.RegisterAt<PropagandaCenterDemolitionSystem>(SystemUpdatePhase.ModificationEnd);

            // ============================================================
            // THREATS - Enemy actions (write decay to PsyPressure)
            // ============================================================

            // PsyImpact lifecycle - load-time household PsyState reconciliation before MHR lazy-create.
            updateSystem.RegisterBefore<PsyImpactLifecycleSystem, MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // IPSO campaign - enemy information operations affecting district integrity.
            // Single anchor (vanilla UpdateSystem does not dedupe, so a system is registered once):
            // ordered after the wave writer because OnThrottledUpdate reads WaveStateSingleton and a
            // stale wave phase would mis-gate the campaign (Axiom 7). MentalHealthResolverSystem reads
            // the per-district IPSOState this system writes; both are throttled cognitive systems, so a
            // wave-consumer placement leaves MHR at most one throttled frame behind — the same benign
            // staleness this ordering itself accepts — without needing a second (illegal) registration.
            updateSystem.RegisterAfter<IPSOCampaignSystem, global::CivicSurvival.Core.Systems.Scheduling.WaveExecutorReadyMarker>(SystemUpdatePhase.GameSimulation);

            // IPSO bot messages - generates bot messages in social feed during campaigns
            updateSystem.RegisterAfter<IPSOBotMessageSystem, global::CivicSurvival.Domains.Cognitive.Threats.Systems.IPSOCampaignSystem>(SystemUpdatePhase.GameSimulation);

            // Discrete PSYOPS launcher - spawns individual mental-attack objects on top of the
            // reactive IPSO field (Tier 1). Ordered after the wave writer (same anchor as the IPSO
            // campaign) because it reads WaveStateSingleton for wave-driven raid cadence (Axiom 7).
            updateSystem.RegisterAfter<PsyOpsLaunchSystem, global::CivicSurvival.Core.Systems.Scheduling.WaveExecutorReadyMarker>(SystemUpdatePhase.GameSimulation);

            // FakeVideo sick-leave waves — reads the landed PsyOpsAttack phase the launcher just
            // advanced (same tick), fires the one-shot wave (vanilla HealthProblem Sick|NoHealthcare
            // on male workers of swayed households) and releases expired leaves. Separate latch
            // component keeps the launcher the sole PsyOpsAttack writer (CIVIC175).
            updateSystem.RegisterAfter<SickLeaveWaveSystem, PsyOpsLaunchSystem>(SystemUpdatePhase.GameSimulation);

            // Cognitive coverage projection — per-stratum defence posture (hero archetype +
            // Telemarathon + Buckwheat via StratumDefense) read by the launcher's gap-targeting and a
            // later UI overlay. Ordered before the launcher so a composed raid reads a current-tick
            // posture. Reads Hero/Telemarathon/Buckwheat singletons; writes none (CIVIC175 clean).
            updateSystem.RegisterBefore<CognitiveCoverageCacheSystem, PsyOpsLaunchSystem>(SystemUpdatePhase.GameSimulation);

            // Telecom broadcast projection — vanilla telecom towers as coverage circles (world X/Z +
            // range) for the radar mental-view "broadcast umbrella" overlay. Independent throttled
            // reader (no same-tick consumer inside GameSimulation; the ThreatUI radar reads the cached
            // projection in UIUpdate), so a plain phase registration suffices. Reads only vanilla
            // TelecomFacility/Transform + prefab range; writes nothing.
            updateSystem.RegisterAt<TelecomBroadcastCacheSystem>(SystemUpdatePhase.GameSimulation);

            // NOTE: CognitiveExposureSystem REMOVED - logic moved to ExposureCalculator
            // in MentalHealthResolverSystem (Logic Composition pattern)

            // ============================================================
            // OPS - Player countermeasures (write recovery to PsyPressure)
            // ============================================================

            // NOTE: CounterPropagandaSystem REMOVED - hero logic moved to CognitiveStateSystem
            // (sole owner of CognitiveState)

            // Buckwheat Protocol - social stabilization
            // Procurement level is a config write — pause-safe ModificationEnd route
            // (Axiom 14); the daily BuckwheatSystem reads the new level on its next tick.
            updateSystem.RegisterAt<BuckwheatProcurementLevelRequestSystem>(SystemUpdatePhase.ModificationEnd);
            // Aid distribution is a player command — pause-safe ModificationEnd intake
            // (Axiom 14) applying synchronously via BuckwheatSystem's managed state.
            updateSystem.RegisterAt<BuckwheatAidIntakeSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.RegisterBefore<BuckwheatSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Telemarathon - unified state media narrative control
            updateSystem.RegisterBefore<TelemarathonSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // ============================================================
            // CORE - State aggregation (reads from Threats/Ops, calculates final state)
            // ============================================================

            // Hero deployment system - sole owner of HeroDeploymentState (split out of CognitiveStateSystem).
            // Ordered before CognitiveStateSystem at the registration site so the throttled cognitive loop
            // sees the latest HeroStatus when computing effective infection/recovery rates.
            updateSystem.RegisterBefore<HeroDeploymentSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.CognitiveStateSystem>(SystemUpdatePhase.GameSimulation);

            // Player hero commands — pause-safe ModificationEnd intake (Axiom 14):
            // synchronous payment + same-call singleton mutation on the owner.
            updateSystem.RegisterAt<HeroActionIntakeSystem>(SystemUpdatePhase.ModificationEnd);

            // Cognitive state system - district integrity tracking, internet modes
            updateSystem.RegisterBefore<CognitiveStateSystem, global::CivicSurvival.Core.Systems.Scheduling.CognitiveStateReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Mental health resolver - central aggregator, single writer to Citizen.m_WellBeing.
            // Anchored AFTER CognitiveStateReadyMarker (its single registration): MHR reads finalized
            // cognitive state, and this lower anchor lets pressure producers (NeighborEnvy) order
            // themselves before MHR through the Core marker chain without importing this domain.
            // The upper invariant (MHR < MentalHealthReadyMarker) is carried by the marker's own
            // registration (SystemRegistrar), so MHR keeps exactly one registration (CIVIC470).
            updateSystem.RegisterAfter<MentalHealthResolverSystem, global::CivicSurvival.Core.Systems.Scheduling.CognitiveStateReadyMarker>(SystemUpdatePhase.GameSimulation);

            // Cross-domain ordering anchor for MentalHealthResolverSystem (Axiom 5).
            // Wellbeing's DistrictPenaltySystem orders itself after this marker
            // instead of importing MentalHealthResolverSystem directly.
            updateSystem.RegisterAfter<global::CivicSurvival.Core.Systems.Scheduling.MentalHealthResolverReadyMarker, MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Sidecar bulk-create (write-fence split out of MHR): the fill job's RW registrations
            // of PsyHouseholdLink/HouseholdPsyState live here so MHR's every-tick Dependency stamp
            // no longer poisons the link write-fence with the resolve chain. Anchored AFTER MHR so
            // the chain PILS < MHR < sidecar-create keeps creation deterministically after the PILS
            // post-load Phase 1 window within a frame; no-op ticks exit with Dependency = default
            // (see SAFETY-LOCK in the system).
            updateSystem.RegisterAfter<PsySidecarCreateSystem, MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Stats aggregator for UI - aggregates household-level stats
            updateSystem.RegisterAfter<CognitiveStatsAggregatorSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Transient reset - clears Pressure_*/Exposure_* after the UI stats aggregation pass.
            updateSystem.RegisterAfter<PsyTransientResetSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.CognitiveStatsAggregatorSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered (Threats -> Ops -> Core)");
        }
    }
}
