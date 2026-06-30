using System;
using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Thread-safe notification queue.
    /// Owned by NotificationSystem, registered in ServiceRegistry.
    /// Uses List + head index instead of Queue to avoid CS0433 ambiguity
    /// (Queue exists in both System.dll and mscorlib.dll in .NET 4.8).
    /// </summary>
    [OwnedByFeatureId(FeatureIds.NotificationsName)]
    public class NotificationState
    {
        private static readonly LogContext Log = new("NotificationState");
        private const int MAX_PENDING = 256;

        private readonly object m_Lock = new();
        private readonly List<NarrativeToastDto> m_Pending = new();
        private int m_Head;
        private int m_DroppedCount;
        private List<NarrativeToastDto>? m_CaptureBuffer;

        /// <summary>Push notification (thread-safe).</summary>
        public void Push(NarrativeToastDto dto)
        {
            int droppedCount = 0;
            lock (m_Lock)
            {
                if (m_CaptureBuffer != null)
                {
                    m_CaptureBuffer.Add(dto);
                    return;
                }

                if (m_Pending.Count - m_Head >= MAX_PENDING)
                {
                    m_Head++;
                    m_DroppedCount = Math.Min(m_DroppedCount, int.MaxValue - 1) + 1;
                    droppedCount = m_DroppedCount;

                    if (m_Head > 16 && (m_Pending.Count >= MAX_PENDING || m_Head > m_Pending.Count / 2))
                    {
                        m_Pending.RemoveRange(0, m_Head);
                        m_Head = 0;
                    }
                }

                m_Pending.Add(dto);
            }
            if (droppedCount > 0 && (droppedCount == 1 || droppedCount % 64 == 0))
                Log.Warn($"Dropped {droppedCount} pending notification(s): queue cap {MAX_PENDING} exceeded");
        }

        /// <summary>
        /// Captures Push() calls into the supplied buffer without mutating the live UI queue.
        /// Used by save-time resolver flushes: resolver batch state is drained, but delivery is
        /// deferred until the simulation is running again.
        /// </summary>
        public void CapturePushes(List<NarrativeToastDto> target, Action action)
        {
            lock (m_Lock)
            {
                if (m_CaptureBuffer != null)
                    throw new InvalidOperationException("Notification capture is already active");
                m_CaptureBuffer = target;
            }

            try
            {
                action();
            }
            finally
            {
                lock (m_Lock)
                {
                    m_CaptureBuffer = null;
                }
            }
        }

        /// <summary>Try to dequeue next notification (called by NotificationSystem).</summary>
        internal bool TryDequeue(out NarrativeToastDto dto)
        {
            lock (m_Lock)
            {
                if (m_Head >= m_Pending.Count)
                {
                    dto = default!;
                    return false;
                }
                dto = m_Pending[m_Head++];

                // Compact when head consumed half the list
                if (m_Head > 16 && m_Head > m_Pending.Count / 2)
                {
                    m_Pending.RemoveRange(0, m_Head);
                    m_Head = 0;
                }
                return true;
            }
        }

        /// <summary>Current queue count.</summary>
        internal int Count
        {
            get { lock (m_Lock) return m_Pending.Count - m_Head; }
        }

        /// <summary>Clear all pending notifications.</summary>
        internal void Clear()
        {
            lock (m_Lock)
            {
                m_Pending.Clear();
                m_Head = 0;
                m_DroppedCount = 0;
            }
        }

    }
}
