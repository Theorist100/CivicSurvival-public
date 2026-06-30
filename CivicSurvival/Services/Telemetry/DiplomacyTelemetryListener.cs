using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    internal sealed class DiplomacyTelemetryListener : IDisposable
    {
        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        public DiplomacyTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<DonorEvent>(OnDonorEvent);
            m_EventBus.Subscribe<HeritageGrantedEvent>(OnHeritageGranted);
            m_EventBus.Subscribe<PreWarTensionEvent>(OnPreWarTension);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<DonorEvent>(OnDonorEvent);
            m_EventBus.Unsubscribe<HeritageGrantedEvent>(OnHeritageGranted);
            m_EventBus.Unsubscribe<PreWarTensionEvent>(OnPreWarTension);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnDonorEvent(DonorEvent evt)
        {
            Record(Donor.Action, new DonorActionData
            {
                Subtype = $"{evt.Type}".ToSnakeCase(),
                Trust = $"{evt.Trust}".ToSnakeCase(),
                Amount = evt.Amount > 0 ? evt.Amount : null,
                Count = evt.Count > 0 ? evt.Count : null,
                MwEach = evt.MWEach > 0 ? evt.MWEach : null,
                Days = evt.Days > 0 ? evt.Days : null,
                Penalty = evt.Penalty > 0 ? evt.Penalty : null
            });
        }

        private void OnHeritageGranted(HeritageGrantedEvent evt)
        {
            Record(Heritage.Granted, new HeritageGrantedData
            {
                Count = evt.Count,
                ProductionMw = evt.ProductionMW
            });
        }

        private void OnPreWarTension(PreWarTensionEvent evt)
        {
            Record(Scenario.PreWarTension, new ScenarioPreWarTensionData
            {
                Effect = evt.Effect.ToString().ToSnakeCase(),
                Value = evt.Value
            });
        }
    }
}
