using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using System.Diagnostics.CodeAnalysis;
using Game;
using Game.Common;
using Game.Rendering;
using Game.Simulation;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Infrastructure.Audio;
using CivicSurvival.Domains.Scenario.Data;
using CivicSurvival.Domains.Scenario.Logic;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Systems.Bootstrap;
using CivicSurvival.Core.Systems.Base;
using B = CivicSurvival.Core.UI.B;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Manages the "04:57 AM" Cold Open intro sequence.
    ///
    /// Sequence:
    /// 1. Game loads -> Pause -> Show modal
    /// 2. Player clicks "Accept Reality"
    /// 3. 2s silence -> Distant explosion -> Screen shake
    /// 4. Siren starts -> Camera pans to power plant
    /// 5. Threats spawn (1 ballistic + 3 shahed)
    /// 6. HUD reveals -> Player regains control
    ///
    /// UI Bindings:
    /// - introPhase: Current phase (for UI state)
    /// - introHudVisible: Show/hide game HUD
    ///
    /// Triggers:
    /// - onAcceptReality: Called when player clicks button
    /// </summary>
    [ActIndependent]
    [SuppressMessage("CivicSurvival", "CIVIC098", Justification = "ModalCoordinator.Instance is static readonly and never null.")]
    public partial class IntroScenarioSystem : CivicUISystemBase, IResettable
    {
        private const float CAMERA_FOCUS_ZOOM = 500f;

        // Defensive cap on how long the siren holds waiting for the threat prefabs to settle.
        // FinalizeMissing settles them at onGameLoadingComplete (well before this), so the hold
        // normally releases the instant the .cok drains; the cap only guards a stuck resolve so
        // the intro can never hang. Launching off-cap falls through to the consumer's missing
        // guard (a genuinely-absent asset), which is the honest outcome for a broken build.
        private const float MAX_SIREN_HOLD = 20f;

        private static readonly LogContext Log = new("IntroScenarioSystem");

        // ===== Dependencies (initialized in OnCreate/OnStartRunning) =====
        private SimulationSystem m_SimulationSystem = null!;
        private CameraFocusState? m_CameraService;
        private IThreatAudioService m_AudioService = null!;
        // Core bootstrap — gates the intro strike on threat-prefab readiness (resolved here,
        // not cross-domain: CivicPrefabInitSystem lives in Core, allowed per Axiom 5).
        private CivicPrefabInitSystem? m_PrefabInit;

        // FIX MED: Cached EntityQuery for power plant focus
        private EntityQuery m_PowerPlantQuery;

        // ===== State =====
        private IntroSequenceState m_State;
        [System.NonSerialized] private bool m_Initialized; // Not serialized: re-initialized in OnStartRunning
        private float m_SavedSpeed;
        private ChirperStormScheduler? m_ChirperStorm;
        [System.NonSerialized] private bool m_JustLoaded; // Not serialized: post-load flag, consumed on first update
        [System.NonSerialized] private float m_SirenHoldElapsed; // Not serialized: transient siren-hold accumulator; resets per intro and on load
        [System.NonSerialized] private bool m_NeedSpeedRestore; // FIX W6-M5: deferred speed restore after BUG-S-012 mid-intro skip
        [System.NonSerialized] private bool m_NeedDeferredIntroComplete;
        [System.NonSerialized] private bool m_NeedIntroAttackReplay;
        [System.NonSerialized] private bool m_NeedIntroModalRestore; // G10-13: deferred modal/speed restore — runtime side effect out of Deserialize
        [System.NonSerialized] private bool m_NeedIntroModalDismiss; // G10-13: deferred ModalCoordinator.Dismiss (mid-intro-skip) — out of Deserialize
        [System.NonSerialized] private bool m_NeedBootDefaultIntroSelfHeal;

        // Online-consent handoff: when the GLOBAL GRID agreement shows first, the cold-open
        // is deferred (not queued behind it) until the player resolves consent, then a short
        // pause-safe beat plays before the air-raid modal so the two modals don't slam in
        // back-to-back. All transient — a fresh-flow nicety; a mid-beat save restores the
        // intro modal directly via m_NeedIntroModalRestore.
        [System.NonSerialized] private bool m_DeferIntroForConsent;
        [System.NonSerialized] private bool m_ConsentBeatActive;
        [System.NonSerialized] private float m_ConsentBeatTimer;

        // ===== UI Bindings (initialized in OnCreate) =====
        private ProfiledBinding<int> m_IntroPhaseBinding = null!;
        private ProfiledBinding<bool> m_IntroHudVisibleBinding = null!;

        /// <summary>
        /// Reset all serializable state to defaults.
        /// Called on new game and when save version is incompatible.
        /// </summary>
        public void ResetState()
        {
            m_State = default;
            m_SavedSpeed = 1f;
            m_ChirperStorm = null;
            m_NeedDeferredIntroComplete = false;
            m_NeedIntroAttackReplay = false;
            m_NeedIntroModalRestore = false;
            m_NeedIntroModalDismiss = false;
            m_NeedSpeedRestore = false;
            m_JustLoaded = false;
            m_SirenHoldElapsed = 0f;
            m_NeedBootDefaultIntroSelfHeal = false;
            m_DeferIntroForConsent = false;
            m_ConsentBeatActive = false;
            m_ConsentBeatTimer = 0f;
            m_CameraService = null;
            ModalCoordinator.Instance.Reset();
            Log.Info("ResetState: Starting fresh");
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Initialize default state
            m_State = default;

            // ECS dependencies (always available)
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            // FIX MED: Cache EntityQuery in OnCreate instead of creating in methods
            m_PowerPlantQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.Building>(),
                ComponentType.ReadOnly<Game.Buildings.ElectricityProducer>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>()
            );

            // Create UI bindings
            m_IntroPhaseBinding = new ProfiledBinding<int>(B.Group, B.IntroPhase, 0);
            m_IntroHudVisibleBinding = new ProfiledBinding<bool>(B.Group, B.IntroHudVisible, false);

            // Register bindings
            AddBinding(m_IntroPhaseBinding.Binding);
            AddBinding(m_IntroHudVisibleBinding.Binding);

            // Register triggers. Lifecycle wrap: intro flow runs only inside a loaded
            // gameplay session; ignore stray menu calls.
            AddBinding(new TriggerBinding(B.Group, B.OnAcceptReality,
                CivicGameLifecycle.GameplayOnly(OnAcceptReality)));

            // BUG-S-020 FIX: Subscribe to ScenarioTypeDetectedEvent (single source of truth from ScenarioStateMachine)
            // instead of independently calling GetCitizenCount() which could return 0 during hot reload
            SubscribeRequired<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);

            // Online-consent handoff: when the cold-open is deferred behind the GLOBAL GRID
            // agreement, this is the signal that the player answered it (any toggle, on/off).
            SubscribeRequired<OnlineConnectionStateChangedEvent>(OnOnlineConsentResolved);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Get audio service via ServiceRegistry (no cross-domain import)
            m_AudioService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatAudioService.Instance);

            // Initialize camera service
            m_CameraService = ServiceRegistry.Instance.Require<CameraFocusState>();

            // Initialize audio
            var audioManager = ServiceRegistry.Instance.Require<AudioManager>();
            audioManager.Initialize();

            m_Initialized = true;
            Log.Info("Dependencies initialized via ServiceRegistry");
        }

        internal void ApplyPauseSafeLoadSideEffects()
        {
            if (!m_Initialized)
                return;

            // Pause-safe beat between the GLOBAL GRID agreement and the air-raid cold-open.
            // Ticks on unscaled real time (this helper runs from UIUpdate while the sim is
            // paused) so the two modals get a short breath instead of slamming in back-to-back.
            if (m_ConsentBeatActive)
            {
                m_ConsentBeatTimer -= UnityEngine.Time.unscaledDeltaTime;
                if (m_ConsentBeatTimer <= 0f)
                {
                    m_ConsentBeatActive = false;
#pragma warning disable CIVIC239 // TryShow result intentionally ignored — idempotent show
                    ModalCoordinator.Instance.TryShow("Intro");
#pragma warning restore CIVIC239
                    Log.Info("Consent beat elapsed - showing air-raid cold-open");
                }
            }

            // W2-REG-032 self-heal restored: deserialize fallback is field-only;
            // runtime-visible intro completion repair happens after load/update.
            if (m_NeedBootDefaultIntroSelfHeal)
            {
                m_NeedBootDefaultIntroSelfHeal = false;
                m_State.IntroCompleted = true;
                m_State.IsIntroPlaying = false;
                m_State.IntroPhase = IntroPhase.Done;
                m_IntroPhaseBinding.Update((int)IntroPhase.Done);
                m_IntroHudVisibleBinding.Update(true);
                m_JustLoaded = true;
                Log.Info("Boot-default intro fallback self-healed after load");
            }

            if (!m_JustLoaded)
                return;

            // FIX W6-M5: Restore player's pre-intro speed after BUG-S-012 mid-intro skip.
            // Deferred because SimulationSystem may deserialize after IntroScenarioSystem.
            if (m_NeedSpeedRestore)
            {
                m_NeedSpeedRestore = false;
                m_SimulationSystem.selectedSpeed = m_SavedSpeed;
                Log.Info($"Restored pre-intro speed {m_SavedSpeed} after mid-intro skip");
            }

            // G10-13: modal/speed restore deferred out of Deserialize (data-only).
            // This helper is called from UIUpdate too, so load-in-pause shows the
            // modal without waiting for GameSimulation to unpause.
            if (m_NeedIntroModalRestore)
            {
                m_NeedIntroModalRestore = false;
                if (m_State.IsIntroPlaying && m_State.IntroPhase == IntroPhase.Modal)
                {
#pragma warning disable CIVIC239 // Modal ownership is restored best-effort after load
                    ModalCoordinator.Instance.TryShow("Intro");
#pragma warning restore CIVIC239
                    m_SimulationSystem.selectedSpeed = 0f;
                }
            }

            if (m_NeedIntroModalDismiss)
            {
                m_NeedIntroModalDismiss = false;
                ModalCoordinator.Instance.Dismiss("Intro");
            }
        }

        protected override void OnUpdateImpl()
        {
            ApplyPauseSafeLoadSideEffects();

            // S18a-3 FIX: Only re-publish IntroCompleteEvent if act is still PreWar.
            // CrisisActCoordinator already self-syncs via m_NeedRePublishAct on load.
            // Re-publishing when act is already Crisis/Adaptation causes dual ActChangedEvent.
            if (m_JustLoaded && m_Initialized)
            {
                if (m_State.IntroCompleted)
                {
                    // M-28 FIX: defer m_JustLoaded=false until singletons confirmed available.
                    // If TryGetSingleton fails, retry next frame instead of silently skipping.
                    if (!SystemAPI.TryGetSingleton<ScenarioSingleton>(out var scenario)
                        || !SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
                    {
                        Log.Warn("Post-load sync deferred: scenario singletons not yet available");
                        return;
                    }
                    bool isPreWar = actSingleton.CurrentAct == Act.PreWar;
                    bool isVillage = scenario.ScenarioType == ScenarioType.Village;

                    if (isPreWar && !isVillage)
                    {
                        using (PerformanceProfiler.Measure("IntroScenario.PostLoadSync"))
                        {
                            if (m_NeedIntroAttackReplay)
                            {
                                m_NeedIntroAttackReplay = false;
                                Log.Info("Post-load sync: replaying IntroAttackEvent before IntroCompleteEvent");
                                EventBus?.SafePublish(new IntroAttackEvent(), "IntroScenarioSystem");
                            }

                            Log.Info("Post-load sync: publishing IntroCompleteEvent (act=PreWar)");
                            EventBus?.SafePublish(new IntroCompleteEvent(), "IntroScenarioSystem");
                        }
                    }
                    else
                    {
                        Log.Info("Post-load: skipping IntroCompleteEvent (act already past PreWar)");
                    }
                }

                m_JustLoaded = false;
            }

            if (m_NeedDeferredIntroComplete && m_Initialized)
            {
                m_NeedDeferredIntroComplete = false;
                Log.Info("Publishing deferred IntroCompleteEvent");
                EventBus?.SafePublish(new IntroCompleteEvent(), "IntroScenarioSystem");
            }

            // Update chirper storm scheduler (bounded real time, one post per update)
            if (m_ChirperStorm != null && m_ChirperStorm.IsActive)
            {
                using (PerformanceProfiler.Measure("IntroScenario.ChirperStorm"))
                {
                    m_ChirperStorm.Update(UnityEngine.Time.unscaledDeltaTime);
                }
            }

            if (!m_State.IsIntroPlaying)
                return;

            // Update intro sequence state machine
            using (PerformanceProfiler.Measure("IntroScenario.StateMachine"))
            {
                UpdateIntroSequence(UnityEngine.Time.deltaTime);
            }

            // Sync UI bindings
            m_IntroPhaseBinding.Update((int)m_State.IntroPhase);
        }

        /// <summary>
        /// BUG-S-020 FIX: React to ScenarioTypeDetectedEvent from ScenarioStateMachine.
        /// Single source of truth — no independent GetCitizenCount() that could return 0 during hot reload.
        /// Fires reliably on every save load (ScenarioStateMachine detects on first throttled update).
        /// </summary>
        private void OnScenarioTypeDetected(ScenarioTypeDetectedEvent evt)
        {
            // Already completed (deserialized from save) — nothing to do
            if (m_State.IntroCompleted || m_State.SkipIntro || m_State.IsIntroPlaying)
            {
                Log.Info($" ScenarioType={evt.Type} but intro already handled (Completed={m_State.IntroCompleted}, Skip={m_State.SkipIntro}, Playing={m_State.IsIntroPlaying})");
                return;
            }

            // Check mod settings
            var introSettings = ServiceRegistry.Instance.Require<ModSettings>();
            if (introSettings.SkipIntro)
            {
                Log.Info("Intro skipped (mod settings)");
                m_State.IntroCompleted = true;
                m_State.SkipIntro = true;
                m_IntroHudVisibleBinding.Update(true);

                // W3-H3 FIX: Town/City need IntroCompleteEvent to trigger StartWar().
                // Village uses OminousSigns → WarStartRequestEvent instead.
                if (evt.Type != ScenarioType.Village)
                {
                    m_NeedDeferredIntroComplete = true;
                }
                else
                {
                    m_IntroHudVisibleBinding.Update(true);
                }
                return;
            }

            // Village → Pre-War phase (OminousSignsSystem handles it), no Cold Open
            if (evt.Type == ScenarioType.Village)
            {
                Log.Info($" Village detected (pop={evt.Population}) - skipping Cold Open, using Pre-War phase");
                m_State.IntroCompleted = true;
                m_State.SkipIntro = true;
                m_IntroHudVisibleBinding.Update(true);
                return;
            }

            Log.Info($" {evt.Type} detected (pop={evt.Population}) - starting Cold Open intro");
            StartIntroSequence();
        }

        /// <summary>
        /// The player answered the GLOBAL GRID agreement (any Online toggle, on or off). If
        /// the cold-open was deferred behind it, start a short pause-safe beat; the air-raid
        /// then shows from ApplyPauseSafeLoadSideEffects. Ignored otherwise (e.g. a mid-game
        /// settings toggle) — guarded by the defer flag.
        /// </summary>
        private void OnOnlineConsentResolved(OnlineConnectionStateChangedEvent evt)
        {
            if (!m_DeferIntroForConsent)
                return;

            m_DeferIntroForConsent = false;
            m_ConsentBeatTimer = BalanceConfig.Current.Scenario.IntroConsentBeat;
            m_ConsentBeatActive = true;
            Log.Info($"Online consent resolved - {m_ConsentBeatTimer}s beat before air-raid cold-open");
        }

        /// <summary>
        /// Start the intro sequence.
        /// </summary>
#pragma warning disable CIVIC239 // TryShow result intentionally ignored — intro always plays
        private void StartIntroSequence()
        {
            Log.Info("Starting intro sequence...");

            // Show the cold-open now, UNLESS the GLOBAL GRID agreement is about to show
            // first (no Online decision recorded yet — OnlineConsentGateSystem shows it on
            // the same ScenarioTypeDetectedEvent). In that case defer the air-raid: queuing
            // it behind consent would make it slam in the instant the player clicks Continue.
            // OnOnlineConsentResolved then plays a short pause-safe beat before showing it.
            if (ConsentStore.Exists(ConsentKey.OnlineConnection))
            {
                ModalCoordinator.Instance.TryShow("Intro");
            }
            else
            {
                m_DeferIntroForConsent = true;
                Log.Info("Intro deferred until Online consent (GLOBAL GRID) is resolved");
            }

            // Pause game. Capture vanilla's pre-intro speed UNCONDITIONALLY so
            // CompleteIntro/SkipIntro can restore it (honors gameplaySettings.
            // pausedAfterLoading, SimulationSystem.cs:213). The old `if (m_SavedSpeed <= 0)`
            // guard plus the ResetState default of 1f meant a fresh intro never captured
            // vanilla's value (1f is not <= 0 -> skip), so the post-intro restore forced 1x
            // and ignored pausedAfterLoading=true. StartIntroSequence runs once per fresh
            // intro (OnScenarioTypeDetected early-returns once IsIntroPlaying) and a mid-intro
            // save restores via m_NeedIntroModalRestore, not here — so capturing here cannot
            // clobber a deserialized value. 0 is a valid captured value.
            m_SavedSpeed = m_SimulationSystem.selectedSpeed;
            m_SimulationSystem.selectedSpeed = 0f;
            // DIAG: log the actual vanilla setting + captured speed to confirm whether
            // selectedSpeed at capture reflects pausedAfterLoading (timing) or not.
            var pausedAfterLoading = Game.Settings.SharedSettings.instance?.gameplay?.pausedAfterLoading;
            Log.Info($"Game paused for intro (captured pre-intro speed: {m_SavedSpeed}, pausedAfterLoading={pausedAfterLoading})");

            // Hide HUD
            m_IntroHudVisibleBinding.Update(false);

            // Set state
            m_State.IsIntroPlaying = true;
            m_State.IntroPhase = IntroPhase.Modal;
            m_State.IntroTimer = BalanceConfig.Current.Scenario.IntroModalDelay;

            // Sync UI bindings immediately (OnUpdate won't run while paused)
            m_IntroPhaseBinding.Update((int)IntroPhase.Modal);

            Log.Info("Intro modal shown, waiting for player...");
        }
#pragma warning restore CIVIC239

        /// <summary>
        /// Called when player clicks "Accept Reality" button.
        /// </summary>
        private void OnAcceptReality()
        {
            Log.Info($"OnAcceptReality: IsIntroPlaying={m_State.IsIntroPlaying}, Phase={m_State.IntroPhase}");

            if (!m_State.IsIntroPlaying || m_State.IntroPhase != IntroPhase.Modal)
            {
                Log.Warn("OnAcceptReality called in wrong state");
                return;
            }

            Log.Info("Player accepted reality");

            // Intentional unpause: intro animation needs OnUpdate to tick. This is the
            // ONLY site where the mod overrides vanilla speed. Complete/SkipIntro restore
            // m_SavedSpeed (which honors gameplaySettings.pausedAfterLoading).
            m_SimulationSystem.selectedSpeed = 1f;
            Log.Info("Game unpaused for intro sequence");

            // Start silence phase
            m_State.IntroPhase = IntroPhase.Silence;
            m_State.IntroTimer = BalanceConfig.Current.Scenario.IntroSilenceDuration;

            // Sync UI binding immediately
            m_IntroPhaseBinding.Update((int)IntroPhase.Silence);
            ModalCoordinator.Instance.Dismiss("Intro");

            Log.Info("Silence phase started...");
        }

        /// <summary>
        /// Update intro sequence state machine.
        /// </summary>
        private void UpdateIntroSequence(float deltaTime)
        {
            // Tick timer. Clamp at zero so a save taken between this subtraction and the
            // phase transition never persists a small negative timer (the <= 0 checks below
            // behave identically at exactly 0).
            m_State.IntroTimer = math.max(0f, m_State.IntroTimer - deltaTime);

            switch (m_State.IntroPhase)
            {
                case IntroPhase.Modal:
                    // Waiting for player click - do nothing
                    break;

                case IntroPhase.Silence:
                    if (m_State.IntroTimer <= 0)
                    {
                        TransitionToSiren();
                    }
                    break;

                case IntroPhase.Siren:
                    if (m_State.IntroTimer > 0)
                        break; // nominal siren still playing

                    // Strike only once the threat prefab is actually present, so the first wave
                    // never fires into an unresolved .cok during the async ParadoxMods drain (the
                    // cause of the "war with no attacks" reports). In-fiction the siren just keeps
                    // wailing until the threats are inbound — no blocking, no empty screen.
                    if (ThreatPrefabsReady())
                    {
                        TransitionToAttack();
                        break;
                    }

                    // Not ready yet. Keep wailing while it is still resolving (within the hold cap).
                    // Once the resolve SETTLES without a model (genuine miss, decided at
                    // load-complete) — or the cap is hit — the model is absent this session: skip the
                    // empty strike entirely and reveal the HUD. No fake "war with no attacks"; the
                    // player is already told by the ModLoadFailure modal (FinalizeMissing).
                    // Unscaled real time, NOT the scaled deltaTime: the hold cap is a
                    // consumer-side safety net for a stalled producer. If the load sits at
                    // selectedSpeed=0 (intro / pausedAfterLoading) the scaled deltaTime is 0,
                    // so the 20s cap would never elapse and the siren could wail forever while
                    // the .cok tail is stuck. unscaledDeltaTime ticks in real time regardless
                    // of game speed (same reason m_ConsentBeatTimer / the chirper storm use it),
                    // so the cap honestly expires even when the prefab producer is frozen.
                    // The nominal phase pacing (m_State.IntroTimer) stays on scaled deltaTime.
#pragma warning disable CIVIC056 // False positive: m_SirenHoldElapsed is bounded — the branch below transitions out at MAX_SIREN_HOLD (20s) — and is reset to 0 on intro start and phase exit. Not an unbounded accumulator; float is exact at ≤20s.
                    m_SirenHoldElapsed += UnityEngine.Time.unscaledDeltaTime;
#pragma warning restore CIVIC056
                    if (ThreatPrefabsSettled() || m_SirenHoldElapsed >= MAX_SIREN_HOLD)
                    {
                        Log.Warn("Threat prefab unavailable this session — skipping the intro strike (no model); ModLoadFailure modal informs the player.");
                        TransitionToReveal();
                    }
                    break;

                case IntroPhase.Attack:
                    if (m_State.IntroTimer <= 0)
                    {
                        TransitionToReveal();
                    }
                    break;

                case IntroPhase.Reveal:
                    if (m_State.IntroTimer <= 0)
                    {
                        CompleteIntro();
                    }
                    break;

                default:
                    Log.Warn($"Unhandled {nameof(IntroPhase)}: {m_State.IntroPhase}");
                    break;
            }
        }

        /// <summary>
        /// Phase 2->3: Silence -> Siren
        /// </summary>
        private void TransitionToSiren()
        {
            Log.Info("Phase: SIREN");

            using (PerformanceProfiler.Measure("IntroScenario.StartSiren"))
            {
                // Start air raid siren (native CS2 SFX via IAudioEffectService)
                m_AudioService.ForceStartSiren();
            }

            using (PerformanceProfiler.Measure("IntroScenario.FocusCamera"))
            {
                // Focus camera on power infrastructure
                FocusOnPowerPlant();
            }

            m_State.IntroPhase = IntroPhase.Siren;
            m_State.IntroTimer = BalanceConfig.Current.Scenario.IntroSirenDelay;
            m_SirenHoldElapsed = 0f;
        }

        /// <summary>
        /// True once the core threat prefabs are resolved or load-complete has decided they are
        /// genuinely absent. Resolves the Core bootstrap system lazily. A missing system (never
        /// expected in a loaded session) does not block the intro — defaults to ready.
        /// </summary>
        private bool ThreatPrefabsSettled()
        {
            m_PrefabInit ??= World.GetExistingSystemManaged<CivicPrefabInitSystem>();
            return m_PrefabInit == null || m_PrefabInit.CoreThreatPrefabsSettled;
        }

        /// <summary>
        /// True once the AttackDrone prefab is actually resolved (a wave can spawn renderable
        /// threats). Distinct from <see cref="ThreatPrefabsSettled"/>, which is also true when the
        /// model was decided genuinely absent — the intro fires on Ready, stops waiting on Settled.
        /// A missing system (never expected in a loaded session) does not block the intro.
        /// </summary>
        private bool ThreatPrefabsReady()
        {
            m_PrefabInit ??= World.GetExistingSystemManaged<CivicPrefabInitSystem>();
            return m_PrefabInit == null || m_PrefabInit.CoreThreatPrefabsReady;
        }

        /// <summary>
        /// Phase 4->5: Siren -> Attack
        /// </summary>
        private void TransitionToAttack()
        {
            Log.Info("Phase: ATTACK");

            using (PerformanceProfiler.Measure("IntroScenario.SpawnWave"))
            {
                // Intro wave routed through standard flow: WaveScheduler calculates threat count,
                // publishes ScheduleWaveCommand → WaveExecutor handles Alert/Attack/Recovery
                EventBus?.SafePublish(new IntroAttackEvent(), "IntroScenarioSystem");
                Log.Info("Published IntroAttackEvent → WaveScheduler will schedule intro wave");
            }

            m_State.IntroPhase = IntroPhase.Attack;
            m_State.IntroTimer = BalanceConfig.Current.Scenario.IntroAttackDelay;
        }

        /// <summary>
        /// Phase 5->6: Attack -> Reveal
        /// </summary>
        private void TransitionToReveal()
        {
            Log.Info("Phase: REVEAL");

            // Show HUD
            m_IntroHudVisibleBinding.Update(true);

            m_State.IntroPhase = IntroPhase.Reveal;
            m_State.IntroTimer = BalanceConfig.Current.Scenario.IntroRevealDelay;
        }

        /// <summary>
        /// Phase 6->Done: Complete intro sequence.
        /// </summary>
        private void CompleteIntro()
        {
            Log.Info("Intro sequence COMPLETE");

            // Stop siren if playing (started during siren phase)
            m_AudioService.ForceStopSiren();

            m_State.IsIntroPlaying = false;
            m_State.IntroCompleted = true;
            m_State.IntroPhase = IntroPhase.Done;

            // Restore vanilla pre-intro speed — honors gameplaySettings.pausedAfterLoading
            // (SimulationSystem.cs:213) and any manual pause before intro started.
            // 0 is a valid restore value; do NOT fall back to 1f.
            m_SimulationSystem.selectedSpeed = m_SavedSpeed;

            // Update UI
            m_IntroPhaseBinding.Update((int)IntroPhase.Done);

            // Release coordinator BEFORE publishing event — WarStartedEvent fires synchronously
            // in the event chain, and MilestoneTutorialSystem.OnWarStarted needs the slot free
            ModalCoordinator.Instance.Dismiss("Intro");

            using (PerformanceProfiler.Measure("IntroScenario.PublishComplete"))
            {
                // IntroScenarioSystem is the single publisher of IntroCompleteEvent.
                // ScenarioStateMachine.StartWar has guard (CurrentAct != PreWar → return)
                // in case OminousSigns already started war via WarStartRequestEvent.
                EventBus?.SafePublish(new IntroCompleteEvent(), "IntroScenarioSystem");
                Log.Info("Published IntroCompleteEvent");
            }

            // Start chirper storm with scheduler (posts appear over 90 seconds)
            m_ChirperStorm = new ChirperStormScheduler();
            m_ChirperStorm.StartStorm();
        }


        /// <summary>
        /// Focus camera on main power plant.
        /// </summary>
        private void FocusOnPowerPlant()
        {
            if (m_CameraService == null)
            {
                Log.Warn(" CameraService not available");
                return;
            }

            if (m_PowerPlantQuery.IsEmptyIgnoreFilter)
            {
                Log.Warn(" No power plants found for camera focus");
                return;
            }

#pragma warning disable S1751 // ECS has no .First() — foreach+break is the idiom for "get first match"
            foreach (var transform in SystemAPI.Query<RefRO<Game.Objects.Transform>>()
                .WithAll<Game.Buildings.Building, Game.Buildings.ElectricityProducer>()
                .WithNone<Game.Common.Deleted>())
            {
                var pos = transform.ValueRO.m_Position;
                m_CameraService.FocusOnPosition(pos, zoom: CAMERA_FOCUS_ZOOM);
                Log.Info($"Camera focused on power plant at ({pos.x:F0}, {pos.z:F0})");
                break;
            }
#pragma warning restore S1751
        }

        /// <summary>
        /// Skip intro sequence (for testing or settings).
        /// </summary>
        public void SkipIntro()
        {
            if (!m_State.IsIntroPlaying) return;

            Log.Info("Skipping intro...");

            // Stop siren if playing (native CS2 SFX via IAudioEffectService)
            m_AudioService.ForceStopSiren();

            // Show HUD
            m_IntroHudVisibleBinding.Update(true);

            // Restore vanilla pre-intro speed — honors gameplaySettings.pausedAfterLoading
            // (SimulationSystem.cs:213) and any manual pause before intro started.
            // 0 is a valid restore value; do NOT fall back to 1f.
            m_SimulationSystem.selectedSpeed = m_SavedSpeed;

            // Mark as complete
            m_State.IsIntroPlaying = false;
            m_State.IntroCompleted = true;
            m_State.IntroPhase = IntroPhase.Done;

            // Release coordinator BEFORE publishing event (same reason as CompleteIntro)
            ModalCoordinator.Instance.Dismiss("Intro");

            // BUG-SCEN-001 FIX: Publish IntroCompleteEvent so other systems can react
            // (CrisisActCoordinator, GameTimeSystem, WaveScheduler, ScenarioStateMachine)
            EventBus?.SafePublish(new Core.Events.IntroCompleteEvent(), "IntroScenarioSystem");
            Log.Info("Published IntroCompleteEvent after skip");

            // W4-H1 FIX: Start chirper storm (same as CompleteIntro)
            m_ChirperStorm = new Logic.ChirperStormScheduler();
            m_ChirperStorm.StartStorm();
        }

        /// <summary>
        /// Check if intro is currently playing.
        /// </summary>
        public bool IsIntroPlaying => m_State.IsIntroPlaying;

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            UnsubscribeSafe<OnlineConnectionStateChangedEvent>(OnOnlineConsentResolved);
            m_ChirperStorm?.Stop();
            base.OnDestroy();
            Log.Info("Destroyed");
        }
    }
}
