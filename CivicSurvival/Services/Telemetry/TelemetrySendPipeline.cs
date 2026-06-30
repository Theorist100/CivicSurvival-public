using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using UnityEngine;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Owns the periodic batch-send pipeline: serialize → persist → HTTP → cleanup.
    /// Single in-flight request at a time, circuit-breaker for repeated failures.
    /// </summary>
    internal sealed class TelemetrySendPipeline
    {
        private const float CIRCUIT_BREAKER_COOLDOWN_SECONDS = 300f;

        private static readonly LogContext Log = new("TelemetrySendPipeline");

        private readonly TelemetryConfig m_Config;
        private readonly TelemetryPersistence m_Persistence;
        private readonly TelemetryHttpClient m_Transport;
        private readonly TelemetryAuth m_Auth;
        private readonly TelemetryRecorder m_Recorder;
        private readonly string m_SessionId;

        private readonly CircuitBreakerState m_CircuitBreaker = new(failureThreshold: 3, cooldownSeconds: CIRCUIT_BREAKER_COOLDOWN_SECONDS);
        private readonly List<string> m_SerializedScratch = new(128);

        private int m_SendInFlight;

        public TelemetrySendPipeline(
            TelemetryConfig config,
            TelemetryPersistence persistence,
            TelemetryHttpClient transport,
            TelemetryAuth auth,
            TelemetryRecorder recorder,
            string sessionId)
        {
            m_Config = config;
            m_Persistence = persistence;
            m_Transport = transport;
            m_Auth = auth;
            m_Recorder = recorder;
            m_SessionId = sessionId;
        }

        public void RecoverRetryQueue()
        {
            var retryEvents = m_Persistence.RecoverRetryQueue();
            if (retryEvents.Count == 0) return;

            m_Recorder.AddRecoveredEvents(retryEvents);
            Log.Info($" Recovered {retryEvents.Count} events from retry queue");
        }

        public void SendBatch()
        {
            float currentTime = UnityEngine.Time.realtimeSinceStartup;
            if (!m_CircuitBreaker.CanProceed(currentTime))
                return;

            if (Interlocked.CompareExchange(ref m_SendInFlight, 1, 0) != 0)
                return;

            var batch = m_Recorder.FlushBatch();
            if (batch.Count == 0)
            {
                Interlocked.Exchange(ref m_SendInFlight, 0);
                return;
            }

            var serializer = TelemetryJsonSerializer.Instance;
            m_SerializedScratch.Clear();
            int sendCount;
            try
            {
                foreach (var evt in batch)
                {
                    m_SerializedScratch.Add(serializer.SerializeEvent(evt));
                }

                sendCount = SelectTelemetrySendCount(m_SerializedScratch);
            }
            catch (Exception ex)
            {
                Log.Error($" SendBatch preflight failed: {ex}");
                m_Recorder.AddRecoveredEvents(batch);
                Interlocked.Exchange(ref m_SendInFlight, 0);
                return;
            }
            if (sendCount <= 0)
            {
                Log.Warn(" Dropping oversized telemetry event that exceeds request cap");
                if (batch.Count > 1)
                    m_Recorder.AddRecoveredEvents(batch.GetRange(1, batch.Count - 1));
                Interlocked.Exchange(ref m_SendInFlight, 0);
                return;
            }

            List<TelemetryEvent> outgoingBatch = batch;
            if (sendCount < batch.Count)
            {
                m_Recorder.AddRecoveredEvents(batch.GetRange(sendCount, batch.Count - sendCount));
                outgoingBatch = batch.GetRange(0, sendCount);
                m_SerializedScratch.RemoveRange(sendCount, m_SerializedScratch.Count - sendCount);
                Log.Warn($" Split telemetry batch to {sendCount}/{batch.Count} events to stay under request cap");
            }

            // This is the FUNCTIONAL event batch (chronicle / stats / leaderboard fuel), so
            // the HTTP-vs-disk decision is gated on Online, NOT on the diagnostics-effective
            // FileOnlyMode. With Online on but diagnostics off the fuel must still leave over
            // HTTP (§2); FileOnlyMode (= !diagnostics) would wrongly keep it on disk. Disk-
            // only fallback applies only when Online itself is off (the pipeline is normally
            // torn down then, but this guards a transitional tick).
            if (!m_Config.OnlineEnabled)
            {
                string debugJson;
                try
                {
                    debugJson = BuildPayloadJson(m_SerializedScratch, string.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error($" SendBatch payload build failed: {ex}");
                    m_Recorder.AddRecoveredEvents(outgoingBatch);
                    Interlocked.Exchange(ref m_SendInFlight, 0);
                    return;
                }
                m_Persistence.WriteDebugLogBackground(debugJson);
                Interlocked.Exchange(ref m_SendInFlight, 0);
                return;
            }

            string jsonl;
            string payloadJson;
            try
            {
                jsonl = BuildJsonl(m_SerializedScratch);
                payloadJson = BuildPayloadJson(m_SerializedScratch);
            }
            catch (Exception ex)
            {
                Log.Error($" SendBatch payload build failed: {ex}");
                m_Recorder.AddRecoveredEvents(outgoingBatch);
                Interlocked.Exchange(ref m_SendInFlight, 0);
                return;
            }

            if (!m_CircuitBreaker.TryBeginProbe(currentTime, out var probe))
            {
                m_Recorder.AddRecoveredEvents(outgoingBatch);
                Interlocked.Exchange(ref m_SendInFlight, 0);
                return;
            }

            var authToken = m_Auth.AuthToken;
            int eventCount = m_SerializedScratch.Count;
            var persistence = m_Persistence;
            var transport = m_Transport;
            var recorder = m_Recorder;
            var circuitBreaker = m_CircuitBreaker;
            var actionStarted = new int[1];
            var batchPersisted = new int[1];
            var terminalDelivered = new int[1];

#pragma warning disable CIVIC075 // Fire-and-forget pipeline: no ECS data access, only disk IO + HTTP
            BackgroundTask.Run(() =>
            {
                Interlocked.Exchange(ref actionStarted[0], 1);
                try
                {
                    var batchPath = persistence.WriteBatchToDisk(jsonl);
                    if (batchPath == null)
                    {
                        FinishSendPipeline(terminalDelivered, () =>
                        {
                            recorder.AddRecoveredEvents(outgoingBatch);
                            circuitBreaker.CancelProbe(probe);
                        });
                        return;
                    }
                    Interlocked.Exchange(ref batchPersisted[0], 1);

                    transport.SendAsync(
                        payloadJson,
                        authToken,
                        eventCount,
                        onSuccess: () =>
                        {
                            FinishSendPipeline(terminalDelivered, () =>
                            {
                                circuitBreaker.RecordSuccess(probe);
                                persistence.DeletePendingSegment(batchPath);
                            });
                        },
                        onFailure: json =>
                        {
                            FinishSendPipeline(terminalDelivered, () =>
                            {
                                circuitBreaker.RecordFailure(probe);
                                if (persistence.AppendToRetryQueue(json))
                                {
                                    persistence.DeletePendingSegment(batchPath);
                                }
                            });
                        },
                        onFinished: () =>
                        {
                            FinishSendPipeline(terminalDelivered, () => circuitBreaker.CancelProbe(probe));
                        }
                    );
                }
                catch (Exception ex)
                {
                    Log.Error($" SendBatch pipeline failed: {ex}");
                    FinishSendPipeline(terminalDelivered, () =>
                    {
                        if (Interlocked.CompareExchange(ref batchPersisted[0], 0, 0) == 0)
                            recorder.AddRecoveredEvents(outgoingBatch);
                        circuitBreaker.CancelProbe(probe);
                    });
                }
            }, () =>
            {
                if (Interlocked.CompareExchange(ref actionStarted[0], 0, 0) == 0)
                {
                    FinishSendPipeline(terminalDelivered, () =>
                    {
                        if (persistence.WriteBatchToDisk(jsonl) == null)
                            recorder.AddRecoveredEvents(outgoingBatch);
                        circuitBreaker.CancelProbe(probe);
                    });
                }
            });
#pragma warning restore CIVIC075
        }

        private void FinishSendPipeline(int[] terminalDelivered, Action terminal)
        {
            if (Interlocked.Exchange(ref terminalDelivered[0], 1) != 0)
                return;

            try
            {
                terminal?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref m_SendInFlight, 0);
            }
        }

        public void PersistShutdownBatch()
        {
            var batch = m_Recorder.FlushBatch();
            if (batch.Count == 0) return;

            var serializer = TelemetryJsonSerializer.Instance;
            m_SerializedScratch.Clear();
            foreach (var evt in batch)
            {
                try
                {
                    m_SerializedScratch.Add(serializer.SerializeEvent(evt));
                }
                catch (Exception ex)
                {
                    Log.Error($" Final telemetry event serialization failed, skipping event: {ex}");
                }
            }
            if (m_SerializedScratch.Count == 0) return;

            try
            {
                var jsonl = BuildJsonl(m_SerializedScratch);
                if (m_Persistence.WriteBatchToDisk(jsonl) == null)
                {
                    Log.Warn(" Final telemetry flush could not be persisted");
                }
            }
            catch (Exception ex)
            {
                Log.Error($" Final telemetry flush failed: {ex}");
            }
        }

        private static string BuildJsonl(List<string> serializedEvents)
            => string.Join("\n", serializedEvents) + "\n";

        private int SelectTelemetrySendCount(List<string> serializedEvents)
        {
            if (serializedEvents == null)
                throw new ArgumentNullException(nameof(serializedEvents));

            int count = serializedEvents.Count;
            if (count <= 0) return 0;

            // The server rejects any batch above the contract event-count cap with
            // HTTP 400, independent of byte size — small events pack well past the cap
            // inside MAX_REQUEST_BYTES. Bound by count first, then shrink for byte size.
            int sendCount = Math.Min(count, TelemetryContract.MaxEventsPerBatch);
            while (sendCount > 0)
            {
                var candidate = sendCount == count
                    ? serializedEvents
                    : serializedEvents.GetRange(0, sendCount);
                var payload = BuildPayloadJson(candidate);
                if (Encoding.UTF8.GetByteCount(payload) <= TelemetryHttpClient.MAX_REQUEST_BYTES)
                    return sendCount;

                if (sendCount == 1)
                    return 0;

                sendCount = Math.Max(1, sendCount / 2);
            }

            return 0;
        }

        private string BuildPayloadJson(List<string> serializedEvents)
            => BuildPayloadJson(serializedEvents, m_Auth.PlayerId);

        // player_id is the durable cross-session identifier — it belongs in the live HTTP
        // body but NOT in the on-disk debug dump. The disk path passes string.Empty
        // so the local diagnostic file never materializes the player's stable id.
        private string BuildPayloadJson(List<string> serializedEvents, string playerId)
            => TelemetryPayloadWriter.BuildPayloadJson(
                m_SessionId,
                Mod.VERSION,
                Application.version,
                DateTime.UtcNow,
                serializedEvents,
                playerId);
    }
}
