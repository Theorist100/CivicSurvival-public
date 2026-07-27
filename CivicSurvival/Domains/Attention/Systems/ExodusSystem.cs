using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Agents;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Economy;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Refugees;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Features.Population;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Interfaces.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Attention.Data;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Attention.Systems
{
    /// <summary>
    /// Manages population exodus based on World Shock level × Cognitive Integrity.
    /// Publishes state via ExodusStateSingleton.
    ///
    /// Mechanism: Adds MovingAway component to random households.
    /// Vanilla HouseholdMoveAwaySystem handles actual emigration, statistics, triggers.
    ///
    /// Logic:
    /// - Headlines tier (30-60%): 0.5% population leaves per day
    /// - GlobalShock tier (60%+): 2-4% population leaves per day
    /// - Even with Patriot, if shock stays high, city empties
    ///
    /// Integrity Multiplier ("Hero City Coefficient"):
    /// - >= 80% (Loyal):      0.5× — "Kyiv 2022", people stay despite attacks
    /// - 50-80% (Anxious):    1.0× — Normal evacuation behavior
    /// - 30-50% (Rebellious): 1.5× — Faster packing, more fear
    /// - 10-30% (Brainwashed):2.0× — Panic spreading
    /// - &lt; 10% (Zombie):       3.0× — Stampede at train station
    ///
    /// The dark truth: Best defense can't save an empty city.
    ///
    /// Uses DayChangedEvent from TimeSystem for reliable daily processing
    /// regardless of game speed.
    ///
    /// S14b-8 ACCEPTED: DayChanged handler bypasses Enabled — harmless because handler checks
    /// shock level (0 during peace) and calculates exodus rate; no exodus when no crisis.
    ///
    /// Constants from: BalanceConstants.cs -> Balance.Attention
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(ExodusStateSingleton))]
    [SingletonOwner(typeof(ExodusRateRegistry))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.ReconcileAfterLoad,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    public partial class ExodusSystem : CivicSystemBase, IResettable, IPostLoadValidation
#if DEBUG
        , IExodusDebugMutator
#endif
    {
        private static readonly LogContext Log = new("ExodusSystem");
        private WorldShockSystem m_WorldShockSystem = null!;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;
        // Both sides of every exodus ratio come from this one view: the household selection
        // AND the alive-citizen sum over that same selection. Exodus deliberately holds no
        // reference to the population scalar — dividing a citywide count by a sampled
        // denominator is the mismatch this system is not allowed to reintroduce.
        // Residency AND per-household citizen counts both come from this one published selection.
        // The Monaco flight pass validates its sidecar-sourced candidates against the same set the
        // citywide pass draws from, so both exodus streams share one residency definition
        // (non-resident tourist/commuter/temp cohorts excluded at the model query) and one count of
        // who lives in a household — the separate eligibility-set handle this system used to keep
        // answered only the first question, which is why the second was being re-derived from a
        // vanilla buffer.
        [System.NonSerialized] private IResidentHouseholdView m_ResidentHouseholdView = null!;
        // Phase 14 — per-stratum psy posture (Core interface, Axiom 5 clean). Supplies the Poor
        // infection / coverage / household weight the psy-exodus driver folds into the base.
        [System.NonSerialized] private ICognitiveCoverageReader m_CoverageReader = null!;

        // Queries (cached in OnCreate)
        private EntityQuery m_CognitiveStateQuery;
        private EntityQuery m_HeroStateQuery;
        private EntityQuery m_ScenarioQuery;
        private EntityQuery m_CurrentActQuery;
        private BufferLookup<CognitiveIntegrityBuffer> m_IntegrityBufferLookup;

        // Random for household selection
        private Unity.Mathematics.Random m_Random;

        // Stats
        private int m_TotalExodusThisSession;
        private float m_BaseExodusRatePercentPerDay;
        private float m_EffectiveExodusRatePercentPerDay;

        // Phase 14C legibility — the resolved base rate split into its two causes (kinetic WorldShock
        // vs psy-war), published on the transient ExodusStateSingleton so the UI can attribute the
        // bleed. Recomputed each processed day; not persisted (mirror the base/effective rates).
        private float m_KineticExodusRatePercentPerDay;
        private float m_PsyExodusRatePercentPerDay;

        // The Die-Hards damper's peak used to live here as a second high-water mark of the same
        // city, raised on a different schedule (only on a processed exodus day) and reset on a
        // different path (this system's own failed deserialize) from the scenario one. Two peaks
        // of one city could therefore disagree, and the ghost-town window and the damper measured
        // decline from different maxima. There is now one peak — ScenarioState.PeakPopulation,
        // projected on ScenarioSingleton — and the damper reads it as an argument.

        // A2 FIX 2a: Removed m_RateOverride — read ScenarioSingleton.ExodusRateOverrideFraction directly

        // FIX S5-03: Track act to skip exodus on transition day (prevents compound spike)
        private Act m_LastProcessedAct;

        // Dedup guard for DayChangedEvent (prevents double-processing same day)
        private DayChangedDedup m_DayDedup = default;

        // Pending-day ownership lives in the resident-population model (its
        // m_PendingDayChanges count, published as ResidentHouseholdSnapshot.PendingDayChanges
        // and acked via AckPendingDays). Exodus is a consumer: it derives both the trigger
        // ("there is pending work") and the day window from the published count plus its own
        // dedup cursor, so a readiness gap cannot desync a second Exodus-side counter from
        // the model's. There is intentionally no m_PendingDayChanged/m_PendingDayNumber here.
        [System.NonSerialized] private int m_ResidentHouseholdObserverVersion;

        // ECS singleton — liveness-validated handle (Inv 2; CIVIC427)
        [System.NonSerialized] private CivicSingletonHandle<ExodusStateSingleton> m_Singleton;
        private ComponentLookup<ExodusStateSingleton> m_SingletonLookup;

        // Sheltered households (refugees at park/border, vanilla homeless) must not be
        // exodus targets: the population model's eligible set includes them, and a
        // MovingAway mark evicts them from the shelter's Renter buffer via vanilla
        // HouseholdMoveAwaySystem — draining the refugee pipeline as fast as it fills.
        private ComponentLookup<HomelessHousehold> m_HomelessLookup;

        // Phase 16 "Batalion Monako" — the targeted Wealthy capital-flight stream. Cumulative
        // counters (persisted via ExodusCodec, mirrored on ExodusStateSingleton): families that
        // have emigrated and the household wealth that walked out with them. The pass reads the
        // per-household psy sidecar (Core component, Axiom 5 clean) to target specifically the
        // swayed wealthy; the wealth sum is informational (vanilla emigration already drains the
        // economy — no extra budget hit, Decision 4).
        private int m_MonacoFamiliesFled;
        private long m_MonacoCapitalFled;
        // Cumulative-counter caps — match the ExodusCodec persistence bounds so runtime and a
        // save-read agree (also bounds the increment for the overflow analyzer).
        private const int MonacoFamiliesCap = 100_000_000;
        private const long MonacoCapitalCap = 1_000_000_000_000L;

        // Per-household psy sidecars (Core.Components.PsyImpact — Axiom 5 clean; single writer stays
        // MentalHealthResolverSystem, Exodus only reads CachedStratum + InfectionLevel, both non-
        // transient so CIVIC266 is respected). Snapshotted daily on the main thread for the flight pass.
        private EntityQuery m_HouseholdPsyStateQuery;
        private ComponentLookup<Household> m_HouseholdLookup;
        private BufferLookup<Resources> m_ResourcesBufferLookup;
        private ComponentLookup<MovingAway> m_MovingAwayLookup;

        /// <summary>Total exodus this session.</summary>
        public int TotalExodus => m_TotalExodusThisSession;

        /// <summary>Current exodus rate.</summary>
        public float CurrentRatePercentPerDay => m_EffectiveExodusRatePercentPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_DependencyWire = new CivicDependencyWire(nameof(ExodusSystem));

            m_Random = new Unity.Mathematics.Random((uint)World.GetHashCode() | 1u); // seed must be non-zero

            // Query for cognitive integrity (for exodus multiplier)
            m_CognitiveStateQuery = GetEntityQuery(
                ComponentType.ReadOnly<CognitiveState>()
            );
            // Read the cross-domain hero singleton to fold the Patriot exodus push.
            // Attention pulls from Core (HeroDeploymentState is Core/CrossDomain) — Cognitive never
            // writes ExodusRateRegistry, so the one-writer invariant (CIVIC175) stays clean.
            m_HeroStateQuery = GetEntityQuery(ComponentType.ReadOnly<HeroDeploymentState>());
            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_IntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);

            // Create ECS singletons
            m_Singleton = CreateSingletonHandle<ExodusStateSingleton>();
            EnsureSingleton(ref m_Singleton, ExodusStateSingleton.Default);
            m_SingletonLookup = GetComponentLookup<ExodusStateSingleton>(false);
            m_HomelessLookup = GetComponentLookup<HomelessHousehold>(true);

            // Phase 16 — targeted Wealthy flight pass reads the per-household psy sidecars and the
            // vanilla household wealth (Axiom 8: queries/lookups built in OnCreate, .Update(this) in
            // the pass).
            m_HouseholdPsyStateQuery = GetEntityQuery(
                ComponentType.ReadOnly<HouseholdPsyState>(),
                ComponentType.ReadOnly<PsyHouseholdLink>(),
                ComponentType.Exclude<Deleted>());
            m_HouseholdLookup = GetComponentLookup<Household>(true);
            m_ResourcesBufferLookup = GetBufferLookup<Resources>(true);
            m_MovingAwayLookup = GetComponentLookup<MovingAway>(true);

            ExodusRateRegistry.EnsureExists(EntityManager);
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IExodusDebugMutator>(this);
#endif

            // A2 FIX 2a: ExodusRateOverrideEvent removed — read ScenarioSingleton.ExodusRateOverrideFraction.
            // Day-change pending state is owned by the resident-population model; Exodus reads
            // its published ResidentHouseholdSnapshot.PendingDayChanges instead of subscribing
            // to DayChangedEvent itself (single pending-day owner).

            Log.Info("Created — population exodus tracking active (MovingAway mechanism)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureSingleton(ref m_Singleton, ExodusStateSingleton.Default);
            ExodusRateRegistry.EnsureExists(EntityManager);
            m_WorldShockSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<WorldShockSystem>());
            m_ResidentHouseholdView ??= ServiceRegistry.Instance.Require<IResidentHouseholdView>();
            // Phase 14 — pull the cognitive coverage posture (per-stratum infection + coverage +
            // households) from Core. [GenerateNullObject] service → resolve via TryGetOrNullObject
            // (CIVIC463): fail-closed when Cognitive is absent, the null-object projects an empty
            // list (no Poor entry → psy base 0), so exodus never throws on a missing reader.
            m_CoverageReader ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCognitiveCoverageReader.Instance);
        }

        protected override void OnDestroy()
        {
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IExodusDebugMutator>(this);
#endif

            // Clean up ECS singleton
            if (m_Singleton.Entity != Entity.Null && EntityManager.Exists(m_Singleton.Entity))
            {
                EntityManager.DestroyEntity(m_Singleton.Entity);
            }

            base.OnDestroy();
        }

        private void UpdateSingleton()
        {
            var state = new ExodusStateSingleton
            {
                BaseRatePercentPerDay = m_BaseExodusRatePercentPerDay,
                EffectiveRatePercentPerDay = m_EffectiveExodusRatePercentPerDay,
                ExodusRatePercentPerDay = m_EffectiveExodusRatePercentPerDay,
                KineticRatePercentPerDay = m_KineticExodusRatePercentPerDay,
                PsyRatePercentPerDay = m_PsyExodusRatePercentPerDay,
                TotalExodus = m_TotalExodusThisSession,
                IsExodusActive = m_EffectiveExodusRatePercentPerDay > 0f,
                MonacoFamiliesFled = m_MonacoFamiliesFled,
                MonacoCapitalFled = m_MonacoCapitalFled
            };

            if (TryGetExodusSingletonEntity(out var singleton))
#pragma warning disable CIVIC035 // Write access — TryGetExodusSingletonEntity verifies HasComponent above
                m_SingletonLookup[singleton] = state;
#pragma warning restore CIVIC035
        }

        public void ValidateAfterLoad()
        {
            EnsureSingleton(ref m_Singleton, ExodusStateSingleton.Default);
            ExodusRateRegistry.EnsureExists(EntityManager);
            UpdateSingleton();
        }

        private bool TryGetExodusSingletonEntity(out Entity entity)
        {
            entity = m_Singleton.Entity;
            if (entity != Entity.Null && m_SingletonLookup.HasComponent(entity))
            {
                return true;
            }

            return m_Singleton.Query.TryGetSingletonEntity<ExodusStateSingleton>(out entity);
        }

        protected override void OnUpdateImpl()
        {
            // Single pending-day owner: the resident-population model publishes the
            // unacknowledged day count as ResidentHouseholdSnapshot.PendingDayChanges.
            // Exodus drives off that count (the trigger and the catch-up window both come
            // from the model) instead of a separate Exodus-side flag/day-number that could
            // desync across a readiness gap. Processing stays here in OnUpdateImpl, which
            // runs after WorldShockSystem (RegisterAfter), so shock state is current-frame.
            m_ResidentHouseholdView ??= ServiceRegistry.Instance.Require<IResidentHouseholdView>();

            var initialObserved = m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion);
            var initialSnapshot = initialObserved.Value;
            int pendingDays = initialSnapshot.PendingDayChanges;
            if (pendingDays <= 0)
                return;

            m_SingletonLookup.Update(this);
            m_IntegrityBufferLookup.Update(this);
            // Phase 16 — refresh the lookups the targeted Wealthy flight pass reads (past the
            // pending-day gate, so only when a day is actually processed).
            m_HouseholdLookup.Update(this);
            m_ResourcesBufferLookup.Update(this);
            m_MovingAwayLookup.Update(this);
            m_HomelessLookup.Update(this);

            // Catch-up window anchors on the dedup cursor (last successfully processed day),
            // so the days are [LastProcessedDay+1 .. LastProcessedDay+pendingDays]. Each
            // processed day advances the cursor by one and acks one pending day in the model,
            // keeping cursor and model count in lockstep.
            int firstDay = m_DayDedup.LastProcessedDay + 1;
            int catchupCitizensAlreadyMarked = 0;
            var alreadyMarked = new NativeParallelHashSet<Entity>(
                math.max(1, initialSnapshot.EligibleHouseholds.Length),
                Allocator.Temp);
            // Shared psy-sidecar snapshot for the whole catch-up window: created lazily inside
            // ProcessWealthyFlight on the first day that needs it, reused by every subsequent
            // day (sidecar data cannot change between days within one frame), disposed here.
            NativeArray<HouseholdPsyState> psySnapshot = default;
            NativeArray<PsyHouseholdLink> linkSnapshot = default;
            // Living-citizen count per eligible household, taken from the owner's published
            // selection. Same lifetime rule as the psy snapshot: built lazily on the first day
            // that needs it, reused by the rest of the window, disposed here.
            NativeParallelHashMap<Entity, int> liveByHousehold = default;
            try
            {
                for (int i = 0; i < pendingDays; i++)
                {
                    int currentDay = firstDay + i;
                    if (m_DayDedup.IsProcessed(currentDay))
                    {
                        // Already processed but still counted by the model: drain the stale
                        // pending day so the model count converges to the cursor.
                        LogPopReady(currentDay, "ack-stale");
                        if (m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion).Value.PendingDayChanges > 0)
                            m_ResidentHouseholdView.AckPendingDays(1);
                        continue;
                    }

                    var observed = m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion);
                    // One observation feeds both sides: the selection the day is applied to
                    // and the alive-citizen sum over exactly that selection.
                    int catchupPopulation = WithCatchupPopulation(observed.Value.AliveCitizensInSelection, catchupCitizensAlreadyMarked);

                    if (!ProcessDailyExodus(currentDay, observed.Value, catchupPopulation, alreadyMarked, ref liveByHousehold, ref psySnapshot, ref linkSnapshot, out int citizensMarked))
                    {
                        // Gate route (e.g. selection not ready or WorldShock missing): stop
                        // before marking/acking. The model keeps the pending count, so the
                        // next frame retries this day — no day is consumed and none is lost.
                        LogPopReady(currentDay, "gate-skip");
                        break;
                    }

                    m_DayDedup.MarkProcessed(currentDay);
                    catchupCitizensAlreadyMarked += citizensMarked;
                    LogPopReady(currentDay, "mark");

                    if (observed.Value.PendingDayChanges > 0)
                    {
                        m_ResidentHouseholdView.AckPendingDays(1);
                        LogPopReady(currentDay, "ack");
                    }
                }
            }
            finally
            {
                if (alreadyMarked.IsCreated) alreadyMarked.Dispose();
                if (psySnapshot.IsCreated) psySnapshot.Dispose();
                if (linkSnapshot.IsCreated) linkSnapshot.Dispose();
                if (liveByHousehold.IsCreated) liveByHousehold.Dispose();
            }
        }

        // [POP-READY] pending-day ledger (Verification table). Gated by the same diagnostic
        // log switch as the rest of the contour so it is provable by Grep without metadata.
        // Reads through a throwaway version cursor so logging never mutates the real observer
        // version used by the catch-up loop.
        private void LogPopReady(int day, string action)
        {
            if (!Log.IsDebugEnabled)
                return;

            // Every caller of this log sits below a Require-resolve of the view, and Require fails
            // loud, so the view is read directly. The null-tolerant form it replaces printed a
            // missing view as "selection not ready" with pending=-1 — a service failure wearing the
            // costume of an ordinary not-ready tick, inside the one ledger written to tell the
            // states of the population contour apart.
            int logCursor = 0;
            int pending = m_ResidentHouseholdView.Observe(ref logCursor).Value.PendingDayChanges;
            // Selection readiness is the gate Exodus actually stands on (both the households
            // and the citizen sum come from the selection), so that is what the ledger prints.
            bool selectionReady = m_ResidentHouseholdView.IsSelectionReady;
            Log.Info($"[POP-READY] Exodus day={day} selectionReady={selectionReady} pending={pending} action={action}");
        }

        /// <summary>
        /// Process daily exodus using current-frame shock state.
        /// Called from OnUpdateImpl (after WorldShockSystem has updated).
        /// </summary>
        /// <remarks>
        /// Collapsed to the bare number it always was: the producer publishes one selection
        /// per model tick, so a multi-day catch-up inside one frame must subtract the citizens
        /// it has already marked itself. Nothing else of the old scalar snapshot was read here.
        /// </remarks>
        private static int WithCatchupPopulation(int aliveCitizens, int citizensAlreadyMarked)
        {
            if (citizensAlreadyMarked <= 0)
                return aliveCitizens;

            return math.max(0, aliveCitizens - citizensAlreadyMarked);
        }

        private bool ProcessDailyExodus(
            int dayNumber,
            ResidentHouseholdSnapshot householdSnapshot,
            int alivePopulation,
            NativeParallelHashSet<Entity> alreadyMarked,
            ref NativeParallelHashMap<Entity, int> liveByHousehold,
            ref NativeArray<HouseholdPsyState> psySnapshot,
            ref NativeArray<PsyHouseholdLink> linkSnapshot,
            out int citizensMarked)
        {
            citizensMarked = 0;
            using var _ = PerformanceProfiler.Measure("Exodus.ProcessDailyExodus");
#pragma warning disable CIVIC256 // Resolved in OnCreate — null only if WorldShockSystem missing
            if (m_WorldShockSystem == null) return false;
#pragma warning restore CIVIC256

            // Readiness gate (A7). Exodus decides and applies entirely from the household
            // selection — the households it marks and the citizen sum it scales by are two
            // faces of one published result — so SelectionReady is the whole gate. The gate is
            // placed here — before MarkProcessed / AckPendingDays / cursor advance in the
            // caller — and returns false via the same break route as the WorldShock check.
            // The model keeps the pending count, so the day is retried next frame: not
            // consumed, not lost. An early return at the top of OnUpdateImpl is forbidden
            // (it is the desync-prone path).
            if (!m_ResidentHouseholdView.IsSelectionReady)
                return false;

            // The scenario singleton carries two things this day needs: the rate override and —
            // since the two peaks were merged — the damper's anchor. It is resolved HERE, above
            // the first line that marks anything, and its absence takes the same gate route as
            // the readiness check: the day is not consumed and the next frame retries.
            //
            // It used to be read as `TryGetSingleton(out sc) ? sc : ScenarioSingleton.Default`
            // further down. With the peak on that singleton, that fallback would be a one-sided
            // silent failure — the default's peak is "not recorded", the damper is gated on
            // having one, so a missed query would switch the damper off and let the exodus run
            // at full speed with nothing in the log to say so.
            if (!m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenario))
            {
                Log.Warn($"Exodus day {dayNumber} not processed — the scenario singleton is missing, so neither the rate override nor the damper's peak can be read; retrying next frame");
                return false;
            }

            // Phase 16 "Batalion Monako" — targeted Wealthy capital-flight pass. Runs BEFORE the
            // citywide random pass so it claims its swayed-wealthy households first (clean
            // attribution) and shares the same alreadyMarked dedup + homeless guard. It is a SEPARATE
            // stream, independent of the kinetic+psy citywide base below: the wealthy can flee even at
            // zero WorldShock. The day is guaranteed to be consumed past the readiness gate, so
            // marking here never double-counts across frame retries. Its citizens roll into
            // citizensMarked so the catch-up population accounting stays correct (gate G7).
            int monacoCitizens = ProcessWealthyFlight(
                alreadyMarked, in householdSnapshot, ref liveByHousehold, ref psySnapshot, ref linkSnapshot);

            // The peak is no longer raised here: ScenarioStateMachine owns the single high-water
            // mark and raises it every second from the city record, which is a strictly better
            // schedule than "only on a day the exodus actually processed".
            int population = alivePopulation;
            float citySizeMultiplier = GetCitySizeMultiplier(population);
            float integrityMultiplier = GetIntegrityMultiplier();

            // A2 FIX 2a: Read exodus rate override from singleton (replaces EventBus m_RateOverride)
            float exodusRateOverrideFraction = scenario.ExodusRateOverrideFraction;
