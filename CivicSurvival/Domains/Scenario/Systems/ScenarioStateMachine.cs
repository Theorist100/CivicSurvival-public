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
            SubscribeRequired<WarStartRequestEvent>(OnWarStartRequest);
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
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<IntroCompleteEvent>(OnIntroComplete);
            UnsubscribeSafe<WarStartRequestEvent>(OnWarStartRequest);
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

            // Fallback only: detection normally runs in ValidateAfterLoad (deterministic
            // post-load point, before gameplay is interactable and before a save is possible).
            // This fires solely if ValidateAfterLoad did not run (e.g. PostLoadValidation
            // fail-safe path), so the type still gets detected rather than staying None.
            if (!m_Initialized)
            {
                m_Initialized = true;
                DetectScenarioType();
                Log.Info(" Initialized (fallback — ValidateAfterLoad did not run)");
            }

            DrainPendingRuntimeTransitions();

            // Track peak population for victory condition
            int currentPop = this.GetCitizenCount();
            if (currentPop > m_State.PeakPopulation)
            {
                m_State.PeakPopulation = currentPop;
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
        /// Detect ScenarioType based on current population. Called once from ValidateAfterLoad
        /// (deterministic post-load point, population already deserialized) for new games, or
        /// from the first-tick fallback if ValidateAfterLoad did not run. Loaded games with a
        /// detected type re-announce via the OriginalPopulation guard instead of reclassifying.
        /// </summary>
        private void DetectScenarioType()
        {
            // Skip detection if already set by deserialization (loaded game)
            // New games start with default OriginalPopulation = 0
            if (m_State.OriginalPopulation > 0)
            {
                Log.Info($"[ScenarioStateMachine] Loaded game - keeping ScenarioType.{m_State.Type}");

                // R4-T2-04 FIX: Re-publish event after load so dependent systems can re-initialize.
                // All subscribers are idempotent (ActivatePreWar has m_Active guard, etc.).
                // Without this, a system that reset (version mismatch, exception) would never recover.
                // Singleton already written (loaded game restores it before first OnUpdate),
                // so announce-only — publish-after-commit holds.
                AnnounceScenarioType(m_State.Type, m_State.OriginalPopulation);
                return;
            }

            int population = this.GetCitizenCount();
            m_State.OriginalPopulation = population;

            var config = BalanceConfig.Current.Scenario;
            ScenarioType detectedType;
            Act startingAct;

            // R5-F1 FIX: All scenario types start in PreWar so StartWar() can properly
            // transition to Crisis and publish WarStartedEvent. Previously Town/City set
            // startingAct=Crisis directly, which caused StartWar() guard to block →
            // WarStartedEvent never published → WaveScheduler, GameTimeSystem,
            // CrisisActCoordinator, CognitiveStateSystem, TelemarathonSystem uninitialized.
            // Shares the single population-bucketing rule with Attention's exodus
            // multiplier and Refugees' spawn rate via StabilityMath.ClassifyPopulationTier,
            // so the Village/Town/City thresholds cannot drift between consumers. The
            // tier→ScenarioType map is local because the result type is domain-specific.
            switch (StabilityMath.ClassifyPopulationTier(population))
            {
                case PopulationTier.Village:
                    detectedType = ScenarioType.Village;
                    break;
                case PopulationTier.Town:
                    detectedType = ScenarioType.Town;
                    break;
                default:
                    detectedType = ScenarioType.City;
                    break;
            }
            startingAct = Act.PreWar;

            m_State.Type = detectedType;
            m_State.CurrentAct = startingAct;

            Log.Info($"[ScenarioStateMachine] Detected ScenarioType.{detectedType} (pop={population}, thresholds: Village<{config.VillageMaxPop}, Town<{config.TownMaxPop})");
            Log.Info($"[ScenarioStateMachine] Starting Act: {startingAct}");

            // publish-after-commit: fix both projections (managed m_State.Type, already
            // set above, + ECS singleton) BEFORE announcing, so a synchronous subscriber
            // reading ScenarioSingleton sees the committed type, not the default.
            CommitScenarioType(detectedType, population);
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
            //    14 lines below; PeakPopulation is still 0 this tick and that later call
            //    overwrites it with the actual peak (same frame, same writer — no skew).
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

        private void OnIntroComplete(IntroCompleteEvent evt)
        {
            ForceNextUpdate();
            Log.Info(" Intro complete - starting war");
            StartWar();
        }

        private void OnWarStartRequest(WarStartRequestEvent evt)
        {
            Log.Info(" War start requested (OminousSigns) - starting war");
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
                    && (waveState.CurrentPhase == GamePhase.Attack || waveState.CurrentPhase == GamePhase.Alert))
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
            int population = this.GetCitizenCount();

            Log.Info($" War started! (milestone={milestone}, pop={population})");

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
        public int PeakPopulation => m_State.PeakPopulation;
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
