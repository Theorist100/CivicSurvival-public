using System;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Infrastructure
{
    public sealed class ThreatLifecycleDedupService : IThreatLifecycleDedup, IDisposable
    {
        private static readonly LogContext Log = new("ThreatLifecycleDedup");
        private const int InitialCapacity = 128;

        private NativeParallelHashSet<Entity> m_QueuedDeleted;
        private bool m_Disposed;

        public ThreatLifecycleDedupService()
        {
            m_QueuedDeleted = new NativeParallelHashSet<Entity>(InitialCapacity, Allocator.Persistent);
            Log.Info("Initialized");
        }

        public bool TryQueueDeleted(Entity entity)
        {
            EnsureLive();
            return m_QueuedDeleted.Add(entity);
        }

        public void Clear()
        {
            if (m_Disposed) return;
            m_QueuedDeleted.Clear();
        }

        public int Count
        {
            get
            {
                EnsureLive();
                return m_QueuedDeleted.Count();
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (m_QueuedDeleted.IsCreated) m_QueuedDeleted.Dispose();
            Log.Info("Disposed");
        }

        private void EnsureLive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(ThreatLifecycleDedupService));
        }
    }
}
