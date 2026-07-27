using Game.Buildings;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.ThreatFlight.Jobs
{
    internal static class BuildingGridCacheMath
    {
        public static bool TryCalculate(
            Entity buildingEntity,
            float3 pos,
            Entity prefabEntity,
            ComponentLookup<ObjectGeometryData> geometryLookup,
            ComponentLookup<BuildingData> buildingDataLookup,
            float3 defaultSize,
            float lotUnitSize,
            float minBuildingRadius,
            float gridCellSize,
            float gridExpansionMargin,
            out CachedBuilding building,
            out int minX,
            out int maxX,
            out int minZ,
            out int maxZ,
            out int entryCount)
        {
            building = default;
            minX = 0;
            maxX = 0;
            minZ = 0;
            maxZ = 0;
            entryCount = 0;

            if (!math.all(math.isfinite(pos)))
                return false;

            float3 size = defaultSize;
            if (geometryLookup.TryGetComponent(prefabEntity, out var geometry))
                size = geometry.m_Size;

            if (!math.all(math.isfinite(size)))
                size = defaultSize;

            float height = pos.y + size.y;
            if (!math.isfinite(height))
                return false;

            float radius = math.max(size.x, size.z) * 0.5f;
            if (buildingDataLookup.TryGetComponent(prefabEntity, out var buildingData))
            {
                float lotWidth = buildingData.m_LotSize.x * lotUnitSize;
                float lotDepth = buildingData.m_LotSize.y * lotUnitSize;
                float radiusFromLot = math.max(lotWidth, lotDepth) * 0.5f;
                radius = math.max(radius, radiusFromLot);
            }

            radius = math.max(radius, minBuildingRadius);
            if (!math.isfinite(radius))
                return false;

            float cellSz = math.max(gridCellSize, 0.001f);
            float expandedRadius = radius + math.max(gridExpansionMargin, 0f);
            minX = (int)math.floor((pos.x - expandedRadius) / cellSz);
            maxX = (int)math.floor((pos.x + expandedRadius) / cellSz);
            minZ = (int)math.floor((pos.z - expandedRadius) / cellSz);
            maxZ = (int)math.floor((pos.z + expandedRadius) / cellSz);

            long width = (long)maxX - minX + 1L;
            long depth = (long)maxZ - minZ + 1L;
            long count = width * depth;
            if (width <= 0L || depth <= 0L || count <= 0L || count > int.MaxValue)
                return false;

            entryCount = checked((int)count);
            building = new CachedBuilding
            {
                Building = BuildingRef.FromEntity(buildingEntity),
                Center = pos,
                Size = size,
                Height = height,
                Radius = radius
            };
            return true;
        }
    }

    /// <summary>
    /// Counts expanded grid entries before the fill job so the MultiHashMap never
    /// grows or overflows inside Burst code.
    /// </summary>
#if ENABLE_BURST
    [Unity.Burst.BurstCompile]
#endif
    public struct CountExpandedBuildingGridEntriesJob : IJobChunk
    {
        public const int TOTAL_ENTRIES_INDEX = 0;
        public const int MAX_ENTRIES_PER_BUILDING_INDEX = 1;
        public const int BUILDING_COUNT_INDEX = 2;
        public const int FILL_OVERFLOW_INDEX = 3;

        [ReadOnly] public EntityTypeHandle EntityHandle;
        [ReadOnly] public ComponentTypeHandle<Transform> TransformHandle;
        [ReadOnly] public ComponentTypeHandle<PrefabRef> PrefabRefHandle;
        [ReadOnly] public ComponentLookup<ObjectGeometryData> GeometryLookup;
        [ReadOnly] public ComponentLookup<BuildingData> BuildingDataLookup;

        public NativeArray<int> Stats;
        public float3 DefaultSize;
        public float LotUnitSize;
        public float MinBuildingRadius;
        public float GridCellSize;
        public float GridExpansionMargin;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var transforms = chunk.GetNativeArray(ref TransformHandle);
            var prefabRefs = chunk.GetNativeArray(ref PrefabRefHandle);

            int total = Stats[TOTAL_ENTRIES_INDEX];
            int maxPerBuilding = Stats[MAX_ENTRIES_PER_BUILDING_INDEX];
            int buildingCount = Stats[BUILDING_COUNT_INDEX];

            for (int i = 0; i < chunk.Count; i++)
            {
                if (!BuildingGridCacheMath.TryCalculate(
                    entities[i],
                    transforms[i].m_Position,
                    prefabRefs[i].m_Prefab,
                    GeometryLookup,
                    BuildingDataLookup,
                    DefaultSize,
                    LotUnitSize,
                    MinBuildingRadius,
                    GridCellSize,
                    GridExpansionMargin,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out int entryCount))
                {
                    continue;
                }

                total = entryCount > int.MaxValue - total
                    ? int.MaxValue
                    : total + entryCount;
                maxPerBuilding = math.max(maxPerBuilding, entryCount);
                buildingCount++;
            }

            Stats[TOTAL_ENTRIES_INDEX] = total;
            Stats[MAX_ENTRIES_PER_BUILDING_INDEX] = maxPerBuilding;
            Stats[BUILDING_COUNT_INDEX] = buildingCount;
        }
    }

    /// <summary>
    /// Burst-compiled IJobChunk that reads Transform/PrefabRef directly from chunks
    /// and builds the obstacle cache. Scheduled sequentially (single worker thread).
    ///
    /// PERF: Eliminates 15ms main-thread sync point from ToComponentDataArray.
    /// ECS chains dependency on Transform writers automatically — worker thread waits
    /// for them to finish instead of blocking main thread.
    /// </summary>
