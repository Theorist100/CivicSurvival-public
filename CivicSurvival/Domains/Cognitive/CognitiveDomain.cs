using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Core.Systems;
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
        public void RegisterContent() => SatireRegistry.Register(new BuckwheatSatireProvider());

        public void RegisterUI(UpdateSystem updateSystem)
        {
            updateSystem.RegisterAt<CognitiveUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<BuckwheatUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info("Registering systems...");

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

            // NOTE: CognitiveExposureSystem REMOVED - logic moved to ExposureCalculator
            // in MentalHealthResolverSystem (Logic Composition pattern)

            // ============================================================
            // OPS - Player countermeasures (write recovery to PsyPressure)
            // ============================================================

            // NOTE: CounterPropagandaSystem REMOVED - hero logic moved to CognitiveStateSystem
            // (sole owner of CognitiveState)

            // Buckwheat Protocol - social stabilization
            updateSystem.RegisterBefore<BuckwheatProcurementLevelRequestSystem, global::CivicSurvival.Core.Systems.Scheduling.CorruptionSchemesReadyMarker>(SystemUpdatePhase.GameSimulation);
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

            // Stats aggregator for UI - aggregates household-level stats
            updateSystem.RegisterAfter<CognitiveStatsAggregatorSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.MentalHealthResolverSystem>(SystemUpdatePhase.GameSimulation);

            // Transient reset - clears Pressure_*/Exposure_* after the UI stats aggregation pass.
            updateSystem.RegisterAfter<PsyTransientResetSystem, global::CivicSurvival.Domains.Cognitive.Core.Systems.CognitiveStatsAggregatorSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("Systems registered (Threats -> Ops -> Core)");
        }
    }
}
