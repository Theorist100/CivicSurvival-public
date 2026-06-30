using Game.City;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Power;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Wave scheduler - decides WHEN to attack.
    ///
    /// Responsibilities:
    /// - Timing logic (calm duration, phase transitions)
    /// - Wave type selection (Harassment vs MassiveStrike)
    /// - Threat count calculation
    /// - Publishes ScheduleWaveCommand for WaveExecutor
    ///
    /// Does NOT:
    /// - Write to WaveStateSingleton (WaveExecutor is Single Writer)
    /// - Handle phase transition side effects
    /// - Manage debriefing or stats
    ///
    /// Pattern: "Writer Notifies" - Scheduler is the brain, Executor is the hands.
    /// </summary>
    // W2-GW-F5 ACCEPTED: EnemyState 1-frame stale on phase-transition frames (~1/5min).
    // Cross-domain RegisterAfter] avoided per Axiom 5. Staleness = negligible pressure delta.
    [ActIndependent]
    public partial class WaveScheduler : CivicSystemBase, IResettable
    {
        private const float MIN_ATTACK_PHASE_EXTENSION = 60f;
        private const float DEFAULT_PHASE_END_TIME = 300f;
        private const float RECOVERY_PHASE_DURATION = 30f;
        private const float DAWN_DUSK_RECHECK_INTERVAL = 30f;
        private const float KW_PER_MW = 1000f; // snapshot NameplateKW (kW) → MW for the readiness gate
        // Throttle for the readiness-gate evaluation (snapshot read + Evaluate). The calm timer ticks
        // every frame; this decision is only SAMPLED — minutes-long calm + dead-band-throttled snapshot
        // make ~0.5s sampling indistinguishable from per-tick. Same idea as OperationalDamageSystem.
        private const int GATE_EVAL_INTERVAL = 30;
        // Cap on how long Calm waits for a dawn/dusk launch window AFTER the calm duration
        // already elapsed. Without it a wave can stall for most of a long in-game day waiting
        // for twilight (the recheck loop was unbounded). Twilight is still preferred when it
        // arrives within the cap; past it, the wave launches off-window so pacing stays bounded.
        private const float MAX_LAUNCH_WINDOW_WAIT = 150f;
        private const int NO_WAVE_NUMBER = 0;
        private const int INTRO_WAVE_NUMBER = 1;
        private const int FIRST_REGULAR_WAVE_NUMBER = 2;

        private static readonly LogContext Log = new("WaveScheduler");

        // Scheduler state
        private GamePhase m_CurrentPhase;
        private float m_TimeInPhase;
        private float m_PhaseEndTime;
        private int m_WaveNumber;
        private WaveRole m_WaveRole;
        private WaveType m_WaveType;
        private int m_ThreatsExpected;
        private bool m_ScenarioStarted;
        private bool m_WarStartedReceived;
        private bool m_IntroAttackFired;
        [System.NonSerialized] private bool m_Initialized; // Not serialized: re-initialized in OnStartRunning
        // CIVIC066 suppressed: m_Random.state IS serialized via GetSaveState().RandomState
        // → WaveScheduler.Serialization.cs writes/reads it. Analyzer can't trace indirection.
#pragma warning disable CIVIC066
        private Random m_Random;
#pragma warning restore CIVIC066

        // Serialization state
        private SchedulerSaveState? m_SavedState;
        private bool m_HasSavedState;

        // Queries
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_DemandPeakQuery;
        // 24h demand-peak ring (Фаза 3) — peak = max over the 24 hourly buckets, computed on read
        // (the singleton stores no cached scalar to avoid a second source of truth that could drift).
        private BufferLookup<DemandPeakBucket> m_DemandPeakBucketLookup;
        // Vanilla city population → defence-potential density surcharge (a populous city that COULD
        // crew a strong defence draws denser waves). RO lookup off the City entity (O(1), no sync
        // point — waits only for the Population writer), same pattern as CrisisActCoordinator.
        private CitySystem m_CitySystem = null!;
        private ComponentLookup<Population> m_PopulationLookup;

        // Services
        [System.NonSerialized] private ClimateState? m_ClimateAdapter;
        [System.NonSerialized] private ILightingPhaseReader m_Lighting = null!;
        // Built-surplus nameplate source (Фаза 7). Engineering is AlwaysOpen (not gated), so the
        // reader is registered before any phase transition — Require, symmetric to IThreatTargetSource.
        [System.NonSerialized] private IPowerCapacitySnapshotReader? m_CapacitySnapshotReader;
        private ModSettings? m_Settings;
        [System.NonSerialized] private bool m_LightingMissingWarned;

        // Launch-window wait signal (Single source of truth → published to WaveExecutor).
        // m_InDawnDuskRecheck: true while sitting in the Calm dawn/dusk recheck loop.
        // m_LaunchWindowWaitPublished: last Waiting value sent (edge-detect, no bus spam).
        [System.NonSerialized] private bool m_InDawnDuskRecheck;
        // Accumulated sim-seconds spent waiting for a dawn/dusk window since the calm duration
        // elapsed — bounds the overshoot via MAX_LAUNCH_WINDOW_WAIT. Transient: reset to 0 on
        // each fresh wait entry and on launch (defaults to 0 after load, restarting the cap).
        [System.NonSerialized] private float m_DawnDuskWaitElapsed;
        [System.NonSerialized] private bool m_LaunchWindowWaitPublished;
        // True while the readiness gate is holding the wave PAST baseTempo (recovering / grace) —
        // feeds the same "waiting" UI signal as the dawn/dusk hold so the countdown switches to a
        // hold state instead of freezing at 0:00. Recomputed every Calm gate tick.
        [System.NonSerialized] private bool m_GateHolding;

        // Cached context — rebuilt via GatherContext() on each phase transition
        [System.NonSerialized] private WaveSimulationContext m_Context;

        // Dynamic readiness gate (Core/Logic/WaveReadinessGate): decides when a Calm phase ends by
        // observing live recovery (lostFraction from the capacity snapshot), not a fixed timer.
        // Transient: on load it re-latches from the observed lostFraction (a mid-Calm save yields
        // slightly more grace — benign and bounded), so it is intentionally NOT in the save codec
        // and carries no persisted-NaN soft-lock risk. m_GatePrevPhase detects Calm entry in ONE
        // place (OnUpdate) so every Calm-entry site resets the gate without scattering reset calls.
        [System.NonSerialized] private WaveReadinessState m_ReadinessState;
        [System.NonSerialized] private GamePhase m_GatePrevPhase;
        [System.NonSerialized] private int m_GateEvalCounter;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Queries
            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_EnemyStateQuery = GetEntityQuery(ComponentType.ReadOnly<EnemyState>());
            m_DemandPeakQuery = GetEntityQuery(ComponentType.ReadOnly<DemandPeakSingleton>());
            m_DemandPeakBucketLookup = GetBufferLookup<DemandPeakBucket>(true);
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_PopulationLookup = GetComponentLookup<Population>(true);

            // Services resolved in OnStartRunning — CIVIC403.
            // Lighting phase is read through the process-lifetime facade; the adapter
            // handles vanilla readiness during early world boot.

            // Random seed from game time only: deterministic across reload/replay.
            var timeProvider = GameTimeSystem.Instance;
            float seedHours = timeProvider != null ? timeProvider.Current.TotalGameHours : 0f;
            uint seed = (uint)(seedHours * GameRate.SECONDS_PER_HOUR) + 0x2001;
            if (seed == 0) seed = 0x2001;
            m_Random = new Random(seed);

            // Events
            SubscribeBufferedUntilReady<WarStartedEvent>(OnWarStarted);
            SubscribeBufferedUntilReady<IntroAttackEvent>(OnIntroAttack);
            SubscribeBufferedUntilReady<ThreatNarrativeEvent>(OnPhaseChanged);
#if DEBUG
            SubscribeBufferedUntilReady<DebugSkipPhaseCommand>(OnDebugSkipPhase);
#endif

            Log.Info($" Created with seed {seed}");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_ClimateAdapter ??= ServiceRegistry.Instance.Require<ClimateState>();
            m_Lighting ??= ServiceRegistry.Instance.Require<ILightingPhaseReader>();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            m_CapacitySnapshotReader ??= ServiceRegistry.Instance.Require<IPowerCapacitySnapshotReader>();
            // Re-init transient gate state (boot and after load): Fresh so the gate re-observes
            // recovery; a non-Calm sentinel so the first Calm tick is detected as a fresh entry.
            m_ReadinessState = WaveReadinessState.Fresh;
            m_GatePrevPhase = GamePhase.Recovery;
            m_GateEvalCounter = 0;
            MarkEventHandlersReady();
        }

        private void OnWarStarted(WarStartedEvent evt)
        {
            EnsureInitializedBeforeReplayEvent();
            m_WarStartedReceived = true;

            if (!Enabled)
            {
                Log.Info(" WarStartedEvent received but system is DISABLED - ignoring");
                return;
            }

            StartSchedulerAfterWarStarted();
        }

        private void StartSchedulerAfterWarStarted()
        {
            if (m_ScenarioStarted)
                return;

            m_ScenarioStarted = true;
            if (m_CurrentPhase != GamePhase.Attack)
            {
                m_WaveNumber = NO_WAVE_NUMBER;
                m_WaveRole = WaveRole.None;
            }
            m_Context = GatherContext();

            // If intro wave (Attack) is still in progress, don't override phase.
            // Let WaveExecutor finish Attack → Recovery naturally.
            if (m_CurrentPhase == GamePhase.Attack)
            {
                m_PhaseEndTime = math.max(m_PhaseEndTime, m_TimeInPhase + MIN_ATTACK_PHASE_EXTENSION);
                Log.Info($" WarStartedEvent - scheduling enabled (Attack in progress, waiting for wave to finish)");
                return;
            }

            // Normal case: intro wave already resolved or never started. This is the FIRST calm
            // before the opening attack — use the dedicated first-calm tempo, not the inter-wave one.
            m_CurrentPhase = GamePhase.Calm;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = WaveScalingService.CalculateFirstCalmDuration(m_Context, ref m_Random);

            Log.Info($" WarStartedEvent - scheduling enabled, first calm {m_PhaseEndTime / 60f:F1}min ({m_Context.CitySizeMW}MW)");
        }

        /// <summary>
        /// Intro cinematic entered attack phase — schedule the intro wave through standard flow.
        /// Threat count is calculated from city size, not hardcoded.
        /// </summary>
        private void OnIntroAttack(IntroAttackEvent evt)
        {
            EnsureInitializedBeforeReplayEvent();

            if (m_IntroAttackFired || m_ScenarioStarted || m_WaveNumber != NO_WAVE_NUMBER || m_CurrentPhase != GamePhase.Calm)
            {
                Log.Info($" Intro attack ignored (fired={m_IntroAttackFired}, started={m_ScenarioStarted}, wave={m_WaveNumber}, phase={m_CurrentPhase})");
                return;
            }

            m_Context = GatherContext();

            // FIX S17-04: Respect AttacksEnabled setting for intro wave.
            // Without this, intro attack fires even when player set difficulty to "no attacks".
            if (!m_Context.AttacksEnabled)
            {
                Log.Info(" Intro attack skipped: attacks disabled in settings");
                return;
            }

            m_IntroAttackFired = true;
            // Intro wave is the normal Harassment formula scaled by IntroStrengthMult — a capped
            // uplift over a regular wave, NOT a forced MassiveStrike (which made the first strike
            // ~2x a normal wave). Fixed noiseMult of 1 (no ±20% roll) so the uplift is exactly
            // IntroStrengthMult above a baseline wave instead of compounding to +44% on a high roll.
            var waveType = WaveType.Harassment;
            float introStrengthMult = BalanceConfig.Current.Waves.IntroStrengthMult;
            int threatCount = WaveScalingService.CalculateThreatCount(
                waveType,
                INTRO_WAVE_NUMBER,
                m_Context.CitySizeMW,
                m_Context.IntensityModifier,
                noiseMult: 1f,
                m_Context.EnemyPressure,
                m_Context.TargetCount,
                m_Context.SurplusRatio,
                m_Context.IntermittentTypeCount,
                m_Context.DensityRatio,
                m_Context.RecoveryFactor,
                introStrengthMult);

            // Alert command: sets up WaveStateSingleton, resets stats/debriefing
            float alertDuration = 0.1f;
            EventBus?.SafePublish(new ScheduleWaveCommand(
                TargetPhase: GamePhase.Alert,
                WaveNumber: INTRO_WAVE_NUMBER,
                WaveType: waveType,
                ThreatsExpected: threatCount,
                PhaseDuration: alertDuration,
                WaveRole: WaveRole.Intro
            ), "WaveScheduler");

            // Attack command: triggers spawn through WaveExecutor
            float attackDuration = WaveScalingService.GetPhaseDuration(GamePhase.Attack, INTRO_WAVE_NUMBER, 1f, m_Random.NextFloat());
            EventBus?.SafePublish(new ScheduleWaveCommand(
                TargetPhase: GamePhase.Attack,
                WaveNumber: INTRO_WAVE_NUMBER,
                WaveType: waveType,
                ThreatsExpected: threatCount,
                PhaseDuration: attackDuration,
                WaveRole: WaveRole.Intro
            ), "WaveScheduler");

            // Explicit local state (defense-in-depth — OnPhaseChanged also syncs via EventBus)
            m_WaveNumber = INTRO_WAVE_NUMBER;
            m_WaveRole = WaveRole.Intro;
            m_WaveType = waveType;
            m_ThreatsExpected = threatCount;
            m_CurrentPhase = GamePhase.Attack;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = attackDuration;

            Log.Info($" Intro wave {INTRO_WAVE_NUMBER}: {threatCount} threats (Harassment x{introStrengthMult:F2}), {m_Context.CitySizeMW}MW city");
        }

        private void EnsureInitializedBeforeReplayEvent()
        {
            if (m_Initialized)
                return;

            ApplyInitialState();
        }

        /// <summary>
        /// Listen to phase changes from Executor to stay synchronized.
        /// Scheduler tracks phase for timing decisions.
        /// </summary>
        private void OnPhaseChanged(ThreatNarrativeEvent evt)
        {
            if (evt.Type != ThreatNarrativeEventType.WavePhaseChanged) return;

            EnsureInitializedBeforeReplayEvent();

            // Sync phase from Executor (Single Source of Truth)
            if (evt.Phase != m_CurrentPhase)
            {
                if (IsStalePhaseEvent(evt.Phase, evt.WaveNumber))
                {
                    Log.Warn($" Ignoring stale phase sync: local wave {m_WaveNumber} {m_CurrentPhase}, event wave {evt.WaveNumber} {evt.Phase}");
                    return;
                }

                if (Log.IsDebugEnabled) Log.Debug($" Phase sync: {m_CurrentPhase} → {evt.Phase}");
                m_CurrentPhase = evt.Phase;
                m_TimeInPhase = 0f;

                // Set appropriate duration so the timer doesn't immediately expire
                // with stale m_PhaseEndTime from the previous phase.
                switch (evt.Phase)
                {
                    case GamePhase.Recovery:
                        m_PhaseEndTime = RECOVERY_PHASE_DURATION;
                        break;
                    case GamePhase.Alert:
                    case GamePhase.Attack:
                        m_PhaseEndTime = MIN_ATTACK_PHASE_EXTENSION;
                        break;
                    case GamePhase.Calm:
                        m_Context = GatherContext();
                        m_PhaseEndTime = WaveScalingService.CalculateCalmDuration(m_Context, ref m_Random);
                        break;
                    default:
                        m_PhaseEndTime = DEFAULT_PHASE_END_TIME;
                        break;
                }
            }
        }

        private bool IsStalePhaseEvent(GamePhase eventPhase, int eventWaveNumber)
        {
            if (eventWaveNumber < m_WaveNumber)
                return true;
            if (eventWaveNumber > m_WaveNumber)
                return false;
            if (m_CurrentPhase == GamePhase.Recovery && eventPhase == GamePhase.Calm)
                return false;
            return GetPhaseOrder(eventPhase) < GetPhaseOrder(m_CurrentPhase);
        }

        private static int GetPhaseOrder(GamePhase phase) => phase switch
        {
            GamePhase.Calm => 0,
            GamePhase.Alert => 1,
            GamePhase.Attack => 2,
            GamePhase.Recovery => 3,
            _ => 0
        };

