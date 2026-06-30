using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Game.Buildings;
using Game.Areas;
using Game.Objects;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.NeighborEnvy.Logic;

namespace CivicSurvival.Domains.NeighborEnvy.Jobs
{
    /// <summary>
    /// Burst-compiled jobs for NeighborEnvy spatial rebuild.
    ///
    /// HYBRID ARCHITECTURE:
    /// - Event-driven for UI-triggered changes (instant response)
    /// - Burst Jobs for periodic full rebuild (catches new buildings, demolished buildings)
    ///
    /// Jobs run every FULL_REBUILD_INTERVAL frames to:
    /// 1. Collect all residential building data
    /// 2. Build spatial hash grid
    /// 3. Calculate power state
    /// 4. Search for powered neighbors
    /// </summary>
    public static class NeighborEnvyJobs
    {
        /// <summary>
        /// Spatial grid cell size (same as search radius for efficiency).
        /// ENGINE constant - must stay as const for Burst compatibility.
        /// </summary>
        public const float CELL_SIZE = Engine.NeighborEnvy.CELL_SIZE;

        /// <summary>
        /// Search radius for neighbor detection.
        /// ENGINE constant - must stay as const for Burst compatibility.
        /// </summary>
        public const float ENVY_RADIUS = Engine.NeighborEnvy.ENVY_RADIUS;

        /// <summary>
        /// Hash function for grid position.
        /// Uses prime numbers for better distribution.
        /// Called from Burst Jobs - gets inlined automatically.
        /// </summary>
        public static int GetGridKey(float3 position)
        {
            int x = (int)math.floor(position.x / CELL_SIZE);
            int z = (int)math.floor(position.z / CELL_SIZE);
            return Engine.NeighborEnvy.GridKeyFromCell(x, z);
        }

        // ============================================================================
        // JOB 1: COLLECT BUILDING DATA
        // ============================================================================

        /// <summary>
        /// Collects building data from ECS chunks into flat NativeArrays.
        /// Runs in parallel across chunks.
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public struct CollectBuildingDataJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Transform> TransformType;
            [ReadOnly] public ComponentTypeHandle<CurrentDistrict> DistrictType;

            // Output arrays (parallel write via atomic counter)
            [NativeDisableParallelForRestriction]
            public NativeArray<Entity> Entities;

            [NativeDisableParallelForRestriction]
            public NativeArray<float3> Positions;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> Districts;

            [NativeDisableParallelForRestriction]
            public NativeArray<long> EntityKeys;

            // Atomic counter for thread-safe indexing
#pragma warning disable CIVIC079 // Counter is written atomically (Interlocked.Increment) — not ReadOnly
            public NativeArray<int> Counter;

            public NativeArray<int> CommittedCounter;
#pragma warning restore CIVIC079

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var transforms = chunk.GetNativeArray(ref TransformType);
                var districts = chunk.GetNativeArray(ref DistrictType);

                int count = chunk.Count;

                // Record total reservation demand (diagnostic only — lets CompleteRebuild detect
                // and warn when the live query grew past the snapshot allocated for it).
                unsafe
                {
                    System.Threading.Interlocked.Add(ref ((int*)Counter.GetUnsafePtr())[0], count);
                }

                // Reserve the write offset from CommittedCounter itself so the written range stays
                // contiguous. Downstream jobs read [0, CommittedCounter) as a dense range; reserving
                // from a separate counter and rolling that back (the previous design) could leave an
                // unwritten hole inside [0, CommittedCounter) if the query grew between sizing and
                // execution. Here baseIndex == the write position, so the only slack ever trimmed is
                // the tail of the single chunk that straddles the array end — never an interior gap.
                int baseIndex;
                unsafe
                {
                    baseIndex = System.Threading.Interlocked.Add(
                        ref ((int*)CommittedCounter.GetUnsafePtr())[0], count) - count;
                }

                int writableCount = math.min(count, Entities.Length - baseIndex);
                if (writableCount <= 0)
                {
                    unsafe
                    {
                        System.Threading.Interlocked.Add(ref ((int*)CommittedCounter.GetUnsafePtr())[0], -count);
                    }
                    return; // Buffer overflow protection
                }

                if (writableCount < count)
                {
                    unsafe
                    {
                        System.Threading.Interlocked.Add(ref ((int*)CommittedCounter.GetUnsafePtr())[0], -(count - writableCount));
                    }
                }

