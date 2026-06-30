using System;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Composite EventBus subscriber for telemetry. Owns seven domain-specific
    /// sub-listeners (Threat, Manpower, Cognitive, Corruption, Diplomacy, GameLoop, Request)
    /// that each subscribe to the EventBus and forward into the <see cref="TelemetryRecorder"/>.
    ///
    /// Stateful read-back surface (<see cref="ActiveBlackoutDistricts"/> and
    /// <see cref="GameOverReceived"/>) lives on the GameLoop sub-listener and is
    /// exposed here via delegation so the orchestrator's contract is unchanged.
    /// </summary>
    public sealed class TelemetryEventListener : IDisposable
    {
        private static readonly LogContext Log = new("TelemetryEventListener");

        private readonly ThreatTelemetryListener m_Threat;
        private readonly ManpowerTelemetryListener m_Manpower;
        private readonly CognitiveTelemetryListener m_Cognitive;
        private readonly CorruptionTelemetryListener m_Corruption;
        private readonly DiplomacyTelemetryListener m_Diplomacy;
        private readonly GameLoopTelemetryListener m_GameLoop;
        private readonly RequestTelemetryListener m_Request;

        public TelemetryEventListener(
            IEventBus eventBus,
            TelemetryRecorder recorder,
            string sessionId)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            if (recorder == null) throw new ArgumentNullException(nameof(recorder));
            if (sessionId == null) throw new ArgumentNullException(nameof(sessionId));

            m_Threat = new ThreatTelemetryListener(eventBus, recorder, sessionId);
            m_Manpower = new ManpowerTelemetryListener(eventBus, recorder, sessionId);
            m_Cognitive = new CognitiveTelemetryListener(eventBus, recorder, sessionId);
            m_Corruption = new CorruptionTelemetryListener(eventBus, recorder, sessionId);
            m_Diplomacy = new DiplomacyTelemetryListener(eventBus, recorder, sessionId);
            m_GameLoop = new GameLoopTelemetryListener(eventBus, recorder, sessionId);
            m_Request = new RequestTelemetryListener(eventBus, recorder, sessionId);

            // One-shot startup event: record which features came up open/closed/preview/dep-skipped.
            // This is a recorded telemetry event, same recorder/sessionId the sub-listeners use, so
            // it belongs here rather than in the orchestrator's pipeline-composition path.
            RecordFeatureManifestSnapshot(recorder, sessionId);

            Log.Debug(" Subscribed to EventBus events");
        }

        private static void RecordFeatureManifestSnapshot(TelemetryRecorder recorder, string sessionId)
        {
            if (!FeatureRegistry.IsInitialized)
                return;

            var registry = FeatureRegistry.Instance;
            recorder.Record(sessionId, EventTypes.Feature.ManifestSnapshot, new FeatureManifestSnapshotData
            {
                Open = new List<string>(registry.OpenFeatureIds),
                Closed = new List<string>(registry.ClosedFeatureIds),
                Preview = new List<string>(registry.PreviewFeatureIds),
                DepSkipped = new List<string>(registry.DepSkippedFeatureIds)
            });
        }

        public void Dispose()
        {
            m_Threat.Dispose();
            m_Manpower.Dispose();
            m_Cognitive.Dispose();
            m_Corruption.Dispose();
            m_Diplomacy.Dispose();
            m_GameLoop.Dispose();
            m_Request.Dispose();
            Log.Debug(" Unsubscribed from events");
        }

        public int ActiveBlackoutDistricts => m_GameLoop.ActiveBlackoutDistricts;
        public bool GameOverReceived => m_GameLoop.GameOverReceived;
    }
}
