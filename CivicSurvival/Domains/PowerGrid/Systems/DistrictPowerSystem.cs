using Game.Simulation;
using Game.Buildings;
using Game.Areas;
using Game.Common;
using Game.Tools;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Domains.PowerGrid.Systems
{
    /// <summary>
    /// Burst IJob: iterate consumer chunks, aggregate per-district power into native output.
    /// Single-threaded Burst — no parallel aggregation complexity, still much faster than managed.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public struct CalculateDistrictPowerJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<ArchetypeChunk> Chunks;

        [ReadOnly] public ComponentTypeHandle<ElectricityConsumer> ConsumerHandle;
        [ReadOnly] public ComponentTypeHandle<CurrentDistrict> DistrictHandle;
        [ReadOnly] public ComponentTypeHandle<ResidentialProperty> ResidentialHandle;
        [ReadOnly] public ComponentTypeHandle<CommercialProperty> CommercialHandle;
        [ReadOnly] public ComponentTypeHandle<IndustrialProperty> IndustrialHandle;
        [ReadOnly] public ComponentTypeHandle<OfficeProperty> OfficeHandle;

        public NativeParallelHashMap<long, DistrictPowerData> TempMap;
        public NativeList<DistrictPowerEntry> OutputEntries;
        public NativeReference<int> OutputTotalDemandKW;

        public void Execute()
        {
            TempMap.Clear();
            OutputEntries.Clear();
            int totalDemandKW = 0;

            for (int chunkIndex = 0; chunkIndex < Chunks.Length; chunkIndex++)
            {
                var chunk = Chunks[chunkIndex];
                var consumers = chunk.GetNativeArray(ref ConsumerHandle);
                bool hasDistrict = chunk.Has(ref DistrictHandle);

                NativeArray<CurrentDistrict> districts = default;
                if (hasDistrict)
                    districts = chunk.GetNativeArray(ref DistrictHandle);

                bool hasResidential = chunk.Has(ref ResidentialHandle);
                bool hasCommercial = chunk.Has(ref CommercialHandle);
                bool hasIndustrial = chunk.Has(ref IndustrialHandle);
                bool hasOffice = chunk.Has(ref OfficeHandle);
                int categoryCount = (hasResidential ? 1 : 0) + (hasCommercial ? 1 : 0) +
                                    (hasIndustrial ? 1 : 0) + (hasOffice ? 1 : 0);

                for (int i = 0; i < chunk.Count; i++)
                {
                    int consumptionKW = consumers[i].m_WantedConsumption;
                    totalDemandKW += consumptionKW;

                    int districtIndex = 0;
                    int districtVersion = 0;
                    if (hasDistrict)
                    {
                        Entity districtEntity = districts[i].m_District;
                        if (districtEntity != Entity.Null)
                        {
                            districtIndex = districtEntity.Index;
                            districtVersion = districtEntity.Version;
                        }
                    }

                    long districtKey = PackDistrictKey(districtIndex, districtVersion);
                    if (!TempMap.TryGetValue(districtKey, out var data))
                        data = new DistrictPowerData();

                    data.TotalKW += consumptionKW;
                    data.BuildingCount++;

                    if (categoryCount == 0)
                    {
                        data.ServicesKW += consumptionKW;
                    }
                    else
                    {
                        int shareKW = consumptionKW / categoryCount;
                        int remainderKW = consumptionKW - (shareKW * categoryCount);
                        if (hasResidential) data.ResidentialKW += shareKW + TakeRemainder(ref remainderKW);
                        if (hasCommercial) data.CommercialKW += shareKW + TakeRemainder(ref remainderKW);
                        if (hasIndustrial) data.IndustrialKW += shareKW + TakeRemainder(ref remainderKW);
                        if (hasOffice) data.OfficeKW += shareKW + TakeRemainder(ref remainderKW);
                    }

                    TempMap[districtKey] = data;
                }
            }

            // Write to native output (main thread will publish to ECS)
            foreach (var kvp in TempMap)
            {
                var d = kvp.Value;
                d.ComputeMW();
                UnpackDistrictKey(kvp.Key, out int districtIndex, out int districtVersion);
                OutputEntries.Add(new DistrictPowerEntry
                {
                    District = new DistrictRef(districtIndex, districtVersion),
                    Data = d
                });
            }
            OutputTotalDemandKW.Value = totalDemandKW;
        }

        private static long PackDistrictKey(int districtIndex, int districtVersion)
        {
            return ((long)districtIndex << 32) | (uint)districtVersion;
        }

        private static void UnpackDistrictKey(long key, out int districtIndex, out int districtVersion)
        {
            districtIndex = (int)(key >> 32);
            districtVersion = (int)(key & 0xFFFFFFFF);
        }

        private static int TakeRemainder(ref int remainderKW)
        {
            if (remainderKW <= 0)
                return 0;
            remainderKW--;
            return 1;
        }
    }

    /// <summary>
    /// Calculates power consumption per district using ECS-native patterns.
    ///
    /// Architecture:
    /// - Burst IJob iterates all consumer chunks + aggregates into native output
    /// - Main thread publishes native output → ECS singleton
    /// - Sync pattern: Schedule().Complete() inside one OnThrottledUpdate. Read-side
    ///   safety on ComponentTypeHandle is enough; no inherited Dependency chain.
    ///
    /// Usage by consumers:
    /// <code>
    /// var singleton = SystemAPI.GetSingletonEntity&lt;DistrictPowerBufferSingleton&gt;();
    /// var buffer = EntityManager.GetBuffer&lt;DistrictPowerEntry&gt;(singleton);
    /// foreach (var entry in buffer) { ... }
    /// </code>
    /// </summary>
    [ActIndependent]
    public partial class DistrictPowerSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("DistrictPowerSystem");

        private EntityQuery m_ConsumerQuery;
        private EntityQuery m_DistrictQuery;
        private CivicSingletonHandle<DistrictPowerBufferSingleton> m_Singleton;

        // Change detection for district list (order version detects delete+create same tick, count does not)
