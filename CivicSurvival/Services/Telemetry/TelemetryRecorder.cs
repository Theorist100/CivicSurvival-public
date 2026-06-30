using System;
using System.Collections.Generic;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Records telemetry events and manages batching.
    /// Thread-safe. Disk persistence handled by TelemetryService.SendBatch.
    /// </summary>
    public sealed class TelemetryRecorder
    {
        private static readonly LogContext Log = new("TelemetryRecorder");
        private const int MAX_QUEUED_EVENTS = 10000;
        private readonly List<TelemetryEvent> m_EventBatch = new();
        private readonly object m_Lock = new();
        private int m_DroppedEvents;

        public int BatchCount
        {
            get
            {
                lock (m_Lock)
                {
                    return m_EventBatch.Count;
                }
            }
        }

        public void Record(string sessionId, string type, object data)
        {
            var evt = new TelemetryEvent
            {
                EventId = Guid.NewGuid().ToString("D"),
                SessionId = sessionId,
                Type = type,
                Timestamp = DateTime.UtcNow,
                Data = data
            };

            lock (m_Lock)
            {
                if (m_EventBatch.Count >= MAX_QUEUED_EVENTS)
                {
                    m_EventBatch.RemoveAt(0);
                    AddDroppedEvents(1);
                }

                m_EventBatch.Add(evt);
            }
        }

        public List<TelemetryEvent> FlushBatch()
        {
            List<TelemetryEvent> result;
            int droppedEvents;

            lock (m_Lock)
            {
                if (m_EventBatch.Count == 0)
                {
                    return new List<TelemetryEvent>();
                }

                result = new List<TelemetryEvent>(m_EventBatch);
                m_EventBatch.Clear();
                droppedEvents = m_DroppedEvents;
                m_DroppedEvents = 0;
            }

            if (droppedEvents > 0)
            {
                Log.Warn($" Telemetry queue overflow dropped {droppedEvents} oldest events");
            }

            return result;
        }

        public void AddRecoveredEvents(List<TelemetryEvent> events)
        {
            if (events == null || events.Count == 0)
            {
                return;
            }

            lock (m_Lock)
            {
                if (events.Count > MAX_QUEUED_EVENTS)
                {
                    var skipped = events.Count - MAX_QUEUED_EVENTS;
                    events = events.GetRange(skipped, MAX_QUEUED_EVENTS);
                    AddDroppedEvents(skipped);
                }

                var overflow = m_EventBatch.Count + events.Count - MAX_QUEUED_EVENTS;
                if (overflow > 0)
                {
                    var remove = Math.Min(overflow, m_EventBatch.Count);
                    if (remove > 0)
                    {
                        m_EventBatch.RemoveRange(0, remove);
                    }

                    AddDroppedEvents(overflow);
                }

                m_EventBatch.AddRange(events);
            }

            if (Log.IsDebugEnabled) Log.Debug($" Added {events.Count} recovered events to batch");
        }

        private void AddDroppedEvents(int count)
        {
            if (count <= 0) return;
            lock (m_Lock)
            {
                m_DroppedEvents = (int)Math.Min((long)m_DroppedEvents + count, int.MaxValue);
            }
        }
    }
}
