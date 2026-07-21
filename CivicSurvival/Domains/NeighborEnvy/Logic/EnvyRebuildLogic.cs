using Colossal.Logging;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.NeighborEnvy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Domains.NeighborEnvy.Data;
using CivicSurvival.Domains.NeighborEnvy.Jobs;
using CivicSurvival.Core.Types;
using static CivicSurvival.Domains.NeighborEnvy.Jobs.NeighborEnvyJobs;

namespace CivicSurvival.Domains.NeighborEnvy.Logic
{
    /// <summary>
    /// Async rebuild state - holds buffers between frames.
    /// Frame N: Schedule jobs, store state
    /// Frame N+1: Complete jobs, apply results, dispose buffers
    /// </summary>
    internal struct PendingRebuildState
    {
        public bool IsValid;
        public int EntityCount;  // Upper bound (query count)
        public JobHandle FinalJobHandle;

        // FIX P1-11: Counter persists between frames for deferred actualCount read
        public NativeArray<int> Counter;
        public NativeArray<int> CommittedCounter;

        // Buffers (Allocator.Persistent - survive between frames)
        public NativeArray<Entity> Entities;
        public NativeArray<float3> Positions;
        public NativeArray<int> Districts;
        public NativeArray<long> EntityKeys;
        public NativeArray<byte> PowerStates;
        public NativeArray<byte> EnvyResults;

        // Hash collections — district/grid/entity indices (managed lifecycle)
        [NonEntityIndex] public NativeHashMap<int, byte> CategoryBlackouts;
        [NonEntityIndex] public NativeHashMap<int, int> DistrictSchedules;
        [NonEntityIndex] public NativeHashSet<int> VIPDistricts;
        [NonEntityIndex] public NativeParallelMultiHashMap<int, long> SpatialGrid;
        [NonEntityIndex] public NativeHashMap<long, byte> PowerStateLookup;
        [NonEntityIndex] public NativeHashMap<long, float3> PositionLookup;

        // S2953: Renamed from Dispose() to avoid IDisposable confusion on struct
        public void DisposeBuffers()
        {
            if (!IsValid) return;

            if (Entities.IsCreated) Entities.Dispose();
            if (Positions.IsCreated) Positions.Dispose();
            if (Districts.IsCreated) Districts.Dispose();
            if (EntityKeys.IsCreated) EntityKeys.Dispose();
            if (PowerStates.IsCreated) PowerStates.Dispose();
            if (EnvyResults.IsCreated) EnvyResults.Dispose();
            if (CategoryBlackouts.IsCreated) CategoryBlackouts.Dispose();
            if (DistrictSchedules.IsCreated) DistrictSchedules.Dispose();
            if (VIPDistricts.IsCreated) VIPDistricts.Dispose();
            if (SpatialGrid.IsCreated) SpatialGrid.Dispose();
            if (PowerStateLookup.IsCreated) PowerStateLookup.Dispose();
            if (PositionLookup.IsCreated) PositionLookup.Dispose();
            if (Counter.IsCreated) Counter.Dispose();
            if (CommittedCounter.IsCreated) CommittedCounter.Dispose();

            IsValid = false;
        }
    }

    /// <summary>
    /// Async rebuild logic for NeighborEnvy.
    ///
    /// ASYNC PATTERN:
    /// - Frame N: ScheduleRebuild() - schedules all jobs, returns immediately
    /// - Frame N+1: CompleteRebuild() - completes jobs, applies results
    ///
    /// This eliminates the 40-100ms spike by spreading work across frames.
    /// </summary>
    internal static class EnvyRebuildLogic
    {
        private static readonly LogContext Log = new("NeighborEnvy");