                for (int i = 0; i < writableCount; i++)
                {
                    int idx = baseIndex + i;
                    Entities[idx] = entities[i];
                    Positions[idx] = transforms[i].m_Position;
                    Districts[idx] = districts[i].m_District.Index;
                    EntityKeys[idx] = BuildingIdentityKey.Pack(entities[i]);
                }
            }
        }

        // ============================================================================
        // JOB 2: BUILD SPATIAL GRID
        // ============================================================================

        /// <summary>
        /// Builds spatial hash grid from collected positions.
        /// Single-threaded but still Burst-compiled for speed.
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public struct BuildSpatialGridJob : IJob
        {
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<long> EntityKeys;
            [ReadOnly] public NativeArray<int> CommittedCounter;

            public NativeParallelMultiHashMap<int, long>.ParallelWriter SpatialGrid;

            public void Execute()
            {
                int count = math.min(CommittedCounter[0], Positions.Length);
                for (int i = 0; i < count; i++)
                {
                    int gridKey = GetGridKey(Positions[i]);
                    SpatialGrid.Add(gridKey, EntityKeys[i]);
                }
            }
        }

        // ============================================================================
        // JOB 3: CALCULATE POWER STATE
        // ============================================================================

        /// <summary>
        /// Calculates power state for each building based on blackout rules.
        /// Runs in parallel.
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public struct CalculatePowerStateJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<int> CommittedCounter;
            [ReadOnly] public ComponentLookup<ElectricityConsumer> ConsumerLookup;
            [ReadOnly] public ComponentLookup<BlackoutState> BlackoutStateLookup;
            public float GridPowerThreshold;

            // Output: 1 = powered, 0 = blacked out
            [WriteOnly] public NativeArray<byte> PowerStates;

            public void Execute(int index)
            {
                int committedCount = math.min(CommittedCounter[0], Entities.Length);
                if (index >= committedCount)
                {
                    PowerStates[index] = 0;
                    return;
                }

                Entity entity = Entities[index];
                if (!ConsumerLookup.TryGetComponent(entity, out var consumer))
                {
                    PowerStates[index] = 0;
                    return;
                }

                bool hasBlackoutState = BlackoutStateLookup.TryGetComponent(entity, out var blackoutState);
                bool isBlackoutEnabled = hasBlackoutState && BlackoutStateLookup.IsComponentEnabled(entity);
                bool isPowered = EnvyPowerStateLogic.IsBuildingPowered(
                    consumer,
                    hasBlackoutState,
                    isBlackoutEnabled,
                    blackoutState,
                    GridPowerThreshold);
                PowerStates[index] = isPowered ? (byte)1 : (byte)0;
            }
        }

        // ============================================================================
        // JOB 4: SPATIAL ENVY SEARCH
        // ============================================================================

        /// <summary>
        /// Searches for powered neighbors within ENVY_RADIUS.
        /// Only processes blacked-out buildings.
        /// Runs in parallel.
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public struct SpatialEnvySearchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<long> EntityKeys;
            [ReadOnly] public NativeArray<byte> PowerStates;
            [ReadOnly] public NativeArray<int> CommittedCounter;
            [ReadOnly] public NativeParallelMultiHashMap<int, long> SpatialGrid;

            // Entity(Index,Version) -> PowerState lookup for neighbors
            [ReadOnly] public NativeHashMap<long, byte> PowerStateLookup;

            // Entity(Index,Version) -> Position lookup for neighbors
            [ReadOnly] public NativeHashMap<long, float3> PositionLookup;

            // Output: 1 = has envy (blacked out with powered neighbor), 0 = no envy
            [WriteOnly] public NativeArray<byte> EnvyResults;

            public void Execute(int index)
            {
                int committedCount = math.min(CommittedCounter[0], Positions.Length);
                if (index >= committedCount)
                {
                    EnvyResults[index] = 0;
                    return;
                }

                // Only check blacked-out buildings
                if (PowerStates[index] == 1)
                {
                    EnvyResults[index] = 0;
                    return;
                }

                float3 position = Positions[index];
                long myEntityKey = EntityKeys[index];

                // Check all 9 adjacent grid cells
                int cx = (int)math.floor(position.x / CELL_SIZE);
                int cz = (int)math.floor(position.z / CELL_SIZE);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int gridKey = Engine.NeighborEnvy.GridKeyFromCell(cx + dx, cz + dz);

                        if (!SpatialGrid.TryGetFirstValue(gridKey, out long neighborKey, out var iterator))
                            continue;

                        do
                        {
                            // Skip self
                            if (neighborKey == myEntityKey)
                                continue;

                            // Check if neighbor is powered
                            if (!PowerStateLookup.TryGetValue(neighborKey, out byte neighborPower))
                                continue;

                            if (neighborPower == 0)
                                continue; // Neighbor also blacked out

                            // Check distance
                            if (!PositionLookup.TryGetValue(neighborKey, out float3 neighborPos))
                                continue;

#pragma warning disable CIVIC078 // Early-exit pattern; ENVY_RADIUS constant — sqrt negligible
                            float distance = math.distance(position, neighborPos);
#pragma warning restore CIVIC078
                            if (distance <= ENVY_RADIUS)
                            {
                                EnvyResults[index] = 1;
                                return; // Found powered neighbor - has envy
                            }

                        } while (SpatialGrid.TryGetNextValue(out neighborKey, ref iterator));
                    }
                }

                EnvyResults[index] = 0; // No powered neighbors
            }
        }

        // ============================================================================
        // JOB 5: BUILD LOOKUP TABLES
        // ============================================================================

        /// <summary>
        /// Builds lookup tables (EntityIndex -> PowerState, EntityIndex -> Position).
        /// Required for SpatialEnvySearchJob parallel access.
        /// Uses single-threaded NativeHashMap (parallel write not needed - runs after data collection).
        /// </summary>
#if ENABLE_BURST
        [BurstCompile]
#endif
        public struct BuildLookupTablesJob : IJob
        {
            [ReadOnly] public NativeArray<long> EntityKeys;
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<byte> PowerStates;
            [ReadOnly] public NativeArray<int> CommittedCounter;

            public NativeHashMap<long, byte> PowerStateLookup;
            public NativeHashMap<long, float3> PositionLookup;

            public void Execute()
            {
                int count = math.min(CommittedCounter[0], EntityKeys.Length);
                for (int i = 0; i < count; i++)
                {
                    long entityKey = EntityKeys[i];
                    PowerStateLookup.TryAdd(entityKey, PowerStates[i]);
                    PositionLookup.TryAdd(entityKey, Positions[i]);
                }
            }
        }
    }
}