#if ENABLE_BURST
    [Unity.Burst.BurstCompile]
#endif
    public struct CacheBuildingsChunkJob : IJobChunk
    {
        // Default building dimensions when prefab geometry is unavailable
        public const float DEFAULT_BUILDING_WIDTH = 30f;
        public const float DEFAULT_BUILDING_HEIGHT = 50f;
        public const float DEFAULT_BUILDING_DEPTH = 30f;
        public const float LOT_UNIT_SIZE = 8f;  // CS2: 1 lot unit = 8 meters
        public const float MIN_BUILDING_RADIUS = 15f;  // Floor: even tiny buildings get a minimum avoidance radius

        [ReadOnly] public EntityTypeHandle EntityHandle;
        [ReadOnly] public ComponentTypeHandle<Transform> TransformHandle;
        [ReadOnly] public ComponentTypeHandle<PrefabRef> PrefabRefHandle;
        [ReadOnly] public ComponentLookup<ObjectGeometryData> GeometryLookup;
        [ReadOnly] public ComponentLookup<BuildingData> BuildingDataLookup;

#pragma warning disable CIVIC129 // Schedule (sequential) — single worker thread, NativeList write is safe
        public NativeList<CachedBuilding> Buildings;
        public NativeParallelMultiHashMap<int2, int> Grid;
        public NativeArray<int> Stats;
#pragma warning restore CIVIC129

        public float3 DefaultSize;
        public float LotUnitSize;
        public float MinBuildingRadius;
        public float GridCellSize;
        public float GridExpansionMargin;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var transforms = chunk.GetNativeArray(ref TransformHandle);
            var prefabRefs = chunk.GetNativeArray(ref PrefabRefHandle);

            int remainingGridCapacity = Grid.Capacity - Grid.Count();

            for (int i = 0; i < chunk.Count; i++)
            {
                var pos = transforms[i].m_Position;
                var prefabRef = prefabRefs[i];
                var entity = entities[i];
                if (!BuildingGridCacheMath.TryCalculate(
                    entity,
                    pos,
                    prefabRef.m_Prefab,
                    GeometryLookup,
                    BuildingDataLookup,
                    DefaultSize,
                    LotUnitSize,
                    MinBuildingRadius,
                    GridCellSize,
                    GridExpansionMargin,
                    out CachedBuilding building,
                    out int minX,
                    out int maxX,
                    out int minZ,
                    out int maxZ,
                    out int entryCount))
                {
                    continue;
                }

                if (Buildings.Length >= Buildings.Capacity)
                {
                    Stats[CountExpandedBuildingGridEntriesJob.FILL_OVERFLOW_INDEX] = 1;
                    continue;
                }

                int cellsNeeded = entryCount;
                if (cellsNeeded > remainingGridCapacity)
                {
                    Stats[CountExpandedBuildingGridEntriesJob.FILL_OVERFLOW_INDEX] = 1;
                    continue;
                }

                int buildingIndex = Buildings.Length;
                Buildings.Add(building);
                remainingGridCapacity -= cellsNeeded;

                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        Grid.Add(new int2(x, z), buildingIndex);
                    }
                }
            }
        }
    }
}