        /// <summary>
        /// Schedule rebuild jobs (non-blocking).
        /// Call CompleteRebuild() next frame to apply results.
        /// </summary>
        /// <returns>Pending state with job handle, or invalid state if nothing to rebuild</returns>
        public static PendingRebuildState ScheduleRebuild(
            EntityQuery residentialQuery,
            EntityTypeHandle entityType,
            ComponentTypeHandle<Game.Objects.Transform> transformType,
            ComponentTypeHandle<Game.Areas.CurrentDistrict> districtType,
            ComponentLookup<Game.Buildings.ElectricityConsumer> consumerLookup,
            ComponentLookup<BlackoutState> blackoutStateLookup,
            float gridPowerThreshold)
        {
            Log.Debug("[NeighborEnvy] Scheduling async rebuild...");

            int entityCount = residentialQuery.CalculateEntityCount();
            if (entityCount == 0)
            {
                Log.Debug("[NeighborEnvy] No residential buildings - skip rebuild");
                return default;
            }

            var state = default(PendingRebuildState);
            var cleanupHandle = default(JobHandle);

            try
            {
                // FIX P1-11: Allocate persistent buffers with entityCount (upper bound)
                // Counter persists to next frame - read actualCount in CompleteRebuild (no blocking)
                state = new PendingRebuildState
                {
                    IsValid = true,
                    EntityCount = entityCount,
                    Entities = new NativeArray<Entity>(entityCount, Allocator.Persistent),
                    Positions = new NativeArray<float3>(entityCount, Allocator.Persistent),
                    Districts = new NativeArray<int>(entityCount, Allocator.Persistent),
                    EntityKeys = new NativeArray<long>(entityCount, Allocator.Persistent),
                    // FIX P1-11: Persistent counter - read in CompleteRebuild
                    Counter = new NativeArray<int>(1, Allocator.Persistent),
                    CommittedCounter = new NativeArray<int>(1, Allocator.Persistent),
                };

                // Phase 1: Collect building data (async - no Complete!)
                int actualCount = entityCount;

                var collectJob = new CollectBuildingDataJob
                {
                    EntityType = entityType,
                    TransformType = transformType,
                    DistrictType = districtType,
                    Entities = state.Entities,
                    Positions = state.Positions,
                    Districts = state.Districts,
                    EntityKeys = state.EntityKeys,
                    Counter = state.Counter,
                    CommittedCounter = state.CommittedCounter
                };

                // FIX P1-11: Schedule but DON'T Complete - read actualCount in CompleteRebuild
#pragma warning disable CIVIC294 // Reads vanilla Transform/CurrentDistrict only — no prior CivicSurvival main-thread writes to these components
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CollectBuildingDataJob.ScheduleParallel actualCount={actualCount} entities={state.Entities.IsCreated}/{state.Entities.Length} positions={state.Positions.IsCreated}/{state.Positions.Length} districts={state.Districts.IsCreated}/{state.Districts.Length} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} counter={state.Counter.IsCreated}/{state.Counter.Length} committedCounter={state.CommittedCounter.IsCreated}/{state.CommittedCounter.Length}");
                var collectHandle = collectJob.ScheduleParallel(residentialQuery, default);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post CollectBuildingDataJob.ScheduleParallel actualCount={actualCount} entities={state.Entities.IsCreated}/{state.Entities.Length} positions={state.Positions.IsCreated}/{state.Positions.Length} districts={state.Districts.IsCreated}/{state.Districts.Length} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} counter={state.Counter.IsCreated}/{state.Counter.Length} committedCounter={state.CommittedCounter.IsCreated}/{state.CommittedCounter.Length}");
#pragma warning restore CIVIC294
                cleanupHandle = collectHandle;

                if (Log.IsDebugEnabled) Log.Debug($"[NeighborEnvy] Scheduled collection for up to {entityCount} buildings (async)");

                // Use entityCount as upper bound for subsequent jobs
                // They will check bounds via Entity key validation

                // Phase 2: Calculate power state from authoritative served-load state.
                state.PowerStates = new NativeArray<byte>(actualCount, Allocator.Persistent);

                var powerJob = new CalculatePowerStateJob
                {
                    Entities = state.Entities,
                    CommittedCounter = state.CommittedCounter,
                    ConsumerLookup = consumerLookup,
                    BlackoutStateLookup = blackoutStateLookup,
                    GridPowerThreshold = gridPowerThreshold,
                    PowerStates = state.PowerStates
                };
                // FIX P1-11: powerJob reads Entities from collectJob - must wait for collectHandle
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CalculatePowerStateJob.Schedule actualCount={actualCount} entities={state.Entities.IsCreated}/{state.Entities.Length} powerStates={state.PowerStates.IsCreated}/{state.PowerStates.Length}");
                var powerHandle = powerJob.Schedule(actualCount, Engine.NeighborEnvy.POWER_JOB_BATCH_SIZE, collectHandle);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post CalculatePowerStateJob.Schedule actualCount={actualCount} entities={state.Entities.IsCreated}/{state.Entities.Length} powerStates={state.PowerStates.IsCreated}/{state.PowerStates.Length}");
                cleanupHandle = powerHandle;

                // Phase 3: Build spatial grid (parallel with power)
                state.SpatialGrid = new NativeParallelMultiHashMap<int, long>(NeighborEnvyData.CalculateSpatialCapacity(actualCount), Allocator.Persistent);

