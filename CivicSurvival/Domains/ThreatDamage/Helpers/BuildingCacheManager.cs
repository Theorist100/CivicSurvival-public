using Unity.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Async double-buffered building cache used by ThreatDamageSystem.
    ///
    /// Buildings don't move — invalidation is event-driven (rebuilds when the
    /// matching entity set changes), not time-based. While a new cache is being
    /// built asynchronously, the stale cache stays usable so impacts never see
    /// a zero-damage frame.
    ///
    /// First use returns unready while the async cache is building; callers retain
    /// pending impacts until a readable cache exists.
    /// </summary>
    internal sealed class BuildingCacheManager
    {
        private EntityQuery m_Query;
        private BuildingFrameCache m_Cache;
        private BuildingFrameCache m_Pending;
        private bool m_HasPending;
        private int m_LastBuildingCount;
        [EntityQueryOrderCursor("Invalidates the async building cache when the building query's archetype set changes.")]
        private int m_BuildingOrderCursor;
        private int m_PendingBuildingCount;
        [EntityQueryOrderCursor("Carries the building query order cursor for the async cache currently being built.")]
        private int m_PendingBuildingOrderVersion;

        // Desync overruns carried over from caches disposed by a mid-frame swap before the owner
        // drained them. A SpatialHash/Positions desync correlates with a building-set change —
        // which is exactly what triggers the async rebuild + swap — so draining only the live
        // cache at end of frame would systematically drop the very signal we added. Accumulate
        // the dying cache's overruns here so TryDrainDesync still reports them.
        private const int MAX_CARRIED_DESYNC = 1_000_000;
        private int m_CarriedDesyncCount;
        private int m_CarriedWorstIdx = -1;

        public bool IsCreated => m_Cache.IsCreated;
        public ref readonly BuildingFrameCache Cache => ref m_Cache;

        /// <summary>
        /// Drain the active cache's desync counter (SpatialHash → Positions index overruns
        /// guarded by <see cref="BuildingFrameCache.TryGetPosition"/>). Returns false when no
        /// overrun was recorded since the last drain. Reports the cache lengths at drain time so
        /// telemetry can size the desync.
        /// </summary>
        public bool TryDrainDesync(out int count, out int worstIdx, out int positionsLength, out int hashCount)
        {
            count = m_CarriedDesyncCount;
            worstIdx = m_CarriedWorstIdx;
            positionsLength = 0;
            hashCount = 0;
            m_CarriedDesyncCount = 0;
            m_CarriedWorstIdx = -1;

            if (m_Cache.IsCreated && m_Cache.TryDrainDesync(out int liveCount, out int liveWorst))
            {
                count += liveCount;
                if (liveWorst > worstIdx) worstIdx = liveWorst;
            }

            if (count == 0)
                return false;

            // Lengths reflect the live cache; carried overruns belong to a now-disposed cache, so
            // worstIdx is the reliable cross-cache signal while the lengths size the current one.
            if (m_Cache.IsCreated)
            {
                positionsLength = m_Cache.Positions.IsCreated ? m_Cache.Positions.Length : 0;
                hashCount = m_Cache.SpatialHash.IsCreated ? m_Cache.SpatialHash.Count() : 0;
            }
            return true;
        }

        public void Initialize(EntityQuery buildingQuery)
        {
            m_Query = buildingQuery;
        }

        /// <summary>
        /// Pre-warm the cache asynchronously. Caller typically invokes this from
        /// OnStartRunning so the cache is ready before the first impact arrives.
        /// </summary>
        public void Prewarm(int frame)
        {
            if (m_Cache.IsCreated || m_HasPending) return;
            StartPendingBuild(frame);
        }

        /// <summary>
        /// Ensure the cache is valid for read this frame.
        /// Returns false while the first async cache is still building.
        /// </summary>
        public bool EnsureValid(int frame)
        {
            // Step 1: Try to swap in pending async cache.
            if (m_HasPending)
            {
                if (m_Pending.TryComplete())
                {
                    if (m_Cache.IsCreated)
                    {
                        // Carry over any overruns the owner has not drained yet — the dying cache's
                        // NativeReference is about to be disposed.
                        if (m_Cache.TryDrainDesync(out int carriedCount, out int carriedWorst))
                        {
                            // Drained every frame by the owner, so this never realistically grows;
                            // clamp anyway so a pathological undrained run can't overflow the counter.
                            m_CarriedDesyncCount = System.Math.Min(m_CarriedDesyncCount + carriedCount, MAX_CARRIED_DESYNC);
                            if (carriedWorst > m_CarriedWorstIdx) m_CarriedWorstIdx = carriedWorst;
                        }
                        m_Cache.Dispose();
                    }
                    m_Cache = m_Pending;
                    m_Pending = default;
                    m_HasPending = false;
                    m_LastBuildingCount = m_PendingBuildingCount;
                    m_BuildingOrderCursor = m_PendingBuildingOrderVersion;
                    m_PendingBuildingCount = 0;
                    m_PendingBuildingOrderVersion = 0;
                }
            }

            // Step 2: If no cache at all, start async build
            if (!m_Cache.IsCreated && !m_HasPending)
            {
                StartPendingBuild(frame);
            }

            if (!m_Cache.IsCreated)
                return false;

            // Step 3: Event-driven invalidation — rebuild when the matching building
            // set changes. Count alone misses same-frame replacement (delete + build).
            // The component order version tracks structural changes for the query.
            if (!m_HasPending && m_Cache.IsCreated)
            {
                int currentCount = m_Query.CalculateEntityCountWithoutFiltering();
                int currentOrderVersion = m_Query.GetCombinedComponentOrderVersion(includeEntityType: true);
                if (currentCount != m_LastBuildingCount
                    || currentOrderVersion != m_BuildingOrderCursor)
                {
                    StartPendingBuild(frame);
                    // Continue using stale cache until async rebuild completes
                }
            }

            return true;
        }

        /// <summary>
        /// Reset state after save load or act transition. Disposes both ready and
        /// pending caches and resets the count, so the next EnsureValid rebuilds
        /// from scratch.
        /// </summary>
        public void ResetForReload()
        {
            if (m_Cache.IsCreated) { m_Cache.Dispose(); m_Cache = default; }
            if (m_HasPending)
            {
                m_Pending.ForceComplete();
                if (m_Pending.IsCreated) m_Pending.Dispose();
                m_Pending = default;
                m_HasPending = false;
            }
            m_LastBuildingCount = 0;
            m_BuildingOrderCursor = 0;
            m_PendingBuildingCount = 0;
            m_PendingBuildingOrderVersion = 0;
            m_CarriedDesyncCount = 0;
            m_CarriedWorstIdx = -1;
        }

        /// <summary>
        /// Final disposal for OnDestroy. Same as ResetForReload but spelled out
        /// so the call site reads as a destructor rather than a recovery action.
        /// </summary>
        public void DisposeAll()
        {
            if (m_Cache.IsCreated) m_Cache.Dispose();
            if (m_HasPending && m_Pending.IsCreated) m_Pending.Dispose();
            m_Cache = default;
            m_Pending = default;
            m_HasPending = false;
            m_LastBuildingCount = 0;
            m_BuildingOrderCursor = 0;
            m_PendingBuildingCount = 0;
            m_PendingBuildingOrderVersion = 0;
            m_CarriedDesyncCount = 0;
            m_CarriedWorstIdx = -1;
        }

        private void StartPendingBuild(int frame)
        {
            m_PendingBuildingCount = m_Query.CalculateEntityCountWithoutFiltering();
            m_PendingBuildingOrderVersion = m_Query.GetCombinedComponentOrderVersion(includeEntityType: true);
            m_Pending = BuildingFrameCache.CreateAsync(m_Query, frame);
            m_HasPending = true;
        }
    }
}
