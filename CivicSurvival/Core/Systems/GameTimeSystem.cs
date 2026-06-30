using System;
using Game;
using Game.Simulation;
using Unity.Entities;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types.Snapshots;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Centralized time tracking system - SINGLE SOURCE OF TRUTH for game time.
    /// Directly accesses Game.Simulation.TimeSystem for current time.
    /// Publishes DayChangedEvent and WarDayChangedEvent at midnight.
    ///
    /// Usage:
    ///   var time = GameTimeSystem.Instance.Current;
    ///   if (time.WarDay > 10) { ... }
    ///
    /// Owns: GameTimeState (registered in SingletonRegistry)
    /// </summary>
    [ActIndependent]
    public partial class GameTimeSystem : CivicSystemBase, IDefaultSerializable, IResettable
    {
        private static readonly LogContext Log = new("GameTimeSystem");

        /// <summary>Static instance for fast runtime access (published after deferred activation, cleared on deactivate/destroy).</summary>
        public static GameTimeSystem? Instance { get; private set; }

        /// <summary>
        /// Required static accessor for systems that cannot run without game time.
        /// Use nullable <see cref="Instance"/> or the safe scalar accessors for optional/time-tolerant paths.
        /// </summary>
        public static GameTimeSystem RequireInstance()
            => Instance ?? throw new InvalidOperationException("GameTimeSystem not initialized.");

        // ===== State (registered in SingletonRegistry) =====
        [NonSerialized] private GameTimeState m_State = null!;

        /// <summary>Current immutable snapshot (convenience for direct system access).</summary>
        public GameTimeSnapshot Current => m_State.Current;

        // ===== Static Accessors =====
        // Runtime callers (OnUpdate / gameplay paths) use the throwing properties:
        // a null Instance there is a real ordering bug, not a tolerable condition.
        // Save/load paths (Serialize / Deserialize / SetDefaults / ResetToBootDefaults /
        // ResetState) MUST use the TryGet pair instead — vanilla CS2 invokes those
        // hooks before OnGameLoaded, so Instance is legitimately null in that window.
        // Enforced at compile time by CIVIC484.

        /// <summary>Total game hours. Throws if Instance not yet activated — runtime contract.</summary>
        public static float GameHours
            => Instance != null
                ? Instance.Current.TotalGameHours
                : throw new InvalidOperationException("GameTimeSystem.GameHours read before activation; use TryGetGameHours in save/load paths");

        /// <summary>Total game seconds computed from integer day + current hour to avoid long-session float bucket collisions. Throws if Instance not yet activated.</summary>
        public static double TotalGameSeconds
        {
            get
            {
                if (Instance == null)
                    throw new InvalidOperationException("GameTimeSystem.TotalGameSeconds read before activation; use TryGetTotalGameSeconds in save/load paths");
                var current = Instance.Current;
                return ((double)current.CurrentDay * GameRate.HOURS_PER_DAY + current.CurrentHour) * GameRate.SECONDS_PER_HOUR;
            }
        }

        /// <summary>Current game day. Throws if Instance not yet activated.</summary>
        public static int Day
            => Instance != null
                ? Instance.Current.CurrentDay
                : throw new InvalidOperationException("GameTimeSystem.Day read before activation; use TryGetDay in save/load paths");

        /// <summary>Safe accessor for save/load paths. Returns false if Instance not yet activated.</summary>
        public static bool TryGetGameHours(out float hours)
        {
            if (Instance != null)
            {
                hours = Instance.Current.TotalGameHours;
                return true;
            }
            hours = 0f;
            return false;
        }

        /// <summary>Safe accessor for save/load paths. Returns false if Instance not yet activated.</summary>
        public static bool TryGetDay(out int day)
        {
            if (Instance != null)
            {
                day = Instance.Current.CurrentDay;
                return true;
            }
            day = 0;
            return false;
        }

        /// <summary>Safe accessor for save/load paths. Returns false if Instance not yet activated.</summary>
        public static bool TryGetTotalGameSeconds(out double seconds)
        {
            if (Instance != null)
            {
                var current = Instance.Current;
                seconds = ((double)current.CurrentDay * GameRate.HOURS_PER_DAY + current.CurrentHour) * GameRate.SECONDS_PER_HOUR;
                return true;
            }
            seconds = 0.0;
            return false;
        }

        // ===== Game Time (from CS2) =====
        // Activation-deferred: resolved in OnGameLoaded (or OnStartRunning fallback),
        // not in OnCreate. Static Instance / SingletonRegistry publication is gated
        // behind the same activation so readers never see a half-built system.
        // V_REGRESSION_FIX_PLAN_PHASE2_EXPANDED §Lifecycle Sequence.
        private Game.Simulation.TimeSystem? m_GameTimeSystem;
        private Game.Simulation.SimulationSystem m_SimulationSystem = null!;
        private EntityQuery m_TimeDataQuery;

        // Activation latch. Flipped true after vanilla TimeSystem resolves and
        // Instance/SingletonRegistry are published. Cleared on world teardown
        // (OnDestroy) and main-menu transition (OnGamePreload(MainMenu)).
#pragma warning disable CIVIC324 // Intentionally survives a reuse-world load: the same World keeps the same vanilla TimeSystem, so the cached Instance stays valid and the latch must not re-arm. Re-armed only by ResetState (DeactivateTimePipeline) and teardown/main-menu (OnDestroy / OnGamePreload). Deserialize does not touch it.
        [NonSerialized] private bool m_Activated;
#pragma warning restore CIVIC324

        // ===== Day Tracking =====
        private float m_LastGameHour = 0f;
        private float m_LastNormalizedTime = 0f;
        private int m_CurrentDay = 0;

        // Vanilla day counter — integer, immune to normalizedTime float wrap issues
        [NonSerialized] private int m_LastVanillaDay = -1;

        // Suppress spurious midnight detection on first frame after load (vanilla normalizedTime may be stale)
        [NonSerialized] private bool m_SkipFirstMidnightCheck;

        // ===== War Day Tracking =====
        private bool m_WarStarted = false;
        private int m_WarStartGameDay = 0;
        private int m_WarDay = 0; // Derived from m_CurrentDay - m_WarStartGameDay (not persisted)

        // BUG-4 FIX: Cache IDistrictStateWriter for GameHour write (simulation thread)
        private IDistrictStateWriter? m_DistrictState;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Construct only what is safe before vanilla dependencies resolve:
            // m_State is needed by Deserialize (runs before OnGameLoaded) and the
            // TimeData query is query-safe regardless of TimeSystem presence.
            //
            // Do NOT publish Instance, do NOT register in SingletonRegistry, do NOT
            // resolve vanilla TimeSystem here. Activation is deferred to
            // OnGameLoaded (primary, fires per-load) with OnStartRunning /
            // OnUpdateImpl as idempotent retry fallbacks. Plan §GameTimeSystem
            // Fix Shape and §Cold-load vs hot-reload vs new-game matrix.
            m_State = new GameTimeState();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Common.TimeData>());

            Log.Info("[GameTimeSystem] Created (activation deferred to OnGameLoaded)");
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            // Primary activation site. Fires for NewGame / LoadGame / Deserialize
            // purposes via vanilla LoadGameSystem.onOnSaveGameLoaded. Retry watchdogs
            // in OnStartRunning / OnUpdateImpl cover the case where vanilla TimeSystem
            // is not yet alive here.
            _ = TryActivateTimePipeline();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // BUG-4 FIX: Cache IDistrictStateWriter for GameHour write on simulation thread.
            m_DistrictState ??= ServiceRegistry.Instance.Require<IDistrictStateWriter>();

            // Hot-reload fallback. When Mod.OnLoad re-enters an already-running
            // city world, vanilla does NOT replay OnGameLoaded — but Unity does
            // call OnStartRunning before the next OnUpdate. Idempotent; OnUpdateImpl
            // retry covers any remaining miss.
            _ = TryActivateTimePipeline();
        }

        /// <summary>
        /// Idempotent activation. Resolves vanilla TimeSystem, then publishes
        /// Instance / SingletonRegistry / WarStartedEvent subscription. Safe to
        /// call from any lifecycle hook; returns true once activated, false until
        /// the vanilla dep is reachable. Plan §GameTimeSystem Fix Shape.
        /// </summary>
        private bool TryActivateTimePipeline()
        {
            if (m_Activated)
                return true;

            m_GameTimeSystem ??= World.GetOrCreateSystemManaged<Game.Simulation.TimeSystem>();

            m_SimulationSystem = World.GetOrCreateSystemManaged<Game.Simulation.SimulationSystem>();

            // Required vanilla dep is live. Safe to publish.
            Instance = this;
            if (SingletonRegistry.IsInitialized)
                SingletonRegistry.Instance.Register<GameTimeSystem>(this, "GameTimeSystem.OnGameLoaded");

            SubscribeRequired<WarStartedEvent>(OnWarStarted);

            m_Activated = true;
            Log.Info("[GameTimeSystem] Activated (vanilla TimeSystem resolved)");
            return true;
        }

        private void DeactivateTimePipeline(string reason)
        {
            if (!m_Activated)
                return;

            UnsubscribeSafe<WarStartedEvent>(OnWarStarted);

            if (ReferenceEquals(Instance, this))
                Instance = null;
            if (SingletonRegistry.IsInitialized)
                SingletonRegistry.Instance.Unregister<GameTimeSystem>();

            m_GameTimeSystem = null;
            m_Activated = false;
            // City sim no longer live — drop the coarse crash fallback marker so a menu/load
            // crash legitimately stays Unknown, not InSimulation.
            NativeCrashBreadcrumb.ClearIfCurrent(NativeCrashMarkers.InSimulation);
            Log.Info($"[GameTimeSystem] Deactivated ({reason})");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // Symmetric to OnGameLoaded: tear down when transitioning back to
            // main menu so menu-world reads don't see a stale Instance from a
            // previously-loaded city.
            if (mode == GameMode.MainMenu)
                DeactivateTimePipeline("OnGamePreload(MainMenu)");
        }

        protected override void OnDestroy()
        {
            // Mirror DeactivateTimePipeline but force the clear regardless of
            // activation state (covers OnCreate-only paths and stripped worlds).
            UnsubscribeSafe<WarStartedEvent>(OnWarStarted);

            if (ReferenceEquals(Instance, this))
                Instance = null;
            if (SingletonRegistry.IsInitialized)
            {
                SingletonRegistry.Instance.Unregister<GameTimeSystem>();
            }
            m_GameTimeSystem = null;
            m_Activated = false;
            NativeCrashBreadcrumb.ClearIfCurrent(NativeCrashMarkers.InSimulation);
            Log.Info("[GameTimeSystem] Destroyed");
            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            // Activation watchdog. If vanilla TimeSystem missed both
            // OnGameLoaded and OnStartRunning (stripped world, late-arriving
            // dep), re-attempt every tick until it lands. Cheap when activated
            // (single bool check). Plan §GameTimeSystem Fix Shape step 4.
            if (!m_Activated && !TryActivateTimePipeline())
                return;

            var timeSystem = m_GameTimeSystem;
            if (timeSystem == null)
            {
                Log.Warn("Activated state with null m_GameTimeSystem; forcing deactivation to retry.");
                DeactivateTimePipeline("OnUpdateImpl: null timeSystem despite activation");
                return;
            }

            // PERF-LOCK: coarse always-on crash fallback. Mark every active tick but the
            // active-set dedup makes it write the marker file exactly once per session (and
            // re-assert cheaply after diagnostics enables late). Finer pipeline markers enter
            // later and override it; it resurfaces only when they clear. Never per-frame I/O.
            NativeCrashBreadcrumb.Mark(NativeCrashMarkers.InSimulation);

            // The first GameSimulation tick is the authoritative "sim is running" signal — this system
            // runs only when selectedSpeed != 0 (decompile-verified SimulationSystem.OnUpdate :273), so
            // reaching here proves the sim ticked. Promote the crash phase Loaded → ActiveSim here, NOT at
            // load-complete: pausedAfterLoading holds the sim at selectedSpeed 0 and this never runs, so the
            // phase honestly stays Loaded until the player unpauses. Idempotent SetPhase — only the first
            // tick (and the return after a Saving boundary) touches disk; per-tick callers are no-ops.
            CrashContextProvider.SetPhase(LifecyclePhase.ActiveSim);

            float normalizedTime = timeSystem.normalizedTime;
            float currentHour = normalizedTime * GameRate.HOURS_PER_DAY;

            // Detect midnight via vanilla integer day counter (immune to normalizedTime float wrap).
            // TimeSystem.GetDay uses (frame - firstFrame) / 262144 — exact, no float precision issues.
            bool midnightCrossed = false;
            if (m_TimeDataQuery.TryGetSingleton<Game.Common.TimeData>(out var timeData))
            {
                int vanillaDay = Game.Simulation.TimeSystem.GetDay(m_SimulationSystem.frameIndex, timeData);
                if (m_SkipFirstMidnightCheck || m_LastVanillaDay < 0)
                {
                    // First frame or post-load: seed the counter without detecting midnight
                    m_SkipFirstMidnightCheck = false;
                }
                else
                {
                    midnightCrossed = vanillaDay > m_LastVanillaDay;
                }
                m_LastVanillaDay = vanillaDay;
            }

            if (midnightCrossed)
            {
#pragma warning disable CIVIC226 // Game day counter — max ~365 per game year
                m_CurrentDay++;
#pragma warning restore CIVIC226
                Log.Info($"[GameTimeSystem] Day changed to {m_CurrentDay}");
            }

            // Always recompute m_WarDay from persisted fields (single source of truth)
            bool warDayChanged = false;
            if (m_WarStarted)
            {
                int newWarDay = m_CurrentDay - m_WarStartGameDay;
                warDayChanged = newWarDay != m_WarDay;
                m_WarDay = newWarDay;
            }

            m_LastNormalizedTime = normalizedTime;
            m_LastGameHour = currentHour;

            // Update state snapshot BEFORE publishing events so subscribers see current state
            UpdateStateSnapshot();

            if (midnightCrossed)
            {
                EventBus?.SafePublish(new DayChangedEvent(m_CurrentDay, currentHour), "GameTimeSystem");

                if (warDayChanged)
                {
                    Log.Info($"[GameTimeSystem] War day changed to {m_WarDay}");
                    EventBus?.SafePublish(new WarDayChangedEvent(m_WarDay, m_CurrentDay), "GameTimeSystem");
                }
            }
        }

        private void UpdateStateSnapshot()
        {
            // Deserialize fires before OnGameLoaded; m_State is constructed in
            // OnCreate so the field-null path should be impossible, but guard
            // defensively in case a stripped/test world tears the system down
            // before Deserialize completes. Without this guard, the m_State
            // null-deref in Publish would crash deserialize. Plan §BLOCKERS
            // (UpdateStateSnapshot during deserialize).
            if (m_State == null)
                return;

            float totalHours = m_CurrentDay * GameRate.HOURS_PER_DAY + m_LastGameHour;
            int warDay = m_WarStarted ? m_WarDay : -1;

            m_State.Publish(new GameTimeSnapshot(
                currentHour: m_LastGameHour,
                normalizedTime: m_LastNormalizedTime,
                currentDay: m_CurrentDay,
                totalGameHours: totalHours,
                isWarStarted: m_WarStarted,
                warDay: warDay
            ));

            // BUG-4 FIX: Write GameHour on simulation thread (was on UI thread in PowerGridUIPanel)
            if (m_DistrictState != null)
                m_DistrictState.GameHour = m_LastGameHour;
        }

        // ===== Event Handlers =====

        private void OnWarStarted(WarStartedEvent evt)
        {
            StartWar();
        }

        /// <summary>
        /// Start war day tracking.
        /// Called when WarStartedEvent is received from OminousSignsSystem.
        /// </summary>
        private void StartWar()
        {
            if (m_WarStarted)
            {
                Log.Debug("[GameTimeSystem] War already started, ignoring duplicate event");
                return;
            }

            m_WarStarted = true;
            m_WarStartGameDay = m_CurrentDay;
            m_WarDay = 0;

            Log.Info($"[GameTimeSystem] WAR STARTED on game day {m_CurrentDay}");

            // Rebuild snapshot before publishing — subscribers may read Current.WarDay
            UpdateStateSnapshot();

            // Publish initial war day event
            EventBus?.SafePublish(new WarDayChangedEvent(m_WarDay, m_CurrentDay), "GameTimeSystem");
        }

#if DEBUG
        internal void DebugEnsureWarStarted()
        {
            StartWar();
        }

        internal void DebugAdvanceDay()
        {
            m_CurrentDay = Math.Min(m_CurrentDay + 1, int.MaxValue);
            m_LastGameHour = 0f;
            m_LastNormalizedTime = 0f;
            if (m_LastVanillaDay >= 0)
                m_LastVanillaDay++;

            bool warDayChanged = false;
            if (m_WarStarted)
            {
                int newWarDay = m_CurrentDay - m_WarStartGameDay;
                warDayChanged = newWarDay != m_WarDay;
                m_WarDay = newWarDay;
            }

            UpdateStateSnapshot();
            EventBus?.SafePublish(new DayChangedEvent(m_CurrentDay, 0f), "GameTimeSystem.DebugAdvanceDay");
            if (warDayChanged)
                EventBus?.SafePublish(new WarDayChangedEvent(m_WarDay, m_CurrentDay), "GameTimeSystem.DebugAdvanceDay");
        }
#endif

    }
}