#if DEBUG
        // FIX S14_CODE1:88: DebugSkipPhase must also advance WaveScheduler's own timer,
        // not just WaveStateSingleton.TimeInPhase — otherwise Calm→Alert and Alert→Attack don't trigger
        private void OnDebugSkipPhase(DebugSkipPhaseCommand cmd)
        {
            m_TimeInPhase = m_PhaseEndTime;
            Log.Info($" Debug: Scheduler timer skipped ({m_CurrentPhase})");
        }
#endif

        protected override void OnDestroy()
        {
            UnsubscribeSafe<WarStartedEvent>(OnWarStarted);
            UnsubscribeSafe<IntroAttackEvent>(OnIntroAttack);
            UnsubscribeSafe<ThreatNarrativeEvent>(OnPhaseChanged);
#if DEBUG
            UnsubscribeSafe<DebugSkipPhaseCommand>(OnDebugSkipPhase);
#endif
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // CIVIC185/Axiom 8: .Update BufferLookup в update-методе. GatherContext (вызывается и
            // отсюда, и из event-обработчиков) делает свой .Update перед чтением кольца — этот
            // покрывает update-путь для анализатора.
            m_DemandPeakBucketLookup.Update(this);
            m_PopulationLookup.Update(this);

            if (!m_Initialized)
            {
                ApplyInitialState();
                return;
            }

            if (!m_ScenarioStarted && m_WarStartedReceived)
            {
                StartSchedulerAfterWarStarted();
            }

            // Freeze timer until scenario starts (intro wave runs through WaveExecutor independently)
            if (!m_ScenarioStarted)
                return;

            // Tick timer
            float deltaTime = SystemAPI.Time.DeltaTime;
#pragma warning disable CIVIC056 // Resets on phase transition in TryScheduleTransition()
            m_TimeInPhase += deltaTime;
#pragma warning restore CIVIC056

            // Check for phase transition. In Calm (before the dawn/dusk recheck loop) the dynamic
            // readiness gate decides: baseTempo (m_PhaseEndTime) is the floor, but the wave also
            // waits for the city to recover the generation a prior strike knocked out plus a grace
            // window — capped so a never-repairing city is still harassed. Other phases and the
            // recheck loop keep the bare timer.
            bool triggerTransition;
            if (m_CurrentPhase == GamePhase.Calm && !m_InDawnDuskRecheck)
            {
                if (m_GatePrevPhase != GamePhase.Calm)
                {
                    m_ReadinessState = WaveReadinessState.Fresh; // first tick of this Calm
                    m_GateEvalCounter = GATE_EVAL_INTERVAL;      // evaluate immediately on Calm entry
                }
                // Sample the gate ~twice a second instead of every tick (snapshot is throttled, calm
                // is minutes long — the few-tick launch latency is invisible). Timer still ticks above.
                if (++m_GateEvalCounter >= GATE_EVAL_INTERVAL)
                {
                    m_GateEvalCounter = 0;
                    triggerTransition = EvaluateReadinessGate();
                }
                else
                {
                    triggerTransition = false;
                }
            }
            else
            {
                triggerTransition = m_TimeInPhase >= m_PhaseEndTime;
            }
            m_GatePrevPhase = m_CurrentPhase;

            if (triggerTransition)
            {
                TryScheduleTransition();
            }

            // Single source of truth for the launch-window wait signal: derive desired state
            // from current fields and publish only on change (or throttled estimate refresh).
            PublishLaunchWindowWaitIfChanged();
        }

        /// <summary>
        /// Run the dynamic readiness gate for one Calm tick: read the live capacity snapshot and ask
        /// <see cref="WaveReadinessGate"/> whether the next wave may launch. The gate is the single
        /// source of the launch decision (variant D) — this is the thin runtime adapter; all clamps
        /// and crash guards live in the gate, not here.
        /// </summary>
        private bool EvaluateReadinessGate()
        {
            var cfg = BalanceConfig.Current;          // single read — no torn hot-reload (CIVIC347)
            var wcfg = cfg.Waves;

            int dispatchableMW = 0;
            int nameplateMW = 0;
            if (m_CapacitySnapshotReader != null
                && m_CapacitySnapshotReader.TryGetSnapshot(out var snap))
            {
                // NameplateKW is kW (built capacity); CityDispatchableMW is already MW (damage-cut, not load).
                nameplateMW = (int)math.round(snap.NameplateKW / KW_PER_MW);
                dispatchableMW = snap.CityDispatchableMW;
            }

            // Ceiling: a fair Municipal repair would take this long — past it, harass anyway so a
            // never-repairing city does not earn permanent peace. Game-hours → the Calm timer's
            // sim-second scale via the fixed vanilla constant (see WaveReadinessGate).
            float maxWaitSeconds = cfg.InfrastructureRepair.MunicipalRepairHours
                                 * WaveReadinessGate.SIM_SEC_PER_GAME_HOUR;

            var result = WaveReadinessGate.Evaluate(
                ref m_ReadinessState,
                m_TimeInPhase,
                m_PhaseEndTime,           // baseTempo — set at Calm entry from CalculateCalmDuration
                dispatchableMW,
                nameplateMW,
                wcfg.RecoveredThreshold,
                wcfg.GraceFraction,
                maxWaitSeconds);

            // Holding past baseTempo (recovering / grace, not yet launched) → UI shows a hold state
            // instead of a frozen 0:00 countdown (same channel as the dawn/dusk wait).
            m_GateHolding = !result.Launch && m_TimeInPhase >= m_PhaseEndTime;

            if (Log.IsDebugEnabled && result.Launch)
                Log.Debug($" Readiness gate launch: reason={result.Reason} t={m_TimeInPhase:F0}s baseTempo={m_PhaseEndTime:F0}s lost={result.LostFraction:P0} tRec={m_ReadinessState.TRecovered:F0}s");

            return result.Launch;
        }

        private void TryScheduleTransition()
        {
            m_Context = GatherContext();

            switch (m_CurrentPhase)
            {
                case GamePhase.Calm:
                    // Accumulate overshoot wait across rechecks. First entry (recheck not yet
                    // active) starts the counter at 0 so the calm duration itself is excluded;
                    // each subsequent recheck adds the interval actually waited.
                    if (m_InDawnDuskRecheck)
                        m_DawnDuskWaitElapsed += m_TimeInPhase;
                    else
                        m_DawnDuskWaitElapsed = 0f;

                    if (IsDawnOrDusk() || m_DawnDuskWaitElapsed >= MAX_LAUNCH_WINDOW_WAIT)
                    {
                        if (m_DawnDuskWaitElapsed >= MAX_LAUNCH_WINDOW_WAIT && !IsDawnOrDusk()
                            && Log.IsDebugEnabled)
                            Log.Debug($" Launch-window wait hit cap ({MAX_LAUNCH_WINDOW_WAIT:F0}s) — launching off twilight");
                        ScheduleAlertPhase();
                    }
                    else
                    {
                        // Wait for dawn/dusk — recheck periodically. Only mark the recheck
                        // state; the wait signal is published by PublishLaunchWindowWaitIfChanged
                        // (single source of truth), so no event is emitted here.
                        m_TimeInPhase = 0f;
                        m_PhaseEndTime = DAWN_DUSK_RECHECK_INTERVAL;
                        m_InDawnDuskRecheck = true;
                        if (Log.IsDebugEnabled)
                            Log.Debug($" Waiting for dawn/dusk (current: {m_Lighting.CurrentPhase}, waited {m_DawnDuskWaitElapsed:F0}/{MAX_LAUNCH_WINDOW_WAIT:F0}s)");
                    }
                    break;

                case GamePhase.Alert:
                    ScheduleAttackPhase();
                    break;

                case GamePhase.Attack:
                    // Attack → Recovery is handled by WaveExecutor (when threats cleared)
                    // Scheduler just extends the timer
                    m_PhaseEndTime = m_TimeInPhase + MIN_ATTACK_PHASE_EXTENSION;
                    break;

                case GamePhase.Recovery:
                    ScheduleCalmPhase();
                    break;

                default:
                    Log.Warn($"Unhandled {nameof(GamePhase)}: {m_CurrentPhase}");
                    break;
            }
        }

        private void ScheduleAlertPhase()
        {
            // Window arrived OR attacks disabled — recheck wait is no longer active.
            // MUST clear before the AttacksEnabled gate's early return, else the flag leaks.
            m_InDawnDuskRecheck = false;
            m_DawnDuskWaitElapsed = 0f;

            if (!m_Context.AttacksEnabled)
            {
                // Attacks disabled - stay in Calm
                m_TimeInPhase = 0f;
                m_PhaseEndTime = WaveScalingService.CalculateCalmDuration(m_Context, ref m_Random);
                if (Log.IsDebugEnabled) Log.Debug($" Attacks disabled, staying in Calm ({m_PhaseEndTime / 60f:F1}min)");
                return;
            }

            int nextWaveNumber = math.max(m_WaveNumber + 1, FIRST_REGULAR_WAVE_NUMBER);
            var waveType = WaveScalingService.DetermineWaveType(nextWaveNumber, ref m_Random);
            int threatCount = WaveScalingService.CalculateThreatCount(m_Context, waveType, nextWaveNumber, ref m_Random);
            float duration = WaveScalingService.GetPhaseDuration(GamePhase.Alert, 0, 1f, m_Random.NextFloat());

            EventBus?.SafePublish(new ScheduleWaveCommand(
                TargetPhase: GamePhase.Alert,
                WaveNumber: nextWaveNumber,
                WaveType: waveType,
                ThreatsExpected: threatCount,
                PhaseDuration: duration,
                WaveRole: WaveRole.Regular
            ), "WaveScheduler");

            // Update local state (will be synced via OnPhaseChanged)
            m_WaveNumber = nextWaveNumber;
            m_WaveRole = WaveRole.Regular;
            m_WaveType = waveType;
            m_ThreatsExpected = threatCount;
            m_CurrentPhase = GamePhase.Alert;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = duration;

            string waveTypeStr = waveType == WaveType.MassiveStrike ? "MASSIVE STRIKE" : "Harassment";
            Log.Info($" Scheduled Alert: Wave #{m_WaveNumber} [{waveTypeStr}], {threatCount} threats, {duration:F0}s");
        }

        private void ScheduleAttackPhase()
        {
            float duration = WaveScalingService.GetPhaseDuration(GamePhase.Attack, 0, 1f, m_Random.NextFloat());

            EventBus?.SafePublish(new ScheduleWaveCommand(
                TargetPhase: GamePhase.Attack,
                WaveNumber: m_WaveNumber,
                WaveType: m_WaveType,
                ThreatsExpected: m_ThreatsExpected,
                PhaseDuration: duration,
                WaveRole: m_WaveRole
            ), "WaveScheduler");

            m_CurrentPhase = GamePhase.Attack;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = duration;

            Log.Info($" Scheduled Attack: Wave #{m_WaveNumber}");
        }

        private void ScheduleCalmPhase()
        {
            // Check for Double Tap (storm mechanic)
            float doubleTapDuration = WaveScalingService.CheckDoubleTap(ref m_Random);
            bool isDoubleTap = doubleTapDuration > 0;
            float duration = isDoubleTap
                ? doubleTapDuration
                : WaveScalingService.CalculateCalmDuration(m_Context, ref m_Random);

            EventBus?.SafePublish(new ScheduleWaveCommand(
                TargetPhase: GamePhase.Calm,
                WaveNumber: m_WaveNumber,
                PhaseDuration: duration,
                IsDoubleTap: isDoubleTap,
                WaveRole: m_WaveRole
            ), "WaveScheduler");

            m_CurrentPhase = GamePhase.Calm;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = duration;

            if (isDoubleTap)
                Log.Info($" Scheduled Calm: DOUBLE TAP! Next wave in {duration / 60f:F1}min");
            else
                Log.Info($" Scheduled Calm: {duration / 60f:F1}min ({m_Context.CitySizeMW}MW city)");
        }

        /// <summary>
        /// Returns true when lighting is in dawn/sunrise/sunset/dusk — best visual window
        /// for attacks (tracers glow visible, drones silhouetted against sky).
        /// Intro wave bypasses this check.
        /// </summary>
        private bool IsDawnOrDusk()
        {
            if (!m_Lighting.IsReady)
            {
                if (!m_LightingMissingWarned)
                {
                    Log.Warn("LightingSystem unavailable — deferring wave until lighting is ready");
                    m_LightingMissingWarned = true;
                }
                return false;
            }

            m_LightingMissingWarned = false;
            return m_Lighting.IsDawnOrDuskLaunchWindow;
        }

        /// <summary>
        /// Derive the launch-window wait state from current fields and notify WaveExecutor
        /// only on change (edge-detect). This is the SINGLE source of truth: waiting is true
        /// ONLY while we are genuinely in Calm, scenario running, attacks enabled, and the
        /// dawn/dusk recheck is active — so every exit from Calm (Alert, attacks-disabled,
        /// external phase-sync) drops it to false automatically without scattered resets.
        /// </summary>
        private void PublishLaunchWindowWaitIfChanged()
        {
            // Waiting = Calm elapsed but the wave is held back, for EITHER reason: the dawn/dusk
            // launch window, or the readiness gate still letting the city recover (past baseTempo).
            // Both drive the same UI hold state so the countdown never freezes at 0:00.
            bool waiting = m_ScenarioStarted
                && m_CurrentPhase == GamePhase.Calm
                && (m_InDawnDuskRecheck || m_GateHolding)
                && m_Context.AttacksEnabled;

            if (waiting == m_LaunchWindowWaitPublished)
                return;

            m_LaunchWindowWaitPublished = waiting;
            EventBus?.SafePublish(new WaveLaunchWindowWaitEvent(waiting), "WaveScheduler");
            // Distinguish the two hold causes (mutually exclusive: recovery-hold has not launched yet,
            // so it is never in the dawn/dusk recheck) for diagnostics.
            string cause = m_GateHolding ? "gate: city recovering (lostFraction > threshold)" : "holding for dawn/dusk";
            Log.Info($" Launch-window wait {(waiting ? $"BEGIN ({cause})" : "END")}");
        }

        private void ApplyInitialState()
        {
            m_Context = GatherContext();

            if (m_HasSavedState && m_SavedState.HasValue)
            {
                var saved = m_SavedState.Value;
                m_CurrentPhase = saved.Phase;
                m_TimeInPhase = saved.TimeInPhase;
                m_PhaseEndTime = saved.PhaseEndTime;
                m_WaveNumber = saved.WaveNumber;
                m_WaveRole = saved.WaveRole;
                m_WaveType = saved.WaveType;
                m_ThreatsExpected = saved.ThreatsExpected;
                m_WarStartedReceived = saved.WarStartedReceived;
                m_IntroAttackFired = saved.IntroAttackFired;
#pragma warning disable CIVIC156 // Placeholder seed: immediately overwritten by saved.RandomState
                m_Random = new Random(1);
#pragma warning restore CIVIC156
                m_Random.state = saved.RandomState;
                m_ScenarioStarted = saved.ScenarioStarted;

                m_HasSavedState = false;
                m_SavedState = null;

                Log.Info($" Restored from save: Phase={m_CurrentPhase}, Wave #{m_WaveNumber}, ScenarioStarted={m_ScenarioStarted}");
            }
            else
            {
                // W4-M8 FIX: Don't consume RNG here — OnWarStarted will calculate real calm duration.
                // Using DEFAULT_PHASE_END_TIME as placeholder prevents RNG sequence shift.
                m_CurrentPhase = GamePhase.Calm;
                m_TimeInPhase = 0f;
                m_PhaseEndTime = DEFAULT_PHASE_END_TIME;
                m_WaveNumber = NO_WAVE_NUMBER;
                m_WaveRole = WaveRole.None;

                Log.Info($" Fresh start. Calm placeholder ({DEFAULT_PHASE_END_TIME / 60f:F1}min, {m_Context.CitySizeMW}MW city)");
            }

            var attackPreset = m_Settings?.AirAttacks ?? AirAttackPreset.Off;
            Log.Info($" Air attacks: {attackPreset} (frequency: {m_Context.FrequencyModifier:F1}x, intensity: {m_Context.IntensityModifier:F1}x)");

            // Launch-window wait state is transient ([NonSerialized] may persist across load):
            // reset so the derive/edge-detect re-establishes it cleanly on the next recheck.
            m_InDawnDuskRecheck = false;
            m_DawnDuskWaitElapsed = 0f;
            m_LaunchWindowWaitPublished = false;

            m_Initialized = true;
        }

        private WaveSimulationContext GatherContext()
        {
            bool powerGridReady = m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var powerGrid);
            if (!powerGridReady)
                powerGrid = default;