#pragma warning disable CIVIC070 // ScenarioSingleton changes at act transitions only; 1-frame lag invisible
            var currentAct = (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var ca)
                    ? ca : CurrentActSingleton.Default)
                .CurrentAct;
#pragma warning restore CIVIC070

            // FIX S5-03: Skip exodus on act transition day when leaving Crisis.
            // Prevents compound spike: act transition clears override to 0, DayChanged fires
            // on same frame → shock-based fallback at 100% shock → 18%/day phantom spike.
            // Next day: shock decays naturally → no spike.
            if (currentAct != m_LastProcessedAct)
            {
                Act previousAct = m_LastProcessedAct;
                m_LastProcessedAct = currentAct;

                if (previousAct == Act.Crisis)
                {
                    Log.Info($"Act transition from Crisis to {currentAct} — skipping exodus for transition day");
                    m_BaseExodusRatePercentPerDay = 0f;
                    m_EffectiveExodusRatePercentPerDay = 0f;
                    m_KineticExodusRatePercentPerDay = 0f;
                    m_PsyExodusRatePercentPerDay = 0f;
                    citizensMarked = monacoCitizens; // targeted Wealthy flight is independent of the transition skip
                    UpdateSingleton();
                    return true;
                }
            }

            // Write modifiers to registry — single source of truth for all exodus rate factors
            // Registry guaranteed by EnsureExists in OnCreate + OnStartRunning + ValidateAfterLoad.
