using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Interfaces.Domain.Scenario;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;
using CivicSurvival.Localization;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Scenario State Machine - manages act transitions and core state.
    /// Owns ScenarioState (serialized). Publishes ScenarioSingleton.
    ///
    /// Responsibilities:
    /// - Act transitions (PreWar → Shock → Exodus/Adaptation → Routine)
    /// - War start/stop lifecycle
    /// - Peak population tracking
    /// - State queries for other systems
    ///
    /// Does NOT handle:
    /// - UI bindings (ScenarioUISystem)
    /// - Statistics aggregation (ScenarioStatisticsSystem)
    /// - Milestone modals (ScenarioMilestonesSystem)
    ///
    /// PERF: Throttled to 1Hz — peak population tracking doesn't need 60fps.
    /// </summary>
    // FIX S6-01: ScenarioDomain and EconomyDomain order this producer before
    // CrisisEconomicsSystem so economy reads fresh TaxMultiplier.
    [SingletonOwner(typeof(ScenarioSingleton))]
    [SingletonOwner(typeof(CurrentActSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    [ActTransitionProducer("Single source of truth for Act transitions; owns CurrentActSingleton + ActEpochClock + ActChangedEvent emission.")]
    public partial class ScenarioStateMachine : ThrottledSystemBase, IScenarioModalReader, IScenarioModalMutator, ICivicSingletonOwner
    {
        private static readonly LogContext Log = new("ScenarioStateMachine");
        private const int MAX_MAJOR_STAT_COUNT = 10_000_000;
        private const int MAX_MINOR_STAT_COUNT = 100_000;
        private const int ROUTINE_MIN_POST_CRISIS_DAYS = 14;

        /// <summary>
        /// How far above the exodus entry threshold the remaining share has to climb before
        /// the city leaves the Exodus act. Entry and exit on the exact same line would let a
        /// city that sits on it flip acts every game day; the margin turns the line into a
        /// band. 1.25 means "a quarter more of the baseline than the act required".
        /// </summary>
        private const float EXODUS_RECOVERY_MARGIN = 1.25f;

        // PERF: 1Hz throttle — peak population tracking doesn't need 60fps
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        private bool m_Initialized;
        [System.NonSerialized] private bool m_HasSaveData; // true after successful Deserialize, false after SetDefaults
        private ScenarioState m_State;

        // Narrative milestone shown flags (not serialized — chirper repeat on load is acceptable)
        private bool m_MilestoneWeekShown;
        private bool m_MilestoneMonthShown;
        private bool m_MilestoneQuarterShown;

        // Track when post-Crisis act started (war day) — prevents zero-duration Adaptation
        private int m_PostCrisisActStartDay;

        // Day-boundary confirmation for the Exodus → Adaptation return: the game day on which
        // the recovered share was first seen, and whether an observation is pending. Persisted
        // for the same reason as the entry latch in CrisisActCoordinator — a save/load inside
        // the window must not restart the wait.
        private int m_ExodusRecoverySinceDay;
        private bool m_HasExodusRecoverySince;

        // Population source for every remaining-population verdict this system makes (scenario
        // classification, peak, Crisis baseline capture, Exodus recovery). One owner of the
        // measure for the whole mod; the producer registers the service in its OnCreate, i.e.
        // before any OnStartRunning, so the contract is mandatory-present and its refusal — not
        // a defensive resolve — is what withholds a verdict.
        [System.NonSerialized] private ICityPopulationReader m_CityPopulationReader = null!;

        /// <summary>
        /// The counter every measurement this system takes and stores is on. Stamped into each
        /// <see cref="RecordedPopulation"/> so a save can say which counter produced its numbers
        /// and a consumer can refuse the ones it cannot divide by. Changing the source of
        /// <see cref="TryGetResidentPopulation"/> means changing this constant with it —
        /// that pairing is the whole migration mechanism.
        /// </summary>
        private const PopulationMeasure CityMeasure = PopulationMeasure.VanillaCityRecord;

        // One-shot so the per-second classification retry does not fill the log while the
        // city streams in. Reset with the rest of the transient state.
        [System.NonSerialized] private bool m_ScenarioTypeDeferralLogged;

        // ECS singleton entity for direct ECS access (hosts both ScenarioSingleton and CurrentActSingleton)
        [System.NonSerialized] private CivicSingletonHandle<ScenarioSingleton> m_Singleton;
        private ComponentLookup<ScenarioSingleton> m_SingletonLookup;
        private ComponentLookup<CurrentActSingleton> m_CurrentActLookup;

        // H21: Wave-active check for Routine transition deferral
        private EntityQuery m_WaveStateQuery;
        // War-start telemetry: achieved city milestone at the moment war begins (the trigger point)
        private EntityQuery m_MilestoneQuery;
        // H21: Retry flag — set when Routine transition is deferred due to active wave
        // [NonSerialized]: ephemeral — on load, next midnight re-evaluates conditions naturally
        [System.NonSerialized] private bool m_RoutineTransitionDeferred;
        [System.NonSerialized] private bool m_PendingStartWar;
        [System.NonSerialized] private bool m_HasPendingActTransition;
        [System.NonSerialized] private Act m_PendingActTransition;

        // C-5 ActEpoch root fix: sole writer of the single managed generation clock
        // (registered in Mod.OnLoad, process-lifetime). Advances on a real act
        // transition and on every load (load = epoch boundary).
        [System.NonSerialized] private ActEpochClock m_actEpochClock = null!;
        [System.NonSerialized] private ThreatGenerationClock m_threatGenerationClock = null!;

        /// <summary>
        /// Resolve the process-lifetime clock once (idempotent). Called from OnCreate
        /// and re-checked in load hooks so the ref survives a fresh-world load (the
        /// ServiceRegistry instance itself survives a same-session load). Never called
        /// from OnUpdate — CIVIC018-safe.
        /// </summary>
        private void EnsureEpochClock() =>
            m_actEpochClock ??= ServiceRegistry.IsInitialized
                ? ServiceRegistry.Instance.Require<ActEpochClock>()
                : null!;

        private void EnsureThreatGenerationClock() =>
            m_threatGenerationClock ??= ServiceRegistry.IsInitialized
                ? ServiceRegistry.Instance.Require<ThreatGenerationClock>()
                : null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_State = ScenarioState.CreateDefault();

            // Create ECS singleton for direct ECS access (hosts both ScenarioSingleton and CurrentActSingleton)
            m_SingletonLookup = GetComponentLookup<ScenarioSingleton>(false);
            m_CurrentActLookup = GetComponentLookup<CurrentActSingleton>(false);
            m_Singleton = CreateSingletonHandle<ScenarioSingleton>();
            // C1 (W2 G1): create via the shared owner path so it is reused by
            // ICivicSingletonOwner.OnLoadRestore after a same-session save→load
            // (CS2 does not re-run OnCreate; the host entity would otherwise stay dead).
            EnsureSingletonEntity(EntityManager);
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_MilestoneQuery = GetEntityQuery(ComponentType.ReadOnly<Game.City.MilestoneLevel>());
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IScenarioModalReader>(this);
                ServiceRegistry.Instance.Register<IScenarioModalMutator>(this);
            }

            // Resolve the act-generation clock (registered in Mod.OnLoad, so present
            // before this OnCreate). Advanced by this system only.
            // EnsureEpochClock moved to OnStartRunning — registration order safety (CIVIC403)

            SubscribeRequired<IntroCompleteEvent>(OnIntroComplete);
            SubscribeRequired<ActTransitionRequestEvent>(OnActTransitionRequest);
            SubscribeRequired<WarDayChangedEvent>(OnWarDayChanged);
            SubscribeRequired<ModalShownEvent>(OnModalShown);
            SubscribeRequired<ExodusRateOverrideFractionCommand>(OnExodusRateOverrideFraction);
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);

            Log.Info(" Created (state holder registered)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureEpochClock();
            EnsureThreatGenerationClock();
            // Canonical resolve point. TryGetResidentPopulation repeats the ??= because
            // ValidateAfterLoad can run before this hook.
            m_CityPopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<IntroCompleteEvent>(OnIntroComplete);
            UnsubscribeSafe<ActTransitionRequestEvent>(OnActTransitionRequest);
            UnsubscribeSafe<WarDayChangedEvent>(OnWarDayChanged);
            UnsubscribeSafe<ModalShownEvent>(OnModalShown);
            UnsubscribeSafe<ExodusRateOverrideFractionCommand>(OnExodusRateOverrideFraction);
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IScenarioModalReader>(this);
                ServiceRegistry.Instance.Unregister<IScenarioModalMutator>(this);
            }

            // Clean up ECS singleton
            var singletonEntity = m_Singleton.Entity;
            if (singletonEntity != Entity.Null && EntityManager.Exists(singletonEntity))
            {
                EntityManager.DestroyEntity(singletonEntity);
            }
            m_Singleton.Invalidate();

            base.OnDestroy();
        }

        protected override void OnThrottledUpdate()
        {
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);

            // Retry loop for the scenario classification. Detection normally runs in
            // ValidateAfterLoad (deterministic post-load point, before gameplay is
            // interactable and before a save is possible), but it refuses to classify a city
            // whose population has not been published yet, and it also fires here if
            // ValidateAfterLoad never ran (PostLoadValidation fail-safe path). Either way the
            // type is pending, not decided, until this succeeds. Once it has, the classification
            // is still not final: a village keeps being re-measured until the war starts, because
            // growing out of village size is what starts it.
            if (!m_Initialized)
                m_Initialized = DetectScenarioType();
            else
                TryPromoteScenarioType();

            DrainPendingRuntimeTransitions();

            // Peak population — the high-water mark every "share of the city" reading divides by:
            // victory retention, the ghost-town notice, grid stress by city scale. The measure is
            // the owner's, not the plain citizen query: that one counts tourists, commuters and
            // dead-but-not-deleted citizens, so on an empty map the peak rose to six passers-by
            // and a drop to one read as the city dying. A high-water mark is also the worst place
            // for that counter, because it latches the largest transient spike forever. A refusal
            // (no city entity) leaves the peak alone — it must not be mistaken for zero.
            //
            // A peak restored from a save carries the measure it was taken on. It is never
            // re-anchored downwards (that would hand out the retention victory on the spot and
            // switch the ghost-town window off): a mark from the older, wider counter keeps its
            // value and is superseded only once a reading on this measure actually exceeds it.
            // Until then every decision that divides by the peak refuses; displays keep showing
            // the number the city really reached.
            if (TryGetResidentPopulation(out int currentResidents))
            {
                _ = RecordedPopulation.TryRaise(currentResidents, CityMeasure, ref m_State.PeakPopulation);

                // "Anyone has ever lived here", latched. One reading above zero settles it for
                // the rest of the city's life; a later zero means the city died out, which is
                // the distinction the flag exists to make.
                if (currentResidents > 0)
                    m_State.CityEverSettled = true;
            }

            // Crisis baseline, deferred capture: a city that reports nobody at act entry
            // (new game, first ticks after load) leaves the baseline uncaptured and gets it
            // on the first tick residents appear. Without the deferral the remaining-population
            // thresholds read an empty city as "everyone fled" and fire immediately.
            switch (m_State.CurrentAct)
            {
                case Act.Crisis:
                    if (TryGetResidentPopulation(out int residentPopulation))
                        TryCaptureCrisisBaseline(residentPopulation);
                    break;
                case Act.Exodus:
                    CheckExodusRecovery();
                    break;
                default:
                    break;
            }

            // Update thread-safe snapshot for readers
            UpdateStateSnapshot();
        }

        private void UpdateStateSnapshot()
        {
            // Update ECS singleton for direct ECS access
            // Read-modify-write to preserve ExodusRateOverrideFraction (written via OnExodusRateOverrideFraction handler)
            var singletonEntity = EnsureSingletonEntity(EntityManager);
            if (m_SingletonLookup.HasComponent(singletonEntity))
            {
                var current = m_SingletonLookup[singletonEntity];
                current.ScenarioType = m_State.Type;
                if (TryResolveCurrentGameDay(out var gameDay))
                    current.GameDay = gameDay.Value;
                current.WarDay = ResolveCurrentWarDay();
                current.PopulationPeak = m_State.PeakPopulation;
                current.CrisisStartPopulation = m_State.CrisisStartPopulation;
                current.CityEverSettled = m_State.CityEverSettled;
                current.IsWarStarted = m_State.CurrentAct != Act.PreWar;
                current.IsDefeated = m_State.IsDefeated;
                current.ExodusRateOverrideFraction = m_State.ExodusRateOverrideFraction;
                current.ShownModals = m_State.ShownModals;
                current.DonorAidReceived = m_State.DonorAidReceived;
                m_SingletonLookup[singletonEntity] = current;
            }
            if (m_CurrentActLookup.HasComponent(singletonEntity))
            {
                m_CurrentActLookup[singletonEntity] = new CurrentActSingleton { CurrentAct = m_State.CurrentAct };
            }
        }

        /// <summary>
        /// Detect ScenarioType based on the current resident population. Called from
        /// ValidateAfterLoad (deterministic post-load point) and retried from the throttled
        /// tick until it succeeds. A city whose population has not been published yet is not
        /// classified: a zero reading would pin the scenario to Village forever and, worse,
        /// persist OriginalPopulation = 0 — the denominator every later size-scaled verdict
        /// measures against. Returns whether the type is now fixed; false means "ask again
        /// next tick".
        ///
        /// A save that already carries a detected type re-announces through the
        /// OriginalPopulation guard instead of reclassifying; a village that is still growing
        /// is re-measured afterwards by <see cref="TryPromoteScenarioType"/>, which is what
        /// ends the pre-war phase.
        /// </summary>
        private bool DetectScenarioType()
        {
            // Skip detection if the size was already measured (loaded game). The question here
            // is "was this city ever classified", not "can I divide by that number", so it is
            // HasValue and not the measure-matched read: a save from the older counter is a
            // classified city, and re-detecting it would rewind a City back to Village and
            // replay the war entry. The size-scaled verdicts that DO divide by this number ask
            // for the measure and refuse on their own.
            if (m_State.OriginalPopulation.HasValue)
            {
                Log.Info($"[ScenarioStateMachine] Loaded game - keeping ScenarioType.{m_State.Type}");

                // R4-T2-04 FIX: Re-publish event after load so dependent systems can re-initialize.
                // All subscribers are idempotent (ActivatePreWar has m_Active guard, etc.).
                // Without this, a system that reset (version mismatch, exception) would never recover.
                // Singleton already written (loaded game restores it before first OnUpdate),
                // so announce-only — publish-after-commit holds.
                AnnounceScenarioType(m_State.Type, m_State.OriginalPopulation.Value);
                return true;
            }

            if (!TryGetResidentPopulation(out int population) || population <= 0)
            {
                if (!m_ScenarioTypeDeferralLogged)
                {
                    m_ScenarioTypeDeferralLogged = true;
                    Log.Info("[ScenarioStateMachine] Scenario type deferred — nobody lives in the city yet (or there is no city entity to ask); retrying every tick");
                }
                return false;
            }

            m_State.OriginalPopulation = RecordedPopulation.On(CityMeasure, population);
            m_State.CityEverSettled = true;

            var config = BalanceConfig.Current.Scenario;
            ScenarioType detectedType = ClassifyScenarioType(population);

            m_State.Type = detectedType;

            // R5-F1 FIX: A new game starts in PreWar so StartWar() can properly transition to
            // Crisis and publish WarStartedEvent. Previously Town/City set the act to Crisis
            // directly, which caused the StartWar() guard to block → WarStartedEvent never
            // published → WaveScheduler, GameTimeSystem, CrisisActCoordinator,
            // CognitiveStateSystem, TelemarathonSystem uninitialized. A save that reaches
            // classification late (its OriginalPopulation was never captured) keeps the act it
            // was saved in — re-detecting the size must never rewind the story.
            if (!m_HasSaveData)
                m_State.CurrentAct = Act.PreWar;

            Log.Info($"[ScenarioStateMachine] Detected ScenarioType.{detectedType} (residents={population}, thresholds: Village<{config.VillageMaxPop}, Town<{config.TownMaxPop})");
            Log.Info($"[ScenarioStateMachine] Current Act: {m_State.CurrentAct}");

            // publish-after-commit: fix both projections (managed m_State.Type, already
            // set above, + ECS singleton) BEFORE announcing, so a synchronous subscriber
            // reading ScenarioSingleton sees the committed type, not the default.
            CommitScenarioType(detectedType, population);
            return true;
        }

        /// <summary>
        /// Village / Town / City from a resident count. The bucketing itself is the shared
        /// <see cref="StabilityMath.ClassifyPopulationTier"/> rule that Attention's exodus
        /// multiplier and the refugee spawn rate also use, so the thresholds cannot drift
        /// between consumers; only the tier→ScenarioType map is local, because the result type
        /// is domain-specific.
        /// </summary>
        private static ScenarioType ClassifyScenarioType(int population)
        {
            switch (StabilityMath.ClassifyPopulationTier(population))
            {
                case PopulationTier.Village:
                    return ScenarioType.Village;
                case PopulationTier.Town:
                    return ScenarioType.Town;
                default:
                    return ScenarioType.City;
            }
        }

        /// <summary>
        /// A village is a stage of growth, not a war scenario: while the settlement is smaller
        /// than a town, nobody comes for it. Crossing the Village/Town boundary is what makes it
        /// worth a strike, so the classification stays open until the war starts, and the
        /// crossing re-announces the type — the city entry (cold open → first strike →
        /// air-defense quest) then runs for a settlement that grew into a city, instead of being
        /// skipped forever because the map held four people on its first tick.
        ///
        /// Upward only, and only in PreWar: once the war is on, the type and the population it
        /// was fixed at are history rather than an estimate, and a city that empties out does
        /// not become a village again.
        /// </summary>
        private void TryPromoteScenarioType()
        {
            if (m_State.Type != ScenarioType.Village || m_State.CurrentAct != Act.PreWar)
                return;

            if (!TryGetResidentPopulation(out int population) || population <= 0)
                return;

            var promotedType = ClassifyScenarioType(population);
            if (promotedType == ScenarioType.Village)
                return;

            m_State.Type = promotedType;

            // The size the city entered the war at, not the size the map was created at: this
            // is the denominator every size-scaled verdict measures against (the collapse floor
            // in DefeatCheckSystem is a tenth of it), and a baseline of four residents would put
            // that floor at one person for a city of a thousand.
            m_State.OriginalPopulation = RecordedPopulation.On(CityMeasure, population);
            m_State.CityEverSettled = true;

            Log.Info($"[ScenarioStateMachine] Village grew past the town boundary (residents={population}) — reclassified as ScenarioType.{promotedType}; the city war entry takes over");

            CommitScenarioType(promotedType, population);
        }

        /// <summary>
        /// People living in the city right now, from the single owner of that measure
        /// (<see cref="ICityPopulationReader"/>, i.e. the vanilla city record). Every
        /// remaining-population verdict here divides a saved baseline by a live counter, so the
        /// counter has to be the one the rest of the mod uses; the raw citizen query this used
        /// to reach through is not — it counts tourists, commuters and dead-but-not-deleted
        /// citizens.
        ///
        /// <c>false</c> means "cannot answer" — there is no city entity — never "empty city":
        /// a city with nobody in it answers <c>true</c> with zero, and callers that need to
        /// distinguish "died out" from "just created" gate on their own captured baselines.
        /// </summary>
        private bool TryGetResidentPopulation(out int population)
        {
            // Require, not TryGet: the interface is feature-owned without a null object, and
            // CIVIC463 rejects a defensive TryGet here as masking a dependency-order problem.
            // The producer registers the service in its OnCreate, ahead of any consumer update.
            // Repeated here because ValidateAfterLoad reaches this before OnStartRunning.
            m_CityPopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
            return m_CityPopulationReader.TryGetResidentCount(out population);
        }

        /// <summary>
        /// Capture the Crisis baseline and print it at the moment it becomes real. The act
        /// coordinator announces the crisis on WarStartedEvent, which is published before the
        /// act transition that captures this number, so its own start line cannot carry it —
        /// this is the only place the value is known to be the one every threshold will use.
        /// </summary>
        private void TryCaptureCrisisBaseline(int residentPopulation)
        {
            if (m_State.CrisisStartPopulation.IsOn(CityMeasure))
                return;

            // A baseline restored from the older counter is treated as absent and re-taken here.
            // This one is an anchor for a measurement that is still running ("the city as the
            // crisis found it"), not a historical maximum, so re-anchoring writes a number that
            // was really observed — and refusing instead would leave every remaining-population
            // threshold of that save unanswerable for the rest of the act.
            bool rebased = m_State.CrisisStartPopulation.HasValue;
            int previous = m_State.CrisisStartPopulation.Value;
            if (!RecordedPopulation.TryCapture(residentPopulation, CityMeasure, ref m_State.CrisisStartPopulation))
                return;

            if (rebased)
                Log.Info($"[ScenarioStateMachine] Crisis baseline re-taken on the current measure: {m_State.CrisisStartPopulation.Value} (was {previous} on the superseded counter)");
            else
                Log.Info($"[ScenarioStateMachine] Crisis baseline captured: {m_State.CrisisStartPopulation.Value} residents");
        }

        /// <summary>
        /// Leave the Exodus act when the city fills up again. Exodus is not a point of no
        /// return: a city that rebuilds should get its story back, and a verdict reached on a
        /// sampled counter must be revisable by the same measurement that produced it. The
        /// condition mirrors the entry — the same size-scaled share of the same baseline, held
        /// until the game day rolls over — with <see cref="EXODUS_RECOVERY_MARGIN"/> above the
        /// entry line so a city sitting on it cannot flip acts every day.
        /// </summary>
        private void CheckExodusRecovery()
        {
            if (m_State.IsDefeated)
                return;

            // A verdict, so the baseline has to be on the measure this live reading is taken
            // with; an absent or superseded one yields no return and the act stays put.
            if (!TryGetResidentPopulation(out int residentPopulation)
                || !m_State.CrisisStartPopulation.TryGetScaledRemaining(
                        residentPopulation,
                        CityMeasure,
                        BalanceConfig.Current.Scenario.ExodusActThreshold,
                        out float populationRemaining,
                        out float scaledThreshold)
                || populationRemaining <= scaledThreshold * EXODUS_RECOVERY_MARGIN)
            {
                if (m_HasExodusRecoverySince)
                {
                    m_HasExodusRecoverySince = false;
                    Log.Info($" Exodus recovery lapsed before the day boundary (observed on day {m_ExodusRecoverySinceDay}) — confirmation restarts");
                }
                return;
            }

            if (!TryResolveCurrentGameDay(out var gameDay))
                return;

            if (!m_HasExodusRecoverySince)
            {
                m_HasExodusRecoverySince = true;
                m_ExodusRecoverySinceDay = gameDay.Value;
                Log.Info($" Exodus recovery observed on day {gameDay.Value}: {populationRemaining:P1} remaining (return band {scaledThreshold * EXODUS_RECOVERY_MARGIN:P1}) — holding until the day boundary");
                return;
            }

            if (gameDay.Value < m_ExodusRecoverySinceDay)
            {
                // Day counter moved backwards (an older save loaded onto this instance) —
                // re-arm on the current day rather than confirm on a stale stamp.
                m_ExodusRecoverySinceDay = gameDay.Value;
                return;
            }

            if (gameDay.Value == m_ExodusRecoverySinceDay)
                return;

            Log.Info($"[ScenarioStateMachine] Exodus recovery confirmed across the day boundary: {populationRemaining:P1} remaining, held from day {m_ExodusRecoverySinceDay} to day {gameDay.Value} — returning to Adaptation");
            m_HasExodusRecoverySince = false;
            TransitionToAct(Act.Adaptation);
        }

        /// <summary>
        /// Commit the detected scenario type to both projections, then announce it.
        /// Caller has already set m_State.Type = detectedType. This writes the ECS
        /// singleton (via UpdateStateSnapshot) and only then publishes
        /// ScenarioTypeDetectedEvent — enforcing publish-after-commit structurally so a
        /// synchronous subscriber that reads ScenarioSingleton observes the fixed type.
        /// </summary>
        private void CommitScenarioType(ScenarioType detectedType, int population)
        {
            // 1. COMMIT both projections. The full snapshot is valid at this point:
            //    lookups were refreshed at the top of OnThrottledUpdate; GameDay/WarDay
            //    use the same defensive resolvers as the regular UpdateStateSnapshot call
            //    14 lines below; PeakPopulation is still unrecorded this tick and that later
            //    call overwrites it with the actual peak (same frame, same writer — no skew).
            UpdateStateSnapshot();

            // 2. ANNOUNCE.
            AnnounceScenarioType(detectedType, population);
        }

        /// <summary>
        /// Single publication point for ScenarioTypeDetectedEvent. Type must already be
        /// committed to both projections before calling.
        /// </summary>
        private void AnnounceScenarioType(ScenarioType type, int population) =>
            EventBus?.SafePublish(new ScenarioTypeDetectedEvent(type, population), "ScenarioStateMachine");

        // ===== Event Handlers =====

        /// <summary>
        /// The single entry into the war, for every settlement size. A city that was born big
        /// gets here through its cold open; a village gets here through the same cold open,
        /// played the moment it grows past the town boundary.
        /// </summary>
        private void OnIntroComplete(IntroCompleteEvent evt)
        {
            ForceNextUpdate();
            Log.Info(" Intro complete - starting war");
            StartWar();
        }

        private void OnActTransitionRequest(ActTransitionRequestEvent evt)
        {
            TransitionToAct(evt.NewAct);
        }

        private void OnWarDayChanged(WarDayChangedEvent evt)
        {
            int previousDay = m_State.WarDay;
            m_State.WarDay = evt.WarDay;

            if (evt.WarDay != previousDay)
            {
                if (evt.WarDay < previousDay)
                    ResetFutureMilestones(evt.WarDay);

                Log.Info($"[ScenarioStateMachine] War Day {evt.WarDay}");
                HandleDayMilestones(evt.WarDay);
            }

            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            UpdateStateSnapshot();
        }

        private void OnModalShown(ModalShownEvent evt)
        {
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            m_State.MarkModalShown(evt.Flag);
            UpdateStateSnapshot();
        }

        /// <summary>
        /// Handle ExodusRateOverrideFractionCommand from CrisisActCoordinator.
        /// SSM is sole writer of ScenarioSingleton — writes ExodusRateOverrideFraction here.
        /// </summary>
        private void OnExodusRateOverrideFraction(ExodusRateOverrideFractionCommand cmd)
        {
            m_SingletonLookup.Update(this);
            var singletonEntity = EnsureSingletonEntity(EntityManager);
            if (m_SingletonLookup.HasComponent(singletonEntity))
            {
                var current = m_SingletonLookup[singletonEntity];
                m_State.ExodusRateOverrideFraction = cmd.RateFraction < 0f ? 0f : cmd.RateFraction;
                current.ExodusRateOverrideFraction = m_State.ExodusRateOverrideFraction;
                m_SingletonLookup[singletonEntity] = current;
                if (Log.IsDebugEnabled) Log.Debug($"ExodusRateOverrideFraction = {m_State.ExodusRateOverrideFraction:F4}");
            }
        }

        private const int MILESTONE_WEEK = 7;
        private const int MILESTONE_MONTH = 30;
        private const int MILESTONE_QUARTER = 90;

        private void HandleDayMilestones(int day)
        {
            // FIX W8-M4: >= with shown-flags to handle day-jump (was == which missed skipped days)
            if (!m_MilestoneWeekShown && day >= MILESTONE_WEEK)
            {
                m_MilestoneWeekShown = true;
                Log.Info(" One week of war");
            }

            if (!m_MilestoneMonthShown && day >= MILESTONE_MONTH)
            {
                m_MilestoneMonthShown = true;
                Log.Info(" One month of war");
                EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.Milestone30.ToKey()), "ScenarioStateMachine");
            }

            // Post-crisis acts must have their own pacing. This is a backstop transition;
            // CrisisActCoordinator owns Crisis exit, SSM owns the later Routine transition.
            if (day >= MILESTONE_MONTH
                && m_PostCrisisActStartDay > 0
                && day - m_PostCrisisActStartDay >= ROUTINE_MIN_POST_CRISIS_DAYS
                && !m_State.IsDefeated
                && IsPostCrisisAct(m_State.CurrentAct))
            {
                // H21: Defer Routine transition if wave is active — mirrors CrisisActCoordinator deferral
                if (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState)
                    && (waveState.CurrentPhase == GamePhase.Attack || waveState.CurrentPhase == GamePhase.PsyAttack || waveState.CurrentPhase == GamePhase.Alert))
                {
                    m_RoutineTransitionDeferred = true;
                    Log.Info($" Routine transition deferred — wave {waveState.WaveNumber} active ({waveState.CurrentPhase})");
                }
                else
                {
                    Log.Info($" Transitioning to Routine Act from {m_State.CurrentAct} (day {day} >= {MILESTONE_MONTH}, post-crisis day {day - m_PostCrisisActStartDay})");
                    TransitionToAct(Act.Routine);
                }
            }

            if (!m_MilestoneQuarterShown && day >= MILESTONE_QUARTER)
            {
                m_MilestoneQuarterShown = true;
                Log.Info(" Three months of war");
                EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.Milestone90.ToKey()), "ScenarioStateMachine");
            }
        }

        /// <summary>
        /// Retry deferred Routine transition when wave ends.
        /// Set when HandleDayMilestones defers due to active wave.
        /// Mirrors CrisisActCoordinator.OnWaveEnded pattern.
        /// </summary>
        private void OnWaveEnded(WaveEndedEvent evt)
        {
            if (!m_RoutineTransitionDeferred) return;
            m_RoutineTransitionDeferred = false;
            if (m_State.IsDefeated) return;

            int day = m_State.WarDay;
            if (day >= MILESTONE_MONTH
                && m_PostCrisisActStartDay > 0
                && day - m_PostCrisisActStartDay >= ROUTINE_MIN_POST_CRISIS_DAYS
                && IsPostCrisisAct(m_State.CurrentAct))
            {
                Log.Info($" Retrying deferred Routine transition after wave {evt.WaveNumber} ended (day {day})");
                TransitionToAct(Act.Routine);
            }
            else
            {
                Log.Info($" Deferred Routine transition cancelled — conditions no longer met (day {day}, act {m_State.CurrentAct})");
            }
        }

        // ===== Public API =====

        public void TransitionToAct(Act newAct)
        {
            if (m_State.CurrentAct == newAct) return;
            if (m_State.IsDefeated && newAct == Act.Routine)
            {
                Log.Info($"[ScenarioStateMachine] Ignoring {m_State.CurrentAct} -> Routine transition because scenario is defeated");
                return;
            }
            if (!GameTimeSystem.TryGetGameHours(out var timestamp))
            {
                QueuePendingActTransition(newAct);
                return;
            }

            Act previousAct = m_State.CurrentAct;
            m_State.CurrentAct = newAct;

            // C-5: advance the generation on a REAL act change (past the :382
            // same-act guard) BEFORE publishing ActChangedEvent — a threat spawned
            // before vs after the transition is naturally discriminated by the clock.
            m_actEpochClock?.AdvanceForActTransition();

            if (newAct == Act.Adaptation || newAct == Act.Exodus)
                m_PostCrisisActStartDay = m_State.WarDay;

            // Leaving or re-entering Exodus ends any pending return observation: the window
            // measures one continuous stretch inside one act, never across an act change.
            m_HasExodusRecoverySince = false;

            // Each Crisis act measures against its own baseline: drop the previous one and
            // take it now if the city reports residents, otherwise the throttled tick captures
            // it as soon as they appear.
            if (newAct == Act.Crisis)
            {
                // Invalidation is a named operation of the contract, not a write of zero: the
                // convention this phase removed would come straight back if "forget it" and "it
                // measured nobody" were the same statement.
                m_State.CrisisStartPopulation = RecordedPopulation.Invalidate();
                if (TryGetResidentPopulation(out int residentPopulation))
                    TryCaptureCrisisBaseline(residentPopulation);
            }

            Log.Info($"[ScenarioStateMachine] Act transition: {previousAct} → {newAct}");

            // R3-C-1 FIX: Update ECS singleton immediately so non-throttled readers
            // see the correct act in the same frame (not stale for up to 1s).
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            UpdateStateSnapshot();

            var actChangedEvent = new ActChangedEvent(previousAct, newAct, timestamp);
            EventBus?.SafePublish(actChangedEvent, "ScenarioStateMachine");

            // Herald bulletin when GridWarfare unlocks (Adaptation phase). The Defense
            // Ministry is an official source, so it goes to the NEWS/Herald feed (not
            // CHIPPER) with a content-stable id — same channel the narrative emitters use.
            switch (newAct)
            {
                case Act.Adaptation:
                    const string defenseHandle = "@DefenseMinistry";
                    string gridWarfareTitle = LocalizationManager.Get("CHIRP_GRIDWARFARE_UNLOCKED");
                    EventBus?.SafePublish(new NewsPostEvent(
                        NotificationIdHelper.ContentId(
                            defenseHandle,
                            gridWarfareTitle,
                            string.Empty,
                            Engine.Narrative.NEWS_CONTENT_BUCKET_SECONDS),
                        NewsAuthorRegistry.GetDisplayName(defenseHandle),
                        gridWarfareTitle,
                        string.Empty,
                        SocialMood.Smug,
                        System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        "official"), "ScenarioStateMachine");
                    break;
                case Act.Routine:
                    EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ActRoutine.ToKey()), "ScenarioStateMachine");
                    break;
                default:
                    break;
            }
        }

        public void StartWar()
        {
            if (m_State.CurrentAct != Act.PreWar)
            {
                Log.Warn("StartWar called but war already started");
                return;
            }

            if (!GameTimeSystem.TryGetGameHours(out var warStartTime))
            {
                QueuePendingStartWar();
                return;
            }

            m_State.WarStartTime = warStartTime;

            int milestone = m_MilestoneQuery.TryGetSingleton<Game.City.MilestoneLevel>(out var ml)
                ? ml.m_AchievedMilestone
                : -1;

            // Diagnostic only: this number decides nothing here. It goes to the log line below
            // and to the event field, whose single subscriber records it as telemetry; the
            // baseline every threshold measures against is captured separately by
            // TryCaptureCrisisBaseline. So a refusal must never hold the war back — the
            // transition proceeds and the reading is reported as absent. (The event field
            // itself becomes an absent-able one in the display-vs-decision phase; the record
            // type is not this change's to alter.)
            bool hasPopulation = TryGetResidentPopulation(out int population);

            Log.Info($" War started! (milestone={milestone}, pop={(hasPopulation ? population.ToString() : "n/a")})");

            EventBus?.SafePublish(new WarStartedEvent(milestone, population), "ScenarioStateMachine");
            TransitionToAct(Act.Crisis);
        }

        private void QueuePendingStartWar()
        {
            m_PendingStartWar = true;
            ForceNextUpdate();
            Log.Warn("StartWar deferred: GameTimeSystem unavailable");
        }

        private void QueuePendingActTransition(Act newAct)
        {
            m_PendingActTransition = newAct;
            m_HasPendingActTransition = true;
            ForceNextUpdate();
            Log.Warn($"[ScenarioStateMachine] Act transition to {newAct} deferred: GameTimeSystem unavailable");
        }

        private void DrainPendingRuntimeTransitions()
        {
            if (m_PendingStartWar)
            {
                m_PendingStartWar = false;
                if (m_State.CurrentAct == Act.PreWar)
                    StartWar();
            }

            if (m_HasPendingActTransition)
            {
                var pendingAct = m_PendingActTransition;
                m_HasPendingActTransition = false;
                if (m_State.CurrentAct != pendingAct)
                    TransitionToAct(pendingAct);
            }
        }

        private void ResetFutureMilestones(int warDay)
        {
            if (warDay < MILESTONE_QUARTER) m_MilestoneQuarterShown = false;
            if (warDay < MILESTONE_MONTH) m_MilestoneMonthShown = false;
            if (warDay < MILESTONE_WEEK) m_MilestoneWeekShown = false;
        }

        private static bool IsPostCrisisAct(Act act) => act == Act.Adaptation || act == Act.Exodus;

        private bool TryResolveCurrentGameDay(out GameDayStamp gameDay)
        {
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider != null
                && GameDayStamp.TryCreate(timeProvider.Current.CurrentDay, out gameDay))
            {
                return true;
            }

            gameDay = default;
            return false;
        }

        private int ResolveCurrentWarDay()
        {
            return WarDayStamp.TryCreate(m_State.WarDay, out var warDay)
                ? warDay.Value
                : -1;
        }

        // ===== Statistics Mutators (called by ScenarioStatisticsSystem) =====

        public void RecordWaveDefended(int missilesIntercepted)
        {
            m_State.WavesDefended = SaturatingAdd(m_State.WavesDefended, 1, MAX_MINOR_STAT_COUNT);
            m_State.MissilesIntercepted = SaturatingAdd(m_State.MissilesIntercepted, missilesIntercepted, MAX_MAJOR_STAT_COUNT);
            if (Log.IsDebugEnabled) Log.Debug($"[ScenarioStateMachine] Wave defended (total: {m_State.WavesDefended})");
        }

        /// <summary>Records one successful donor aid package, regardless of package contents.</summary>
        public void RecordDonorAidReceived()
        {
            m_State.DonorAidReceived = SaturatingAdd(m_State.DonorAidReceived, 1, MAX_MINOR_STAT_COUNT);
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            UpdateStateSnapshot();
        }

        public void RecordRefugeesReceived(int count)
        {
            m_State.RefugeesReceived = SaturatingAdd(m_State.RefugeesReceived, count, MAX_MAJOR_STAT_COUNT);
        }

        public void RecordCitizensLeft(int count)
        {
            m_State.CitizensLeft = SaturatingAdd(m_State.CitizensLeft, count, MAX_MAJOR_STAT_COUNT);
        }

        public void RecordBlackoutRecovery()
        {
            m_State.BlackoutRecoveries = SaturatingAdd(m_State.BlackoutRecoveries, 1, MAX_MINOR_STAT_COUNT);
        }

        public void RecordBuildingsDamaged(int count = 1)
        {
            m_State.BuildingsDamaged = SaturatingAdd(m_State.BuildingsDamaged, count, MAX_MAJOR_STAT_COUNT);
        }

        private static int SaturatingAdd(int current, int delta, int max)
        {
            if (delta <= 0) return current;
            long next = (long)current + delta;
            return next >= max ? max : checked((int)next);
        }

        // ===== Public Accessors =====

        public ScenarioState State => m_State;
        public Act CurrentAct => m_State.CurrentAct;
        public int WarDay => m_State.WarDay;
        /// <summary>
        /// The city's high-water mark with the measure it was reached on. Consumers that divide
        /// a live reading by it must ask <see cref="RecordedPopulation.TryGetOn"/>; consumers
        /// that only print it may read the value.
        /// </summary>
        public RecordedPopulation PeakPopulation => m_State.PeakPopulation;

        /// <summary>Whether anyone has ever lived in this city (see <see cref="ScenarioState.CityEverSettled"/>).</summary>
        public bool CityEverSettled => m_State.CityEverSettled;
        public bool IsWarStarted => m_State.CurrentAct != Act.PreWar;
        public bool IsDefeated => m_State.IsDefeated;
        public DefeatCause DefeatCause => m_State.DefeatCause;
        public bool IsDefeatDismissed => m_State.DefeatDismissed;
        public PostVictoryMode PostVictoryMode => m_State.PostVictoryMode;

        public void SetDefeated(DefeatCause cause)
        {
            m_State.IsDefeated = true;
            m_State.DefeatCause = cause;
            m_State.DefeatDismissed = false;
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            UpdateStateSnapshot();
        }

        public void MarkDefeatDismissed()
        {
            m_State.DefeatDismissed = true;
        }

        public void ClearDefeatDismissed()
        {
            m_State.DefeatDismissed = false;
        }

        public void SetPostVictoryMode(PostVictoryMode mode)
        {
            m_State.PostVictoryMode = mode;
            m_SingletonLookup.Update(this);
            m_CurrentActLookup.Update(this);
            UpdateStateSnapshot();
        }
    }
}
