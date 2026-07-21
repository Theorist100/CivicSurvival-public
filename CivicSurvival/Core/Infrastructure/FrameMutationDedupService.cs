using System;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Collections;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Shared frame-scoped Destroy/Ignite dedup state. Single instance
    /// published through <see cref="ServiceRegistry"/> (registered in
    /// <c>Mod.OnLoad</c>, disposed in <c>Mod.OnDispose</c>). Each call
    /// records or queries one entry in a
    /// <see cref="NativeParallelHashMap{int, FrameMutationKind}"/> keyed by
    /// <c>Entity.Index</c>. A dedicated frame-end ECS system
    /// (<c>FrameMutationDedupClearSystem</c>) calls <see cref="Clear"/>
    /// once per frame before <c>ModCleanupBarrier</c> playback.
    ///
    /// See <see cref="IFrameMutationDedup"/> for the contract.
    /// </summary>
    public sealed class FrameMutationDedupService : IFrameMutationDedup, IDisposable
    {
        private static readonly LogContext Log = new("FrameMutationDedup");
        private const int InitialCapacity = 64;

        // CIVIC014 false positive: int key is Entity.Index, but dedup state is
        // frame-local and Clear() runs in PostSimulation before ModCleanupBarrier
        // playback — so a recycled-index collision cannot reach across frames.
        // Cross-frame entity-version safety is delivered by HasComponent<Destroyed/Deleted>
        // checks in BuildingDamageHelper, not by this map.
#pragma warning disable CIVIC014
        private NativeParallelHashMap<int, FrameMutationKind> m_Queued;
#pragma warning restore CIVIC014
        private bool m_Disposed;

        public FrameMutationDedupService()
        {
            m_Queued = new NativeParallelHashMap<int, FrameMutationKind>(InitialCapacity, Allocator.Persistent);
            Log.Info("Initialized");
        }

        public bool TryQueueDestroy(int entityIndex)
        {
            EnsureLive();
            if (m_Queued.TryGetValue(entityIndex, out var existing))
            {
                if ((existing & FrameMutationKind.Destroy) != 0)
                    return false; // already destroy-queued this frame
                m_Queued[entityIndex] = existing | FrameMutationKind.Destroy;
                return true;
            }
            m_Queued.Add(entityIndex, FrameMutationKind.Destroy);
            return true;
        }

        public bool TryQueueIgnite(int entityIndex)
        {
            EnsureLive();
            if (m_Queued.TryGetValue(entityIndex, out var existing))
            {
                // Destroy-already-queued short-circuit. Igniting a building that
                // is about to receive Game.Objects.Destroy is wasted work
                // (BuildingCondition write on an entity headed for the destroy
                // archetype) and routes a duplicate journal entry through
                // vanilla IgniteSystem.
                if ((existing & FrameMutationKind.Destroy) != 0)
                    return false;
                if ((existing & FrameMutationKind.Ignite) != 0)
                    return false; // duplicate ignite from sibling system
                m_Queued[entityIndex] = existing | FrameMutationKind.Ignite;
                return true;
            }
            m_Queued.Add(entityIndex, FrameMutationKind.Ignite);
            return true;
        }

        public bool IsQueued(int entityIndex)
        {
            EnsureLive();
            return m_Queued.ContainsKey(entityIndex);
        }

        public FrameMutationKind GetQueuedKind(int entityIndex)
        {
            EnsureLive();
            return m_Queued.TryGetValue(entityIndex, out var kind) ? kind : FrameMutationKind.None;
        }

        public void Clear()
        {
            if (m_Disposed) return;
            m_Queued.Clear();
        }

        public int Count
        {
            get
            {
                EnsureLive();
                return m_Queued.Count();
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (m_Queued.IsCreated) m_Queued.Dispose();
            Log.Info("Disposed");
        }

        private void EnsureLive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(FrameMutationDedupService));
        }
    }
}