#pragma warning disable CIVIC241 // Not serialized — cache, not state
        [EntityQueryOrderCursor("EntityQuery structural order cursor for the district entity buffer.")]
        private int m_LastDistrictOrderVersion;
#pragma warning restore CIVIC241

        // Type handles for chunk iteration (passed to Burst job)
        private ComponentTypeHandle<ElectricityConsumer> m_ConsumerHandle;
        private ComponentTypeHandle<CurrentDistrict> m_DistrictHandle;
        private ComponentTypeHandle<ResidentialProperty> m_ResidentialHandle;
        private ComponentTypeHandle<CommercialProperty> m_CommercialHandle;
        private ComponentTypeHandle<IndustrialProperty> m_IndustrialHandle;
        private ComponentTypeHandle<OfficeProperty> m_OfficeHandle;

        // Lookups for publishing results to ECS (main thread only)
        private BufferLookup<DistrictPowerEntry> m_EntryBufferLookup;
        private ComponentLookup<DistrictPowerBufferSingleton> m_SingletonLookup;
        // Lookup for UpdateDistrictEntitiesBuffer (rare update, UI only)
        private BufferLookup<DistrictEntityEntry> m_DistrictEntityEntryLookup;

        // Persistent collections
        [NonEntityIndex] private NativeParallelHashMap<long, DistrictPowerData> m_TempMap;
        [NonEntityIndex] private NativeList<DistrictPowerEntry> m_OutputEntries;
        [System.NonSerialized, NonEntityIndex] private NativeReference<int> m_OutputTotalDemandKW;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info($"{nameof(DistrictPowerSystem)} created (Burst IJob, sync)");

            m_ConsumerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ElectricityConsumer>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );

            m_DistrictQuery = GetEntityQuery(
                ComponentType.ReadOnly<District>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );

            // Type handles
            m_ConsumerHandle = GetComponentTypeHandle<ElectricityConsumer>(true);
            m_DistrictHandle = GetComponentTypeHandle<CurrentDistrict>(true);
            m_ResidentialHandle = GetComponentTypeHandle<ResidentialProperty>(true);
            m_CommercialHandle = GetComponentTypeHandle<CommercialProperty>(true);
            m_IndustrialHandle = GetComponentTypeHandle<IndustrialProperty>(true);
            m_OfficeHandle = GetComponentTypeHandle<OfficeProperty>(true);

            // Lookups for job writes
            m_EntryBufferLookup = GetBufferLookup<DistrictPowerEntry>(false);
            m_SingletonLookup = GetComponentLookup<DistrictPowerBufferSingleton>(false);
            m_DistrictEntityEntryLookup = GetBufferLookup<DistrictEntityEntry>(false);

            // Singleton (Inv 2; CIVIC427) — liveness-validated handle; the shape
            // callback ensures the two buffers exist on resolve/create.
            m_Singleton = CreateSingletonHandle<DistrictPowerBufferSingleton>();
            EnsureSingleton(ref m_Singleton, new DistrictPowerBufferSingleton(), EnsureDistrictPowerShape);

            // Persistent collections
            m_TempMap = new NativeParallelHashMap<long, DistrictPowerData>(128, Allocator.Persistent);
            m_OutputEntries = new NativeList<DistrictPowerEntry>(128, Allocator.Persistent);
            m_OutputTotalDemandKW = new NativeReference<int>(Allocator.Persistent);

            Log.Info($"Singleton entity created: {m_Singleton.Entity.Index}");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // CS2 destroys mod-created singletons on deserialization; re-resolve the
            // liveness-validated handle here so save/load restores publish targets.
            EnsureSingleton(ref m_Singleton, new DistrictPowerBufferSingleton(), EnsureDistrictPowerShape);
            m_SingletonLookup.Update(this);
            m_LastDistrictOrderVersion = -1;
        }

        protected override void OnThrottledUpdate()
        {
            // Re-resolve singleton each tick — CS2 destroys mod-created singletons on
            // deserialization and OnCreate does not re-run.
            m_SingletonLookup.Update(this);
            if (!m_SingletonLookup.HasComponent(m_Singleton.Entity))
            {
                Log.Warn("[DistrictPowerSystem] Singleton missing during update; publish deferred until lifecycle restore");
                return;
            }

            m_EntryBufferLookup.Update(this);
            m_DistrictEntityEntryLookup.Update(this);
            RefreshDistrictEntityEntries(force: false);

            RunCalculationCycle();
        }

        /// <summary>
        /// One sync execution of the aggregation job: refresh handles, schedule, complete,
        /// publish — all in the calling tick. Shared between OnThrottledUpdate (steady-state)
        /// and SeedFromRestoredVanillaData (post-load).
        /// </summary>
        private void RunCalculationCycle()
        {
            if (m_ConsumerQuery.IsEmpty)
            {
                // Clear stale data when no consumers exist (all buildings demolished / empty city).
                if (m_EntryBufferLookup.TryGetBuffer(m_Singleton.Entity, out var buf))
                    buf.Clear();
                if (m_SingletonLookup.HasComponent(m_Singleton.Entity))
                {
                    m_SingletonLookup[m_Singleton.Entity] = new DistrictPowerBufferSingleton
                    {
                        DistrictCount = 0,
                        TotalDemandKW = 0,
                        LastUpdateFrame = UnityEngine.Time.frameCount
                    };
                }
                return;
            }

            // Refresh chunk type handles for this tick (read-only).
            m_ConsumerHandle.Update(this);
            m_DistrictHandle.Update(this);
            m_ResidentialHandle.Update(this);
            m_CommercialHandle.Update(this);
            m_IndustrialHandle.Update(this);
            m_OfficeHandle.Update(this);

            // PERF-LOCK: Schedule().Complete() inside this single OnThrottledUpdate
            // (throttle interval 1s). The job is ECS read-only — ComponentTypeHandle
            // read-side safety is sufficient, so Schedule() does not inherit the
            // SystemBase.Dependency chain. TempJob chunks live only for the duration of
            // this call, well within the 4-frame TempJob contract. Per-tick cost
            // avg 0.05ms / max 6.2ms (PERF.log) fits the 16ms frame budget; measure on
            // a worst-case city before reintroducing async N→N+1.
            // Derive the consumer count from the chunk array we already materialize below, instead
            // of a separate CalculateEntityCount() sync point (the chunk fetch is the only sync needed).
            var chunks = m_ConsumerQuery.ToArchetypeChunkArray(Allocator.TempJob);
            int consumerCount = 0;
            for (int ci = 0; ci < chunks.Length; ci++)
                consumerCount += chunks[ci].Count;
            EnsureAggregationCapacity(consumerCount);
            var job = new CalculateDistrictPowerJob
            {
                Chunks = chunks,
                ConsumerHandle = m_ConsumerHandle,
                DistrictHandle = m_DistrictHandle,
                ResidentialHandle = m_ResidentialHandle,
                CommercialHandle = m_CommercialHandle,
                IndustrialHandle = m_IndustrialHandle,
                OfficeHandle = m_OfficeHandle,
                TempMap = m_TempMap,
                OutputEntries = m_OutputEntries,
                OutputTotalDemandKW = m_OutputTotalDemandKW
            };
#pragma warning disable CIVIC112 // Sync intentional — see PERF-LOCK above. Job is ECS read-only and per-tick cost fits the frame budget; async N→N+1 is not needed here.
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CalculateDistrictPowerJob.Schedule chunks={chunks.IsCreated}/{chunks.Length} tempMap={m_TempMap.IsCreated}/capacity={m_TempMap.Capacity} outputs={m_OutputEntries.IsCreated}/{m_OutputEntries.Length} totalRef={m_OutputTotalDemandKW.IsCreated}");
            job.Schedule().Complete();
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post CalculateDistrictPowerJob.Schedule outputs={m_OutputEntries.IsCreated}/{m_OutputEntries.Length} totalDemandKW={(m_OutputTotalDemandKW.IsCreated ? m_OutputTotalDemandKW.Value : 0)}");
#pragma warning restore CIVIC112

            PublishResults();

            if (Log.IsDebugEnabled) Log.Debug("CalculateDistrictPowerJob completed");
        }

        /// <summary>
        /// Copy completed job results from native containers to ECS singleton.
        /// Runs on main thread — no sync points for consumers reading DistrictPowerEntry.
        /// </summary>
        private void PublishResults()
        {
            if (!m_EntryBufferLookup.TryGetBuffer(m_Singleton.Entity, out var buffer))
                return;

            buffer.Clear();
            for (int i = 0; i < m_OutputEntries.Length; i++)
                buffer.Add(m_OutputEntries[i]);

            if (m_SingletonLookup.HasComponent(m_Singleton.Entity))
            {
                m_SingletonLookup[m_Singleton.Entity] = new DistrictPowerBufferSingleton
                {
                    DistrictCount = buffer.Length,
                    LastUpdateFrame = UnityEngine.Time.frameCount,
                    TotalDemandKW = m_OutputTotalDemandKW.Value
                };
            }
        }

        private void EnsureAggregationCapacity(int consumerCount)
        {
            int required = consumerCount > 128 ? consumerCount : 128;
            if (m_TempMap.Capacity < required)
                m_TempMap.Capacity = required + (required / 4) + 8;
            if (m_OutputEntries.Capacity < required)
                m_OutputEntries.Capacity = required + (required / 4) + 8;
        }

        /// <summary>
        /// Refresh DistrictEntityEntry buffer for UI row enumeration. Uses
        /// EntityQuery.ToEntityArray rather than SystemAPI.Query so the seed call
        /// path stays out of the source-gen iteration context. The tick caller is
        /// gated by an order-version cursor so the sync point only fires when the
        /// district set actually changes (player action), not every throttle.
        /// </summary>
        private void RefreshDistrictEntityEntries(bool force)
        {
            int currentOrderVersion = m_DistrictQuery.GetCombinedComponentOrderVersion(includeEntityType: true);
            if (!force && currentOrderVersion == m_LastDistrictOrderVersion)
                return;

            m_LastDistrictOrderVersion = currentOrderVersion;

            if (!m_DistrictEntityEntryLookup.TryGetBuffer(m_Singleton.Entity, out var districtBuffer))
                return;
            districtBuffer.Clear();

#pragma warning disable CIVIC218 // ToEntityArray here is gated by order-version cache in the tick path and is one-shot in the post-load seed path; per-frame sync point that CIVIC218 protects against does not apply.
            using var districts = m_DistrictQuery.ToEntityArray(Allocator.Temp);
#pragma warning restore CIVIC218
            for (int i = 0; i < districts.Length; i++)
            {
                districtBuffer.Add(new DistrictEntityEntry
                {
                    District = DistrictRef.FromEntity(districts[i])
                });
            }

            if (Log.IsDebugEnabled) Log.Debug($"District list updated: {districtBuffer.Length} districts (orderVersion={currentOrderVersion})");
        }

        private void EnsureDistrictPowerShape(EntityManager em, Entity e)
        {
            if (!em.HasBuffer<DistrictPowerEntry>(e))
                em.AddBuffer<DistrictPowerEntry>(e);
            if (!em.HasBuffer<DistrictEntityEntry>(e))
                em.AddBuffer<DistrictEntityEntry>(e);
        }

        /// <summary>
        /// Post-load seed: aggregate per-district power from already-restored vanilla ECS
        /// (ElectricityConsumer.m_WantedConsumption + CurrentDistrict + property archetypes)
        /// and publish to DistrictPowerBufferSingleton + DynamicBuffer&lt;DistrictPowerEntry&gt;.
        /// Runs the same RunCalculationCycle as the steady-state OnThrottledUpdate; the only
        /// extra work here is the em-passing EnsureSingleton overload (seed runs outside the
        /// SystemAPI-cached update context) and force-refresh of the district list cursor
        /// (stale after deserialize: OnCreate ran before vanilla entities loaded).
        /// </summary>
        public void SeedFromRestoredVanillaData(EntityManager em)
        {
            EnsureSingleton(ref m_Singleton, em, new DistrictPowerBufferSingleton(), EnsureDistrictPowerShape);
            m_SingletonLookup.Update(this);
            m_EntryBufferLookup.Update(this);
            m_DistrictEntityEntryLookup.Update(this);

            if (!m_SingletonLookup.HasComponent(m_Singleton.Entity))
            {
                Log.Warn("DistrictPowerSystem.Seed: singleton missing after EnsureSingleton — abort");
                return;
            }

            RefreshDistrictEntityEntries(force: true);
            RunCalculationCycle();
        }

        protected override void OnDestroy()
        {
            Log.Info($"{nameof(DistrictPowerSystem)} destroyed");

            if (m_TempMap.IsCreated)
                m_TempMap.Dispose();
            if (m_OutputEntries.IsCreated)
                m_OutputEntries.Dispose();
            if (m_OutputTotalDemandKW.IsCreated)
                m_OutputTotalDemandKW.Dispose();

            if (EntityManager.Exists(m_Singleton.Entity))
                EntityManager.DestroyEntity(m_Singleton.Entity);

            base.OnDestroy();
        }
    }
}
