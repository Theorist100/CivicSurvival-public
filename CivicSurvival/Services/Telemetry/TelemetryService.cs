using System;
using CivicSurvival.Core.Services;
using Game;
using Game.Common;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Threats;
using Unity.Entities;
using CivicSurvival.Services.Arena;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Telemetry orchestrator — manages lifecycle, config, and composes sub-services.
    /// Delegates batch sending to TelemetrySendPipeline, error capture and session
    /// bookkeeping to TelemetryCrashDetector, event subscriptions to TelemetryEventListener,
    /// and periodic sampling to TelemetryPulse.
    /// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    [ActIndependent]
    public partial class TelemetryService : CivicSystemBase
#pragma warning restore CA1001
    {
        private static readonly LogContext Log = new("TelemetryService");

        /// <summary>
        /// Static instance for access from static services like ErrorReportService.
        /// </summary>
        public static TelemetryService? Instance { get; private set; }

        private TelemetryConfig m_Config = null!;
        private string m_SessionId = "";

        // Subsystems (SRP)
        private TelemetryAuth m_Auth = null!;
        private TelemetryRecorder m_Recorder = null!;
        private TelemetryPersistence m_Persistence = null!;
        private TelemetryHttpClient m_Transport = null!;
        private TelemetrySendPipeline m_SendPipeline = null!;
        private TelemetryCrashDetector m_CrashDetector = null!;

        // Composed components
        private TelemetryEventListener m_EventListener = null!;
        private TelemetryPulse m_Pulse = null!;

        // Arena leaderboard reporting
        private ArenaReporter m_ArenaReporter = null!;

        // Batch timer
        private float m_TimeSinceLastSend;

        // Diagnostics readiness: the Unity-log error-capture hook is attached. Crash detection +
        // native breadcrumb are now Online-scoped (event pipeline), so this flag tracks ONLY the
        // hook. Gated by the EFFECTIVE diagnostics gate (opt-in AND Online).
        private volatile bool m_SubsystemsReady;

        // Functional event-pipeline readiness: recorder / persistence / transport /
        // sendPipeline / listener / pulse are up. Gated by Online ALONE — the chronicle /
        // stats / leaderboard fuel (game events) is functional data the player asked for by
        // enabling Online, so it must flow with diagnostics off. Separate from
        // m_SubsystemsReady so turning diagnostics off does not silence the fuel (§2).
        private volatile bool m_EventPipelineReady;

        private volatile bool m_ShuttingDown;

        // Strictly "Online is active". Single source: set from the authoritative Online
        // value (OnlineConnectionStateChangedEvent / the persisted setting at start), never
        // overloaded to mean "is the ArenaReporter instantiated". Instance existence is
        // tested via (m_ArenaReporter != null); this flag tells the diagnostics teardown
        // path whether Online still wants the functional services kept alive. m_Auth
        // (player_id) is eternal once resolved.
        private bool m_IdentityActive;

        // ECS queries (created once in OnCreate, reused on hot-reload)
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_AirDefenseQuery;
        private EntityQuery m_CurrentActQuery;
        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_ThreatStatsQuery;
        private EntityQuery m_CognitiveQuery;
        private EntityQuery m_MobilizationQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SessionId = Guid.NewGuid().ToString();
            m_Config = TelemetryConfig.Load(null);

            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_AirDefenseQuery = GetEntityQuery(
                ComponentType.ReadOnly<AirDefenseInstallation>(),
                ComponentType.ReadOnly<Simulate>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_ThreatStatsQuery = GetEntityQuery(ComponentType.ReadOnly<ThreatStatsSingleton>());
            m_CognitiveQuery = GetEntityQuery(ComponentType.ReadOnly<CognitiveState>());
            m_MobilizationQuery = GetEntityQuery(ComponentType.ReadOnly<MobilizationStateSingleton>());

            Instance = this;
            if (SingletonRegistry.IsInitialized)
                SingletonRegistry.Instance.Register<TelemetryService>(this, "TelemetryService.OnCreate");

            // Functional server identity (registration / auth_token / ArenaReporter) is
            // gated by Online, not diagnostics. React to the authoritative post-write Online
            // signal (carries the final value after the single writer persisted it) so
            // identity comes up when the player enables Online even with diagnostics off.
            // Reacting to this event instead of the raw command removes the dispatch-order
            // race: the value is already written and is carried in the event, so the order
            // in which this system and the writer subscribed no longer matters.
            SubscribeRequired<OnlineConnectionStateChangedEvent>(OnOnlineStateChanged);
        }

        private bool m_ConfigLoaded;

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            if (m_ConfigLoaded) return;
            m_ConfigLoaded = true;

            var settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_Config = TelemetryConfig.Load(settings);

            if (!m_Config.Enabled)
            {
                Log.Info(" Diagnostics disabled (opt-in required)");
                // Diagnostics off, but the functional path follows Online: if connected, bring up
                // identity AND the event pipeline (chronicle / stats / leaderboard fuel) so game
                // events still leave with diagnostics off (§2). The pipeline ALSO brings up the
                // Online-scoped crash detector + breadcrumb markers, so a diagnostics-off crash is
                // still detected and the anonymous counter still fires; only the Unity-log hook
                // (error.report) stays down with diagnostics off.
                if (m_Config.OnlineEnabled)
                {
                    EnsureIdentityAndFunctionalServices();
                    EnsureEventPipeline();
                }
                else
                {
                    // Online off ⇒ nothing leaves the machine; keep breadcrumb markers off.
                    NativeCrashBreadcrumb.SetEnabled(false);
                }
                return;
            }

            InitializeSubsystems();
        }

        /// <summary>
        /// Hot-reload: reconcile the diagnostics subsystems to the EFFECTIVE gate after the
        /// diagnostics opt-in changed in settings. The caller (OnSetTelemetryEnabled) has
        /// already persisted the new opt-in, so the reloaded config carries the effective
        /// <c>Enabled = opt-in &amp;&amp; Online</c>. The literal <paramref name="optIn"/> is
        /// the player's raw choice; the start/stop decision uses the effective value, so
        /// flipping the opt-in on while Online is off does NOT start diagnostics.
        /// </summary>
        public void SetEnabled(bool optIn)
        {
            if (!ServiceRegistry.IsInitialized)
            {
                Log.Warn(" Cannot hot-reload telemetry before ServiceRegistry is ready");
                return;
            }

            var settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_Config = TelemetryConfig.Load(settings);

            // The pulse lives in the Online-gated event pipeline and is not recreated on a
            // diagnostics toggle, so push the fresh config so its analytics-only per-record
            // gates (hardware snapshot, balance session report) see the new opt-in.
            m_Pulse?.RefreshConfig(m_Config);

            // No effective diagnostics change → nothing else to do (e.g. opt-in toggled while
            // Online is off, so effective diagnostics stays false).
            if (m_Config.Enabled == m_SubsystemsReady)
                return;

            if (m_Config.Enabled)
            {
                // The functional event pipeline is already up (Online is on, a precondition
                // of effective diagnostics). Reuse its session id so diagnostics and the
                // event batch share one session; only bring up the diagnostics-only layer.
                EnsureEventPipeline();
                StartDiagnostics();
                Log.Info(" Hot-reload: diagnostics ENABLED");
            }
            else
            {
                // Tear down ONLY the diagnostics-only layer. The functional event pipeline
                // keeps running while Online is on — turning diagnostics off must not silence
                // the chronicle / stats / leaderboard fuel (§2).
                StopDiagnostics();
                Log.Info(" Hot-reload: diagnostics DISABLED");
            }
        }

        /// <summary>
        /// Bring up the functional server-identity services gated by Online (not by
        /// diagnostics): resolve auth, run player registration to obtain auth_token, and
        /// stand up the ArenaReporter leaderboard uploader. Idempotent — safe to call
        /// from OnStartRunning, the Online toggle, or InitializeSubsystems. m_Auth is the
        /// process-wide identity (player_id eternal); it is never torn down here.
        /// </summary>
        private void EnsureIdentityAndFunctionalServices()
        {
            if (!ServiceRegistry.IsInitialized)
            {
                Log.Warn(" Cannot bring up identity before ServiceRegistry is ready");
                return;
            }

            m_Auth = ServiceRegistry.Instance.Require<TelemetryIdentityService>().GetAuth(m_Config);

            if (m_ArenaReporter == null)
                m_ArenaReporter = new ArenaReporter(m_Config, m_Auth);
            else
                m_ArenaReporter.RefreshConfig(m_Config);

            if (!m_Auth.IsRegistered && m_Config.OnlineEnabled)
            {
                m_Auth.RegisterPlayerAsync(OnPlayerRegistered);
            }

            m_IdentityActive = m_Config.OnlineEnabled;

            var idPrefix = m_Auth.PlayerId.Length >= 8 ? m_Auth.PlayerId.Substring(0, 8) : m_Auth.PlayerId;
            Log.Info(m_Config.OnlineEnabled
                ? $" Identity active (online) — player {idPrefix}..."
                : $" Identity resolved (online off — no server registration) — player {idPrefix}...");
        }

        // Did the previous-session durable-file recovery (run once when the event pipeline
        // came up) find segmented pending evidence? Captured here so the diagnostics layer —
        // which may start later than the event pipeline (Online first, opt-in after) — can
        // still feed it to crash detection without re-running recovery.
        private bool m_HadRecoverableEvidenceAtPipelineStart;

        /// <summary>
        /// Bring up the FUNCTIONAL event pipeline — recorder, persistence, transport,
        /// send pipeline, event listener, periodic pulse, and previous-session recovery.
        /// Gated by Online alone (the caller checks <see cref="TelemetryConfig.OnlineEnabled"/>):
        /// this is the chronicle / stats / leaderboard fuel the player asked for by enabling
        /// Online, independent of the diagnostics opt-in (§2). Idempotent.
        /// </summary>
        private void EnsureEventPipeline()
        {
            if (m_EventPipelineReady) return;

            m_ShuttingDown = false;

            // Identity (Online-gated) must exist first — the send pipeline needs m_Auth.
            EnsureIdentityAndFunctionalServices();

            m_Persistence = new TelemetryPersistence(m_Config);
            m_Transport = new TelemetryHttpClient(m_Config);
            m_Recorder = new TelemetryRecorder();
            m_SendPipeline = new TelemetrySendPipeline(m_Config, m_Persistence, m_Transport, m_Auth, m_Recorder, m_SessionId);

            // ORDER-INVARIANT: recovery drains previous-session durable files before the current
            // session writes anything, AND before DetectPreviousCrash below consumes the snapshot.
            m_HadRecoverableEvidenceAtPipelineStart = m_Persistence.HasRecoverablePendingEvents();
            var recovered = m_Persistence.RecoverPendingEvents();
            m_Recorder.AddRecoveredEvents(recovered);
            m_HadRecoverableEvidenceAtPipelineStart |= recovered.Count > 0;
            m_SendPipeline.RecoverRetryQueue();

            // The crash detector is Online-scoped (NOT diagnostics): it must run whenever Online is
            // on so the anonymous per-version crash counter reaches the whole audience and breadcrumb
            // markers are written even with diagnostics off. The DETAILED breadcrumb event still only
            // HTTP-sends under diagnostics (SendImmediateEvent self-gates on FileOnlyMode); only the
            // minimal counter (gated on OnlineEnabled) leaves the machine with diagnostics off. Built
            // BEFORE the EventBus early-return so crash detection runs even in the degraded path.
            m_CrashDetector = new TelemetryCrashDetector(m_Config, m_Persistence, m_Transport, m_Auth, m_Recorder, m_SessionId);
            // Robustness: also stamp the clean-shutdown flag on Unity's application-quit signal, so a
            // normal desktop exit always leaves it even if ECS OnDestroy / World teardown is skipped
            // (otherwise an honest close reads as abnormal next launch). Idempotent with the teardown
            // write. Re-subscribe defensively (-= then +=) so a pipeline restart never double-hooks.
            UnityEngine.Application.quitting -= OnApplicationQuitting;
            UnityEngine.Application.quitting += OnApplicationQuitting;
            // Stamp the running build + session into the native-crash writers BEFORE enabling them,
            // so a crash this run records the build/session that actually died — not the relaunch
            // session that recovers and reports it (sessions.mod_version is the latter).
            CrashBreadcrumbIdentity.Set(Mod.VERSION, m_SessionId);
            NativeCrashBreadcrumb.SetEnabled(true);
            m_CrashDetector.DetectPreviousCrash(m_HadRecoverableEvidenceAtPipelineStart);

            if (EventBus == null)
            {
                Log.Warn(" EventBus not ready, event pipeline subscriptions skipped");
                m_EventPipelineReady = true;
                return;
            }

            m_EventListener = new TelemetryEventListener(EventBus, m_Recorder, m_SessionId);

            m_Pulse = new TelemetryPulse(
                World,
                m_PowerGridQuery,
                m_AirDefenseQuery,
                m_CurrentActQuery,
                m_WaveStateQuery,
                m_ThreatStatsQuery,
                m_CognitiveQuery,
                m_MobilizationQuery,
                m_Recorder,
                EventBus,
                m_SessionId,
                m_Config,
                () => m_EventListener.ActiveBlackoutDistricts
            );

            var playerPrefix = m_Auth.PlayerId.Length >= 8 ? m_Auth.PlayerId.Substring(0, 8) : m_Auth.PlayerId;
            var sessionPrefix = m_SessionId.Length >= 8 ? m_SessionId.Substring(0, 8) : m_SessionId;
            Log.Info($" Event pipeline ready (online) — player {playerPrefix}..., session {sessionPrefix}...");

            m_EventPipelineReady = true;
        }

        /// <summary>
        /// Bring up the DIAGNOSTICS-only layer. The crash detector, native breadcrumb markers, and
        /// previous-crash detection are now Online-scoped (built in <see cref="EnsureEventPipeline"/>);
        /// diagnostics adds ONLY the Unity-log error-capture hook (<c>error.report</c>). Gated by the
        /// effective diagnostics gate (opt-in AND Online); requires the event pipeline up first (the
        /// detector is constructed there). Idempotent.
        /// </summary>
        private void StartDiagnostics()
        {
            if (m_SubsystemsReady) return;

            m_ShuttingDown = false;

            // Detector / breadcrumb / DetectPreviousCrash already ran in EnsureEventPipeline (Online).
            // Diagnostics adds only the Unity-log error hook.
            m_CrashDetector.AttachUnityLogHook();
            m_SubsystemsReady = true;

            Log.Info(" Diagnostics ready (opt-in + online)");
        }

        /// <summary>
        /// Tear down ONLY the diagnostics-scoped part: detach the Unity-log error hook. The crash
        /// detector instance is Online-scoped and stays alive — breadcrumb markers keep being
        /// written, and session_end + the clean-shutdown flag happen at Online teardown
        /// (<see cref="ShutdownSubsystems"/>), not on a diagnostics-off toggle. The functional event
        /// pipeline is untouched — it lives while Online is on.
        /// </summary>
        private void StopDiagnostics()
        {
            if (!m_SubsystemsReady) return;
            m_SubsystemsReady = false;

            // Detach the Unity-log hook only. Do NOT record session_end, persist, write the clean
            // flag, or dispose the detector here — the session continues (Online still on) and the
            // Online-scoped detector lives on; doing so would record a premature mid-session
            // session_end and retire breadcrumb detection while the player is still playing.
            m_CrashDetector?.DetachUnityLogHook();
        }

        /// <summary>
        /// Compose the full stack for the effective-diagnostics-on path: functional event
        /// pipeline first, then the diagnostics layer on top. Used by the Online-toggle and
        /// boot paths where opt-in AND Online are both set.
        /// </summary>
        private void InitializeSubsystems()
        {
            EnsureEventPipeline();
            StartDiagnostics();
        }

        /// <summary>
        /// Full teardown of both layers — used when Online goes off (no functional fuel may
        /// leave) and on world destroy. Tears down diagnostics (if up — which records the
        /// final session_end) AND the functional event pipeline, persisting any pending
        /// batch. The durable identity (m_Auth / player_id) and the ArenaReporter survive
        /// unless Online is no longer active (m_IdentityActive == false).
        /// </summary>
        private void ShutdownSubsystems(bool eraseConsentQueue = false)
        {
            m_ShuttingDown = true;

            // Crash-heartbeat: shutdown initiated. A crash during teardown is then classified
            // ShuttingDown (exit in progress), not an in-game fault.
            CivicSurvival.Core.Diagnostics.CrashContextProvider.SetPhase(CivicSurvival.Core.Diagnostics.LifecyclePhase.ShuttingDown);

            // Diagnostics layer: detach the Unity-log hook if it was attached (no-ops otherwise).
            if (m_SubsystemsReady)
                StopDiagnostics();

            // Online-scoped crash-detector teardown — runs whether or not diagnostics was up, so a
            // diagnostics-off clean exit STILL writes the clean-shutdown flag. Without this, a
            // diag-off session leaves no flag and the next launch reads "unclean" → false crash
            // count every launch. session_end is recorded before the batch is persisted, while
            // m_Pulse / m_Recorder are still alive (the functional teardown below disposes them).
            if (m_CrashDetector != null)
            {
                RecordSessionEndForShutdown();
            }
            if (m_Recorder != null && m_Recorder.BatchCount > 0)
            {
                m_SendPipeline?.PersistShutdownBatch();
            }
            UnityEngine.Application.quitting -= OnApplicationQuitting;
            if (m_CrashDetector != null)
            {
                m_CrashDetector.WriteCleanShutdownFlag(); // also clears the marker + persisted context
                m_CrashDetector.Dispose();                // hook already detached if diagnostics was up
                m_CrashDetector = null!;
            }
            NativeCrashBreadcrumb.SetEnabled(false);

            // Functional event pipeline teardown.
            m_EventPipelineReady = false;
            m_EventListener?.Dispose();
            m_Pulse?.Dispose();

            // Consent revoke only (Online off by the player): erase the on-disk queue before
            // dropping persistence so collected events cannot resend on a later opt-in.
            // World destroy passes false — surviving the restart and resending is legitimate.
            if (eraseConsentQueue)
                m_Persistence?.ClearAllQueued();

            m_Recorder = null!;
            m_Persistence = null!;
            m_Transport = null!;
            m_SendPipeline = null!;
            m_EventListener = null!;
            m_Pulse = null!;

            // Identity / functional services follow Online, not diagnostics. Dispose the
            // ArenaReporter here only when Online is no longer active; while Online is on the
            // player would otherwise lose the ArenaReporter / auth wiring. m_IdentityActive
            // means strictly "Online active"; instance existence is the (m_ArenaReporter !=
            // null) check below.
            if (!m_IdentityActive)
            {
                m_ArenaReporter?.Dispose();
                m_ArenaReporter = null!;
                m_Auth = null!;
            }
        }

        // Phase 1 robustness: write the clean-shutdown flag on Unity's quit signal too. ECS
        // OnDestroy / ShutdownSubsystems may be skipped on an abrupt-but-honest desktop exit,
        // which would otherwise look abnormal next launch. Idempotent with the teardown write;
        // null-safe if the detector was already torn down.
        private void OnApplicationQuitting()
        {
            // Crash-heartbeat: an abrupt-but-honest desktop quit. Mark ShuttingDown before the flag so a
            // crash in the quit path is not read as an in-game fault.
            CivicSurvival.Core.Diagnostics.CrashContextProvider.SetPhase(CivicSurvival.Core.Diagnostics.LifecyclePhase.ShuttingDown);
            m_CrashDetector?.WriteCleanShutdownFlag();
        }

        private void RecordSessionEndForShutdown()
        {
            if (m_CrashDetector == null) return;

            string exitReason = m_EventListener?.GameOverReceived == true ? "game_over" : "quit";
#pragma warning disable CIVIC005 // Telemetry fallback: session_end must record even if subsystems torn down
            float totalPlaytime = (float)(m_Pulse?.TotalSessionTime ?? 0.0);
#pragma warning restore CIVIC005

            int gameDay = 0;
            string act = "";
            int population = 0;
            long money = 0;

            try
            {
                act = (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var ca)
                        ? ca : CurrentActSingleton.Default)
                    .CurrentAct
                    .ToString()
                    .ToSnakeCase();

                var gts = Core.Systems.GameTimeSystem.Instance;
                if (gts != null) gameDay = gts.Current.CurrentDay;

                // Teardown-independent: use the balance the pulse snapshotted during play. A live
                // CityBudgetService.GetBalance here would hit the already-unregistered facade
                // (Mod.OnDispose runs before this OnDestroy) → SystemUnavailable + error log.
#pragma warning disable CIVIC005 // Telemetry fallback: session_end must record even if subsystems torn down
                money = m_Pulse?.LastKnownMoney ?? 0;
#pragma warning restore CIVIC005
                if (World.IsCreated)
                {
                    population = PopulationUtils.GetCitizenCount();
                }
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Session end state query failed: {ex}");
            }

            m_CrashDetector.RecordSessionEnd(exitReason, totalPlaytime, gameDay, act, population, money);
        }

        protected override void OnUpdateImpl()
        {
            if (m_Config == null || m_ShuttingDown) return;

            var deltaTime = UnityEngine.Time.deltaTime;

            // ArenaReporter is an Online-gated functional uploader, not diagnostics: pump it
            // whenever it exists (it self-gates its server report on OnlineEnabled + auth), so
            // leaderboard reporting works with diagnostics off but Online on.
            m_ArenaReporter?.Update(deltaTime);

            // Functional event pipeline — gated by Online (m_EventPipelineReady), NOT by the
            // diagnostics opt-in. The chronicle / stats / leaderboard fuel (game events) is
            // functional data the player asked for by enabling Online, so the pulse sampling
            // and the HTTP batch upload run with diagnostics off (§2). Per-record diagnostics-
            // only signals (hardware snapshot, balance session report) are gated inside Pulse.
            if (m_EventPipelineReady)
            {
                m_Pulse?.Update(deltaTime);

                m_TimeSinceLastSend += deltaTime;
                var shouldSend = m_TimeSinceLastSend >= m_Config.SendIntervalSeconds
                              || m_Recorder.BatchCount >= m_Config.MaxBatchSize;

                if (shouldSend)
                {
                    m_SendPipeline.SendBatch();
                    m_TimeSinceLastSend = 0;
                }
            }
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<OnlineConnectionStateChangedEvent>(OnOnlineStateChanged);

            // Force functional teardown on destroy regardless of Online state — the world
            // is going away, so the ArenaReporter / auth references must be released.
            m_IdentityActive = false;

            if (m_Config != null && (m_SubsystemsReady || m_EventPipelineReady || m_CrashDetector != null || m_Recorder != null))
            {
                ShutdownSubsystems();
            }
            else
            {
                m_ArenaReporter?.Dispose();
                m_ArenaReporter = null!;
                NativeCrashBreadcrumb.SetEnabled(false);
            }

            if (SingletonRegistry.IsInitialized)
            {
                SingletonRegistry.Instance.Unregister<TelemetryService>();
            }

            Instance = null;
            base.OnDestroy();
        }

        private void OnPlayerRegistered()
        {
            var settings = ServiceRegistry.Instance.Require<ModSettings>();
            var nickname = settings.PlayerNickname;
            if (!string.IsNullOrEmpty(nickname))
            {
                m_Auth.RegisterNicknameAsync(nickname!);
            }
        }

        /// <summary>
        /// React to the authoritative post-write Online signal. Identity / functional
        /// services are gated by Online independently of diagnostics: enabling brings up
        /// auth + ArenaReporter and registers; disabling tears down the functional uploader
        /// but keeps the durable player_id (m_Auth) alive. The Online value comes from the
        /// event (already written by the single writer), NOT from re-reading the setting —
        /// the config is loaded from settings only for the durable fields (server URL,
        /// timeouts) and its OnlineEnabled is overridden with the event's value, so the
        /// identity gate and the ArenaReporter report gate share one source.
        /// Pause-safe — runs on the UI thread that publishes the toggle (AXIOM 14);
        /// registration is fire-and-forget background.
        /// </summary>
        private void OnOnlineStateChanged(OnlineConnectionStateChangedEvent evt)
        {
            // A toggle arriving during teardown / destroy must not reanimate the
            // ArenaReporter after it was disposed. The world is going away; ignore it.
            if (m_ShuttingDown || !ServiceRegistry.IsInitialized) return;

            var settings = ServiceRegistry.Instance.Require<ModSettings>();
            m_Config = TelemetryConfig.Load(settings).WithOnlineEnabled(evt.Enabled);

            // Keep a live pulse's config current (it is not recreated when already up).
            m_Pulse?.RefreshConfig(m_Config);

            if (evt.Enabled)
            {
                // Online on ⇒ bring up the functional path regardless of diagnostics:
                // identity (player_id / auth / ArenaReporter) AND the event pipeline so the
                // chronicle / stats / leaderboard fuel starts flowing immediately (§2).
                // A fresh session id is needed before the pipeline so its events carry it.
                if (!m_EventPipelineReady)
                    m_SessionId = Guid.NewGuid().ToString();

                EnsureIdentityAndFunctionalServices();
                EnsureEventPipeline();

                // Effective diagnostics = opt-in AND Online. If the player had the
                // diagnostics opt-in set while Online was off, turning Online on now makes
                // diagnostics effective — stand up the diagnostics layer on top.
                if (m_Config.Enabled && !m_SubsystemsReady)
                {
                    StartDiagnostics();
                    Log.Info(" Diagnostics ENABLED (online on, opt-in set)");
                }
            }
            else
            {
                // Online off ⇒ nothing functional or diagnostic may leave the machine. Tear
                // down the event pipeline AND the diagnostics layer (ShutdownSubsystems does
                // both). m_IdentityActive is now false, so the functional uploader / auth are
                // disposed too. The durable player_id survives only via the persisted store.
                m_IdentityActive = false;
                if (m_EventPipelineReady || m_SubsystemsReady)
                {
                    ShutdownSubsystems(eraseConsentQueue: true);
                    Log.Info(" Functional + diagnostics DISABLED (online off)");
                }
                else
                {
                    TeardownFunctionalServices();
                }
            }
        }

        /// <summary>
        /// Tear down the Online-gated functional uploader when Online goes off and neither
        /// the event pipeline nor the diagnostics layer is running (the only caller's else
        /// branch). The durable player identity (m_Auth / player_id) persists in the global
        /// store — Online off does not erase it — but the in-memory ArenaReporter is disposed
        /// since nothing holds it any longer.
        /// </summary>
        private void TeardownFunctionalServices()
        {
            m_IdentityActive = false;

            m_ArenaReporter?.Dispose();
            m_ArenaReporter = null!;

            // Consent revoke with no live pipeline: a prior session may have left queued
            // segments on disk that would resend on a later opt-in. Erase them too.
            // The live persistence is already gone here, so bind a throwaway one to the dirs.
            (m_Persistence ?? new TelemetryPersistence(m_Config)).ClearAllQueued();

            Log.Info(" Identity functional services stopped (online off)");
        }

        /// <summary>
        /// Record a manual bug report submitted by the user. Builds the event here
        /// then delegates the immediate write/send to <see cref="TelemetryCrashDetector"/>.
        /// </summary>
        public void RecordManualReport(string userComment, string reportContent)
        {
            if (!m_Config.Enabled || !m_SubsystemsReady || m_ShuttingDown)
            {
                Log.Warn(" Cannot send manual report: telemetry unavailable");
                return;
            }

            if (m_Config.FileOnlyMode)
            {
                Log.Warn(" Manual report not sent: file-only mode (no network)");
                return;
            }

            // Manual reports go to a dedicated /manual-report endpoint, NOT the
            // telemetry-event pipeline: the full report text would otherwise be
            // rejected by the server's per-event 10KB Data cap. This path has a
            // much larger ceiling. JsonConvert handles escaping of the multi-line
            // report body.
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                SessionId = m_SessionId,
                UserComment = userComment ?? "",
                ReportContent = reportContent ?? "",
                ModVersion = Mod.VERSION,
            });

            const int manualReportTimeoutMs = 10000;
            CivicSurvival.Core.Utils.HttpUtils.PostAsync(
                m_Config.ServerUrl + "/manual-report",
                body,
                m_Auth.AuthToken,
                timeoutMs: manualReportTimeoutMs,
                maxRetries: 1,
                onComplete: result =>
                {
                    if (result.Success)
                    {
                        if (Log.IsDebugEnabled) Log.Debug(" Manual report sent");
                    }
                    else
                    {
                        Log.Warn($" Manual report send failed: {result.ErrorMessage}");
                    }
                });
            Log.Info(" Manual bug report submitted");
        }

        /// <summary>
        /// Capture the upload context for a crash-dump POST on the MAIN thread, so the heavy
        /// off-thread zip+send (<see cref="DevTools.ErrorReportService.SubmitCrashDumps"/>) touches no
        /// TelemetryService state from a background thread. Returns false when telemetry cannot send
        /// (diagnostics off, file-only, not ready, shutting down).
        /// </summary>
#pragma warning disable CA1054 // URL passed as string by design — matches HttpUtils / ServerUrl convention
        public bool TryGetCrashDumpUpload(out string serverUrl, out string authToken, out string sessionId)
#pragma warning restore CA1054
        {
            serverUrl = string.Empty;
            authToken = string.Empty;
            sessionId = m_SessionId;

            if (!m_Config.Enabled || !m_SubsystemsReady || m_ShuttingDown || m_Config.FileOnlyMode || m_Auth == null)
                return false;

            serverUrl = m_Config.ServerUrl;
            authToken = m_Auth.AuthToken;
            return !string.IsNullOrEmpty(serverUrl);
        }

    }
}
