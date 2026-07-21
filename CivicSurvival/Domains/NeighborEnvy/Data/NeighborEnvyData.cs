using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.NeighborEnvy.Data
{
    /// <summary>
    /// Thread-safe data containers for Neighbor Envy system.
    /// Event-driven with persistent spatial grid.
    /// </summary>
    // IDISP008 suppressed: struct owns NativeContainers created in Create(), disposed in Dispose()
#pragma warning disable IDISP008
    public struct NeighborEnvyData : System.IDisposable
    {
        /// <summary>
        /// Search radius for neighbor detection in meters.
        /// </summary>
        public const float ENVY_RADIUS = Engine.NeighborEnvy.ENVY_RADIUS;

        /// <summary>
        /// Grid cell size for spatial hashing (same as search radius).
        /// </summary>
        public const float CELL_SIZE = Engine.NeighborEnvy.CELL_SIZE;

        // ============================================================================
        // PERSISTENT DATA (not cleared every frame)
        // ============================================================================

        /// <summary>
        /// Spatial hash grid: gridKey -> packed Entity(Index,Version) keys in that cell.
        /// PERSISTENT - built once, updated incrementally when buildings added/removed.
        /// </summary>
        [NonEntityIndex] public NativeParallelMultiHashMap<int, long> SpatialGrid;

        /// <summary>
        /// Positions of all registered buildings.
        /// Key = packed Entity(Index,Version), Value = world position.
        /// PERSISTENT - updated only when buildings change.
        /// </summary>
        [NonEntityIndex] public NativeParallelHashMap<long, float3> BuildingPositions;

        /// <summary>
        /// Entity map for packed Entity(Index,Version) -> Entity.
        /// PERSISTENT - needed for component operations.
        /// </summary>
        [NonEntityIndex] public NativeParallelHashMap<long, Entity> EntityMap;

        /// <summary>
        /// Power state of buildings: packed Entity(Index,Version) -> isPowered (1) or blacked out (0).
        /// Updated each frame for dirty districts only.
        /// </summary>
        [NonEntityIndex] public NativeParallelHashMap<long, byte> PowerState;

        /// <summary>
        /// District index for each building.
        /// packed Entity(Index,Version) -> districtIndex
        /// </summary>
        [NonEntityIndex] public NativeParallelHashMap<long, int> BuildingDistricts;

        // ============================================================================
        // DIRTY TRACKING (event-driven)
        // ============================================================================

        /// <summary>
        /// Queue of district indices that need recalculation.
        /// Populated by NeighborEnvySystem.OnDistrictStateChanged → MarkDistrictDirtyWithNeighbors().
        /// </summary>
        public NativeQueue<int> DirtyDistricts;

        /// <summary>
        /// Set of dirty district indices (for deduplication).
        /// </summary>
        [NonEntityIndex] public NativeHashSet<int> DirtyDistrictSet;

        /// <summary>
        /// Flag: spatial grid needs full rebuild (first run or major change).
        /// </summary>
        public bool NeedsFullRebuild;

        // ============================================================================
        // TEMPORARY DATA (cleared each processing cycle)
        // ============================================================================

        /// <summary>
        /// Buildings to check for envy in current cycle.
        /// </summary>
        public NativeList<long> BuildingsToProcess;

        private const int SPATIAL_GRID_CAPACITY_MULTIPLIER = 2;

        public bool IsCreated => SpatialGrid.IsCreated;

        public static int CalculateSpatialCapacity(int entityCount)
        {
            if (entityCount <= 0)
                return 1;

            return entityCount > int.MaxValue / SPATIAL_GRID_CAPACITY_MULTIPLIER
                ? int.MaxValue
                : entityCount * SPATIAL_GRID_CAPACITY_MULTIPLIER;
        }

        private static int AddCapacityHeadroom(int entityCount)
        {
            if (entityCount <= 0)
                return Engine.NeighborEnvy.ENTITY_BUFFER_HEADROOM;

            return entityCount > int.MaxValue - Engine.NeighborEnvy.ENTITY_BUFFER_HEADROOM
                ? int.MaxValue
                : entityCount + Engine.NeighborEnvy.ENTITY_BUFFER_HEADROOM;
        }

        public static NeighborEnvyData Create(int capacity = Engine.DataStructures.LARGE_CAPACITY)
        {
            return new NeighborEnvyData
            {
                // Persistent
                SpatialGrid = new NativeParallelMultiHashMap<int, long>(CalculateSpatialCapacity(capacity), Allocator.Persistent),
                BuildingPositions = new NativeParallelHashMap<long, float3>(capacity, Allocator.Persistent),
                EntityMap = new NativeParallelHashMap<long, Entity>(capacity, Allocator.Persistent),
                PowerState = new NativeParallelHashMap<long, byte>(capacity, Allocator.Persistent),
                BuildingDistricts = new NativeParallelHashMap<long, int>(capacity, Allocator.Persistent),

                // Dirty tracking
                DirtyDistricts = new NativeQueue<int>(Allocator.Persistent),
                // FIX P1-NE-002: Use Balance constant instead of magic number
                DirtyDistrictSet = new NativeHashSet<int>(Engine.NeighborEnvy.INITIAL_DISTRICT_SET_CAPACITY, Allocator.Persistent),
                NeedsFullRebuild = true,

                // Temporary
                BuildingsToProcess = new NativeList<long>(capacity, Allocator.Persistent)
            };
        }

        /// <summary>
        /// Mark a district as needing recalculation.
        /// NOTE: Must be called from main thread only (NativeQueue/NativeHashSet are NOT thread-safe).
        /// </summary>
        public void MarkDistrictDirty(int districtIndex)
        {
            // Deduplicate
            if (!DirtyDistrictSet.Contains(districtIndex))
            {
                DirtyDistrictSet.Add(districtIndex);
                DirtyDistricts.Enqueue(districtIndex);
            }
        }

        /// <summary>
        /// Expand current dirty districts with spatially adjacent districts.
        /// Single O(total buildings) pass regardless of how many districts are dirty.
        /// Solves cross-district boundary problem (FIX S7-03): envy is spatial (100m radius),
        /// so changing district A can affect buildings in neighboring district B.
        /// Call once before EnvyIncrementalLogic.Execute — batches all pending events.
        /// Must be called from main thread only.
        /// </summary>
        public void ExpandDirtyDistrictsWithNeighbors()
        {
            if (DirtyDistrictSet.Count == 0)
                return;

            var neighborDistricts = new NativeHashSet<int>(Engine.NeighborEnvy.INITIAL_DISTRICT_SET_CAPACITY, Allocator.Temp);
            var keysBuffer = new NativeList<int>(Engine.NeighborEnvy.ADJACENT_CELLS_COUNT, Allocator.Temp);

            try
            {
                // Single pass — find neighbors for ALL dirty districts at once
                foreach (var kvp in BuildingDistricts)
                {
                    if (!DirtyDistrictSet.Contains(kvp.Value))
                        continue;

                    long entityKey = kvp.Key;
                    if (!BuildingPositions.TryGetValue(entityKey, out float3 position))
                        continue;

                    keysBuffer.Clear();
                    GetAdjacentGridKeys(position, ref keysBuffer);

                    foreach (int gridKey in keysBuffer)
                    {
                        if (!SpatialGrid.TryGetFirstValue(gridKey, out long neighborKey, out var iterator))
                            continue;

                        do
                        {
                            if (!BuildingDistricts.TryGetValue(neighborKey, out int neighborDistrict))
                                continue;

                            if (!DirtyDistrictSet.Contains(neighborDistrict))
                                neighborDistricts.Add(neighborDistrict);

                        } while (SpatialGrid.TryGetNextValue(out neighborKey, ref iterator));
                    }
                }

                // Mark all discovered neighbor districts as dirty
                foreach (int neighbor in neighborDistricts)
                {
                    MarkDistrictDirty(neighbor);
                }
            }
            finally
            {
                neighborDistricts.Dispose();
                keysBuffer.Dispose();
            }
        }

        /// <summary>
        /// Check if any districts need processing.
        /// </summary>
        public bool HasDirtyDistricts => DirtyDistricts.Count > 0 || NeedsFullRebuild;

        /// <summary>
        /// Get next dirty district to process.
        /// Returns -1 if none.
        /// </summary>
        public int DequeueNextDirtyDistrict()
        {
            if (DirtyDistricts.TryDequeue(out int districtIndex))
            {
                DirtyDistrictSet.Remove(districtIndex);
                return districtIndex;
            }
            return -1;
        }

        /// <summary>
        /// Clear temporary data for new processing cycle.
        /// Does NOT clear persistent spatial grid.
        /// </summary>
        public void ClearTemporary()
        {
            BuildingsToProcess.Clear();
            TrimScratchCapacity();
        }

        /// <summary>
        /// Clear spatial data for full rebuild.
        /// FIX T10-3: Dirty flags are NOT cleared — they track changes that arrived
        /// AFTER the rebuild snapshot was taken and must survive into next update.
        /// </summary>
        public void ClearAll()
        {
            SpatialGrid.Clear();
            BuildingPositions.Clear();
            EntityMap.Clear();
            PowerState.Clear();
            BuildingDistricts.Clear();

            ClearTemporary();
        }

        /// <summary>
        /// Clear dirty district tracking. Used by SetDefaults on load/new game
        /// to discard stale flags from previous session.
        /// </summary>
        public void ClearDirtyDistricts()
        {
            DirtyDistrictSet.Clear();
            DirtyDistricts.Clear();
        }

        /// <summary>
        /// Ensure capacity for expected entity count.
        /// </summary>
        public void EnsureCapacity(int entityCount)
        {
            if (BuildingPositions.Capacity < entityCount)
            {
                // FIX P1-NE-002: Use Balance constant instead of magic number
                int newCapacity = AddCapacityHeadroom(entityCount);
                BuildingPositions.Capacity = newCapacity;
                EntityMap.Capacity = newCapacity;
                PowerState.Capacity = newCapacity;
                BuildingDistricts.Capacity = newCapacity;
            }

            int spatialCapacity = CalculateSpatialCapacity(entityCount);
            if (SpatialGrid.Capacity < spatialCapacity)
            {
                SpatialGrid.Capacity = spatialCapacity;
            }

            if (BuildingsToProcess.Capacity < entityCount)
            {
                BuildingsToProcess.Capacity = entityCount;
            }
        }

        private void TrimScratchCapacity()
        {
            if (!BuildingsToProcess.IsCreated)
                return;

            int liveBuildings = BuildingPositions.IsCreated ? BuildingPositions.Count() : 0;
            int targetCapacity = math.max(Engine.DataStructures.LARGE_CAPACITY, liveBuildings);
            if (BuildingsToProcess.Capacity > CalculateSpatialCapacity(targetCapacity))
                BuildingsToProcess.Capacity = targetCapacity;
        }

        /// <summary>
        /// Add building to spatial grid.
        /// Called incrementally when new buildings detected.
        /// </summary>
        // Grid cell keys stay int; building identity values are packed Entity(Index,Version).
#pragma warning disable CIVIC097
        public void RegisterBuilding(Entity entity, float3 position, int districtIndex)
        {
            long entityKey = BuildingIdentityKey.Pack(entity);

            if (BuildingPositions.ContainsKey(entityKey))
                return; // Already registered

            EnsureCapacity(BuildingPositions.Count() + 1);

            BuildingPositions.Add(entityKey, position);
            EntityMap.Add(entityKey, entity);
            BuildingDistricts.Add(entityKey, districtIndex);
            PowerState.Add(entityKey, 1); // Assume powered initially

            // Add to spatial grid
            int gridKey = GetGridKey(position);
            SpatialGrid.Add(gridKey, entityKey);
        }

#pragma warning restore CIVIC097

        /// <summary>
        /// Remove building from spatial grid.
        /// Called when building destroyed.
        /// </summary>
        public void UnregisterBuilding(long entityKey)
        {
            if (!BuildingPositions.TryGetValue(entityKey, out float3 position))
                return;

            // Remove from spatial grid
            int gridKey = GetGridKey(position);

            // Remove from multi-hash map (need to iterate)
            if (SpatialGrid.TryGetFirstValue(gridKey, out long value, out var iterator))
            {
                do
                {
                    if (value == entityKey)
                    {
                        SpatialGrid.Remove(iterator);
                        break;
                    }
                } while (SpatialGrid.TryGetNextValue(out value, ref iterator));
            }

            BuildingPositions.Remove(entityKey);
            EntityMap.Remove(entityKey);
            BuildingDistricts.Remove(entityKey);
            PowerState.Remove(entityKey);
        }

        /// <summary>
        /// Update power state for a building.
        /// </summary>
        public void SetPowerState(Entity entity, bool isPowered)
        {
            SetPowerState(BuildingIdentityKey.Pack(entity), isPowered);
        }

        public void SetPowerState(long entityKey, bool isPowered)
        {
            if (PowerState.ContainsKey(entityKey))
            {
                PowerState[entityKey] = isPowered ? (byte)1 : (byte)0;
            }
        }

        /// <summary>
        /// Hash function for grid position.
        /// Uses prime numbers for better distribution.
        /// </summary>
        public static int GetGridKey(float3 position)
        {
            int x = (int)math.floor(position.x / CELL_SIZE);
            int z = (int)math.floor(position.z / CELL_SIZE);
            return Engine.NeighborEnvy.GridKeyFromCell(x, z);
        }

        /// <summary>
        /// Get all 9 grid keys for position and adjacent cells.
        /// </summary>
        public static void GetAdjacentGridKeys(float3 position, ref NativeList<int> keys)
        {
            int cx = (int)math.floor(position.x / CELL_SIZE);
            int cz = (int)math.floor(position.z / CELL_SIZE);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int key = Engine.NeighborEnvy.GridKeyFromCell(cx + dx, cz + dz);
                    keys.Add(key);
                }
            }
        }

        public void Dispose()
        {
            if (SpatialGrid.IsCreated) SpatialGrid.Dispose();
            if (BuildingPositions.IsCreated) BuildingPositions.Dispose();
            if (EntityMap.IsCreated) EntityMap.Dispose();
            if (PowerState.IsCreated) PowerState.Dispose();
            if (BuildingDistricts.IsCreated) BuildingDistricts.Dispose();
            if (DirtyDistricts.IsCreated) DirtyDistricts.Dispose();
            if (DirtyDistrictSet.IsCreated) DirtyDistrictSet.Dispose();
            if (BuildingsToProcess.IsCreated) BuildingsToProcess.Dispose();
        }
    }
#pragma warning restore IDISP008
}
