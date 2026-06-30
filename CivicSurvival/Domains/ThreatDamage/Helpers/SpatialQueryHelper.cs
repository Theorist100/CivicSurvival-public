using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Game.Objects;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ThreatDamage.Helpers
{
    /// <summary>
    /// Frame-cached building data with spatial hash for O(1) radius queries.
    ///
    /// PERF optimizations:
    /// - Burst-compiled job for O(N) spatial hash building
    /// - Cache valid for maxAge frames (default 30 = ~0.5s)
    /// - O(K) queries where K = buildings in overlapping cells
    /// - ASYNC PATTERN: CreateAsync() schedules jobs, TryComplete() checks/completes them
    ///
    /// NOTE: Entity stored as Index+Version arrays to avoid vanilla orphan detection (homeless spike bug).
    /// </summary>
    public struct BuildingFrameCache : IDisposable
    {
        public NativeArray<int> EntityIndices;
        public NativeArray<int> EntityVersions;
        public NativeArray<float2> Positions;
        [NonEntityIndex] public NativeParallelMultiHashMap<int, int> SpatialHash;
        public float CellSize;

        // Desync diagnostics: the SpatialHash maps a cell key to an index into the
        // index-parallel arrays (Positions/EntityIndices). A hash/array desync would point
        // at an index past the arrays; TryGetPosition bounds-guards the read and records the
        // overrun here. NativeReference (not a plain int) because the query helpers take the
        // cache by `in` — only reference semantics survive a defensive copy of the struct.
        private NativeReference<int> m_DesyncCount;
        private NativeReference<int> m_DesyncWorstIdx;

        /// <summary>Reconstruct Entity from Index+Version at given index.</summary>
        public readonly Entity GetEntity(int index) => new Entity { Index = EntityIndices[index], Version = EntityVersions[index] };

        /// <summary>
        /// Bounds-guarded position read. <paramref name="idx"/> comes from the SpatialHash, a
        /// separate container from Positions; an out-of-range index would be a managed
        /// IndexOutOfRange on the main thread (these helpers are not Burst). Skip the entry and
        /// record the overrun instead of throwing. Returns false when the index is out of range.
        /// </summary>
        public readonly bool TryGetPosition(int idx, out float2 pos)
        {
            if ((uint)idx >= (uint)Positions.Length)
            {
                if (m_DesyncCount.IsCreated)
                {
                    // NativeReference is a handle into native memory: a local copy of the handle
                    // points at the SAME backing store, so writing through the copy keeps this method
                    // `readonly` (no struct-field mutation → no CS1604) while the increment still lands
                    // in the shared counter. Required because the query helpers take the cache by `in`.
                    var count = m_DesyncCount;
                    var worstIdx = m_DesyncWorstIdx;
                    count.Value++;
                    if (idx > worstIdx.Value) worstIdx.Value = idx;
                }
                pos = default;
                return false;
            }
            pos = Positions[idx];
            return true;
        }

        /// <summary>
        /// Drain the desync counter (reset to zero). Returns false when no overrun was seen.
        /// Called by the cache owner once per update to emit a single aggregated telemetry event.
        /// </summary>
        public readonly bool TryDrainDesync(out int count, out int worstIdx)
        {
            count = 0;
            worstIdx = -1;
            if (!m_DesyncCount.IsCreated || m_DesyncCount.Value == 0)
                return false;

            // Local handle copies write to the same native store, keeping the method `readonly`
            // (CS1604-safe) — see TryGetPosition.
            var countRef = m_DesyncCount;
            var worstRef = m_DesyncWorstIdx;
            count = countRef.Value;
            worstIdx = worstRef.Value;
            countRef.Value = 0;
            worstRef.Value = -1;
            return true;
        }

        // Async job state
        private JobHandle m_PendingJobHandle;
        private NativeArray<Transform> m_PendingTransforms; // Held until job completes
        private bool m_IsPending;

        public bool IsCreated => EntityIndices.IsCreated;
        public bool IsPending => m_IsPending;

        /// <summary>
        /// Try to complete pending jobs. Call this each frame while IsPending is true.
        /// Returns true when jobs are complete and cache is ready to use.
        /// </summary>
        public bool TryComplete()
        {
            if (!m_IsPending) return true; // Already complete

            // Check if job is done (non-blocking)
            if (!m_PendingJobHandle.IsCompleted) return false;

            // Complete and cleanup
            m_PendingJobHandle.Complete();
            if (m_PendingTransforms.IsCreated) m_PendingTransforms.Dispose();
            m_IsPending = false;
            return true;
        }

        /// <summary>
        /// Force complete pending jobs (blocking). Use only in OnDestroy.
        /// </summary>
        public void ForceComplete()
        {
            if (!m_IsPending) return;
            m_PendingJobHandle.Complete();
            if (m_PendingTransforms.IsCreated) m_PendingTransforms.Dispose();
            m_IsPending = false;
        }

        /// <summary>
        /// Cell size for spatial hash. 100m = good balance for 50-200m query radii.
        /// </summary>
        public const float DEFAULT_CELL_SIZE = 100f;

        // Spatial hash coordinate range: cells packed into 32-bit int (16 bits per axis)
        public const int CELL_HALF_RANGE = 16384;

        /// <summary>
        /// Create cache from building query with Burst-compiled spatial hash.
        /// BLOCKING: Waits for jobs to complete. Use CreateAsync for non-blocking.
        /// Caller must Dispose.
        /// </summary>
        public static BuildingFrameCache Create(EntityQuery buildingQuery, int frame)
        {
            var cache = CreateAsync(buildingQuery, frame);
            cache.ForceComplete();
            return cache;
        }

        /// <summary>
        /// Create cache asynchronously (non-blocking).
        /// Jobs are scheduled but NOT completed. Call TryComplete() each frame until ready.
        /// Caller must Dispose (handles pending state correctly).
        /// </summary>
        public static BuildingFrameCache CreateAsync(EntityQuery buildingQuery, int frame)
        {
            // NOTE: Use Persistent allocator - cache lives 30+ frames, TempJob only valid for 4
            var entities = buildingQuery.ToEntityArray(Allocator.Persistent);
            var transforms = buildingQuery.ToComponentDataArray<Transform>(Allocator.Persistent);

            int count = entities.Length;

            // NOTE: Extract Index+Version from entities to avoid vanilla orphan detection
            var entityIndices = new NativeArray<int>(count, Allocator.Persistent);
            var entityVersions = new NativeArray<int>(count, Allocator.Persistent);
            for (int i = 0; i < count; i++)
            {
                entityIndices[i] = entities[i].Index;
                entityVersions[i] = entities[i].Version;
            }
            if (entities.IsCreated) entities.Dispose(); // No longer need Entity array

            if (count == 0)
            {
                if (transforms.IsCreated) transforms.Dispose();
                return new BuildingFrameCache
                {
                    EntityIndices = entityIndices,
                    EntityVersions = entityVersions,
                    Positions = new NativeArray<float2>(0, Allocator.Persistent),
                    SpatialHash = new NativeParallelMultiHashMap<int, int>(1, Allocator.Persistent),
                    CellSize = DEFAULT_CELL_SIZE,
                    m_DesyncCount = new NativeReference<int>(0, Allocator.Persistent),
                    m_DesyncWorstIdx = new NativeReference<int>(-1, Allocator.Persistent),
                    m_IsPending = false
                };
            }

            var positions = new NativeArray<float2>(count, Allocator.Persistent);
            // ParallelWriter capacity must exceed the number of parallel Adds. BuildSpatialHashJob
            // does exactly `count` Adds via .AsParallelWriter(); the writer's AllocEntry reserves
            // slots in 16-blocks per worker (UnsafeParallelHashMapBase.AllocEntry), so a capacity of
            // exactly `count` can drive the allocator into the steal/false-exhaustion path under
            // contention (a negative index → c0000005 in a Burst build with bounds-checks stripped).
            // Headroom = one full block per worker thread removes that race; the others in this
            // codebase use a 2x multiplier (NeighborEnvyData.CalculateSpatialCapacity) for the same reason.
            var spatialHash = new NativeParallelMultiHashMap<int, int>(count + 16 * JobsUtility.MaxJobThreadCount, Allocator.Persistent);

            // PERF-LOCK: in-memory scalar capture only — per-cache-build path, no disk/alloc/format/DateTime
            // here (Axiom 15). Lets a native crash in this class be classified without a dump: at crash time
            // the breadcrumb embeds these; Count == Capacity ⇒ the false-exhaustion overrun (fix a57f0e912).
            CivicSurvival.Core.Diagnostics.CrashScalars.SetSpatialHash(count, spatialHash.Capacity);
            CivicSurvival.Core.Diagnostics.CrashScalars.SetBuildingCacheLength(count);
            CivicSurvival.Core.Diagnostics.CrashScalars.LastJob = "BuildSpatialHashJob";

            // Burst-compiled job for positions extraction
            var extractJob = new ExtractPositionsJob
            {
                Transforms = transforms,
                Positions = positions
            };
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ExtractPositionsJob.Schedule count={count} transforms={transforms.IsCreated}/{transforms.Length} positions={positions.IsCreated}/{positions.Length}");
            var extractHandle = extractJob.Schedule(count, 64);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ExtractPositionsJob.Schedule count={count} transforms={transforms.IsCreated}/{transforms.Length} positions={positions.IsCreated}/{positions.Length}");

            // Burst-compiled job for spatial hash building (depends on positions)
            var hashJob = new BuildSpatialHashJob
            {
                Positions = positions,
                CellSize = DEFAULT_CELL_SIZE,
                SpatialHash = spatialHash.AsParallelWriter()
            };
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BuildSpatialHashJob.Schedule count={count} positions={positions.IsCreated}/{positions.Length} spatialHash={spatialHash.IsCreated}/capacity={spatialHash.Capacity}");
            var hashHandle = hashJob.Schedule(count, 64, extractHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post BuildSpatialHashJob.Schedule count={count} positions={positions.IsCreated}/{positions.Length} spatialHash={spatialHash.IsCreated}/capacity={spatialHash.Capacity}");

            // ASYNC: Don't Complete(), return pending cache
            return new BuildingFrameCache
            {
                EntityIndices = entityIndices,
                EntityVersions = entityVersions,
                Positions = positions,
                SpatialHash = spatialHash,
                CellSize = DEFAULT_CELL_SIZE,
                m_DesyncCount = new NativeReference<int>(0, Allocator.Persistent),
                m_DesyncWorstIdx = new NativeReference<int>(-1, Allocator.Persistent),
                m_PendingJobHandle = hashHandle,
                m_PendingTransforms = transforms,
                m_IsPending = true
            };
        }

        public void Dispose()
        {
            // Complete any pending jobs before disposal
            if (m_IsPending)
            {
                m_PendingJobHandle.Complete();
                if (m_PendingTransforms.IsCreated) m_PendingTransforms.Dispose();
                m_IsPending = false;
            }

            if (EntityIndices.IsCreated) EntityIndices.Dispose();
            if (EntityVersions.IsCreated) EntityVersions.Dispose();
            if (Positions.IsCreated) Positions.Dispose();
            if (SpatialHash.IsCreated) SpatialHash.Dispose();
            if (m_DesyncCount.IsCreated) m_DesyncCount.Dispose();
            if (m_DesyncWorstIdx.IsCreated) m_DesyncWorstIdx.Dispose();
        }

    }

    /// <summary>
    /// Burst job: Extract XZ positions from Transform array.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    struct ExtractPositionsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Transform> Transforms;
        [WriteOnly] public NativeArray<float2> Positions;

        public void Execute(int index)
        {
            Positions[index] = new float2(Transforms[index].m_Position.x, Transforms[index].m_Position.z);
        }
    }

    /// <summary>
    /// Burst job: Build spatial hash from positions.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    struct BuildSpatialHashJob : IJobParallelFor
    {
        private const int CELL_HALF_RANGE = BuildingFrameCache.CELL_HALF_RANGE;

        [ReadOnly] public NativeArray<float2> Positions;
        public float CellSize;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter SpatialHash;

        public void Execute(int index)
        {
            float cs = math.max(CellSize, 0.001f);
            int cx = math.clamp((int)math.floor(Positions[index].x / cs), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int cz = math.clamp((int)math.floor(Positions[index].y / cs), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int cellKey = (cx + CELL_HALF_RANGE) + ((cz + CELL_HALF_RANGE) << 16);
            SpatialHash.Add(cellKey, index);
        }
    }

    /// <summary>
    /// Spatial query utilities for threat damage calculations.
    /// Uses spatial hash for O(K) queries instead of O(N).
    /// </summary>
    public static class SpatialQueryHelper
    {
        private const int CELL_HALF_RANGE = BuildingFrameCache.CELL_HALF_RANGE;

        /// <summary>
        /// Find all buildings within radius using spatial hash.
        /// Performance: O(K) where K = buildings in overlapping cells.
        /// </summary>
        public static NativeList<Entity> FindBuildingsInRadius(
            float3 center,
            float radius,
            in BuildingFrameCache cache)
        {
            var result = new NativeList<Entity>(Allocator.Temp);

            if (!cache.IsCreated || cache.IsPending || cache.EntityIndices.Length == 0)
                return result;

            float radiusSq = radius * radius;
            float2 centerPos = new float2(center.x, center.z);
            float cellSize = math.max(cache.CellSize, 0.001f);

            int minCX = math.clamp((int)math.floor((center.x - radius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int maxCX = math.clamp((int)math.floor((center.x + radius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int minCZ = math.clamp((int)math.floor((center.z - radius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int maxCZ = math.clamp((int)math.floor((center.z + radius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);

            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cz = minCZ; cz <= maxCZ; cz++)
                {
                    int cellKey = (cx + CELL_HALF_RANGE) + ((cz + CELL_HALF_RANGE) << 16);

                    if (cache.SpatialHash.TryGetFirstValue(cellKey, out int buildingIdx, out var iterator))
                    {
                        do
                        {
                            if (!cache.TryGetPosition(buildingIdx, out float2 buildingPos))
                                continue;
                            if (math.distancesq(centerPos, buildingPos) <= radiusSq)
                            {
                                result.Add(cache.GetEntity(buildingIdx));
                            }
                        }
                        while (cache.SpatialHash.TryGetNextValue(out buildingIdx, ref iterator));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get 2D distance from position to building.
        /// </summary>
        public static float GetDistance2D(float3 position, float3 buildingPosition)
        {
            return math.distance(
                new float2(position.x, position.z),
                new float2(buildingPosition.x, buildingPosition.z));
        }

        /// <summary>
        /// Find the closest building to a position within maxRadius.
        /// Returns Entity.Null if no building found.
        /// Used for resolving impact target from Position (replaces Entity TargetEntity).
        /// </summary>
        public static Entity FindClosestBuilding(
            float3 position,
            float maxRadius,
            in BuildingFrameCache cache)
        {
            if (!cache.IsCreated || cache.IsPending || cache.EntityIndices.Length == 0)
                return Entity.Null;

            float2 centerPos = new float2(position.x, position.z);
            float cellSize = math.max(cache.CellSize, 0.001f);
            float maxRadiusSq = maxRadius * maxRadius;

            Entity closest = Entity.Null;
            float closestDistSq = float.MaxValue;

            int minCX = math.clamp((int)math.floor((position.x - maxRadius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int maxCX = math.clamp((int)math.floor((position.x + maxRadius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int minCZ = math.clamp((int)math.floor((position.z - maxRadius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);
            int maxCZ = math.clamp((int)math.floor((position.z + maxRadius) / cellSize), -CELL_HALF_RANGE, CELL_HALF_RANGE - 1);

            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cz = minCZ; cz <= maxCZ; cz++)
                {
                    int cellKey = (cx + CELL_HALF_RANGE) + ((cz + CELL_HALF_RANGE) << 16);

                    if (cache.SpatialHash.TryGetFirstValue(cellKey, out int buildingIdx, out var iterator))
                    {
                        do
                        {
                            if (!cache.TryGetPosition(buildingIdx, out float2 buildingPos))
                                continue;
                            float distSq = math.distancesq(centerPos, buildingPos);
                            if (distSq <= maxRadiusSq && distSq < closestDistSq)
                            {
                                closestDistSq = distSq;
                                closest = cache.GetEntity(buildingIdx);
                            }
                        }
                        while (cache.SpatialHash.TryGetNextValue(out buildingIdx, ref iterator));
                    }
                }
            }

            return closest;
        }
    }
}

