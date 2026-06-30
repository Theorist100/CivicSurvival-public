using System;
using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Game-loop, session, and infrastructure-level telemetry. Owns the stateful
    /// blackout-district set, day dedup, save/load timestamps, and the
    /// GameOverReceived flag that the orchestrator reads when shutting down.
    /// </summary>
    internal sealed class GameLoopTelemetryListener : IDisposable
    {
        private static readonly LogContext Log = new("GameLoopTelemetryListener");

        private const float RELOAD_DETECTION_WINDOW_SECONDS = 300f;

        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        [NonEntityIndex] private readonly HashSet<int> m_ActiveBlackoutDistricts = new();
        [NonEntityIndex] private readonly HashSet<int> m_RecordedGameDays = new();
        [NonEntityIndex] private readonly HashSet<int> m_RecordedWarDays = new();

        private float m_LastSaveTime;
        private float m_LastLoadTime;
        private int m_LastLoadGameDay = -1;
        private Act m_LastLoadAct = Act.PreWar;

        public int ActiveBlackoutDistricts => m_ActiveBlackoutDistricts.Count;
        public bool GameOverReceived { get; private set; }

        public GameLoopTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<BlackoutStartedEvent>(OnBlackoutStarted);
            m_EventBus.Subscribe<BlackoutEndedEvent>(OnBlackoutEnded);
            m_EventBus.Subscribe<DistrictLifecycleEvent>(OnDistrictLifecycle);
            m_EventBus.Subscribe<InfraEvent>(OnInfraEvent);
            m_EventBus.Subscribe<ActChangedEvent>(OnActChanged);
            m_EventBus.Subscribe<WarStartedEvent>(OnWarStarted);
            m_EventBus.Subscribe<NarrativeTriggerEvent>(OnNarrativeTrigger);
            m_EventBus.Subscribe<GameOverEvent>(OnGameOver);
            m_EventBus.Subscribe<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            m_EventBus.Subscribe<DayChangedEvent>(OnDayChanged);
            m_EventBus.Subscribe<WarDayChangedEvent>(OnWarDayChanged);
            m_EventBus.Subscribe<SetNicknameCommand>(OnSetNickname);
            m_EventBus.Subscribe<GameSavedEvent>(OnGameSaved);
            m_EventBus.Subscribe<GameLoadedEvent>(OnGameLoaded);
            m_EventBus.Subscribe<LoadValidationCompleteEvent>(OnLoadValidationComplete);
            m_EventBus.Subscribe<ModalShownEvent>(OnModalShown);
            m_EventBus.Subscribe<TutorialStepEvent>(OnTutorialStepShown);
            m_EventBus.Subscribe<TutorialStepDismissedEvent>(OnTutorialStepDismissed);
            m_EventBus.Subscribe<TutorialCrisisSummaryEvent>(OnTutorialCrisisSummary);
            m_EventBus.Subscribe<SettingChangedEvent>(OnSettingChanged);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<BlackoutStartedEvent>(OnBlackoutStarted);
            m_EventBus.Unsubscribe<BlackoutEndedEvent>(OnBlackoutEnded);
            m_EventBus.Unsubscribe<DistrictLifecycleEvent>(OnDistrictLifecycle);
            m_EventBus.Unsubscribe<InfraEvent>(OnInfraEvent);
            m_EventBus.Unsubscribe<ActChangedEvent>(OnActChanged);
            m_EventBus.Unsubscribe<WarStartedEvent>(OnWarStarted);
            m_EventBus.Unsubscribe<NarrativeTriggerEvent>(OnNarrativeTrigger);
            m_EventBus.Unsubscribe<GameOverEvent>(OnGameOver);
            m_EventBus.Unsubscribe<ScenarioTypeDetectedEvent>(OnScenarioTypeDetected);
            m_EventBus.Unsubscribe<DayChangedEvent>(OnDayChanged);
            m_EventBus.Unsubscribe<WarDayChangedEvent>(OnWarDayChanged);
            m_EventBus.Unsubscribe<SetNicknameCommand>(OnSetNickname);
            m_EventBus.Unsubscribe<GameSavedEvent>(OnGameSaved);
            m_EventBus.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
            m_EventBus.Unsubscribe<LoadValidationCompleteEvent>(OnLoadValidationComplete);
            m_EventBus.Unsubscribe<ModalShownEvent>(OnModalShown);
            m_EventBus.Unsubscribe<TutorialStepEvent>(OnTutorialStepShown);
            m_EventBus.Unsubscribe<TutorialStepDismissedEvent>(OnTutorialStepDismissed);
            m_EventBus.Unsubscribe<TutorialCrisisSummaryEvent>(OnTutorialCrisisSummary);
            m_EventBus.Unsubscribe<SettingChangedEvent>(OnSettingChanged);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnBlackoutStarted(BlackoutStartedEvent evt)
        {
            m_ActiveBlackoutDistricts.Add(evt.DistrictIndex);
            Record(Blackout.Start, new BlackoutStartData { DistrictIndex = evt.DistrictIndex });
        }

        private void OnBlackoutEnded(BlackoutEndedEvent evt)
        {
            m_ActiveBlackoutDistricts.Remove(evt.DistrictIndex);
            Record(Blackout.End, new BlackoutEndData { DistrictIndex = evt.DistrictIndex });
        }

        private void OnDistrictLifecycle(DistrictLifecycleEvent evt)
        {
            if (evt.Lifecycle == DistrictLifecycle.Destroyed)
                m_ActiveBlackoutDistricts.Remove(evt.DistrictIndex);
        }

        private void OnInfraEvent(InfraEvent evt)
        {
            Record(Infra.Status, new InfraStatusData
            {
                Subtype = evt.Type.ToString().ToSnakeCase(),
                BatteryPercent = evt.BatteryPercent > 0 ? evt.BatteryPercent : null,
                StressPercent = evt.StressPercent > 0 ? evt.StressPercent : null,
                StressZone = evt.Type == InfraEventType.GridStressWarning || evt.StressZone != GridStressZone.Normal
                    ? evt.StressZone.ToString().ToSnakeCase()
                    : null,
                WearPercent = evt.WearPercent > 0 ? evt.WearPercent : null
            });
        }

        private void OnActChanged(ActChangedEvent evt)
        {
            Record(Scenario.ActChanged, new ScenarioActChangedData
            {
                PreviousAct = evt.PreviousAct.ToString().ToSnakeCase(),
                NewAct = evt.NewAct.ToString().ToSnakeCase()
            });
        }

        private void OnWarStarted(WarStartedEvent evt)
            => Record(Scenario.WarStarted, new ScenarioWarStartedData
            {
                Milestone = evt.Milestone,
                Population = evt.Population
            });

        private void OnNarrativeTrigger(NarrativeTriggerEvent evt)
        {
            string? contextJson = TelemetryMappers.SerializeNarrativeContext(evt.ContextData);
            Record(Scenario.NarrativeTrigger, new ScenarioNarrativeTriggerData
            {
                TriggerKey = evt.TriggerKey,
                ContextData = contextJson
            });
        }

        private void OnGameOver(GameOverEvent evt)
        {
            GameOverReceived = true;
            Record(Scenario.GameOver, new ScenarioGameOverData
            {
                Cause = evt.Cause.ToString().ToSnakeCase(),
                DaysSurvived = evt.DaysSurvived
            });
        }

        private void OnScenarioTypeDetected(ScenarioTypeDetectedEvent evt)
        {
            Record(Scenario.ScenarioTypeDetected, new ScenarioScenarioTypeDetectedData
            {
                ScenarioType = evt.Type.ToString().ToSnakeCase(),
                Population = evt.Population
            });
        }

        private void OnDayChanged(DayChangedEvent evt)
        {
            if (!m_RecordedGameDays.Add(evt.DayNumber))
                return;

            Record(Time.DayChanged, new TimeDayChangedData
            {
                DayNumber = evt.DayNumber,
                Hour = (float)Math.Round(evt.Hour, 2)
            });
        }

        private void OnWarDayChanged(WarDayChangedEvent evt)
        {
            if (!m_RecordedWarDays.Add(evt.WarDay))
                return;

            Record(Time.WarDayChanged, new TimeWarDayChangedData
            {
                WarDay = evt.WarDay,
                GameDay = evt.GameDay
            });
        }

        private void OnSetNickname(SetNicknameCommand cmd)
        {
            if (string.IsNullOrEmpty(cmd.Nickname))
            {
                Log.Debug(" Nickname cleared");
                return;
            }

            Log.Info($" Nickname command observed (len={cmd.Nickname.Length})");
        }

        private void OnGameSaved(GameSavedEvent evt)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            float sinceLast = m_LastSaveTime > 0 ? now - m_LastSaveTime : 0f;
            m_LastSaveTime = now;

            Record(Session.GameSaved, new SessionGameSavedData
            {
                GameDay = evt.GameDay,
                Act = evt.CurrentAct.ToString().ToSnakeCase(),
                SecondsSinceLastSave = (float)Math.Round(sinceLast, 1)
            });
        }

        private void OnGameLoaded(GameLoadedEvent evt)
        {
            m_RecordedGameDays.Clear();
            m_RecordedWarDays.Clear();
            GameOverReceived = false;

            float now = UnityEngine.Time.realtimeSinceStartup;
            float sinceLast = m_LastLoadTime > 0 ? now - m_LastLoadTime : 0f;
            bool isReload = m_LastLoadGameDay == evt.GameDay && m_LastLoadAct == evt.CurrentAct && sinceLast < RELOAD_DETECTION_WINDOW_SECONDS;
            m_LastLoadTime = now;
            m_LastLoadGameDay = evt.GameDay;
            m_LastLoadAct = evt.CurrentAct;

            Record(Session.GameLoaded, new SessionGameLoadedData
            {
                GameDay = evt.GameDay,
                Act = evt.CurrentAct.ToString().ToSnakeCase(),
                SavedModVersion = evt.SavedModVersion,
                SavedFormatVersion = evt.SavedFormatVersion,
                IsReload = isReload,
                SecondsSinceLastLoad = (float)Math.Round(sinceLast, 1)
            });

            ResyncActiveBlackouts();
        }

        private void OnLoadValidationComplete(LoadValidationCompleteEvent evt)
        {
            Record(Session.LoadValidation, new SessionLoadValidationData
            {
                Successes = evt.Successes,
                Failures = evt.Failures,
                FailedSystems = evt.FailedSystems
            });
        }

        private void OnModalShown(ModalShownEvent evt)
            => Record(Tutorial.MilestoneShown, new TutorialMilestoneShownData { ModalFlag = evt.Flag.ToString() });

        private void OnTutorialStepShown(TutorialStepEvent evt)
            => Record(Tutorial.CrisisStepShown, new TutorialCrisisStepShownData { StepName = evt.StepName, Trigger = evt.Trigger });

        private void OnTutorialStepDismissed(TutorialStepDismissedEvent evt)
            => Record(Tutorial.StepDismissed, new TutorialStepDismissedData
            {
                StepName = evt.StepName,
                DurationSeconds = (float)Math.Round(evt.DurationSeconds, 1)
            });

        private void OnTutorialCrisisSummary(TutorialCrisisSummaryEvent evt)
            => Record(Tutorial.CrisisSummary, new TutorialCrisisSummaryData
            {
                StepsShown = evt.StepsShown,
                StepsTotal = evt.StepsTotal,
                CrisisDurationDays = evt.CrisisDurationDays
            });

        private void OnSettingChanged(SettingChangedEvent evt)
            => Record(Settings.Changed, new SettingsChangedData
            {
                SettingName = evt.SettingName,
                OldValue = evt.OldValue,
                NewValue = evt.NewValue
            });

        private void ResyncActiveBlackouts()
        {
            var reader = ServiceRegistry.TryGet<IDistrictStateReader>();
            if (reader == null)
            {
                Log.Warn(" Skipping blackout resync: district state reader unavailable");
                return;
            }

            try
            {
                var snapshot = reader.TakeSnapshot();

                m_ActiveBlackoutDistricts.Clear();
                foreach (var kvp in snapshot.DistrictBlackouts)
                {
                    if (kvp.Value.Count > 0)
                    {
                        m_ActiveBlackoutDistricts.Add(kvp.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($" Skipping blackout resync: {ex}");
            }
        }
    }
}
