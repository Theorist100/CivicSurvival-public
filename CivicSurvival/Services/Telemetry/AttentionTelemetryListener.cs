using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    internal sealed class AttentionTelemetryListener : IDisposable
    {
        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        public AttentionTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<ShockTierChangedEvent>(OnShockTierChanged);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<ShockTierChangedEvent>(OnShockTierChanged);
        }

        private void Record(string type, object data) => m_Recorder.Record(m_SessionId, type, data);

        private void OnShockTierChanged(ShockTierChangedEvent evt)
        {
            Record(Attention.ShockTierChanged, new AttentionShockTierChangedData
            {
                FromTier = $"{evt.FromTier}".ToSnakeCase(),
                ToTier = $"{evt.ToTier}".ToSnakeCase(),
                ShockLevel = evt.ShockLevel,
                TotalCasualties = evt.TotalCasualties,
                TotalBuildingsDestroyed = evt.TotalBuildingsDestroyed
            });
        }
    }
}