#pragma warning disable CIVIC211 // Wave scaling depends on enemy HP — read-only via Core singleton
            bool enemyStateReady = m_EnemyStateQuery.TryGetSingleton<EnemyState>(out var enemyState);
            if (!enemyStateReady)
                enemyState = default;
#pragma warning restore CIVIC211

            // Attackable-building count from the threat-target cache scales the wave down
            // on small/empty maps (size formula floors on production, blind to targets).
            // IThreatTargetSource is AlwaysOpen (owned by Waves, no null-object) so Require
            // is mandated — the producer registers in OnCreate, before any phase transition.
            // Count stays -1 ("unknown", scaling off) only until the cache's first refresh.
            int targetCount = -1;
            var targetSource = ServiceRegistry.Instance.Require<IThreatTargetSource>();
            if (targetSource.IsReady)
            {
                targetCount = targetSource.Energy.Length
                            + targetSource.Critical.Length
                            + targetSource.Service.Length
                            + targetSource.Civilian.Length;
            }

            // Built nameplate from the Core power-capacity snapshot (Фаза 7, путь B). Degradation
            // never touches it, so a spam city's over-build is visible even when its effective
            // production was cut. LargestPlantKW feeds the N+1 unit buffer (one biggest built
            // unit is forgiven before surplus counts). Null-guard covers the window before
            // OnStartRunning resolves the reader (then nameplateKW=0 → surplusRatio floors to 1,
            // no surcharge for one tick).
            int nameplateKW = 0;
            int largestPlantKW = 0;
            int intermittentTypeCount = 0;
            // Live dispatchable capacity (MW, damage-cut) feeds the surcharge recovery gate: a city
            // knocked down past SurchargeLethalFraction earns no density/surplus escalation. Already
            // read MW from the snapshot, same as EvaluateReadinessGate.
            int dispatchableMW = 0;
            if (m_CapacitySnapshotReader != null
                && m_CapacitySnapshotReader.TryGetSnapshot(out var capSnap))
            {
                nameplateKW = capSnap.NameplateKW;
                largestPlantKW = capSnap.LargestPlantKW;
                intermittentTypeCount = capSnap.IntermittentTypeCount;
                dispatchableMW = capSnap.CityDispatchableMW;
            }

            // City population → defence-potential density signal. 0 (no city / boot) → no density
            // surcharge (fail-safe, handled in Gather). RO lookup, no sync point.
            int population = GetPopulation();

            // 24h demand peak (Фаза 3) = max over the 24 hourly buckets. The singleton stores no
            // cached scalar (would be a second source of truth that drifts on load), so derive it
            // here. 0 → Gather falls back to instantaneous powerGrid.Demand until the ring is ready.
            int peakDemandKW = 0;
            m_DemandPeakBucketLookup.Update(this);
            if (m_DemandPeakQuery.TryGetSingletonEntity<DemandPeakSingleton>(out var demandPeakEntity)
                && m_DemandPeakBucketLookup.TryGetBuffer(demandPeakEntity, out var demandRing)
                && demandRing.Length == DemandPeakSingleton.BUCKETS)
            {
                for (int b = 0; b < demandRing.Length; b++)
                    peakDemandKW = math.max(peakDemandKW, demandRing[b].PeakKW);
            }

            // Game-hour for the enemy respite window (Phase 3.6.3): the respite expiries on
            // EnemyState are absolute game-hours, so the gatherer needs the current one to tell
            // which suppressed axes are still in their regroup window. 0 before GameTime activates
            // → no axis reads as in-respite, matching the pre-respite behaviour for one tick.
            float gameTimeHours = GameTimeSystem.TryGetGameHours(out var gh) ? gh : 0f;

            // ThreatScaleDiag (Debug): surplus inputs the gatherer feeds the count. nameplate is BUILT
            // capacity (drives the surcharge); peak falls back to instantaneous demand until the 24h
            // ring fills. Per-wave only, not hot-path. Enable Debug for [WaveScheduler] to see it.
            if (Log.IsDebugEnabled)
            {
                int diagNameplateMW = (int)math.round(nameplateKW / KW_PER_MW);
                float diagLost = WaveReadinessGate.ComputeLostFraction(dispatchableMW, diagNameplateMW);
                // DIAG (Debug): the density/recovery INPUTS — pairs with [ThreatMath]. Enable Debug for [WaveScheduler].
                Log.Debug($"[ThreatScaleDiag] gather: production={powerGrid.Production / 1000}MW citySize={WaveContextGatherer.ToCitySizeMW(nameplateKW)}MW(nameplate) dispatchable={dispatchableMW}MW lost={diagLost:P0} pop={population} peak={(peakDemandKW > 0 ? peakDemandKW : powerGrid.Demand) / 1000}MW largestPlant={largestPlantKW / 1000}MW");
            }

            return WaveContextGatherer.Gather(
                powerGrid,
                enemyState,
                m_ClimateAdapter!,
                m_Settings!,
                nameplateKW,
                peakDemandKW,
                powerGridReady,
                enemyStateReady,
                targetCount,
                largestPlantKW,
                intermittentTypeCount,
                gameTimeHours,
                dispatchableMW,
                population);
        }

        /// <summary>
        /// Read population from the vanilla Population singleton on the City entity. O(1), no sync
        /// point (RO ComponentLookup waits only for the Population writer). 0 when the city is not
        /// ready — the density surcharge then fail-safes off.
        /// </summary>
        private int GetPopulation()
        {
            // Sync the lookup HERE, not only in OnUpdate: GatherContext is also driven from event
            // handlers (war-started / intro / phase-changed) that do not pass through the OnUpdate
            // refresh that frame — mirror the m_DemandPeakBucketLookup.Update inside GatherContext.
            // Idempotent (a second Update on the OnUpdate path is a no-op), same as the demand ring.
            m_PopulationLookup.Update(this);
            var city = m_CitySystem.City;
            if (city == Entity.Null || !m_PopulationLookup.HasComponent(city))
                return 0;
            return m_PopulationLookup[city].m_Population;
        }

        // ============================================================================
        // Public API (read-only for diagnostics)
        // ============================================================================

        public GamePhase CurrentPhase => m_CurrentPhase;
        public int WaveNumber => m_WaveNumber;
        public float TimeInPhase => m_TimeInPhase;
        public float PhaseEndTime => m_PhaseEndTime;
        public bool ScenarioStarted => m_ScenarioStarted;
        public bool IsInitialized => m_Initialized;

        // ============================================================================
        // IResettable
        // ============================================================================

        public void ResetState()
        {
            Log.Info(" ResetState - clearing session data");
            WaveContextGatherer.ResetStaticState();

            m_Initialized = false;
            m_ScenarioStarted = false;
            m_WarStartedReceived = false;
            m_IntroAttackFired = false;
            m_HasSavedState = false;
            m_SavedState = null;
            m_CurrentPhase = GamePhase.Calm;
            m_TimeInPhase = 0f;
            m_PhaseEndTime = 0f;
            m_WaveNumber = NO_WAVE_NUMBER;
            m_WaveRole = WaveRole.None;
            m_WaveType = WaveType.Harassment;
            m_ThreatsExpected = 0;
            m_LightingMissingWarned = false;
            m_InDawnDuskRecheck = false;
            m_DawnDuskWaitElapsed = 0f;
            m_LaunchWindowWaitPublished = false;
            m_ReadinessState = WaveReadinessState.Fresh;
            m_GatePrevPhase = GamePhase.Recovery;
            m_GateHolding = false;
            m_GateEvalCounter = 0;

            // Re-seed random to prevent stale sequence from previous session.
            var timeProvider = GameTimeSystem.Instance;
            float seedHours = timeProvider != null ? timeProvider.Current.TotalGameHours : 0f;
            uint seed = (uint)(seedHours * GameRate.SECONDS_PER_HOUR) + 0x2001;
            if (seed == 0) seed = 0x2001;
            m_Random = new Random(seed);
        }

        // ============================================================================
        // Serialization Support
        // ============================================================================

        internal readonly struct SchedulerSaveState
        {
            public readonly GamePhase Phase;
            public readonly float TimeInPhase;
            public readonly float PhaseEndTime;
            public readonly int WaveNumber;
            public readonly WaveRole WaveRole;
            public readonly WaveType WaveType;
            public readonly int ThreatsExpected;
            public readonly uint RandomState;
            public readonly bool ScenarioStarted;
            public readonly bool WarStartedReceived;
            public readonly bool IntroAttackFired;

            public SchedulerSaveState(GamePhase phase, float timeInPhase, float phaseEndTime,
                int waveNumber, WaveRole waveRole, WaveType waveType, int threatsExpected,
                uint randomState, bool scenarioStarted, bool warStartedReceived, bool introAttackFired)
            {
                Phase = phase;
                TimeInPhase = timeInPhase;
                PhaseEndTime = phaseEndTime;
                WaveNumber = waveNumber;
                WaveRole = waveRole;
                WaveType = waveType;
                ThreatsExpected = threatsExpected;
                RandomState = randomState;
                ScenarioStarted = scenarioStarted;
                WarStartedReceived = warStartedReceived;
                IntroAttackFired = introAttackFired;
            }
        }

        internal SchedulerSaveState GetSaveState()
        {
            if (!m_Initialized && m_HasSavedState && m_SavedState.HasValue)
                return m_SavedState.Value;

            return new SchedulerSaveState(
                m_CurrentPhase,
                m_TimeInPhase,
                m_PhaseEndTime,
                m_WaveNumber,
                m_WaveRole,
                m_WaveType,
                m_ThreatsExpected,
                m_Random.state,
                m_ScenarioStarted,
                m_WarStartedReceived,
                m_IntroAttackFired);
        }

        internal void SetSaveState(SchedulerSaveState state)
        {
            m_SavedState = state;
            m_HasSavedState = true;
            m_Initialized = false;
        }

    }
}
