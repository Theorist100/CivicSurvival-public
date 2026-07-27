using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    internal sealed class ManpowerTelemetryListener : IDisposable
    {
        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        public ManpowerTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<ManpowerCriticalEvent>(OnManpowerCritical);
            m_EventBus.Subscribe<ConscriptionActivatedEvent>(OnConscriptionActivated);
            m_EventBus.Subscribe<ConscriptionDeactivatedEvent>(OnConscriptionDeactivated);
            m_EventBus.Subscribe<ManpowerRecruitedEvent>(OnManpowerRecruited);
            m_EventBus.Subscribe<ManpowerReleasedEvent>(OnManpowerReleased);
            m_EventBus.Subscribe<ManpowerCasualtiesEvent>(OnManpowerCasualties);
            m_EventBus.Subscribe<ManpowerForceReleasedEvent>(OnManpowerForceReleased);
            m_EventBus.Subscribe<CallToArmsEvent>(OnCallToArms);
            m_EventBus.Subscribe<RefugeesReceivedEvent>(OnRefugeesReceived);
            m_EventBus.Subscribe<CitizensLeftEvent>(OnCitizensLeft);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<ManpowerCriticalEvent>(OnManpowerCritical);
            m_EventBus.Unsubscribe<ConscriptionActivatedEvent>(OnConscriptionActivated);
            m_EventBus.Unsubscribe<ConscriptionDeactivatedEvent>(OnConscriptionDeactivated);
            m_EventBus.Unsubscribe<ManpowerRecruitedEvent>(OnManpowerRecruited);
            m_EventBus.Unsubscribe<ManpowerReleasedEvent>(OnManpowerReleased);
            m_EventBus.Unsubscribe<ManpowerCasualtiesEvent>(OnManpowerCasualties);
            m_EventBus.Unsubscribe<ManpowerForceReleasedEvent>(OnManpowerForceReleased);
            m_EventBus.Unsubscribe<CallToArmsEvent>(OnCallToArms);
            m_EventBus.Unsubscribe<RefugeesReceivedEvent>(OnRefugeesReceived);
            m_EventBus.Unsubscribe<CitizensLeftEvent>(OnCitizensLeft);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnManpowerCritical(ManpowerCriticalEvent evt)
        {
            Record(Mobilization.ManpowerCritical, new MobilizationManpowerCriticalData
            {
                Available = evt.Available,
                Total = evt.Total,
                Percent = evt.Percent
            });
        }

        private void OnConscriptionActivated(ConscriptionActivatedEvent evt)
            => Record(Mobilization.ConscriptionActivated, new MobilizationConscriptionActivatedData());

        private void OnConscriptionDeactivated(ConscriptionDeactivatedEvent evt)
            => Record(Mobilization.ConscriptionDeactivated, new MobilizationConscriptionDeactivatedData());

        private void OnManpowerRecruited(ManpowerRecruitedEvent evt)
        {
            Record(Mobilization.ManpowerRecruited, new MobilizationManpowerRecruitedData
            {
                Amount = evt.Amount,
                Reason = evt.Reason,
                Remaining = evt.Remaining
            });
        }

        private void OnManpowerReleased(ManpowerReleasedEvent evt)
        {
            Record(Mobilization.ManpowerReleased, new MobilizationManpowerReleasedData
            {
                Amount = evt.Amount,
                Reason = evt.Reason,
                Available = evt.Available
            });
        }

        private void OnManpowerCasualties(ManpowerCasualtiesEvent evt)
        {
            Record(Mobilization.Casualties, new MobilizationCasualtiesData
            {
                Amount = evt.Amount,
                TotalCasualties = evt.TotalCasualties,
                Reason = evt.Reason
            });
        }

        private void OnManpowerForceReleased(ManpowerForceReleasedEvent evt)
        {
            Record(Mobilization.ManpowerForceReleased, new MobilizationManpowerForceReleasedData
            {
                Released = evt.Released,
                NewTotal = evt.NewTotal
            });
        }

        private void OnCallToArms(CallToArmsEvent evt)
        {
            Record(Mobilization.CallToArms, new MobilizationCallToArmsData
            {
                Recovered = evt.Recovered,
                RemainingCasualties = evt.RemainingCasualties
            });
        }

        private void OnRefugeesReceived(RefugeesReceivedEvent evt)
            => Record(Population.RefugeesReceived, new PopulationRefugeesReceivedData { Count = evt.Count });

        private void OnCitizensLeft(CitizensLeftEvent evt)
            => Record(Population.CitizensLeft, new PopulationCitizensLeftData { Count = evt.Count });
    }
}
