using Colossal.Logging;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Domains.Tutorial.Systems
{
    /// <summary>
    /// Crisis-act tutorial system. Two distinct responsibilities:
    ///
    /// <para><b>1. Modal tutorials (event-driven, real UI):</b></para>
    /// Owns FirstStrike (after first wave) and ExodusWarning (population drop)
    /// modals. Both have live UI components (FirstStrikeModal.tsx,
    /// ExodusWarningModal.tsx) and go through ModalCoordinator. One-shot per
    /// Crisis act.
    ///
    /// <para><b>2. Tab-open analytics (telemetry-only, no UI):</b></para>
    /// Records the first GRID/SHADOW tab open during Crisis as telemetry events
    /// (grid_tab_first_open, shadow_tab_first_open) for the analytics server.
    /// Pre-Crisis tab opens are remembered and counted on Crisis entry, so the
    /// analytics signal is not lost if the player visited the tab before war
    /// started.
    /// No UI modal is shown — what was once a tutorial modal here was removed
    /// in commit 76e060de9 in favour of unified per-section "?" HelpSection
    /// portals (handled by HelpStateSystem, independent of this system).
    ///
    /// <para><b>Update Order:</b></para>
    /// UIUpdate reads scenario state after the GameSimulation scenario systems
    /// have published their current frame state.
    /// </summary>
    public partial class CrisisTutorialSystem : CivicUISystemBase, IDefaultSerializable, IResettable, IBootDefaultsReset, IPostLoadValidation
    {
        private static readonly LogContext Log = new("CrisisTutorial");

        // ===== Modal State (event-driven, real UI) =====
        private bool m_FirstStrikeShown;
        private bool m_ExodusWarningShown;

        // ===== Tab-Open Analytics State (telemetry-only, no UI modal) =====
        // One bit per UI signal we track for "first open in Crisis" telemetry.
        // GRID/SHADOW are the legacy pair; WAR/RADAR/DEFENSE form the air-defense
        // funnel (opened the war tab → looked at the coverage radar → went to the
        // build panel). Stored as two bitmasks instead of one bool per signal so a
        // new signal is one enum entry + one StepName row, not a copy-pasted field
        // set (Serialize/Deserialize/Reset all key off the mask).
        [System.Flags]
        private enum CrisisTab
        {
            None    = 0,
            Grid    = 1 << 0,
            Shadow  = 1 << 1,
            War     = 1 << 2,
            Radar   = 1 << 3,
            Defense = 1 << 4,
        }

        private static readonly CrisisTab[] s_AllTabs =
        {
            CrisisTab.Grid, CrisisTab.Shadow, CrisisTab.War, CrisisTab.Radar, CrisisTab.Defense,
        };

        // Total distinct tutorial steps for the crisis-summary denominator:
        // FirstStrike + ExodusWarning + the five tab/view signals.
        private const int TOTAL_TUTORIAL_STEPS = 2 + 5;

        private static string StepNameFor(CrisisTab tab) => tab switch
        {
            // None is the storage-only sentinel — never a real signal; returns empty so a stray
            // None does not crash telemetry (RecordFirstTabOpen is only ever called with a real bit).
            CrisisTab.None    => "",
            CrisisTab.Grid    => "grid_tab_first_open",
            CrisisTab.Shadow  => "shadow_tab_first_open",
            CrisisTab.War     => "war_tab_first_open",
            CrisisTab.Radar   => "radar_view_first_open",
            CrisisTab.Defense => "defense_build_first_open",
            // Combined [Flags] value is a caller bug (single bits only).
            _ => throw new System.ArgumentOutOfRangeException(nameof(tab), tab, "No StepName for this CrisisTab combination"),
        };

        // Bitmask of CrisisTab signals seen this Crisis act.
        private int m_TabsOpenedInCrisis;

        // Pre-Crisis opens are remembered and replayed on Crisis entry, so a visit
        // before war started is not lost.
        private int m_TabsOpenedPreCrisis;

        // ===== Crisis Act Tracking =====
        private bool m_CrisisActActive;
        private int m_CrisisStartDay;
        private bool m_FirstWaveEnded;
        private bool m_FirstWaveCausedDamage;

        // PERF: Throttle modal checks — the exodus warning is a once-per-act decision and
        // does not need frame resolution.
        private float m_ThrottleTimer;
        private const float CHECK_INTERVAL_SECONDS = 1.0f;

        // Telemetry: track modal show time for dismiss duration (ephemeral — not serialized)
        [System.NonSerialized] private float m_ModalShowTime;
        [System.NonSerialized] private string m_ActiveModalName = "";
        [System.NonSerialized] private bool m_NeedBootDefaultCrisisRestore;

        // Read-only view of the shared Crisis baseline owned by the Scenario domain.
        private EntityQuery m_ScenarioQuery;

        // Vanilla city population record (Core interface, Axiom 5 clean) — numerator of the
        // exodus-warning ratio. Not the raw citizen query: that one counts tourists, commuters
        // and dead-but-undeleted citizens. The record is restored from the save whole and before
        // the mod's first tick, so it never reads low merely because a load is in progress, and
        // its refusal ("no city entity") is a real answer the warning can act on.
        [System.NonSerialized] private ICityPopulationReader m_CityPopulation = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());

            // Modal dismiss triggers (FirstStrike, ExodusWarning have live UI modals)
            // Lifecycle wrap: DismissModal mutates persisted ScenarioSingleton.
            AddBinding(new TriggerBinding(Group, DismissFirstStrike,
                CivicGameLifecycle.GameplayOnly(() => DismissModal("FirstStrike"))));
            AddBinding(new TriggerBinding(Group, DismissExodusWarning,
                CivicGameLifecycle.GameplayOnly(() => DismissModal("ExodusWarning"))));

            // Tab-open analytics signals from UI (Dashboard's TutorialTabSignals component)
            // Lifecycle wrap: tab-open triggers nudge gameplay UI panels; ignore in menu.
            AddBinding(new TriggerBinding(Group, Core.UI.B.OnOpenGridTab,
                CivicGameLifecycle.GameplayOnly(() => OnOpenTab(CrisisTab.Grid))));
            AddBinding(new TriggerBinding(Group, Core.UI.B.OnOpenShadowTab,
                CivicGameLifecycle.GameplayOnly(() => OnOpenTab(CrisisTab.Shadow))));
            // Air-defense funnel: war tab → coverage radar → build panel.
            AddBinding(new TriggerBinding(Group, Core.UI.B.OnOpenWarTab,
                CivicGameLifecycle.GameplayOnly(() => OnOpenTab(CrisisTab.War))));
            AddBinding(new TriggerBinding(Group, Core.UI.B.OnOpenRadarView,
                CivicGameLifecycle.GameplayOnly(() => OnOpenTab(CrisisTab.Radar))));
            AddBinding(new TriggerBinding(Group, Core.UI.B.OnOpenDefenseBuild,
                CivicGameLifecycle.GameplayOnly(() => OnOpenTab(CrisisTab.Defense))));

            // Subscribe to orchestration events
            SubscribeRequired<ActChangedEvent>(OnActChanged);
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);
            SubscribeRequired<ModalActivatedEvent>(OnModalActivated);

            Log.Info("[CrisisTutorial] Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_CityPopulation ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ActChangedEvent>(OnActChanged);
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);
            UnsubscribeSafe<ModalActivatedEvent>(OnModalActivated);

            base.OnDestroy();
        }

        /// <summary>
        /// React to act changes — initialize Crisis state when entering Crisis
        /// act, emit telemetry summary and reset state on exit.
        /// </summary>
        private void OnActChanged(ActChangedEvent evt)
        {
#pragma warning disable CIVIC102 // Tutorial only reacts to Crisis act, other acts no-op
            if (evt.NewAct == Act.Crisis)
#pragma warning restore CIVIC102
            {
                bool wasCrisis = m_CrisisActActive;
                m_CrisisActActive = true;

                // Idempotency: skip re-init on post-load re-publish (flags already deserialized)
                if (wasCrisis) return;

                var timeProvider = GameTimeSystem.Instance;
                if (m_CrisisStartDay <= 0)
                    m_CrisisStartDay = timeProvider != null ? timeProvider.Current.CurrentDay : 0;
                m_FirstWaveEnded = false;
                m_FirstWaveCausedDamage = false;

                // Reset per-Crisis-act state
                m_FirstStrikeShown = false;
                m_ExodusWarningShown = false;
                m_TabsOpenedInCrisis = 0;

                Log.Info($"[CrisisTutorial] Crisis act started — scheduling tutorials (Day {m_CrisisStartDay}, Baseline {DescribeCrisisStartPopulation()})");

                // Pre-Crisis opens count toward in-Crisis analytics: a tab/view
                // visited before war started still fires "first open in Crisis" on
                // entry rather than being lost.
                if (m_TabsOpenedPreCrisis != 0)
                {
                    int pre = m_TabsOpenedPreCrisis;
                    m_TabsOpenedPreCrisis = 0;
                    foreach (var tab in s_AllTabs)
                    {
                        if ((pre & (int)tab) != 0 && (m_TabsOpenedInCrisis & (int)tab) == 0)
                        {
                            Log.Info($"[CrisisTutorial] Pre-Crisis {tab} open → counting on Crisis entry");
                            RecordFirstTabOpen(tab);
                        }
                    }
                }
            }
            else if (evt.PreviousAct == Act.Crisis)
            {
                m_CrisisActActive = false;

                // Telemetry: emit crisis tutorial summary before reset
                int tabsShown = 0;
                foreach (var tab in s_AllTabs)
                    if ((m_TabsOpenedInCrisis & (int)tab) != 0) tabsShown++;
                int stepsShown = (m_FirstStrikeShown ? 1 : 0) + (m_ExodusWarningShown ? 1 : 0) + tabsShown;
                int crisisDays = 0;
                var gts = GameTimeSystem.Instance;
                if (gts != null && m_CrisisStartDay > 0)
                    crisisDays = gts.Current.CurrentDay - m_CrisisStartDay;
                EventBus?.SafePublish(new TutorialCrisisSummaryEvent(stepsShown, TOTAL_TUTORIAL_STEPS, crisisDays));

                // BUG-T-007 FIX: Reset modal state for potential re-entry
                m_FirstStrikeShown = false;
                m_ExodusWarningShown = false;
                m_TabsOpenedInCrisis = 0;
                m_FirstWaveEnded = false;
                m_FirstWaveCausedDamage = false;
                m_TabsOpenedPreCrisis = 0;

                // Scoped dismiss: only Crisis tutorial modal ids that have real
                // UI. Tab-open analytics (Grid/Shadow) have no modal to dismiss.
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
                ModalCoordinator.Instance.DismissMany("FirstStrike", "ExodusWarning");
#pragma warning restore CIVIC098

                Log.Info("[CrisisTutorial] Crisis act ended — tutorial system reset");
            }
        }

        /// <summary>
        /// React to wave completion — trigger First Strike modal after first wave ends.
        /// </summary>
        private void OnWaveEnded(WaveEndedEvent evt)
        {
            if (!m_CrisisActActive) return;
            if (m_FirstWaveEnded) return;

            m_FirstWaveEnded = true;
            m_FirstWaveCausedDamage = evt.HadDamage;
            Log.Info($"[CrisisTutorial] First wave ended (wave {evt.WaveNumber}, hits: {evt.Hits}, intercepted: {evt.Intercepted}, damaged: {m_FirstWaveCausedDamage})");

            // Claim the modal slot on the SAME event Debriefing reacts to, so
            // FirstStrike (priority 60) is active before ThreatUISystem's UIUpdate
            // tick calls TryShow("Debriefing") (priority 10). Debriefing then queues
            // and shows once after FirstStrike is dismissed — no flicker, no
            // double-show. Previously FirstStrike fired from the 1s-throttled
            // CheckModalTriggers poll, which let Debriefing show first, get evicted,
            // and re-show (Debriefing → FirstStrike → Debriefing churn).
            if (m_FirstWaveCausedDamage && !m_FirstStrikeShown)
                ShowFirstStrikeModal();
        }

        protected override void OnUpdateImpl()
        {
            if (!m_CrisisActActive)
                return;

            // PERF: Throttle — CheckModalTriggers is a poll, one check per second is enough.
            m_ThrottleTimer += UnityEngine.Time.deltaTime;
            if (m_ThrottleTimer < CHECK_INTERVAL_SECONDS) return;
            m_ThrottleTimer = 0f;

            CheckModalTriggers();
        }

        public void ValidateAfterLoad()
        {
            if (m_NeedBootDefaultCrisisRestore)
            {
                m_NeedBootDefaultCrisisRestore = false;

                // W2-REG-055 self-heal restored: boot reset is field-only; ECS
                // act/baseline repair waits for post-load validation.
                bool crisisActive = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                    && actSingleton.CurrentAct == Act.Crisis;
                m_CrisisActActive = crisisActive;
                if (crisisActive)
                {
                    var timeProvider = GameTimeSystem.Instance;
                    if (m_CrisisStartDay <= 0)
                        m_CrisisStartDay = timeProvider != null ? timeProvider.Current.CurrentDay : 0;
                    Log.Info($"[CrisisTutorial] Boot-default fallback restored after load: CrisisActive={m_CrisisActActive}, Baseline={DescribeCrisisStartPopulation()}");
                }
            }

            // FirstStrike lost across save/load: the modal queue is not persisted
            // (ModalCoordinator resets on load). If the save landed between first-wave
            // end and FirstStrike activation (m_FirstStrikeShown flips only on
            // ModalActivatedEvent), re-fire it here. Previously covered by the
            // CheckModalTriggers poll, which now no longer owns FirstStrike.
            if (m_CrisisActActive && m_FirstWaveEnded && m_FirstWaveCausedDamage && !m_FirstStrikeShown)
            {
                Log.Info("[CrisisTutorial] Post-load: re-showing FirstStrike lost with the modal queue");
                ShowFirstStrikeModal();
            }
        }

        /// <summary>
        /// Check and trigger event-based modals (FirstStrike, ExodusWarning).
        /// Tab-open analytics fire from the OnOpenTab signals (GRID/SHADOW/WAR/RADAR/DEFENSE).
        /// </summary>
        private void CheckModalTriggers()
        {
            // FirstStrike is shown immediately in OnWaveEnded (same event Debriefing
            // reacts to), not polled here — see OnWaveEnded for the ordering rationale.

            // ExodusWarning - when population drops (event-based, keep). The baseline is the
            // Scenario domain's shared one; while it is uncaptured the ratio helper returns
            // false and the warning stays silent instead of firing on an unpopulated city.
            if (m_ExodusWarningShown)
                return;

            // Refusal gate. The record and the baseline are both restored from the save whole,
            // so the post-load window in which one side read as mass flight is gone; what is
            // left to gate is the one honest refusal — no city entity, so there is no number to
            // divide. Skip the tick instead of guessing: the one-second poll retries, and
            // nothing about the decision is one-shot. The re-resolve mirrors the load path
            // (ValidateAfterLoad runs before OnStartRunning).
            m_CityPopulation ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
            if (!m_CityPopulation.TryGetResidentCount(out int currentPopulation))
                return;

            // "Has anyone ever lived here" is a real recorded flag now, owned and persisted by the
            // Scenario domain, and this is the place that used to hand-roll it out of the live
            // count: `currentPopulation <= 0` answered two questions at once — "the city was
            // never settled" and "the city is already empty" — and got the first one wrong the
            // moment a settled city dipped to zero. A decision that cannot tell those apart
            // cannot be right about either.
            if (!TryGetCityEverSettled(out bool cityEverSettled) || !cityEverSettled)
                return;

            // The second question, now asked on its own terms and answered on its own merits: a
            // city standing at zero has completed its exodus, and that verdict belongs to the
            // exodus act, not to a tutorial warning about people starting to leave.
            if (currentPopulation <= 0)
                return;

            // A decision (it shows a modal once per act), so the baseline must be a measurement
            // on the same counter as the reading above; absent, superseded, or no singleton at
            // all — the warning stays silent and the one-second poll tries again.
            if (!TryGetCrisisStartPopulation(out var crisisStartPopulation))
                return;

            if (crisisStartPopulation.TryGetLostFraction(
                    currentPopulation, PopulationMeasure.VanillaCityRecord, out float populationLost)
                && populationLost >= BalanceConfig.Current.Scenario.ExodusWarningThreshold)
            {
                ShowExodusWarningModal();
            }
        }

        /// <summary>
        /// Shared Crisis population baseline, owned and persisted by the Scenario domain and
        /// republished on <see cref="ScenarioSingleton"/>. The return value separates "the
        /// scenario singleton is not there" from "no baseline has been taken yet" — until now
        /// both arrived as the same zero, and only the second of them is an ordinary state.
        /// </summary>
        /// <summary>
        /// Whether anyone has ever lived in this city, from the Scenario domain's persisted flag
        /// (<c>ScenarioState.CityEverSettled</c>, republished on the singleton). <c>false</c>
        /// return means the singleton is not there to ask — separated from an answered "never
        /// settled" for the same reason the baseline separates them: only one of the two is an
        /// ordinary state, and a caller that folds them keeps deciding on a missing answer.
        /// </summary>
        private bool TryGetCityEverSettled(out bool everSettled)
        {
            if (!m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenario))
            {
                everSettled = false;
                return false;
            }

            everSettled = scenario.CityEverSettled;
            return true;
        }

        private bool TryGetCrisisStartPopulation(out RecordedPopulation baseline)
        {
            if (!m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenario))
            {
                baseline = RecordedPopulation.NotRecorded;
                return false;
            }

            baseline = scenario.CrisisStartPopulation;
            return true;
        }

        /// <summary>
        /// The baseline as a log-friendly number: the value if anything was ever recorded,
        /// otherwise a marker. Diagnostics only — never a decision input.
        /// </summary>
        private string DescribeCrisisStartPopulation()
        {
            if (!TryGetCrisisStartPopulation(out var baseline))
                return "n/a (no scenario singleton)";

            return baseline.HasValue
                ? $"{baseline.Value} on {baseline.Measure}"
                : "not taken yet";
        }

        // ===== Tab-Open Analytics =====

        /// <summary>
        /// Called when the player opens a tracked tab/view (GRID, SHADOW, or the
        /// air-defense funnel WAR/RADAR/DEFENSE). Records "first open in Crisis"
        /// telemetry; pre-Crisis opens are remembered for replay on Crisis entry.
        /// </summary>
        private void OnOpenTab(CrisisTab tab)
        {
            if ((m_TabsOpenedInCrisis & (int)tab) != 0) return;
            if (!m_CrisisActActive)
            {
                m_TabsOpenedPreCrisis |= (int)tab;
                return;
            }
            RecordFirstTabOpen(tab);
        }

