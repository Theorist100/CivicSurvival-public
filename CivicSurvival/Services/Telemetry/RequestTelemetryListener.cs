using System;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using static CivicSurvival.Services.Telemetry.EventTypes;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Forwards command-request lifecycle diagnostics into telemetry. Currently records
    /// orphaned-command cleanups so the server can tell whether a producer/consumer pair
    /// leaks command entities (one-off vs recurring class) across sessions.
    /// </summary>
    internal sealed class RequestTelemetryListener : IDisposable
    {
        private readonly IEventBus m_EventBus;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        public RequestTelemetryListener(IEventBus eventBus, TelemetryRecorder recorder, string sessionId)
        {
            m_EventBus = eventBus;
            m_Recorder = recorder;
            m_SessionId = sessionId;

            m_EventBus.Subscribe<CommandRequestOrphanedEvent>(OnCommandRequestOrphaned);
        }

        public void Dispose()
        {
            m_EventBus.Unsubscribe<CommandRequestOrphanedEvent>(OnCommandRequestOrphaned);
        }

        private void OnCommandRequestOrphaned(CommandRequestOrphanedEvent evt)
        {
            m_Recorder.Record(m_SessionId, Diagnostics.OrphanCleaned, new DiagnosticsOrphanCleanedData
            {
                RequestType = evt.RequestType,
                AgeSeconds = evt.AgeSeconds,
                Reason = evt.Reason
            });
        }
    }
}
