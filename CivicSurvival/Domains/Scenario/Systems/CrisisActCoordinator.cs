using Game;
using Unity.Entities;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Scenario.Systems
{
    /// <summary>
    /// Coordinates Crisis act lifecycle (first 3-7 days of war).
    /// Pure orchestration — no economics, no UI, no finance.
    ///
    /// Responsibilities:
    /// - Track Crisis act state (active/inactive)
    /// - Check exit conditions (Exodus vs Adaptation)
    /// - Publish ActChangedEvent when transitioning
    /// - Track statistics (waves survived)
    ///
    /// Does NOT:
    /// - Implement economics (see CrisisEconomicsSystem in Economics domain)
    /// - Show UI (see CrisisTutorialSystem in Tutorial domain)
    /// - Spawn refugees (see RefugeeInfluxCoordinator in Refugees domain)
    /// - Handle finance (banking withdrawals removed)
    ///
    /// Transitions:
    /// - Crisis → Exodus: When the remaining share falls to the size-scaled exodus threshold,
    ///   measured against the shared baseline ScenarioStateMachine captures on Crisis entry
    ///   (ScenarioSingleton.CrisisStartPopulation) and confirmed across a game-day boundary.
    ///   A baseline that was never taken, or was taken with a counter this build no longer
    ///   uses, yields no verdict — see RecordedPopulation.
    /// - Crisis → Adaptation: After 7 days + 3 waves survived
    ///
    /// PERF: Throttled to 1Hz. Population and day checks don't need 60fps.
    /// </summary>
    public partial class CrisisActCoordinator : ThrottledSystemBase, IPostLoadValidation
    {
        private static readonly LogContext Log = new("CrisisActCoordinator");

        // ===== State Tracking =====
        private bool m_CrisisActive;
        private int m_CrisisStartDay;
        private bool m_HasCrisisStartDay;
        private int m_CurrentDay;
        private int m_WavesSurvived;
        // Baseline lives in ScenarioState (projected on ScenarioSingleton.CrisisStartPopulation)
        // so the coordinator and the tutorial measure the same city against the same number.
        // The numerator comes from the one owner of the population measure, not the raw citizen
        // query — see TryGetResidentPopulation for why, and why "no reading" is a distinct state.
        private int m_CurrentPopulation;
        // Recomputed by every UpdateState before it is read; a stale "yes" restored from a
        // save would let the first post-load evaluation answer with last session's number.
        [System.NonSerialized] private bool m_HasPopulationReading;
        private int m_IntroWaveThreatCount;

        // S18a-6 FIX: Defer act transition until wave ends (Attack/Alert → Recovery → execute)
        private bool m_HasPendingTransition;
        private Act m_PreviousAct;

        // Day-boundary confirmation of the exodus verdict: the day the condition was first
        // observed, and whether an observation is currently pending. Persisted so a save/load
        // inside the confirmation window does not restart the wait — a player who reloads
        // more often than once per game day would otherwise never reach the verdict.
        private int m_ExodusPendingSinceDay;
        private bool m_HasExodusPendingSince;

        // Population source — the single owner of the measure. The producer registers the
        // service in its OnCreate, i.e. before any consumer update, so the contract is
        // mandatory-present and its refusal — not a defensive resolve — is what withholds a
        // verdict.
        [System.NonSerialized] private ICityPopulationReader m_CityPopulationReader = null!;

        // Banking-collapse satire chirp: one-shot per Crisis act at Day 1, 08:00.
        // Hour is fixed (was Scenario.BankingCollapseHour in older balance contract,
        // dropped from C# generators when the modal trigger was removed in commit
        // a23ba2274 — chirp text remains as flavour and reuses the same hour).
        private const float BANKING_CHIRP_HOUR = 8f;
        private bool m_BankingChirpSent;

        // Not serialized: post-load deferred action flag, consumed in first OnUpdateImpl, reset in ResetState
        [System.NonSerialized] private bool m_NeedRePublishAct;

        // ===== Dependencies (initialized in OnCreate) =====
        private GameTimeSystem? m_TimeProvider;
        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_ScenarioQuery;
        private EntityQuery m_ActSingletonQuery;

        // PERF: 1Hz throttle — population and day thresholds don't need per-frame checks
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        /// <summary>Is Crisis Act currently active?</summary>
        public bool IsActive => m_CrisisActive;

        protected override void OnCreate()
        {
            base.OnCreate();

            // GameTimeSystem owns the time cache; post-load replay gates on Instance
            // instead of throwing before the owner has published.
            m_TimeProvider = GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                Log.Warn(" GameTimeSystem not registered yet - will retry on update");
            }

            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_ActSingletonQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            // Subscribe to orchestration events
            SubscribeRequired<ScheduleWaveCommand>(OnScheduleWaveCommand);
            SubscribeRequired<WarStartedEvent>(OnWarStarted);
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);

            Log.Info(" Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Canonical resolve point. TryGetResidentPopulation repeats the ??= because
            // ValidateAfterLoad can run before this hook.
            m_CityPopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<ScheduleWaveCommand>(OnScheduleWaveCommand);
            UnsubscribeSafe<WarStartedEvent>(OnWarStarted);
            UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);

            m_TimeProvider = null;  // Clear injected dependency

            base.OnDestroy();
        }

        /// <summary>
        /// Capture threat count from the intro Alert command (published by WaveScheduler).
        /// Used for FirstStrikeCascadeEvent narrative instead of hardcoded config.
        /// </summary>
        private void OnScheduleWaveCommand(ScheduleWaveCommand cmd)
        {
            ForceNextUpdate();
            if (cmd.WaveRole == WaveRole.Intro && cmd.TargetPhase == GamePhase.Alert && cmd.ThreatsExpected > 0)
            {
                m_IntroWaveThreatCount = cmd.ThreatsExpected;
                if (Log.IsDebugEnabled) Log.Debug($" Captured intro wave threat count: {m_IntroWaveThreatCount}");
            }
        }

        /// <summary>
        /// Handle WarStartedEvent — start Crisis Act when war begins.
        /// Published by ScenarioStateMachine.StartWar (single source of truth).
        /// NOTE: Checks Enabled to respect UI toggle.
        /// </summary>
        private void OnWarStarted(WarStartedEvent evt)
        {
            if (!Enabled)
            {
                Log.Info(" Received WarStartedEvent but system is DISABLED - ignoring");
                return;
            }

            Log.Info(" Received WarStartedEvent");
            StartCrisisAct();
        }

        /// <summary>
        /// Handle WaveEndedEvent — count waves survived for Adaptation transition.
        /// NOTE: Checks Enabled to respect UI toggle.
        /// </summary>
        private void OnWaveEnded(WaveEndedEvent evt)
        {
            // BUG FIX: Respect UI toggle
            if (!Enabled || !m_CrisisActive) return;
            if (evt.WaveRole == WaveRole.Intro) return; // Intro is not a real defense

#pragma warning disable CIVIC226 // Bounded by scenario wave count
            m_WavesSurvived++;
#pragma warning restore CIVIC226
            Log.Info($"[CrisisActCoordinator] Wave {evt.WaveNumber} ended, survived count: {m_WavesSurvived}");

            // S18a-6 FIX: Execute deferred act transition now that wave ended
            if (m_HasPendingTransition)
            {
                m_HasPendingTransition = false;

                // Re-evaluate with fresh data — population may have recovered during wave
                UpdateState();
                var targetAct = EvaluateTargetAct();

                if (targetAct == null)
                {
                    Log.Info(" Deferred act transition cancelled — conditions no longer met");
                    return;
                }

                Log.Info($" Executing deferred act transition to {targetAct.Value} (wave ended)");
                ExecuteTransition(targetAct.Value);
            }
        }

        protected override void OnThrottledUpdate()
        {
            // Fallback retry if post-load validation ran before GameTimeSystem was available.
            if (m_NeedRePublishAct)
            {
                if (!TryRunPostLoadActReplay())
                    return;
            }

            if (!m_CrisisActive)
                return;

            UpdateState();
            CheckBankingChirp();
            CheckActTransitions();
        }

        public void ValidateAfterLoad()
        {
            if (!m_NeedRePublishAct)
                return;

            // Discard is intentional: if GameTimeSystem isn't active yet the replay
            // returns false, keeps m_NeedRePublishAct set and ForceNextUpdate-arms the
            // OnThrottledUpdate fallback to retry. Nothing here depends on the result.
            _ = TryRunPostLoadActReplay();
        }

        /// <summary>
        /// Publish the banking-collapse satire chirp once at Day 1, 08:00 of the
        /// current Crisis act. Spreads narrative away from the entry burst
        /// (ShockStarted + ActShock + FirstStrikeCascade) and gives the player a
        /// quiet morning beat that explains why card payments stopped working.
        /// </summary>
        private void CheckBankingChirp()
        {
            if (m_BankingChirpSent) return;
            if (m_TimeProvider == null) return;
            if (!m_HasCrisisStartDay || m_CurrentDay != m_CrisisStartDay) return;
            if (m_TimeProvider.Current.CurrentHour < BANKING_CHIRP_HOUR) return;

            m_BankingChirpSent = true;
            EventBus?.SafePublish(
                new NarrativeTriggerEvent(NarrativeTrigger.ShockBanking.ToKey()),
                "CrisisActCoordinator");
            Log.Info($"[CrisisActCoordinator] Published ShockBanking chirp (Day {m_CurrentDay}, Hour {m_TimeProvider.Current.CurrentHour:F1})");
        }

        /// <summary>
        /// Start Crisis Act (called when intro completes).
        /// Publishes FirstStrikeCascadeEvent, ExodusRateOverrideFractionCommand, NarrativeTriggerEvent.
        /// ActChangedEvent is NOT published here — ScenarioStateMachine.TransitionToAct handles it.
        /// </summary>
        public void StartCrisisAct()
        {
            if (m_CrisisActive)
            {
                Log.Warn(" Crisis Act already active");
                return;
            }

            m_CrisisActive = true;
            m_PreviousAct = Act.PreWar;
            m_CrisisStartDay = GetCurrentDay();
            m_HasCrisisStartDay = GameDayStamp.TryCreate(m_CrisisStartDay, out _);
            m_WavesSurvived = 0;
            m_BankingChirpSent = false;
            m_HasExodusPendingSince = false;
            m_ExodusPendingSinceDay = 0;

            // The population baseline is captured by ScenarioStateMachine on the Crisis
            // transition and republished on ScenarioSingleton, so it survives load and is
            // shared with the tutorial. An unpopulated city leaves it uncaptured (0) and
            // EvaluateTargetAct holds the exodus verdict until citizens actually exist.
            // The baseline is deliberately not printed here: this method runs on
            // WarStartedEvent, which the state machine publishes BEFORE the act transition
            // that captures the number, so any value read here is the stale pre-capture one.
            // The state machine logs the baseline at the moment it becomes real.
            Log.Info($"[CrisisActCoordinator] CRISIS ACT STARTED - Day {m_CrisisStartDay}");

            // NOTE: ActChangedEvent is NOT published here — ScenarioStateMachine.TransitionToAct
            // is the single source of truth for act transitions and already publishes it
            // when handling the same IntroCompleteEvent (via StartWar → TransitionToAct).

            // Publish FirstStrikeCascadeEvent for narrative systems (Tutorial, Narrative, Statistics)
            // NOTE: Actual damage is done by drones via ThreatDamageSystem, not by scripted FirstStrike
            // Count comes from WaveScheduler via ScheduleWaveCommand (intro Alert phase)
            EventBus?.SafePublish(new FirstStrikeCascadeEvent(m_IntroWaveThreatCount), "CrisisActCoordinator");
            Log.Info($" Published FirstStrikeCascadeEvent for narrative ({m_IntroWaveThreatCount} drones)");
            m_IntroWaveThreatCount = 0;

            // Override exodus rate to 4%/day during Crisis Act
            EventBus?.SafePublish(new ExodusRateOverrideFractionCommand(BalanceConfig.Current.Scenario.ShockExodusRate), "CrisisActCoordinator");

            // Post social feed message
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ShockStarted.ToKey()), "CrisisActCoordinator");
            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ActShock.ToKey()), "CrisisActCoordinator");

        }

        /// <summary>
        /// Update Crisis Act state each frame.
        /// </summary>
        private void UpdateState()
        {
            m_CurrentDay = GetCurrentDay();
            m_HasPopulationReading = TryGetResidentPopulation(out m_CurrentPopulation);
        }

        /// <summary>
        /// People living in the city right now, from the single owner of that measure. The
        /// exodus verdict divides a captured baseline by this live counter, and the raw citizen
        /// query is the wrong counter for that: it includes tourists, commuters and
        /// dead-but-not-deleted citizens.
        ///
        /// <c>false</c> means "cannot answer" — there is no city entity — never "empty city":
        /// the caller withholds the verdict instead of reading a refusal as a completed exodus.
        /// A city that really is empty answers <c>true</c> with zero, and the baseline gate
        /// (an uncaptured baseline yields no verdict) is what keeps a not-yet-populated city
        /// from being read as one that emptied out.
        ///
        /// Note the baseline it is compared against: a baseline captured before this owner
        /// existed was recorded on the previous, wider measure, so the remaining share reads
        /// low on such saves until the migration phase rebases it.
        /// </summary>
        private bool TryGetResidentPopulation(out int population)
        {
            // Require, not TryGet: the interface is feature-owned without a null object, and
            // CIVIC463 rejects a defensive TryGet here as masking a dependency-order problem.
            // The producer registers the service in its OnCreate, ahead of any consumer update.
            m_CityPopulationReader ??= ServiceRegistry.Instance.Require<ICityPopulationReader>();
            return m_CityPopulationReader.TryGetResidentCount(out population);
        }

        /// <summary>
        /// Check if Crisis Act should transition to next act.
        /// </summary>
        private void CheckActTransitions()
        {
            // S18a-6: Already deferred — wait for OnWaveEnded
            if (m_HasPendingTransition)
                return;

            var targetAct = EvaluateTargetAct();
            if (targetAct == null)
                return;

            // S18a-6 FIX: Defer transition if wave is in Attack or Alert phase
            if (IsWaveActive())
            {
                m_HasPendingTransition = true;
                Log.Info($" Act transition to {targetAct.Value} deferred — wave in progress");
                return;
            }

            ExecuteTransition(targetAct.Value);
        }

        /// <summary>
        /// Evaluate which act transition (if any) should fire right now.
        /// Single source of truth — used by both CheckActTransitions and OnWaveEnded re-validation.
        /// Exodus takes priority over Adaptation.
        /// </summary>
        private Act? EvaluateTargetAct()
        {
            if (!m_HasCrisisStartDay)
                return null;

            int daysSinceStart = m_CurrentDay - m_CrisisStartDay;

            // R4-T2-07 FIX: Use scenario-appropriate crisis duration (was hardcoded to City=7).
            // Village=3, Town=5, City=7 — affects when Crisis→Adaptation transition fires.
            var scenarioCfg = BalanceConfig.Current.Scenario;
            int crisisDuration = scenarioCfg.ShockDaysCity;
            if (m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenarioForDuration))
            {
                switch (scenarioForDuration.ScenarioType)
                {
                    case ScenarioType.Village: crisisDuration = scenarioCfg.ShockDaysVillage; break;
                    case ScenarioType.Town:    crisisDuration = scenarioCfg.ShockDaysTown;    break;
                    case ScenarioType.City:    crisisDuration = scenarioCfg.ShockDaysCity;     break;
                    default:
                        Log.Warn($"Unknown ScenarioType {scenarioForDuration.ScenarioType}, defaulting to City");
                        crisisDuration = scenarioCfg.ShockDaysCity;
                        break;
                }
            }

            // Check for Exodus transition. A baseline that was never captured (city
            // unpopulated when Crisis began) yields no verdict at all — the act stays put
            // rather than reading an empty city as a completed exodus.
            if (IsExodusVerdictConfirmed(scenarioCfg.ExodusActThreshold))
                return Act.Exodus;

            // Check for Adaptation transition (early success OR natural end)
            // BUG-S-004 FIX: Combine conditions to prevent duplicate transitions
            if (daysSinceStart >= crisisDuration ||
                (daysSinceStart >= scenarioCfg.AdaptationTriggerDays &&
                 m_WavesSurvived >= scenarioCfg.AdaptationWavesRequired))
            {
                return Act.Adaptation;
            }

            return null;
        }

        /// <summary>
        /// Exodus verdict with its day-boundary confirmation. The act change is irreversible
        /// from the coordinator's side, while the population reading is a sampled quantity
        /// that dips and recovers within a single act, so one sample may never decide it: the
        /// condition has to be observed and then still hold once the game day has rolled over.
        /// A sample that fails clears the pending observation, so the window always measures
        /// one continuous stretch rather than a count of scattered hits.
        ///
        /// The threshold itself is scaled to the size of the city (RecordedPopulation), so a
        /// village is not sent into exodus by the departure of a handful of people.
        /// </summary>
        private bool IsExodusVerdictConfirmed(float configuredThreshold)
        {
            if (!TryGetCrisisStartPopulation(out var crisisStartPopulation))
            {
                Log.Warn("[CrisisActCoordinator] Exodus verdict withheld — the scenario singleton is missing, so there is no baseline to measure against (this is a wiring fault, not an uncaptured baseline)");
                return false;
            }

            // A verdict: the baseline has to be a measurement on the same counter as the live
            // reading. Absent or superseded — no verdict, the act stays where it is.
            if (!m_HasPopulationReading
                || !crisisStartPopulation.TryGetScaledRemaining(
                        m_CurrentPopulation,
                        PopulationMeasure.VanillaCityRecord,
                        configuredThreshold,
                        out float populationRemaining,
                        out float scaledThreshold)
                || populationRemaining > scaledThreshold)
            {
                if (m_HasExodusPendingSince)
                {
                    m_HasExodusPendingSince = false;
                    Log.Info($"[CrisisActCoordinator] Exodus condition lapsed before the day boundary (observed on day {m_ExodusPendingSinceDay}) — confirmation restarts");
                }
                return false;
            }

            if (!m_HasExodusPendingSince)
            {
                m_HasExodusPendingSince = true;
                m_ExodusPendingSinceDay = m_CurrentDay;
                Log.Info($"[CrisisActCoordinator] Exodus condition observed on day {m_CurrentDay}: {populationRemaining:P1} remaining (threshold {scaledThreshold:P1}) — holding until the day boundary");
                return false;
            }

            if (m_CurrentDay < m_ExodusPendingSinceDay)
            {
                // The day counter moved backwards (an older save was loaded onto this
                // instance). Re-arm on the current day instead of confirming on a stamp
                // that belongs to a future the city no longer has.
                m_ExodusPendingSinceDay = m_CurrentDay;
                return false;
            }

            if (m_CurrentDay == m_ExodusPendingSinceDay)
                return false;

            Log.Info($"[CrisisActCoordinator] Exodus threshold confirmed across the day boundary: {populationRemaining:P1} remaining (threshold {scaledThreshold:P1}), held from day {m_ExodusPendingSinceDay} to day {m_CurrentDay}");
            return true;
        }

        private void ExecuteTransition(Act targetAct)
        {
            if (targetAct == Act.Exodus)
                TransitionToExodus();
            else
                TransitionToAdaptation();
        }

        /// <summary>
        /// Check if a wave is currently in Attack or Alert phase (not safe for act transition).
        /// </summary>
        private bool IsWaveActive()
        {
            if (!m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState))
                return false;
            return waveState.CurrentPhase == GamePhase.Attack || waveState.CurrentPhase == GamePhase.PsyAttack || waveState.CurrentPhase == GamePhase.Alert;
        }

        /// <summary>
        /// Transition from Crisis to Exodus act.
        /// </summary>
        private void TransitionToExodus()
        {
            Log.Info(" Transitioning to EXODUS act");
            m_PreviousAct = Act.Crisis;
            m_CrisisActive = false;

            // NOTE: ActChangedEvent is published by ScenarioStateMachine.TransitionToAct
            // (triggered by ActTransitionRequestEvent below). Do NOT publish here — causes double event.

            // Clear exodus rate override
            EventBus?.SafePublish(new ExodusRateOverrideFractionCommand(0f), "CrisisActCoordinator");

            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ActExodus.ToKey()), "CrisisActCoordinator");

            // Request act transition (ScenarioDirectorSystem subscribes)
            EventBus?.SafePublish(new ActTransitionRequestEvent(Act.Exodus), "CrisisActCoordinator");
        }

        /// <summary>
        /// Transition from Crisis to Adaptation act.
        /// </summary>
        private void TransitionToAdaptation()
        {
            Log.Info(" Transitioning to ADAPTATION act");
            m_PreviousAct = Act.Crisis;
            m_CrisisActive = false;

            // NOTE: ActChangedEvent is published by ScenarioStateMachine.TransitionToAct
            // (triggered by ActTransitionRequestEvent below). Do NOT publish here — causes double event.

            // Clear exodus rate override
            EventBus?.SafePublish(new ExodusRateOverrideFractionCommand(0f), "CrisisActCoordinator");

            EventBus?.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ActAdaptation.ToKey()), "CrisisActCoordinator");

            // Request act transition (ScenarioDirectorSystem subscribes)
            EventBus?.SafePublish(new ActTransitionRequestEvent(Act.Adaptation), "CrisisActCoordinator");
        }

        // ===== Helpers =====

        private bool TryRunPostLoadActReplay()
        {
            m_TimeProvider = GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                ForceNextUpdate();
                Log.Warn("GameTimeSystem unavailable — deferring ActChangedEvent replay after load");
                return false;
            }

            // TN-1 FIX: Read actual act from CurrentActSingleton instead of
            // hardcoding PreWar→Crisis. Save in Adaptation/Exodus would confuse
            // subscribers that check PreviousAct/NewAct.
            var currentAct = Act.Crisis;
            // NO_MIGRATE: deliberate Act.Crisis post-load recovery override.
            if (m_ActSingletonQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
                currentAct = actSingleton.CurrentAct;

            // Skip re-publish for PreWar saves — no act change to replay
            if (currentAct == Act.PreWar && !m_CrisisActive)
            {
                m_NeedRePublishAct = false;
                Log.Info("Skipping ActChangedEvent re-publish — PreWar save, no crisis started");
                return true;
            }

            EventBus?.SafePublish(new ActChangedEvent(
                m_PreviousAct,
                currentAct,
                m_TimeProvider.Current.TotalGameHours,
                GetCrisisStartDayForEvent()
            ), "CrisisActCoordinator");

            // Restore exodus rate override after load
            var exodusRateFraction = currentAct == Act.Crisis
                ? BalanceConfig.Current.Scenario.ShockExodusRate
                : 0f;
            EventBus?.SafePublish(new ExodusRateOverrideFractionCommand(exodusRateFraction), "CrisisActCoordinator");

            m_NeedRePublishAct = false;
            Log.Info($"Re-published ActChangedEvent + ExodusRateOverrideFractionCommand after load (deferred, act={currentAct})");
            return true;
        }

        private int GetCurrentDay()
        {
            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                Log.Warn("[CrisisActCoordinator] TimeProvider unavailable");
                return m_HasCrisisStartDay ? m_CrisisStartDay : 0;
            }
            return m_TimeProvider.Current.CurrentDay;
        }

        private int GetCrisisStartDayForEvent()
            => m_HasCrisisStartDay ? m_CrisisStartDay : 0;

        /// <summary>
        /// Shared Crisis population baseline — captured and persisted by ScenarioStateMachine,
        /// republished on ScenarioSingleton. Two different failures used to arrive here as the
        /// same zero: "the scenario singleton is not there" (a wiring or liveness problem) and
        /// "no baseline has been taken yet" (an ordinary, expected state). They are now told
        /// apart — the return value answers the first, the <see cref="RecordedPopulation"/>
        /// answers the second — so a missing singleton can be logged as the anomaly it is
        /// instead of quietly extending the exodus deferral.
        /// </summary>
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

    }
}