#pragma warning disable CIVIC070, CIVIC055 // Registry guaranteed by EnsureExists in lifecycle restore paths
            ref var reg = ref SystemAPI.GetSingletonRW<ExodusRateRegistry>().ValueRW;
#pragma warning restore CIVIC070, CIVIC055
            reg.Rate.Set(ExodusRateRegistry.Source.CitySize, citySizeMultiplier);
            reg.Rate.Set(ExodusRateRegistry.Source.Integrity, integrityMultiplier);
            reg.Rate.Set(ExodusRateRegistry.Source.HeroArchetype, GetPatriotExodusMultiplier());

            if (exodusRateOverrideFraction > 0f)
            {
                reg.Rate.SetOverride(exodusRateOverrideFraction * 100f);
            }
            else
            {
                reg.Rate.ClearOverride();
            }

            var shockState = m_WorldShockSystem.GetCurrentState();
            float kineticBase = CalculateExodusRatePercentPerDay(shockState);   // WorldShock (demoted, 14B)

            // Phase 14 — psy-war exodus driver. The swayed, uncovered Poor bleed on their own: the
            // driver rate is ADDED to the kinetic base BEFORE Resolve (the registry is multiplicative-
            // only, so a psy Source would multiply a zero-shock base to zero and could never start an
            // exodus). CitySize×Integrity×Hero then amplify the combined kinetic+psy base. Poor-only —
            // Middle/Wealthy psy harm routes to commerce / capital flight, not exodus.
            float psyBase = ComputePoorPsyExodusRate(out float poorInfection, out float poorCoverageFraction, out float poorShare);
            float combinedBase = kineticBase + psyBase;
            m_BaseExodusRatePercentPerDay = reg.Rate.Resolve(combinedBase);

            // 14C legibility — attribute the resolved base to its two causes for the UI. Scale each raw
            // contribution by the same resolve ratio so kinetic+psy == BaseRatePercentPerDay (the
            // multipliers amplify the sum, so they distribute proportionally between the causes).
            float resolveRatio = combinedBase > 0f ? m_BaseExodusRatePercentPerDay / combinedBase : 0f;
            m_KineticExodusRatePercentPerDay = kineticBase * resolveRatio;
            m_PsyExodusRatePercentPerDay = psyBase * resolveRatio;

            // Only exodus if rate > 0
            if (m_BaseExodusRatePercentPerDay <= 0f)
            {
                m_EffectiveExodusRatePercentPerDay = 0f;
                citizensMarked = monacoCitizens; // Monaco may still have marked wealthy families this day
                UpdateSingleton();
                return true;
            }

            if (Log.IsDebugEnabled)
                Log.Debug($"Day {dayNumber}: pop={population}, cityMult={citySizeMultiplier:F1}x, integrityMult={integrityMultiplier:F1}x, " +
                    $"kinetic={kineticBase:F2} psy={psyBase:F2} poorInf={poorInfection:F2} poorCov={poorCoverageFraction:F2} poorShare={poorShare:F2}, " +
                    $"exodus={m_BaseExodusRatePercentPerDay:F1}%/day");
            m_EffectiveExodusRatePercentPerDay = ProcessExodus(m_BaseExodusRatePercentPerDay, householdSnapshot, alivePopulation, alreadyMarked, scenario.PopulationPeak, out citizensMarked);
            citizensMarked += monacoCitizens; // fold the targeted Wealthy flight into the day's total

            // Update singleton after daily processing
            UpdateSingleton();
            return true;
        }

        /// <summary>
        /// Cognitive integrity exodus multiplier ("Hero City Coefficient").
        ///
        /// Logic: Mental resilience determines exodus speed during crises.
        /// - High integrity (80%+): "Kyiv 2022" - people STAY despite attacks
        /// - Low integrity (&lt;10%): Panic, stampede at train station
        ///
        /// This multiplier only affects exodus rate when there IS a crisis (WorldShock).
        /// Low integrity alone doesn't cause exodus - but amplifies it when attacked.
        ///
        /// Aligned with CrisisEconomicsSystem thresholds: 80/50/30/10%
        /// </summary>
        private float GetIntegrityMultiplier()
        {
            if (!m_CognitiveStateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return 1.0f; // No cognitive data = normal rate

            // Calculate average integrity from district buffer (Cognitive data read stays
            // in this domain). The threshold → multiplier RULE lives in Core so it is not a
            // cross-domain decision baked into Attention (Axiom 5).
            float integrity = CalculateAvgIntegrity(stateEntity);
            return StabilityMath.IntegrityExodusMultiplier(integrity);
        }

        /// <summary>
        /// Patriot hero-archetype exodus multiplier. Only an actively deployed Patriot
        /// pushes exodus ("stand and die" fatalism). The threshold→multiplier RULE lives in Core
        /// (<see cref="StabilityMath.PatriotExodusMultiplier"/>), so Attention only reads the
        /// cross-domain singleton and delegates the decision (Axiom 5), exactly like the integrity
        /// multiplier.
        /// </summary>
        private float GetPatriotExodusMultiplier()
        {
            if (!m_HeroStateQuery.TryGetSingleton<HeroDeploymentState>(out var hero)
                || hero.HeroStatus == HeroStatus.Inactive)
                return 1.0f;
            return StabilityMath.PatriotExodusMultiplier(hero.Archetype);
        }

        /// <summary>
        /// Calculate average integrity from CognitiveIntegrityBuffer.
        /// </summary>
        private float CalculateAvgIntegrity(Entity stateEntity)
        {
            if (!m_IntegrityBufferLookup.HasBuffer(stateEntity))
                return 1.0f; // No buffer = assume full integrity

            var buffer = m_IntegrityBufferLookup[stateEntity];
            if (buffer.Length == 0)
                return 1.0f;

            float total = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                total += buffer[i].Integrity;
            }

            return total / buffer.Length;
        }

        /// <summary>
        /// City size exodus multiplier ("Autonomy Coefficient").
        ///
        /// Logic: Infrastructure dependency determines exodus speed.
        /// - Villages are autonomous (wells, wood stoves, cellars) - people STAY
        /// - Cities are "concrete traps" (no power = no water pumps, no heating, no elevators) - people FLEE
        ///
        /// This inverts the intuitive "big city has more inertia" logic because
        /// during infrastructure warfare, high-rises become death traps without electricity.
        /// </summary>
        private float GetCitySizeMultiplier(int population)
        {
            // Population bucketing + per-tier exodus multiplier (with negative-clamp) live in
            // Core so the Refugees spawn-rate path keys off the same tier thresholds.
            return StabilityMath.CitySizeExodusMultiplier(population);
        }

        /// <summary>
        /// Calculate exodus rate based on shock level.
        /// </summary>
        private float CalculateExodusRatePercentPerDay(WorldShockState shockState)
        {
            var attn = BalanceConfig.Current.Attention;
            switch (shockState.CurrentTier)
            {
                case AidTier.DeepConcern:
                    // Below Headlines - minimal exodus
                    if (shockState.ShockLevel >= attn.TierDeepConcern)
                    {
                        return attn.ExodusDeepConcern;
                    }
                    return 0f;

                case AidTier.Headlines:
                    // 30-60% shock = 0.5%/day exodus
                    return attn.ExodusHeadlines;

                case AidTier.GlobalShock:
                    // 60-100% shock = 2-4%/day exodus (scales with shock)
                    // FIX BUG-ATT-001: Guard against division by zero if TierGlobalShock == 100
                    float divisor = 100f - attn.TierGlobalShock;
                    float shockScale = divisor > 0f
                        ? (shockState.ShockLevel - attn.TierGlobalShock) / divisor
                        : 1f;
                    return math.lerp(
                        attn.ExodusGlobalMin,
                        attn.ExodusGlobalMax,
                        math.saturate(shockScale)
                    );

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Poor psy-war exodus contribution (%/day, additive base). Reads the per-stratum psy posture
        /// from the Core coverage reader (Axiom 5 — no Domains.Cognitive import) and delegates the
        /// threshold→rate curve to <see cref="StabilityMath.PoorPsyExodusRatePercentPerDay"/>. The Poor
        /// signal / coverage / share are returned for the diagnostic line. Empty reader (pre-first-tick)
        /// or no Poor entry → 0 (fail-closed, no phantom bleed).
        /// </summary>
        private float ComputePoorPsyExodusRate(out float poorInfection, out float poorCoverageFraction, out float poorShare)
        {
            poorInfection = 0f;
            poorCoverageFraction = 0f;
            poorShare = 0f;

            var coverage = m_CoverageReader?.GetCoverage();
            if (coverage == null || coverage.Count == 0)
                return 0f;

            float totalHouseholds = 0f;
            float poorHouseholds = 0f;
            bool hasPoor = false;
            for (int i = 0; i < coverage.Count; i++)
            {
                var stratum = coverage[i];
                totalHouseholds += stratum.Households;
                if (stratum.Stratum == SocialStratum.Poor)
                {
                    poorInfection = stratum.Infection;
                    poorCoverageFraction = stratum.CoverageFraction;
                    poorHouseholds = stratum.Households;
                    hasPoor = true;
                }
            }

            if (!hasPoor)
                return 0f;

            poorShare = totalHouseholds > 0f ? poorHouseholds / totalHouseholds : 0f;
            return StabilityMath.PoorPsyExodusRatePercentPerDay(poorInfection, poorCoverageFraction, poorHouseholds, totalHouseholds);
        }

        /// <summary>
        /// Process daily exodus.
        /// </summary>
        private float ProcessExodus(
            float ratePercent,
            ResidentHouseholdSnapshot householdSnapshot,
            int alivePopulation,
            NativeParallelHashSet<Entity> alreadyMarked,
            RecordedPopulation peakPopulation,
            out int actualLeaving)
        {
            actualLeaving = 0;
            // Sum over householdSnapshot's own selection — the same selection ApplyExodusEffects
            // divides it by below, and the same one it marks households from.
            int totalPopulation = alivePopulation;
            if (totalPopulation == 0) return 0f;

            // Die-Hards damper: "Everyone who could flee already fled. The rest are staying."
            // Below 50% of peak → exodus slows asymptotically. Near 10% → nearly zero.
            //
            // The peak is the city's single high-water mark, passed in from the scenario
            // singleton (this system no longer keeps a second one). Its measure is read as
            // "recorded at all", not "recorded on my counter", and that is a deliberate, bounded
            // choice: a mark left by a previous build is a real maximum this same city reached
            // on a counter that read slightly higher, so using it makes the ratio smaller and
            // the damper STRONGER. The failure direction is towards holding people in the city,
            // never towards emptying it faster, which is the only direction that can be accepted
            // silently. Refusing it instead would leave a mid-exodus save undamped or frozen.
            if (peakPopulation.HasValue && peakPopulation.Value > 0)
            {
                float popRatio = (float)totalPopulation / peakPopulation.Value;
                float damper = math.smoothstep(0.1f, 0.5f, popRatio);
                ratePercent *= damper;
            }

            // Calculate number leaving (use double for precision, ceiling to ensure minimum 1 if rate > 0)
            double exactLeaving = totalPopulation * (ratePercent / 100.0);
            int leaving = exactLeaving > 0 ? (int)math.ceil(exactLeaving) : 0;
            if (leaving == 0) return 0f;

            // Apply exodus effects — returns actual citizen count in marked households
            actualLeaving = ApplyExodusEffects(householdSnapshot, totalPopulation, leaving, alreadyMarked);
            if (actualLeaving == 0) return 0f;

            int prevTotal = m_TotalExodusThisSession;
            m_TotalExodusThisSession += actualLeaving;

            // The damper's peak is printed here because it was previously observable in exactly
            // one line — this system's deserialize log — which made "was the damper applied, and
            // against what" unanswerable from a running game's log (Axiom 1).
            Log.Warn($"EXODUS: {actualLeaving} citizens leaving ({ratePercent:F1}%/day, target {leaving}). " +
                $"Population: {totalPopulation} -> {totalPopulation - actualLeaving}. " +
                $"Peak: {peakPopulation.Value} on {peakPopulation.Measure}. " +
                $"Total exodus this session: {m_TotalExodusThisSession}");

            // Social feed notification
            PostExodusNotification(actualLeaving, ratePercent, prevTotal);

            // CIVIC243 FIX: Notify ScenarioStatisticsSystem for CitizensLeft counter
            EventBus?.SafePublish(new CitizensLeftEvent(actualLeaving), "ExodusSystem");
            return ratePercent;
        }

        /// <summary>
        /// Apply exodus effects by marking households with MovingAway component.
        /// Uses vanilla CS2 mechanism — HouseholdMoveAwaySystem will process them.
        ///
        /// NOTE: We target HOUSEHOLDS, not individual citizens.
        /// Each household has ~2-4 citizens on average.
        /// </summary>
        private int ApplyExodusEffects(
            ResidentHouseholdSnapshot snapshot,
            int aliveCitizensInSelection,
            int targetCitizenCount,
            NativeParallelHashSet<Entity> alreadyMarked)
        {
            int householdCount = snapshot.EligibleHouseholds.Length;
            if (householdCount == 0)
            {
                Log.Debug("No eligible households for exodus");
                return 0;
            }

            // Household average over ONE population measurement: numerator is the sum of
            // live citizens across this snapshot's selection, denominator is the length of
            // that same selection. A citywide count over a sampled denominator would inflate
            // the average and under-evict; that pairing is what this ratio must never be.
            int avgCitizensPerHousehold = math.max(1, aliveCitizensInSelection / math.max(1, householdCount));
            int targetHouseholds = math.max(1, targetCitizenCount / avgCitizensPerHousehold);
            targetHouseholds = math.min(targetHouseholds, householdCount);

            var indices = new NativeArray<int>(householdCount, Allocator.Temp);
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;

            // Fisher-Yates shuffle first N elements for random selection
            for (int i = 0; i < targetHouseholds && i < indices.Length - 1; i++)
            {
                int j = m_Random.NextInt(i, indices.Length);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            // Create ECB and mark households for exodus
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            int actualCitizens = 0;
            m_HomelessLookup.Update(this);

            int markedHouseholds = 0;
            for (int i = 0; i < indices.Length && markedHouseholds < targetHouseholds; i++)
            {
                int snapshotIndex = indices[i];
                Entity household = snapshot.EligibleHouseholds[snapshotIndex];

                // Sheltered (homeless/refugee) households are not exodus candidates —
                // marking them MovingAway would evict them from the park shelter.
                if (m_HomelessLookup.HasComponent(household))
                    continue;

                if (!alreadyMarked.Add(household))
                    continue;

                if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }
                markedHouseholds++;
                actualCitizens += snapshot.LiveCitizensPerHousehold[snapshotIndex];

                // Mark for exodus via vanilla mechanism
                ecb.AddComponent(household, new MovingAway
                {
                    m_Reason = MoveAwayReason.NotHappy
                });
            }

            if (indices.IsCreated) indices.Dispose();

            if (Log.IsDebugEnabled) Log.Debug($"Marked {markedHouseholds} households (~{actualCitizens} citizens) for exodus via MovingAway");

            return actualCitizens;
        }

        /// <summary>
        /// Phase 16 "Batalion Monako" — the targeted Wealthy capital-flight pass. Marks specifically
        /// the swayed wealthy households <c>MovingAway</c> (vanilla <c>HouseholdMoveAwaySystem</c>
        /// emigrates them), summing the wallets that walk out. A SEPARATE stream from the citywide
        /// kinetic+psy exodus (Decision 1): the rate applies to the wealthy stratum directly (no
        /// city-share factor), targeting is per-household via the psy sidecar, and the pass bypasses
        /// the <c>ExodusRateRegistry</c> multipliers + die-hard damper (gate G6). Returns the count of
        /// citizens in the marked wealthy households (folded into the day's exodus total, gate G7).
        /// Reads only <c>CachedStratum</c> + <c>InfectionLevel</c> (both non-transient — CIVIC266 OK).
        /// </summary>
        [CompletesDependency("ProcessWealthyFlight: main-thread snapshot of HouseholdPsyState sidecars to target swayed-wealthy households for the Monaco flight pass; one snapshot per frame, created lazily and reused across catch-up days")]
        private int ProcessWealthyFlight(
            NativeParallelHashSet<Entity> alreadyMarked,
            in ResidentHouseholdSnapshot householdSnapshot,
            ref NativeParallelHashMap<Entity, int> liveByHousehold,
            ref NativeArray<HouseholdPsyState> psySnapshot,
            ref NativeArray<PsyHouseholdLink> linkSnapshot)
        {
            // Wealthy posture from Core (Axiom 5 — no Domains.Cognitive import). Empty reader
            // (pre-first-tick) or no Wealthy entry → no flight (fail-closed, no phantom bleed).
            var coverage = m_CoverageReader?.GetCoverage();
            if (coverage == null || coverage.Count == 0)
                return 0;

            float wealthyInfection = 0f;
            float wealthyCoverageFraction = 0f;
            float wealthyHouseholds = 0f;
            bool hasWealthy = false;
            for (int i = 0; i < coverage.Count; i++)
            {
                var stratum = coverage[i];
                if (stratum.Stratum == SocialStratum.Wealthy)
                {
                    wealthyInfection = stratum.Infection;
                    wealthyCoverageFraction = stratum.CoverageFraction;
                    wealthyHouseholds = stratum.Households;
                    hasWealthy = true;
                    break;
                }
            }
            if (!hasWealthy || wealthyHouseholds <= 0f)
                return 0;

            // Rate decision delegated to Core (Axiom 5), mirroring the poor psy rule.
            float ratePercent = StabilityMath.WealthyPsyFlightRatePercentPerDay(wealthyInfection, wealthyCoverageFraction);
            if (ratePercent <= 0f)
                return 0;

            int familiesTarget = (int)math.ceil(wealthyHouseholds * (ratePercent / 100f));
            if (familiesTarget <= 0)
                return 0;

            float threshold = BalanceConfig.Current.Attention.WealthyFlightInfectionThreshold;

            // PERF-LOCK: one psy-sidecar snapshot per frame, created lazily after the
            // cheap early-outs and reused across every catch-up day — a multi-day catch-up
            // must not multiply the full-array copy + MHR sync point. Owned (and disposed)
            // by the pending-days loop in OnUpdateImpl.
            if (!psySnapshot.IsCreated)
            {
                // Both arrays come from the SAME query with no structural change in between —
                // index i refers to the same sidecar entity in both.
                psySnapshot = m_HouseholdPsyStateQuery.ToComponentDataArray<HouseholdPsyState>(Allocator.Temp);
                linkSnapshot = m_HouseholdPsyStateQuery.ToComponentDataArray<PsyHouseholdLink>(Allocator.Temp);
            }

            var candidates = new NativeList<MonacoCandidate>(math.max(1, familiesTarget), Allocator.Temp);
            try
            {
                for (int i = 0; i < psySnapshot.Length; i++)
                {
                    var psy = psySnapshot[i];
                    if (psy.CachedStratum != SocialStratum.Wealthy || psy.InfectionLevel < threshold)
                        continue;
                    candidates.Add(new MonacoCandidate(psy.InfectionLevel, linkSnapshot[i].GetHouseholdEntity()));
                }

                if (candidates.Length == 0)
                    return 0;

                // Most-swayed flee first — deterministic, legible, burns no RNG (gate G5).
                candidates.AsArray().Sort(new MonacoInfectionComparer());

                // Residency filter AND the citizen count in one lookup, both from the owner's
                // published selection. The psy sidecars cover EVERY household (the MHR/PILS
                // lifecycle queries, like our snapshot query, filter nothing beyond Deleted), so
                // without this membership check the flight pass would mark tourist/commuter/temp
                // households MovingAway — the non-resident cohorts the citywide pass excludes by
                // construction — and inflate the persisted Monaco counters and TotalExodus with them.
                //
                // The count comes from the same entry rather than from a HouseholdCitizen buffer walk
                // that used to sit in the marking loop below. That walk was a second measure of the
                // same thing and it did not even agree with the first: the buffer holds every member
                // of the household, while the owner counts the LIVING ones and drops households with
                // none. Whenever a family carried a dead member the two answers differed, inside the
                // system whose whole output is "how many citizens left today".
                //
                // Main-thread read is safe: the published selection has no in-flight writers after
                // publish, and the SelectionReady gate in ProcessDailyExodus has already passed.
                if (!liveByHousehold.IsCreated)
                {
                    // PERF-LOCK: one map per frame, built after the cheap early-outs and reused by
                    // every catch-up day — the same contract as the psy snapshot above. Rebuilding it
                    // per day would walk the whole selection once per pending day.
                    var eligible = householdSnapshot.EligibleHouseholds;
                    var liveCounts = householdSnapshot.LiveCitizensPerHousehold;
                    liveByHousehold = new NativeParallelHashMap<Entity, int>(
                        math.max(1, eligible.Length), Allocator.Temp);
                    for (int i = 0; i < eligible.Length; i++)
                        liveByHousehold.TryAdd(eligible[i], liveCounts[i]);
                }

                EntityCommandBuffer ecb = default;
                bool hasEcb = false;
                int markedFamilies = 0;
                int markedCitizens = 0;
                long capitalThisPass = 0L;

                for (int i = 0; i < candidates.Length && markedFamilies < familiesTarget; i++)
                {
                    Entity household = candidates[i].Household;

                    // Recycled-slot guard: the sidecar's Index+Version may point at a dead/reused
                    // slot. HasComponent version-matches, so a recycled slot (new version) reads false.
                    if (!m_HouseholdLookup.HasComponent(household))
                        continue;
                    // Non-resident cohorts (tourist/commuter/temp) are not flight candidates —
                    // only members of the Population model's resident eligibility set may emigrate.
                    // A miss here is the residency filter; a hit also hands over the living-citizen
                    // count this household contributes when it leaves.
                    if (!liveByHousehold.TryGetValue(household, out int liveCitizens))
                        continue;
                    // Sheltered (homeless/refugee) households are not flight candidates — marking them
                    // MovingAway would evict them from the park shelter.
                    if (m_HomelessLookup.HasComponent(household))
                        continue;
                    // Already emigrating (vanilla natural churn) — don't double-mark/-count.
                    if (m_MovingAwayLookup.HasComponent(household))
                        continue;
                    if (!alreadyMarked.Add(household))
                        continue;

                    long wealth = 0L;
                    if (m_ResourcesBufferLookup.TryGetBuffer(household, out var resources))
                        wealth = math.max(0, EconomyUtils.GetHouseholdTotalWealth(m_HouseholdLookup[household], resources));
                    capitalThisPass += wealth;

                    markedCitizens += liveCitizens;

                    if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }
                    ecb.AddComponent(household, new MovingAway { m_Reason = MoveAwayReason.NotHappy });
                    markedFamilies++;
                }

                if (markedFamilies == 0)
                    return 0;

                int prevFamilies = m_MonacoFamiliesFled;
                // Clamp to the same bounds ExodusCodec persists (a save read caps them anyway) — also
                // silences the overflow analyzer on these cumulative counters.
                m_MonacoFamiliesFled = (int)math.min(m_MonacoFamiliesFled + (long)markedFamilies, MonacoFamiliesCap);
                m_MonacoCapitalFled = math.min(m_MonacoCapitalFled + capitalThisPass, MonacoCapitalCap);
                // Marked wealthy citizens left the city — count them into the shared exodus stats (G7).
                m_TotalExodusThisSession += markedCitizens;

                Log.Info($"MONACO: {markedFamilies} families fled with ${capitalThisPass:N0} " +
                    $"(rate {ratePercent:F2}%/day, wealthyInf {wealthyInfection:F2}, cov {wealthyCoverageFraction:F2}; " +
                    $"total {m_MonacoFamiliesFled} families / ${m_MonacoCapitalFled:N0})");

                EventBus?.SafePublish(new CitizensLeftEvent(markedCitizens), "ExodusSystem");
                PostMonacoNotification(prevFamilies);
                return markedCitizens;
            }
            finally
            {
                if (candidates.IsCreated) candidates.Dispose();
            }
        }

        /// <summary>
        /// Post the "Batalion Monako" social-feed satire on the FIRST flight and on each cumulative
        /// family-count milestone crossing (NOT daily — feed-spam guard). Args: {0} = total families
        /// fled, {1} = total capital fled (formatted).
        /// </summary>
        private void PostMonacoNotification(int prevFamilies)
        {
            if (EventBus == null)
                return;

            int interval = BalanceConfig.Current.Attention.MonacoMilestoneFamilies;
            bool firstFlight = prevFamilies == 0 && m_MonacoFamiliesFled > 0;
            bool milestone = false;
            if (interval > 0)
                milestone = prevFamilies / interval != m_MonacoFamiliesFled / interval;
            if (!firstFlight && !milestone)
                return;

            EventBus.SafePublish(new NarrativeTriggerEvent(
                NarrativeTrigger.ExodusMonaco.ToKey(),
#pragma warning disable CIVIC050 // returned/stored in event, not per-frame (first flight + milestones only)
                new Dictionary<string, string>
#pragma warning restore CIVIC050
                {
                    ["arg0"] = m_MonacoFamiliesFled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["arg1"] = m_MonacoCapitalFled.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                }));
        }

        /// <summary>
        /// A swayed-wealthy flight candidate: the reconstructed vanilla household entity and its
        /// enemy-sway level. Ordered by <see cref="MonacoInfectionComparer"/> (most-swayed first).
        /// </summary>
        private readonly struct MonacoCandidate
        {
            public readonly float Infection;
            public readonly Entity Household;

            public MonacoCandidate(float infection, Entity household)
            {
                Infection = infection;
                Household = household;
            }
        }

        /// <summary>Orders <see cref="MonacoCandidate"/> by infection descending (most-swayed flee first).</summary>
        private readonly struct MonacoInfectionComparer : IComparer<MonacoCandidate>
        {
            public int Compare(MonacoCandidate a, MonacoCandidate b) => b.Infection.CompareTo(a.Infection);
        }

        /// <summary>
        /// Post social feed notification about exodus.
        /// </summary>
        private void PostExodusNotification(int leaving, float rate, int prevTotal)
        {
            if (EventBus == null)
            {
                Log.Warn("EventBus not ready - exodus notification suppressed");
                return;
            }

            var shockState = m_WorldShockSystem.GetCurrentState();

#pragma warning disable CIVIC102 // Only GlobalShock/Concern trigger exodus narratives
            if (shockState.CurrentTier == AidTier.GlobalShock)
#pragma warning restore CIVIC102
            {
                EventBus.SafePublish(new NarrativeTriggerEvent(
                    NarrativeTrigger.ExodusMass.ToKey(),
#pragma warning disable CIVIC050 // returned/stored in event, not per-frame
                    new Dictionary<string, string>
#pragma warning restore CIVIC050
                    {
                        ["arg0"] = leaving.ToString(),
                        ["arg1"] = $"{rate:F1}"
                    }
                ));
            }
            else if (shockState.CurrentTier == AidTier.Headlines)
            {
                EventBus.SafePublish(new NarrativeTriggerEvent(
                    NarrativeTrigger.ExodusModerate.ToKey(),
#pragma warning disable CIVIC050 // returned/stored in event, not per-frame
                    new Dictionary<string, string>
#pragma warning restore CIVIC050
                    {
                        ["arg0"] = leaving.ToString(),
                        ["arg1"] = $"{rate:F1}"
                    }
                ));
            }

            // Occasional IT brain drain message
            var attnCfg = BalanceConfig.Current.Attention;
            if (attnCfg.ExodusMilestoneInterval > 0 &&
                m_TotalExodusThisSession > attnCfg.ExodusMilestoneMinThreshold &&
                prevTotal / attnCfg.ExodusMilestoneInterval != m_TotalExodusThisSession / attnCfg.ExodusMilestoneInterval)
            {
#pragma warning disable CIVIC242 // Multi-publisher by design — each system publishes distinct NarrativeTrigger keys
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ExodusBraindrain.ToKey()));
#pragma warning restore CIVIC242
            }
        }

        /// <summary>
        /// Data-only reset of every local/persisted field to its default. Single source of the field
        /// list — shared by <see cref="ResetState"/> (new game) and ResetToBootDefaults (corrupt /
        /// version-mismatch mid-Deserialize), so a newly-added field can never drift between the two
        /// legs again. No RNG seed, no singleton invalidate/publish here — those differ per leg.
        /// </summary>
        private void ResetLocalFieldsToDefaults()
        {
            m_TotalExodusThisSession = 0;
            m_BaseExodusRatePercentPerDay = 0f;
            m_EffectiveExodusRatePercentPerDay = 0f;
            m_KineticExodusRatePercentPerDay = 0f;
            m_PsyExodusRatePercentPerDay = 0f;
            m_MonacoFamiliesFled = 0;
            m_MonacoCapitalFled = 0L;
            // A2 FIX 2a: m_RateOverride removed — ScenarioSingleton resets on new game.
            // The damper peak is gone from this list too: it lives on the scenario state now, so
            // this system's reset paths can no longer zero one of the city's two peaks while
            // leaving the other standing.
            m_LastProcessedAct = Act.PreWar; // FIX S5-03
            m_DayDedup.Reset();
            m_ResidentHouseholdObserverVersion = 0;
        }

        /// <summary>
        /// Reset all serializable state to defaults (IResettable implementation).
        /// </summary>
        public void ResetState()
        {
            ResetLocalFieldsToDefaults();
            m_Random = new Unity.Mathematics.Random((uint)World.GetHashCode() | 1u); // seed must be non-zero
            m_Singleton.Invalidate();
            UpdateSingleton();
        }

#if DEBUG
        public bool DebugIsExodusActive => m_EffectiveExodusRatePercentPerDay > 0f;

        public void DebugSetExodusActive(bool active, string source)
        {
            if (active)
            {
                float rate = math.max(m_EffectiveExodusRatePercentPerDay, math.max(m_BaseExodusRatePercentPerDay, 1f));
                m_BaseExodusRatePercentPerDay = rate;
                m_EffectiveExodusRatePercentPerDay = rate;
            }
            else
            {
                m_BaseExodusRatePercentPerDay = 0f;
                m_EffectiveExodusRatePercentPerDay = 0f;
            }
            UpdateSingleton();
            Log.Info($"[DEBUG] {source}: exodus active = {active}");
        }

        public void DebugResetExodus(string source)
        {
            ResetState();
            Log.Info($"[DEBUG] {source}: exodus reset");
        }
#endif
    }
}
