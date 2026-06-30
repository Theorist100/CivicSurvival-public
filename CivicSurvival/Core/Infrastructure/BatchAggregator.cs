using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Generic batching aggregator for high-frequency events.
    /// Accumulates events within a real-time window, then flushes them as a batch.
    /// Used to prevent spam from rapid-fire events (notifications, sounds, etc).
    ///
    /// Uses real-time (not game-time) so the batch window is speed-invariant —
    /// the player always sees events grouped over the same wall-clock duration.
    ///
    /// THREAD SAFETY: All public methods are thread-safe via internal locking.
    /// Safe to call from multiple threads (e.g., event handlers, callbacks).
    /// </summary>
    /// <typeparam name="T">Event data type to batch</typeparam>
    public enum BatchIdentityMode
    {
        Unknown = 0,
        NoDedup = 1,
        PerKeyDistinct = 2,
        PerKeyLatest = 3
    }

    public sealed class BatchIdentityPolicy<T>
    {
        internal BatchIdentityPolicy(BatchIdentityMode mode, Func<T, object>? keySelector)
        {
            Mode = mode;
            KeySelector = keySelector;
        }

        internal BatchIdentityMode Mode { get; }
        internal Func<T, object>? KeySelector { get; }
    }

    public static class BatchIdentityPolicy
    {
        public static BatchIdentityPolicy<T> NoDedup<T>() => new(BatchIdentityMode.NoDedup, null);

        public static BatchIdentityPolicy<T> PerKeyDistinct<T, TKey>(Func<T, TKey> keySelector)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            return new BatchIdentityPolicy<T>(BatchIdentityMode.PerKeyDistinct, data => keySelector(data)!);
        }

        public static BatchIdentityPolicy<T> PerKeyLatest<T, TKey>(Func<T, TKey> keySelector)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            return new BatchIdentityPolicy<T>(BatchIdentityMode.PerKeyLatest, data => keySelector(data)!);
        }
    }

    public sealed class BatchAggregator<T>
    {
        private List<T> m_Pending = new();
        private List<T> m_FlushBuffer = new();
        private readonly HashSet<object> m_SeenKeys = new();
        private readonly Dictionary<object, int> m_KeyIndexes = new();
        private readonly BatchIdentityPolicy<T> m_IdentityPolicy;
        private readonly float m_BatchWindowSeconds;
        private readonly int m_MaxBatchSize;
        private readonly object m_Lock = new();
        private volatile int m_Count;

        // Track oldest realtime stamp for O(1) readiness check
        private float m_OldestRealtime = float.MaxValue;

        /// <summary>
        /// Number of pending items in the batch.
        /// </summary>
        public int Count => m_Count;

        /// <summary>
        /// Creates a new batch aggregator.
        /// </summary>
        /// <param name="identityPolicy">Explicit identity policy for events in one batch window</param>
        /// <param name="batchWindowSeconds">Time window in real seconds before flush</param>
        /// <param name="maxBatchSize">Safety cap to force flush</param>
        public BatchAggregator(BatchIdentityPolicy<T> identityPolicy, float batchWindowSeconds = Engine.Narrative.BATCH_WINDOW_SECONDS, int maxBatchSize = 100)
        {
            m_IdentityPolicy = identityPolicy ?? throw new ArgumentNullException(nameof(identityPolicy));
            m_BatchWindowSeconds = batchWindowSeconds;
            m_MaxBatchSize = maxBatchSize;
        }

        /// <summary>
        /// Add an item to the pending batch using the configured identity policy.
        /// </summary>
        /// <param name="data">Event data to accumulate</param>
        /// <returns>True if batch should be force-flushed (hit max size)</returns>
        public bool Add(T data)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            lock (m_Lock)
            {
                if (!TryTrackIdentity(data))
                {
                    return false;
                }

                m_Pending.Add(data);
                m_Count = m_Pending.Count;

                if (now < m_OldestRealtime)
                {
                    m_OldestRealtime = now;
                }

                return m_Pending.Count >= m_MaxBatchSize;
            }
        }

        private bool TryTrackIdentity(T data)
        {
            lock (m_Lock)
            {
                if (m_IdentityPolicy.Mode == BatchIdentityMode.NoDedup)
                {
                    return true;
                }

                object key = m_IdentityPolicy.KeySelector!(data);
                if (m_IdentityPolicy.Mode == BatchIdentityMode.PerKeyDistinct)
                {
                    return m_SeenKeys.Add(key);
                }

                if (m_KeyIndexes.TryGetValue(key, out int index))
                {
                    m_Pending[index] = data;
                    return false;
                }

                m_KeyIndexes.Add(key, m_Pending.Count);
                return true;
            }
        }

        /// <summary>
        /// Check if batch is ready to flush (oldest item exceeded window).
        /// O(1) operation using tracked oldest time.
        /// </summary>
        /// <returns>True if ready to flush</returns>
        public bool IsReadyToFlush()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            lock (m_Lock)
            {
                if (m_Pending.Count == 0) return false;
                return now - m_OldestRealtime >= m_BatchWindowSeconds;
            }
        }

        /// <summary>
        /// Flush and return all pending items, clearing the batch.
        /// The returned buffer is reused by the next flush; consume it synchronously.
        /// </summary>
        /// <returns>List of accumulated data items</returns>
        public IReadOnlyList<T> FlushAndGet()
        {
            lock (m_Lock)
            {
                if (m_Pending.Count == 0) return Array.Empty<T>();

                var result = m_Pending;
                m_Pending = m_FlushBuffer;
                m_FlushBuffer = result;

                m_Pending.Clear();
                m_SeenKeys.Clear();
                m_KeyIndexes.Clear();
                m_Count = 0;
                m_OldestRealtime = float.MaxValue;
                return result;
            }
        }

        /// <summary>
        /// Force flush immediately (used before save).
        /// </summary>
        /// <returns>List of accumulated data items</returns>
        public IReadOnlyList<T> ForceFlush()
        {
            return FlushAndGet();
        }

        /// <summary>
        /// Clear all pending items without processing.
        /// </summary>
        public void Clear()
        {
            lock (m_Lock)
            {
                m_Pending.Clear();
                m_FlushBuffer.Clear();
                m_SeenKeys.Clear();
                m_KeyIndexes.Clear();
                m_Count = 0;
                m_OldestRealtime = float.MaxValue;
            }
        }
    }
}