#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
        // ===== Modal Display Methods =====

        private void ShowFirstStrikeModal()
        {
            if (!ModalCoordinator.Instance.TryShow("FirstStrike"))
                return;
            Log.Info("[CrisisTutorial] Requested First Strike modal");
        }

        private void ShowExodusWarningModal()
        {
            if (!ModalCoordinator.Instance.TryShow("ExodusWarning"))
                return;
            Log.Info("[CrisisTutorial] Requested Exodus Warning modal");
        }

        // ===== Tab-Open Analytics Recording (no UI modal) =====

        private void RecordFirstTabOpen(CrisisTab tab)
        {
            m_TabsOpenedInCrisis |= (int)tab;
            string step = StepNameFor(tab);
            Log.Info($"[CrisisTutorial] First {tab} open in Crisis — analytics recorded ({step})");
            RecordStepShown(step, "tab_open");
        }

        private void OnModalActivated(ModalActivatedEvent evt)
        {
            if (evt.Id == "FirstStrike")
            {
                if (m_FirstStrikeShown) return;
                m_FirstStrikeShown = true;
                Log.Info("[CrisisTutorial] Activated First Strike modal");
                RecordStepShown("first_strike", "event");
            }
            else if (evt.Id == "ExodusWarning")
            {
                if (m_ExodusWarningShown) return;
                m_ExodusWarningShown = true;
                Log.Info("[CrisisTutorial] Activated Exodus Warning modal");
                RecordStepShown("exodus_warning", "poll");
            }
        }

        private void DismissModal(string modalId)
        {
            ModalCoordinator.Instance.Dismiss(modalId);

            // Telemetry: record dismiss with time spent
            if (!string.IsNullOrEmpty(m_ActiveModalName))
            {
                float elapsed = UnityEngine.Time.realtimeSinceStartup - m_ModalShowTime;
                EventBus?.SafePublish(new TutorialStepDismissedEvent(m_ActiveModalName, elapsed));
                m_ActiveModalName = "";
            }
        }

        private void RecordStepShown(string stepName, string trigger)
        {
            if (trigger != "tab_open")
            {
                m_ActiveModalName = stepName;
                m_ModalShowTime = UnityEngine.Time.realtimeSinceStartup;
            }
            EventBus?.SafePublish(new TutorialStepEvent(stepName, trigger));
        }

        /// <summary>
        /// BUG-T-001 FIX: Reset all serializable state to defaults.
        /// Called on new game and when save version is incompatible.
        /// </summary>
        public void ResetState()
        {
            m_FirstStrikeShown = false;
            m_ExodusWarningShown = false;
            m_TabsOpenedInCrisis = 0;
            m_TabsOpenedPreCrisis = 0;
            m_CrisisActActive = false;
            m_CrisisStartDay = 0;
            m_FirstWaveEnded = false;
            m_FirstWaveCausedDamage = false;
            m_ThrottleTimer = 0f;
            m_ModalShowTime = 0f;
            m_ActiveModalName = "";
            m_NeedBootDefaultCrisisRestore = false;
            ModalCoordinator.Instance.Reset();

            Log.Info("[CrisisTutorial] State reset");
        }

        public void ResetToBootDefaults(ResetReason reason)
        {
            m_FirstStrikeShown = false;
            m_ExodusWarningShown = false;
            m_TabsOpenedInCrisis = 0;
            m_TabsOpenedPreCrisis = 0;
            m_CrisisActActive = false;
            m_CrisisStartDay = 0;
            m_FirstWaveEnded = false;
            m_FirstWaveCausedDamage = false;
            m_ThrottleTimer = 0f;
            m_ModalShowTime = 0f;
            m_ActiveModalName = "";
            m_NeedBootDefaultCrisisRestore = false;

            Log.Info($"[BOOT-RESET] CrisisTutorialSystem reason={reason} FirstWaveCausedDamage={m_FirstWaveCausedDamage} FirstStrikeShown={m_FirstStrikeShown} ExodusWarningShown={m_ExodusWarningShown} CrisisActActive={m_CrisisActActive}");
        }
#pragma warning restore CIVIC098
    }
}