                var gridJob = new BuildSpatialGridJob
                {
                    Positions = state.Positions,
                    EntityKeys = state.EntityKeys,
                    CommittedCounter = state.CommittedCounter,
                    SpatialGrid = state.SpatialGrid.AsParallelWriter()
                };
                // FIX P1-11: gridJob reads Positions/EntityKeys from collectJob - must wait for collectHandle
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BuildSpatialGridJob.Schedule actualCount={actualCount} positions={state.Positions.IsCreated}/{state.Positions.Length} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} committedCounter={state.CommittedCounter.IsCreated}/{state.CommittedCounter.Length} spatialGrid={state.SpatialGrid.IsCreated}/capacity={state.SpatialGrid.Capacity}");
                var gridHandle = gridJob.Schedule(collectHandle);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post BuildSpatialGridJob.Schedule actualCount={actualCount} positions={state.Positions.IsCreated}/{state.Positions.Length} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} committedCounter={state.CommittedCounter.IsCreated}/{state.CommittedCounter.Length} spatialGrid={state.SpatialGrid.IsCreated}/capacity={state.SpatialGrid.Capacity}");
                cleanupHandle = JobHandle.CombineDependencies(powerHandle, gridHandle);

                // Phase 4: Build lookup tables (depends on power)
                state.PowerStateLookup = new NativeHashMap<long, byte>(actualCount, Allocator.Persistent);
                state.PositionLookup = new NativeHashMap<long, float3>(actualCount, Allocator.Persistent);

                var lookupJob = new BuildLookupTablesJob
                {
                    EntityKeys = state.EntityKeys,
                    Positions = state.Positions,
                    PowerStates = state.PowerStates,
                    CommittedCounter = state.CommittedCounter,
                    PowerStateLookup = state.PowerStateLookup,
                    PositionLookup = state.PositionLookup
                };
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BuildLookupTablesJob.Schedule actualCount={actualCount} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} positions={state.Positions.IsCreated}/{state.Positions.Length} powerStates={state.PowerStates.IsCreated}/{state.PowerStates.Length} committedCounter={state.CommittedCounter.IsCreated}/{state.CommittedCounter.Length} powerStateLookup={state.PowerStateLookup.IsCreated}/capacity={state.PowerStateLookup.Capacity} positionLookup={state.PositionLookup.IsCreated}/capacity={state.PositionLookup.Capacity}");
                var lookupHandle = lookupJob.Schedule(powerHandle);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post BuildLookupTablesJob.Schedule actualCount={actualCount} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} positions={state.Positions.IsCreated}/{state.Positions.Length} powerStates={state.PowerStates.IsCreated}/{state.PowerStates.Length} committedCounter={state.CommittedCounter.IsCreated}/{state.CommittedCounter.Length} powerStateLookup={state.PowerStateLookup.IsCreated}/capacity={state.PowerStateLookup.Capacity} positionLookup={state.PositionLookup.IsCreated}/capacity={state.PositionLookup.Capacity}");
                cleanupHandle = JobHandle.CombineDependencies(gridHandle, lookupHandle);

                // Phase 5: Spatial search (depends on grid + lookup)
                state.EnvyResults = new NativeArray<byte>(actualCount, Allocator.Persistent);

                var searchJob = new SpatialEnvySearchJob
                {
                    Positions = state.Positions,
                    EntityKeys = state.EntityKeys,
                    PowerStates = state.PowerStates,
                    CommittedCounter = state.CommittedCounter,
                    SpatialGrid = state.SpatialGrid,
                    PowerStateLookup = state.PowerStateLookup,
                    PositionLookup = state.PositionLookup,
                    EnvyResults = state.EnvyResults
                };
                // FIX P1-11: gridHandle and lookupHandle already depend on collectHandle (via powerHandle)
                var combinedHandle = JobHandle.CombineDependencies(gridHandle, lookupHandle);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre SpatialEnvySearchJob.Schedule actualCount={actualCount} positions={state.Positions.IsCreated}/{state.Positions.Length} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} powerStates={state.PowerStates.IsCreated}/{state.PowerStates.Length} spatialGrid={state.SpatialGrid.IsCreated}/capacity={state.SpatialGrid.Capacity} powerStateLookup={state.PowerStateLookup.IsCreated}/capacity={state.PowerStateLookup.Capacity} positionLookup={state.PositionLookup.IsCreated}/capacity={state.PositionLookup.Capacity} envyResults={state.EnvyResults.IsCreated}/{state.EnvyResults.Length}");
                state.FinalJobHandle = searchJob.Schedule(actualCount, Engine.NeighborEnvy.SEARCH_JOB_BATCH_SIZE, combinedHandle);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post SpatialEnvySearchJob.Schedule actualCount={actualCount} positions={state.Positions.IsCreated}/{state.Positions.Length} entityKeys={state.EntityKeys.IsCreated}/{state.EntityKeys.Length} powerStates={state.PowerStates.IsCreated}/{state.PowerStates.Length} spatialGrid={state.SpatialGrid.IsCreated}/capacity={state.SpatialGrid.Capacity} powerStateLookup={state.PowerStateLookup.IsCreated}/capacity={state.PowerStateLookup.Capacity} positionLookup={state.PositionLookup.IsCreated}/capacity={state.PositionLookup.Capacity} envyResults={state.EnvyResults.IsCreated}/{state.EnvyResults.Length}");
                cleanupHandle = state.FinalJobHandle;

                Log.Debug("[NeighborEnvy] Rebuild jobs scheduled (async)");

                return state;
            }
            catch (System.Exception ex)
            {
                cleanupHandle.Complete();
                if (state.IsValid)
                    state.DisposeBuffers();

                Log.Exception("[NeighborEnvy] Rebuild schedule failed; disposed partial rebuild state", ex);
                return default;
            }
        }

        /// <summary>
        /// Complete pending rebuild and apply results.
        /// Call this frame after ScheduleRebuild().
        /// </summary>
        /// <returns>(affected, processed) counts</returns>
        public static (int affected, int processed) CompleteRebuild(
            ref PendingRebuildState state,
            ref NeighborEnvyData envyData,
            EntityManager em)
        {
            if (!state.IsValid)
                return (0, 0);

            // Complete all jobs
            state.FinalJobHandle.Complete();

            envyData.ClearAll();

            // FIX P1-11: Read actualCount AFTER jobs complete (no blocking in ScheduleRebuild)
            int actualCount = math.min(state.CommittedCounter[0], state.EntityCount);
            if (state.Counter[0] > state.CommittedCounter[0])
                Log.Warn($"[NeighborEnvy] Rebuild reserved {state.Counter[0]} building slots but committed {state.CommittedCounter[0]} into a {state.EntityCount}-slot snapshot; overflow chunks will be picked up by the next rebuild");
            envyData.EnsureCapacity(actualCount);
            int affected = 0;
            int processed = 0;

            for (int i = 0; i < actualCount; i++)
            {
                Entity entity = state.Entities[i];

                // VANILLA ISOLATION FIX: Entity may have been deleted between frames
                // (async pattern: ScheduleRebuild frame N → CompleteRebuild frame N+1)
                if (!em.Exists(entity))
                    continue;

                bool isPowered = state.PowerStates[i] == 1;

                // S07-C1 FIX: Register ALL buildings (powered + unpowered) in spatial grid.
                // Previously powered buildings were skipped → HasPoweredNeighbor() always false.
                envyData.RegisterBuilding(entity, state.Positions[i], state.Districts[i]);
                envyData.SetPowerState(entity, isPowered);

                if (isPowered)
                {
                    // S07-H1 FIX: Clear stale envy on powered buildings
#pragma warning disable CIVIC485 // Presence check selects AddComponent vs enable/disable for this enableable marker.
                    if (em.HasComponent<EnvyAffected>(entity))
#pragma warning restore CIVIC485
                        em.SetComponentEnabled<EnvyAffected>(entity, false);
                    continue;
                }

                processed++;

                bool hasEnvy = state.EnvyResults[i] == 1;
#pragma warning disable CIVIC485 // Presence check selects AddComponent vs enable/disable for this enableable marker.
                bool hasComponent = em.HasComponent<EnvyAffected>(entity);
#pragma warning restore CIVIC485

                if (hasEnvy)
                {
                    if (hasComponent)
                    {
                        em.SetComponentEnabled<EnvyAffected>(entity, true); // Bit flip, no chunk move
                        affected++;
                    }
                    // else: not yet seeded by EnvyAffectedSetupSystem (Modification4). Skip enabling
                    // this tick — the building is seeded (disabled) on the next setup throttle and
                    // enabled on the following rebuild. ≤1-throttle window of no-envy for a brand-new
                    // building; graceful, and keeps this GameSimulation hot path free of any
                    // structural add (render chunk-cache crash class — enforced by CIVIC520).
                }
                else if (hasComponent)
                {
                    em.SetComponentEnabled<EnvyAffected>(entity, false); // Bit flip, no chunk move
                }
            }

            // Dispose all buffers
            state.DisposeBuffers();

            Log.Info($"[NeighborEnvy] Async rebuild complete: {processed} processed, {affected} affected");

            return (affected, processed);
        }
    }
}
