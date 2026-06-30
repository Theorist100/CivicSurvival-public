using Game;
using Game.Buildings;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using Game.Common;
using Game.Objects;
using Game.Prefabs;

using Game.Simulation;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.ThreatFlight.Helpers;
using CivicSurvival.Domains.ThreatFlight.Jobs;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ThreatFlight.Systems
{
    /// <summary>
    /// Moves threat entities toward their targets using async job pattern.
    /// Schedule this frame, apply results next frame (1-frame latency, 5x faster than sync).
    ///
    /// Architecture:
    /// - Frame N: Schedule movement job (non-blocking)
    /// - Frame N+1: Complete + Apply results, schedule next job
    /// - Ballistic: IJobEntity (few entities, sync OK)
    /// - Obstacle avoidance: Merged into ShahedMovementJob (Burst, parallel)
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(RadarDataSingleton))]
    [SingletonOwner(typeof(ThreatCameraProximitySingleton))]
    [OwnedSingletonLifecycle(
        Persisted = false,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    [HotPathSystem]
    public partial class ThreatMovementSystem : CivicSystemBase, IThreatArrivalSource, IBallisticSnapshotSource,
        ICivicSingletonOwner<RadarDataSingleton>, ICivicSingletonOwner<ThreatCameraProximitySingleton>
    {
        private static readonly LogContext Log = new("ThreatMovementSystem");
        private const RenderWriteComponentMask RenderWriteMask =
            RenderWriteComponentMask.ThreatTransform |
            RenderWriteComponentMask.ThreatMoving |
            RenderWriteComponentMask.ThreatTransformFrame;

        // Sub-toggles for perf bisection (toggled via DevTools UI in debug builds).
#pragma warning disable CS0649, CIVIC031, CIVIC059 // Release builds have no writer; default false is intentional.
        internal static volatile bool SkipRender;
        internal static volatile bool SkipMovement;
        internal static volatile bool SkipObstacles;
        internal static volatile bool SkipApply;
        internal static volatile bool SkipCollect;
        internal static volatile bool SkipBallistic;
        internal static volatile bool SkipRadar;
#pragma warning restore CS0649, CIVIC031, CIVIC059

        // Vanilla pattern: run once per 16 sim ticks with fixed time step.
        // AircraftMoveSystem uses the same interval+step вЂ" ensures equal keyframe spacing.
        private const int UPDATE_INTERVAL = 16;
        private const float FIXED_TIME_STEP = 4f / 15f;  // = UPDATE_INTERVAL / 60f вЂ" vanilla AircraftMoveSystem constant

        private const int UPDATE_OFFSET = 10; // Match vanilla AircraftMoveSystem

        public override int GetUpdateInterval(SystemUpdatePhase phase) => UPDATE_INTERVAL;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => UPDATE_OFFSET;

        private EntityQuery m_ActiveThreatQuery;
        private EntityQuery m_BallisticQuery;
        private EntityQuery m_RadarSingletonQuery;
        private EntityQuery m_ProximitySingletonQuery;
        private EntityQuery m_BuildingQuery;
        private EntityQuery m_ObjectGeometryQuery;
        private EntityQuery m_BuildingDataQuery;
        private EntityQuery m_ShahedQuery;

        private CivicSingletonHandle<RadarDataSingleton> m_Radar;

        private const float OBSTACLE_CHECK_SECONDS = 0.5f;

        // ===== Capacity & Throttle =====
        private const int INITIAL_BUILDING_CAPACITY = 2048;
        private const int INITIAL_GRID_CAPACITY = 4096;
        private const int BUILDING_CAPACITY_REBUILD_HEADROOM = 256;
        private const long GRID_CAPACITY_REBUILD_HEADROOM = 1024L;
        private const int RADAR_THROTTLE_FRAMES = 6;

        // ===== Fallback Speeds =====
        private const float FALLBACK_SHAHED_SPEED = 40f;
        private const float FALLBACK_BALLISTIC_SPEED = 400f;
        private const float FALLBACK_CAMERA_HEIGHT = 50000f;

#pragma warning disable CIVIC168 // Timer resets to 0 every OBSTACLE_CHECK_SECONDS вЂ" not stale after load
        private float m_ObstacleCheckTimer;
#pragma warning restore CIVIC168

        private ThrottleHelper m_RadarThrottle;

        // PERF: Cached camera object + position (Camera.main = FindObjectWithTag вЂ" expensive!)
        private UnityEngine.Camera m_CachedCamera = null!;
        private float3 m_CachedCameraPos;
        private float m_CameraCacheTimer;

        // Cached buildings for obstacle avoidance (double-buffered async)
        private NativeList<CachedBuilding> m_CachedBuildings;
        private NativeParallelMultiHashMap<int2, int> m_BuildingGrid;
        private NativeList<CachedBuilding> m_PendingBuildings;
        private NativeParallelMultiHashMap<int2, int> m_PendingGrid;
        private NativeArray<int> m_PendingGridStats;
        private JobHandle m_BuildingGridCountJobHandle;
        private JobHandle m_BuildingCacheJobHandle;
        private bool m_HasPendingBuildingGridCount;
        private bool m_HasPendingBuildingCache;
        private bool m_BuildingsCached;
        private float m_BuildingCacheTimer;

        // Unified arrival list: TMS fills during Apply (Shahed) and after ballistic Complete.
        // TAS reads via IThreatArrivalSource — zero component queries, zero sync points.
        private NativeList<ThreatArrivalInfo> m_ArrivedThreats;
        public NativeArray<ThreatArrivalInfo>.ReadOnly ArrivedThreats => m_ArrivedThreats.AsArray().AsReadOnly();
        public int ArrivalCount => m_ArrivedThreats.IsCreated ? m_ArrivedThreats.Length : 0;
        public bool IsCreated => m_ArrivedThreats.IsCreated;
        public void ConsumeAndClear() { if (m_ArrivedThreats.IsCreated) m_ArrivedThreats.Clear(); }

        // Completed ballistic kinematics for AirDefense. Refreshed only in the safe window
        // after BallisticMovementJobEntity is complete, before the next ballistic job is scheduled.
        private NativeList<BallisticSnapshotInfo> m_BallisticInfoList;
        public NativeArray<BallisticSnapshotInfo>.ReadOnly BallisticSnapshots => m_BallisticInfoList.AsArray().AsReadOnly();
        public int BallisticCount => m_BallisticInfoList.IsCreated ? m_BallisticInfoList.Length : 0;
        public bool IsBallisticSnapshotCreated => m_BallisticInfoList.IsCreated;

        // FIX S06-M3: Prevent re-reporting ballistic arrivals every frame until entity destroyed.
        private NativeHashSet<Entity> m_ReportedBallisticArrivals;

        // Async collect (Variant B): CollectShahedInputsJob frame N writes the back buffer;
        // STEP 1 frame N+1 completes it and swaps it into published; movement frame N+1 reads
        // published; apply frame N+2. One extra TMS cycle of latency vs the old same-frame
        // main-thread collect, in exchange for zero main-thread chunk materialisation.
        private NativeList<ShahedCollectedInput> m_CollectedPub;
        private NativeList<ShahedCollectedInput> m_CollectedBack;
        private JobHandle m_CollectJobHandle;
        private bool m_HasPendingCollectJob;
        // NaN/Inf-skipped Shahed indices recorded by the Burst collect job, warned on the
        // main thread after completion (Burst can't call Log.*).
        private NativeQueue<int> m_InvalidShahedIndices;

        // Obstacle-avoidance building-grid indices that overran the building cache (native AV
        // guard in ObstacleAvoidanceHelper). Drained + reported to telemetry on the main thread.
        private NativeQueue<int> m_ObstacleOobLog;
        private int m_ObstacleOobEmitsThisSession;
        private double m_NextRenderLogTime;

        // Async state: movement job (Frame N в†’ N+1)
        private JobHandle m_MovementJobHandle;
        private bool m_HasPendingMovementJob;

        // Apply is now synchronous (main-thread loop) вЂ" no async state needed

        // Async state: ballistic movement job
        private JobHandle m_BallisticJobHandle;
        private bool m_HasPendingBallisticJob;

        // Render job handle. Folded into system.Dependency at its schedule site (Branch B) so ECS fences
        // vanilla job readers of Transform/Moving/TransformFrame against the worker (closing the torn-read
        // race lib_burst+0x9525c0 the Modification4 manual drain left open), without a same-frame Complete
        // (the ThreatLifecycleBarrier force-complete that caused GPU starvation stays removed). Still
        // published through RenderWriteBarrier for main-thread readers Dependency can't fence
        // (CameraTracking TransformFrame, spawn/delete structural Consume). See STEP 5-RENDER.
        private JobHandle m_RenderJobHandle;

        // Previous frame's render-job handle, folded into the next render job's input dependency so
        // render N+1 cannot start writing the same Transform/Moving/TransformFrame chunks while
        // render N is still in flight (the render→render self-race that the removed same-frame
        // barrier Complete used to mask). NOT in Dependency — this self-sync is local to the render
        // schedule and does not contaminate downstream. NonSerialized: reset to default in
        // OnStopRunning/OnLoadRestore so a handle from a previous city does not leak.
        [System.NonSerialized] private JobHandle m_PrevRenderJobHandle;

        // Double-buffering to avoid race condition:
        // ApplyJob reads from buffer A while MovementJob writes to buffer B (then swap)
        private NativeList<Entity> m_EntitiesA;
        private NativeList<Entity> m_EntitiesB;
        private NativeArray<ShahedMovementOutput> m_OutputsA;
        private NativeArray<ShahedMovementOutput> m_OutputsB;
        private bool m_UseBufferA; // true = MovementJob writes to A, ApplyJob reads from B
        private double m_LastElapsedTime = double.NegativeInfinity;  // For computing real elapsed time between 16-tick updates


        /// <summary>Fixed sub-frame matching GetUpdateOffset вЂ" used by ThreatSpawnSystem for UpdateFrameData.</summary>
        public const int TmsSubFrame = UPDATE_OFFSET;

        // Services
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;
        private IEventBus m_EventBus = null!;

        // PERF: Read IdentifiedTarget via lookup, NOT in SystemAPI.Query — avoids sync point.
        // ThreatIdentifySystem writes IdentifiedTarget before this system via ThreatIdentifyReadyMarker.
        // Including it in Query creates implicit dependency chain that degrades over time.
        private ComponentLookup<IdentifiedTarget> m_IdentifiedTargetLookupRO;

        // PERF: ComponentLookup for ballistic arrival scan (STEP 1c).
        // SystemAPI.Query<RefRO<Ballistic>> was contaminating Dependency chain,
        // making subsequent ballistic job schedule heavier.
        private ComponentLookup<Ballistic> m_BallisticLookupRO;

        // Cached lookups for ApplyJobOutputs (read-write)
        private ComponentLookup<Shahed> m_ShahedLookupRW;
        private ComponentLookup<ShahedCombatState> m_CombatStateLookup;
        private ComponentLookup<ThreatPosition> m_ThreatPositionLookupRW;
        private ComponentLookup<ThreatFlightProgress> m_FlightProgressLookupRW;

        // PERF FIX: Cached lookups for building data (used in CacheBuildingsChunkJob)
        private ComponentLookup<ObjectGeometryData> m_PrefabGeometryLookup;
        private ComponentLookup<BuildingData> m_PrefabBuildingDataLookup;

        // PERF: TypeHandles for IJobChunk building cache (reads chunks directly, no sync point on main thread)
        private ComponentTypeHandle<Transform> m_TransformTypeHandle;
        private ComponentTypeHandle<PrefabRef> m_PrefabRefTypeHandle;

        // PERF: TypeHandles for CollectInputs chunk iteration (replaces degrading SystemAPI.Query)
        private ComponentTypeHandle<Shahed> m_ShahedTypeHandle;
        private ComponentTypeHandle<ShahedCombatState> m_CombatStateTypeHandle;
        private ComponentTypeHandle<ThreatPosition> m_ThreatPositionTypeHandle;
        private ComponentTypeHandle<ThreatFlightProgress> m_FlightProgressTypeHandle;
        private ComponentTypeHandle<ActiveThreat> m_ActiveThreatTypeHandle;
        private EntityTypeHandle m_EntityTypeHandle;

        // SimulationSystem: needed for frameIndex in DroneRenderWriteJob
        private SimulationSystem m_SimulationSystem = null!;
        private TerrainSystem m_TerrainSystem = null!;

        // PERF: Cached proximity singleton (avoids SystemAPI.TryGetSingletonRW overhead)
        [System.NonSerialized] private CivicSingletonHandle<ThreatCameraProximitySingleton> m_Proximity;
        private ComponentLookup<ThreatCameraProximitySingleton> m_ProximityLookupRW;


        protected override void OnCreate()
        {
            base.OnCreate();

            // PERF: Broad gate for RequireForUpdate вЂ" matches any active threat (Shahed or Ballistic)
            m_ActiveThreatQuery = GetEntityQuery(ComponentType.ReadOnly<ActiveThreat>());

            m_BallisticQuery = GetEntityQuery(
                ComponentType.ReadWrite<Ballistic>(),
                ComponentType.ReadOnly<BallisticInterceptState>(),
                ComponentType.ReadWrite<ThreatPosition>(),
                ComponentType.ReadWrite<ThreatFlightProgress>(),
                ComponentType.ReadOnly<ActiveThreat>()
            );

            // Keep destroyed rubble out of target-distance caching; ThreatDamageSystem uses
            // the same Deleted/Destroyed liveness contract for building damage targets.
            m_BuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );
            m_ObjectGeometryQuery = GetEntityQuery(ComponentType.ReadOnly<ObjectGeometryData>());
            m_BuildingDataQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingData>());

            // PERF: Dedicated Shahed query for chunk iteration (avoids SystemAPI.Query degradation)
            m_ShahedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Shahed>(),
                ComponentType.ReadOnly<ShahedCombatState>(),
                ComponentType.ReadOnly<ThreatPosition>(),
                ComponentType.ReadOnly<ThreatFlightProgress>(),
                ComponentType.ReadOnly<ActiveThreat>()
            );

            // Persistent collections (double-buffered for async cache rebuild)
            m_CachedBuildings = new NativeList<CachedBuilding>(INITIAL_BUILDING_CAPACITY, Allocator.Persistent);
            m_BuildingGrid = new NativeParallelMultiHashMap<int2, int>(INITIAL_GRID_CAPACITY, Allocator.Persistent);
            m_PendingBuildings = new NativeList<CachedBuilding>(INITIAL_BUILDING_CAPACITY, Allocator.Persistent);
            m_PendingGrid = new NativeParallelMultiHashMap<int2, int>(INITIAL_GRID_CAPACITY, Allocator.Persistent);
            m_PendingGridStats = new NativeArray<int>(4, Allocator.Persistent);

            // Async collect double-buffer (published / back) + NaN-diagnostics queue
            m_CollectedPub = new NativeList<ShahedCollectedInput>(256, Allocator.Persistent);
            m_CollectedBack = new NativeList<ShahedCollectedInput>(256, Allocator.Persistent);
            m_InvalidShahedIndices = new NativeQueue<int>(Allocator.Persistent);
            m_ObstacleOobLog = new NativeQueue<int>(Allocator.Persistent);

            // Double-buffered outputs (prevents race condition between ApplyJob and MovementJob)
            m_EntitiesA = new NativeList<Entity>(256, Allocator.Persistent);
            m_EntitiesB = new NativeList<Entity>(256, Allocator.Persistent);
            m_UseBufferA = true;

            // Camera proximity singleton (single-writer: this system)
            ThreatCameraProximitySingleton.EnsureExists(EntityManager);
            m_ProximitySingletonQuery = GetEntityQuery(ComponentType.ReadWrite<ThreatCameraProximitySingleton>());
            m_Proximity = CreateSingletonHandle<ThreatCameraProximitySingleton>(m_ProximitySingletonQuery);
            ResolveProximitySingleton(EntityManager);
            m_ProximityLookupRW = GetComponentLookup<ThreatCameraProximitySingleton>(false);

            // Radar singleton
            m_RadarSingletonQuery = GetEntityQuery(ComponentType.ReadOnly<RadarDataSingleton>());
            m_Radar = CreateSingletonHandle<RadarDataSingleton>(m_RadarSingletonQuery);
            EnsureRadarDataSingleton(EntityManager);

            // PERF: Radar UI doesn't need 30fps updates, 10fps is smooth enough
            m_RadarThrottle = new ThrottleHelper(RADAR_THROTTLE_FRAMES);

            m_IdentifiedTargetLookupRO = GetComponentLookup<IdentifiedTarget>(true);
            m_BallisticLookupRO = GetComponentLookup<Ballistic>(true);

            // PERF: Cache read-write lookups for ApplyJobOutputs (avoid GetComponentLookup per frame)
            m_ShahedLookupRW = GetComponentLookup<Shahed>(false);
            m_CombatStateLookup = GetComponentLookup<ShahedCombatState>(true);
            m_ThreatPositionLookupRW = GetComponentLookup<ThreatPosition>(false);
            m_FlightProgressLookupRW = GetComponentLookup<ThreatFlightProgress>(false);

            // PERF FIX: Cache lookups for building data (used in CacheBuildingsChunkJob)
            m_PrefabGeometryLookup = GetComponentLookup<ObjectGeometryData>(true);
            m_PrefabBuildingDataLookup = GetComponentLookup<BuildingData>(true);

            // PERF: TypeHandles for IJobChunk вЂ" reads Transform/PrefabRef from chunks (no main-thread sync point)
            m_TransformTypeHandle = GetComponentTypeHandle<Transform>(true);
            m_PrefabRefTypeHandle = GetComponentTypeHandle<PrefabRef>(true);

            // PERF: TypeHandles for CollectInputs chunk iteration (replaces SystemAPI.Query)
            m_ShahedTypeHandle = GetComponentTypeHandle<Shahed>(true);
            m_CombatStateTypeHandle = GetComponentTypeHandle<ShahedCombatState>(true);
            m_ThreatPositionTypeHandle = GetComponentTypeHandle<ThreatPosition>(true);
            m_FlightProgressTypeHandle = GetComponentTypeHandle<ThreatFlightProgress>(true);
            m_ActiveThreatTypeHandle = GetComponentTypeHandle<ActiveThreat>(true);
            m_EntityTypeHandle = GetEntityTypeHandle();

            // SimulationSystem: needed for frameIndex in DroneRenderWriteJob
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();

            // PERF: Skip entirely when no active threats (RequireForUpdate)
            RequireForUpdate(m_ActiveThreatQuery);

            // Unified arrival list: filled during Apply (Shahed) + after ballistic Complete.
            // TAS reads via IThreatArrivalSource — zero component queries, zero sync points.
            m_ArrivedThreats = new NativeList<ThreatArrivalInfo>(32, Allocator.Persistent);
            m_BallisticInfoList = new NativeList<BallisticSnapshotInfo>(8, Allocator.Persistent);
            m_ReportedBallisticArrivals = new NativeHashSet<Entity>(16, Allocator.Persistent);

            // Register as IThreatArrivalSource so TAS resolves via Core interface (Axiom 5)
#pragma warning disable CIVIC098 // Instance is guaranteed initialized — Mod.OnLoad() calls ServiceRegistry.Initialize() before any system OnCreate
            ServiceRegistry.Instance.Register<IThreatArrivalSource>(this);
            ServiceRegistry.Instance.Register<IBallisticSnapshotSource>(this);
#pragma warning restore CIVIC098

            // Ensure first camera proximity update fires immediately (avoids (0,0,0) position for first wave tick)
            m_CameraCacheTimer = 0.5f;

            Log.Info(" Created (async job pattern, Burst rendering, unified arrival source)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
            m_EventBus ??= ServiceRegistry.Instance.Require<IEventBus>();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            ThreatCameraProximitySingleton.EnsureExists(EntityManager);
            ResolveProximitySingleton(EntityManager);
            Log.Info(" Started");
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            ResetTransientRuntimeStateAfterLoad();
            ThreatCameraProximitySingleton.EnsureExists(entityManager);
            ResolveProximitySingleton(entityManager);
            EnsureRadarDataSingleton(entityManager);
        }

        private void ResetTransientRuntimeStateAfterLoad()
        {
            if (m_HasPendingMovementJob || m_HasPendingBallisticJob)
                JobHandle.CombineDependencies(m_MovementJobHandle, m_BallisticJobHandle).Complete();
            if (m_HasPendingCollectJob) m_CollectJobHandle.Complete();
            m_BuildingGridCountJobHandle.Complete();
            m_BuildingCacheJobHandle.Complete();
            m_RenderJobHandle.Complete();
            // Reset the render→render self-sync latch so a handle from the previous city does not
            // leak into the first render schedule of the loaded city.
            m_PrevRenderJobHandle = default;

            ResetDebugSkipToggles();
            m_HasPendingMovementJob = false;
            m_HasPendingBallisticJob = false;
            m_HasPendingCollectJob = false;
            m_HasPendingBuildingGridCount = false;
            m_HasPendingBuildingCache = false;
            m_BuildingsCached = false;
            m_LastElapsedTime = double.NegativeInfinity;
            m_ObstacleCheckTimer = 0f;
            m_BuildingCacheTimer = 0f;
            m_CameraCacheTimer = 0.5f;
            m_CachedCamera = null!;
            if (m_CachedBuildings.IsCreated) m_CachedBuildings.Clear();
            if (m_BuildingGrid.IsCreated) m_BuildingGrid.Clear();
            if (m_PendingBuildings.IsCreated) m_PendingBuildings.Clear();
            if (m_PendingGrid.IsCreated) m_PendingGrid.Clear();
            if (m_ArrivedThreats.IsCreated) m_ArrivedThreats.Clear();
            if (m_BallisticInfoList.IsCreated) m_BallisticInfoList.Clear();
            if (m_ReportedBallisticArrivals.IsCreated) m_ReportedBallisticArrivals.Clear();
            if (m_CollectedPub.IsCreated) m_CollectedPub.Clear();
            if (m_CollectedBack.IsCreated) m_CollectedBack.Clear();
            if (m_InvalidShahedIndices.IsCreated) m_InvalidShahedIndices.Clear();
            if (m_ObstacleOobLog.IsCreated) m_ObstacleOobLog.Clear();
            m_ObstacleOobEmitsThisSession = 0;
            if (m_EntitiesA.IsCreated) m_EntitiesA.Clear();
            if (m_EntitiesB.IsCreated) m_EntitiesB.Clear();
        }

        private static void ResetDebugSkipToggles()
        {
            SkipRender = false;
            SkipMovement = false;
            SkipObstacles = false;
            SkipApply = false;
            SkipCollect = false;
            SkipBallistic = false;
            SkipRadar = false;
        }

        private void ResolveProximitySingleton(EntityManager entityManager)
        {
            var singleton = EnsureSingleton(ref m_Proximity, entityManager, ThreatCameraProximitySingleton.Default);
            if (entityManager.HasComponent<ThreatCameraProximitySingleton>(singleton))
                entityManager.SetComponentData(singleton, ThreatCameraProximitySingleton.Default);
        }

        private void EnsureRadarDataSingleton(EntityManager entityManager)
        {
            // EntityManager-based EnsureSingleton: called from OnCreate and
            // ICivicSingletonOwner.OnLoadRestore (no valid SystemAPI context).
            // Canonical Inv-2 contract (liveness → query-first → dedup →
            // create-if-absent) centralized in CivicSystemBase; the shape
            // callback guarantees the RadarThreatBuffer on resolve/create.
            EnsureSingleton(ref m_Radar, entityManager, default, EnsureRadarShape);
        }

        private static void EnsureRadarShape(EntityManager em, Entity e)
        {
            if (!em.HasBuffer<RadarThreatBuffer>(e))
                em.AddBuffer<RadarThreatBuffer>(e);
        }

        private bool TryGetProximitySingleton(out Entity entity)
        {
            entity = m_Proximity.Entity;
            if (entity != Entity.Null && m_ProximityLookupRW.HasComponent(entity))
            {
                return true;
            }

            return m_ProximitySingletonQuery.TryGetSingletonEntity<ThreatCameraProximitySingleton>(out entity);
        }

        protected override void OnStopRunning()
        {
            // Complete pending async jobs when system goes idle (RequireForUpdate gate)
            m_MovementJobHandle.Complete();
            m_BallisticJobHandle.Complete();
            if (m_HasPendingCollectJob) m_CollectJobHandle.Complete();
            m_BuildingGridCountJobHandle.Complete();
            m_BuildingCacheJobHandle.Complete();
            m_RenderJobHandle.Complete();
            m_PrevRenderJobHandle = default;
            Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(Core.Diagnostics.NativeCrashMarkers.ThreatMovementPipeline);
            m_HasPendingMovementJob = false;
            m_HasPendingBallisticJob = false;
            m_HasPendingCollectJob = false;
            m_HasPendingBuildingGridCount = false;
            m_HasPendingBuildingCache = false;
            m_BuildingsCached = false;
            m_BuildingCacheTimer = 0f;
            m_ObstacleCheckTimer = 0f;
            if (m_BallisticInfoList.IsCreated) m_BallisticInfoList.Clear();
            // Clear collect buffers so a restart doesn't publish a stale drone snapshot.
            if (m_CollectedPub.IsCreated) m_CollectedPub.Clear();
            if (m_CollectedBack.IsCreated) m_CollectedBack.Clear();
            if (m_InvalidShahedIndices.IsCreated) m_InvalidShahedIndices.Clear();
            if (m_ObstacleOobLog.IsCreated) m_ObstacleOobLog.Clear();
            m_ReportedBallisticArrivals.Clear();

            // Clear radar buffer — prevents ghost dots on UI after all threats destroyed
            if (m_Radar.Entity != Entity.Null && EntityManager.Exists(m_Radar.Entity)
                && EntityManager.HasBuffer<RadarThreatBuffer>(m_Radar.Entity))
            {
                EntityManager.GetBuffer<RadarThreatBuffer>(m_Radar.Entity).Clear();
            }

            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IThreatArrivalSource>(this);
                ServiceRegistry.Instance.Unregister<IBallisticSnapshotSource>(this);
            }

            // Complete any pending async jobs before disposal
            m_MovementJobHandle.Complete();
            m_BallisticJobHandle.Complete();
            if (m_HasPendingCollectJob) m_CollectJobHandle.Complete();
            m_RenderJobHandle.Complete();
            m_BuildingGridCountJobHandle.Complete();
            m_BuildingCacheJobHandle.Complete();
            Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(Core.Diagnostics.NativeCrashMarkers.ThreatMovementPipeline);

            if (m_Radar.Entity != Entity.Null && EntityManager.Exists(m_Radar.Entity)
                && EntityManager.HasComponent<RadarDataSingleton>(m_Radar.Entity))
                EntityManager.DestroyEntity(m_Radar.Entity);

            if (m_CachedBuildings.IsCreated) m_CachedBuildings.Dispose();
            if (m_BuildingGrid.IsCreated) m_BuildingGrid.Dispose();
            if (m_PendingBuildings.IsCreated) m_PendingBuildings.Dispose();
            if (m_PendingGrid.IsCreated) m_PendingGrid.Dispose();
            if (m_PendingGridStats.IsCreated) m_PendingGridStats.Dispose();

            if (m_ArrivedThreats.IsCreated) m_ArrivedThreats.Dispose();
            if (m_BallisticInfoList.IsCreated) m_BallisticInfoList.Dispose();
            if (m_ReportedBallisticArrivals.IsCreated) m_ReportedBallisticArrivals.Dispose();

            if (m_CollectedPub.IsCreated) m_CollectedPub.Dispose();
            if (m_CollectedBack.IsCreated) m_CollectedBack.Dispose();
            if (m_InvalidShahedIndices.IsCreated) m_InvalidShahedIndices.Dispose();
            if (m_ObstacleOobLog.IsCreated) m_ObstacleOobLog.Dispose();

            // Double-buffered outputs
            if (m_EntitiesA.IsCreated) m_EntitiesA.Dispose();
            if (m_EntitiesB.IsCreated) m_EntitiesB.Dispose();
            if (m_OutputsA.IsCreated) m_OutputsA.Dispose();
            if (m_OutputsB.IsCreated) m_OutputsB.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdateImpl()
        {
            using (PerformanceProfiler.Measure("ThreatMovement.OnUpdate"))
            {
                // With GetUpdateInterval=16 + GetUpdateOffset=10, this system runs every 16 sim ticks.
                // DeltaTime is one tick's time вЂ" use ElapsedTime delta for timers.
                double currentTime = SystemAPI.Time.ElapsedTime;
                float realElapsed = (float)(currentTime - m_LastElapsedTime);
                m_LastElapsedTime = currentTime;
                // Clamp to avoid huge jumps on first frame or after pause
                if (realElapsed > 1f || realElapsed <= 0f) realElapsed = FIXED_TIME_STEP;

                m_ObstacleCheckTimer += realElapsed;
                bool doObstacleCheck = m_ObstacleCheckTimer >= OBSTACLE_CHECK_SECONDS;
                if (doObstacleCheck) m_ObstacleCheckTimer = 0f;

                // PERF: Cache buildings every ~60 seconds (async вЂ" no main-thread sync point)
                const float BUILDING_CACHE_SECONDS = 60.0f;
                m_BuildingCacheTimer += realElapsed;

                // NOTE: InterceptRequest processing moved to InterceptProcessingSystem (Core)
                // Single consumer pattern - avoids race conditions

                DynamicBuffer<RadarThreatBuffer> radarBuffer = default;
#pragma warning disable CIVIC187 // ArrivedThreats cleared by TAS (consumer-owned clear pattern), not here
                // TMS appends arrivals, TAS reads and clears every 16 ticks.
                // If TMS cleared here, arrivals between TAS ticks would be lost.

                // ============================================================
                // STEP 1: Complete pending jobs from previous frame
                // PERF: CombineDependencies → single Complete() instead of two kernel transitions
                // M8 FIX: Complete BEFORE Update(this) — RW lookups trigger implicit
                // CompleteDependencyBeforeRW which hides sync point cost from profiler.
                // ============================================================
                bool hadBallistic = m_HasPendingBallisticJob;
                bool hadMovement = m_HasPendingMovementJob;
                if (hadBallistic || hadMovement)
                {
                    using (PerformanceProfiler.Measure("SP:TMS.JobComplete"))
                    {
                        if (hadBallistic && hadMovement)
                            JobHandle.CombineDependencies(m_BallisticJobHandle, m_MovementJobHandle).Complete();
                        else if (hadBallistic)
                            m_BallisticJobHandle.Complete();
                        else
                            m_MovementJobHandle.Complete();
                    }
                    m_HasPendingBallisticJob = false;
                    m_HasPendingMovementJob = false;
                }

                // Drain obstacle-OOB diagnostics from the just-completed movement job. Read here
                // (before STEP A swaps building buffers) so m_CachedBuildings/m_BuildingGrid still
                // match the generation the job indexed.
                if (hadMovement) DrainObstacleOobLog();

                // Footprint diagnostic: publish the building-cache byte sizes here — pending jobs
                // are complete (STEP 1) and the cache is not yet swapped, so .Capacity is a stable
                // main-thread read with no job-completing sync point. The grid entry overhead
                // (key + value + internal next-index) is approximated per multi-hashmap slot.
                if (m_CachedBuildings.IsCreated)
                    NativeFootprintTracker.ReportThreatBuildingCache(
                        (long)m_CachedBuildings.Capacity * UnsafeUtility.SizeOf<CachedBuilding>());
                if (m_BuildingGrid.IsCreated)
                    NativeFootprintTracker.ReportThreatBuildingGrid(
                        (long)m_BuildingGrid.Capacity * (UnsafeUtility.SizeOf<int2>() + sizeof(int) + sizeof(int)));

                // STEP 1a: Complete the collect job scheduled last cycle and publish its
                // result. Owner is m_HasPendingCollectJob (NOT the movement/ballistic gate
                // above) — collect can be in flight on a cycle where movement was skipped
                // (SkipMovement, ballistic-only, first cycle of a wave). Tying its complete
                // to hadMovement would let radar/movement read a half-written back buffer.
                // Movement (which reads the published buffer) is already completed above, so
                // swapping the published<->back references here is safe.
                bool hadCollect = m_HasPendingCollectJob;
                if (hadCollect)
                {
                    using (PerformanceProfiler.Measure("SP:TMS.CollectComplete"))
                    {
                        m_CollectJobHandle.Complete();
                    }
                    m_HasPendingCollectJob = false;
                    DrainInvalidShahedLog();
                    (m_CollectedPub, m_CollectedBack) = (m_CollectedBack, m_CollectedPub);
                }
                else
                {
                    // No fresh collect this cycle (skip toggle / empty query). Drop the stale
                    // published snapshot so radar/audio/movement don't act on ghost drones.
                    if (m_CollectedPub.IsCreated) m_CollectedPub.Clear();
                }

                // DIAG: avoidance-input trace — relocated off the Burst collect job (which
                // can't Log.*) to a main-thread walk of the freshly-published snapshot. The
                // avoidance fields live in the collect record, so the trace is preserved.
                if (Log.IsDebugEnabled && m_CollectedPub.Length > 0)
                {
                    for (int di = 0; di < m_CollectedPub.Length; di++)
                    {
                        var avoidInput = m_CollectedPub[di].Input;
                        if (!avoidInput.IsAvoiding && avoidInput.AvoidanceCooldown <= 0f) continue;
                        var wp = avoidInput.AvoidanceWaypoint;
                        var p = avoidInput.CurrentPosition;
                        Log.Debug($"[TMS:AVOID] INPUT: entity={m_CollectedPub[di].Entity.Index} avoiding={avoidInput.IsAvoiding} cooldown={avoidInput.AvoidanceCooldown:F2} wp=({wp.x:F0},{wp.y:F0},{wp.z:F0}) pos=({p.x:F0},{p.y:F0},{p.z:F0})");
                    }
                }

                // Step A: If pending cache job is done, swap buffers only after
                // the previous movement job has completed. The movement job reads
                // m_CachedBuildings/m_BuildingGrid; after swap those containers
                // become pending and ScheduleBuildingCacheFillJob clears them.
                // PERF-LOCK: m_HasPendingBuildingCache gate guarantees Clear/Capacity-set
                // in ScheduleBuildingCacheFillJob never races an in-flight worker fill —
                // removing this guard reintroduces a NativeContainer write race.
                if (m_HasPendingBuildingCache)
                {
                    if (m_BuildingCacheJobHandle.IsCompleted)
                    {
                        m_BuildingCacheJobHandle.Complete(); // free safety handle
                        bool fillOverflowed = m_PendingGridStats[CountExpandedBuildingGridEntriesJob.FILL_OVERFLOW_INDEX] != 0;
                        if (!fillOverflowed)
                        {
                            // Swap: pending в†’ active
                            (m_CachedBuildings, m_PendingBuildings) = (m_PendingBuildings, m_CachedBuildings);
                            (m_BuildingGrid, m_PendingGrid) = (m_PendingGrid, m_BuildingGrid);
                            m_BuildingsCached = true;
                        }
                        else
                        {
                            // Keep the previous complete cache and rebuild again using a fresh count.
                            m_BuildingCacheTimer = BUILDING_CACHE_SECONDS;
                        }
                        m_HasPendingBuildingCache = false;
                    }
                }

                if (m_HasPendingBuildingGridCount && m_BuildingGridCountJobHandle.IsCompleted)
                {
                    m_BuildingGridCountJobHandle.Complete(); // free safety handle
                    ScheduleBuildingCacheFillJob();
                    m_HasPendingBuildingGridCount = false;
                }

                // Step B: Schedule new cache rebuild if timer expired (and no pending job)
                // PERF-LOCK: !m_HasPendingBuildingCache mirrors the Step A safety — same gate.
                if (!m_HasPendingBuildingGridCount && !m_HasPendingBuildingCache && (!m_BuildingsCached || m_BuildingCacheTimer >= BUILDING_CACHE_SECONDS))
                {
                    ScheduleBuildingGridCountJob();
                    m_BuildingCacheTimer = 0f;
                }

                m_ShahedLookupRW.Update(this);
                m_CombatStateLookup.Update(this);
                m_ThreatPositionLookupRW.Update(this);
                m_FlightProgressLookupRW.Update(this);
                m_BallisticLookupRO.Update(this);
                m_IdentifiedTargetLookupRO.Update(this);
                m_ProximityLookupRW.Update(this);

                // ============================================================
                // STEP 1b: Apply movement results from previous frame
                // Double-buffer: reads from the buffer MovementJob wrote to last frame
                // ============================================================
                if (hadMovement && !SkipApply)
                {
                    var readEntities = m_UseBufferA ? m_EntitiesB : m_EntitiesA;
                    var readOutputs = m_UseBufferA ? m_OutputsB : m_OutputsA;

                    if (readEntities.Length > 0 && readOutputs.IsCreated)
                    {
                        using (PerformanceProfiler.Measure("ThreatMovement.ApplyLoop"))
                        {
                            for (int ai = 0; ai < readEntities.Length; ai++)
                            {
                                var entity = readEntities[ai];
                                var output = readOutputs[ai];

                                if (!m_ShahedLookupRW.HasComponent(entity))
                                    continue;

                                // Coast gate: a Patriot-intercepted (awaiting) drone keeps flying until
                                // its interceptor arrives. Only a non-awaiting intercept (gun kill)
                                // freezes here. AwaitingInterceptorImpact rides ShahedCombatState
                                // (already loaded) → no new lookup.
                                if (m_CombatStateLookup.TryGetComponent(entity, out var combatState)
                                    && combatState.IsIntercepted && !combatState.AwaitingInterceptorImpact)
                                    continue;

                                var shahed = m_ShahedLookupRW[entity];
                                shahed.CurrentDistance += output.DistanceDelta;
                                shahed.AvoidanceCooldown = output.AvoidanceCooldown;
                                shahed.TimeSinceCheckpoint = output.TimeSinceCheckpoint;
                                shahed.LastCheckpointPos = output.LastCheckpointPos;

                                if (output.IsAvoiding || output.AvoidanceCleared)
                                {
                                    shahed.IsAvoiding = output.IsAvoiding;
                                    shahed.AvoidanceWaypoint = output.AvoidanceWaypoint;
                                    shahed.AvoidanceObstacle = output.AvoidanceObstacle;
                                    shahed.PreviousAvoidanceObstacle = output.PreviousAvoidanceObstacle;
                                }

                                shahed.CurrentDirection = output.NewDirection;
                                shahed.BankAngle = output.NewBankAngle;

                                // Propagate Burst-computed arrival flags to entity component.
                                if (output.HasArrived || output.IsExhausted)
                                    shahed.IsArrived = true;

                                m_ShahedLookupRW[entity] = shahed;

                                if (m_FlightProgressLookupRW.HasComponent(entity))
                                {
                                    m_FlightProgressLookupRW[entity] = new ThreatFlightProgress
                                    {
                                        MinDistanceToTarget = output.NewMinDistanceToTarget,
                                        MinDistanceTime = output.NewMinDistanceTime
                                    };
                                }

                                float3 arrivalPos = shahed.TargetPosition; // fallback if ThreatPosition absent

                                if (m_ThreatPositionLookupRW.HasComponent(entity))
                                {
                                    var threatPos = m_ThreatPositionLookupRW[entity];
                                    threatPos.Position += output.PositionDelta;
                                    threatPos.Rotation = output.NewRotation;
                                    threatPos.Velocity = output.Velocity;
                                    m_ThreatPositionLookupRW[entity] = threatPos;
                                    arrivalPos = threatPos.Position;
                                }

                                // Collect arrival for TAS (NativeList — zero sync points).
                                // Burst already computed HasArrived/IsExhausted; position is fresh.
                                if (output.HasArrived || output.IsExhausted)
                                {
                                    // DIAG: watchdog-exhausted (stuck) Shahed reaching crashed-arrival.
                                    // High [TMS:STUCK] rate ⇒ live set inflated by stuck threats.
                                    if (output.IsExhausted && !output.HasArrived && Log.IsDebugEnabled)
                                        Log.Debug($"[TMS:STUCK] Shahed entity={entity.Index} watchdog-exhausted minDist={output.NewMinDistanceToTarget:F0}");
                                    m_ArrivedThreats.Add(ThreatArrivalInfo.FromShahed(
                                        entity, arrivalPos, shahed.TargetPosition,
                                        output.HasArrived, shahed.ThreatGeneration));
                                }
                            }
                        }
                    }
                }

                // ============================================================
                // STEP 1c: Ballistic arrival scan (ComponentLookup — avoids Dependency contamination)
                // SystemAPI.Query<RefRO<Ballistic>> was adding Ballistic to Dependency chain,
                // making subsequent BallisticMovementJobEntity.ScheduleParallel(Dependency) heavier.
                // ComponentLookup doesn't touch Dependency — just reads component data directly.
                // Job already completed in STEP 1, so all Ballistic data is safe to read.
                // ============================================================
                // FIX S06-M3: Clear reported set when TAS has consumed arrivals
                if (m_ArrivedThreats.Length == 0 && m_ReportedBallisticArrivals.Count > 0)
                    m_ReportedBallisticArrivals.Clear();

                if (hadBallistic && !m_BallisticQuery.IsEmpty)
                {
                    using (PerformanceProfiler.Measure("TMS.Step1c"))
                    {
                    var ballisticEntities = SnapshotArrivedBallistics();
                    for (int i = 0; i < ballisticEntities.Length; i++)
                    {
                        var entity = ballisticEntities[i];
                        var ballistic = m_BallisticLookupRO[entity];
                        if (!ballistic.IsArrived) continue;
                        if (!m_ReportedBallisticArrivals.Add(entity)) continue; // Already reported

                        // M-62 FIX: Sanitize Infinity TargetPosition — use last valid ThreatPosition
                        var arrivalPos = ballistic.TargetPosition;
                        if (math.any(math.isnan(arrivalPos)) || math.any(math.isinf(arrivalPos)))
                            arrivalPos = m_ThreatPositionLookupRW.HasComponent(entity)
                                ? m_ThreatPositionLookupRW[entity].Position
                                : float3.zero;

                        bool isHit = !ballistic.IsExhausted;
                        if (!isHit && Log.IsDebugEnabled)
                            Log.Debug($"[TMS:STUCK] Ballistic entity={entity.Index} exhausted; reporting crashed arrival");

                        float impactRadius = ballistic.IsExhausted && ballistic.SuppressExhaustedImpact
                            ? 0f
                            : ballistic.ImpactRadius;
                        float damageSeverity = ballistic.IsExhausted && ballistic.SuppressExhaustedImpact
                            ? 0f
                            : ballistic.DamageSeverity;

                        m_ArrivedThreats.Add(ThreatArrivalInfo.FromBallistic(
                            entity,
                            arrivalPos,
                            arrivalPos,
                            isHit,
                            ballistic.ThreatGeneration,
                            impactRadius,
                            damageSeverity));
                    }
                    if (ballisticEntities.IsCreated) ballisticEntities.Dispose();
                    } // end TMS.Step1c
                }

                RefreshBallisticSnapshots();

                // ============================================================
                // STEP 2: Radar + audio-closest from the published collect snapshot.
                // The drone set was gathered off the main thread by CollectShahedInputsJob
                // last cycle (STEP 3b) and completed at STEP 1a this cycle, so here we only
                // walk a finished native list — no ToArchetypeChunkArray, no chunk
                // materialisation, no ActiveThreat write-dependency drain on the main thread.
                // ============================================================
                // PERF: Check throttle BEFORE loop to avoid branch in hot path
                bool updateRadar = m_RadarThrottle.ShouldUpdate() && !SkipRadar;
                if (updateRadar)
                {
                    if (!EntityManager.HasBuffer<RadarThreatBuffer>(m_Radar.Entity))
                    {
                        updateRadar = false;
                    }
                    else
                    {
                        radarBuffer = SystemAPI.GetBuffer<RadarThreatBuffer>(m_Radar.Entity);
                        radarBuffer.Clear();
                    }
                }

                // Track closest threat to camera (for audio system)
                float3 cameraPos = updateRadar ? GetCameraPosition(realElapsed) : float3.zero;
                float closestDist = float.MaxValue;
                float3 closestPos = float3.zero;

                // Radar + audio-closest walk the published collect snapshot (gathered by
                // CollectShahedInputsJob last cycle). No ToArchetypeChunkArray, no main-thread
                // chunk materialisation — just a loop over a completed native list. Entries are
                // already NaN/Inf-guarded and intercept/arrival-filtered by the collect job.
                if (updateRadar && m_CollectedPub.Length > 0)
                {
                    using (PerformanceProfiler.Measure("TMS.RadarAudioPass"))
                    {
                        for (int i = 0; i < m_CollectedPub.Length; i++)
                        {
                            var collected = m_CollectedPub[i];
                            float3 pos = collected.Input.CurrentPosition;

                            float speed = collected.Input.Speed;
                            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f)
                                speed = FALLBACK_SHAHED_SPEED;

                            radarBuffer.Add(new RadarThreatBuffer
                            {
                                EntityIndex = collected.Entity.Index,
                                EntityVersion = collected.Entity.Version,
                                Position = pos,
                                TargetPosition = collected.Input.TargetPosition,
                                Speed = speed,
                                Type = RadarThreatType.Shahed,
                                MissedShotsCount = collected.MissedShotsCount,
                                IsIdentified = collected.IsIdentified
                            });

#pragma warning disable CIVIC078 // UI closest-drone: comparison only, but small count (~50 threats)
                            float dist = math.distance(cameraPos, pos);
#pragma warning restore CIVIC078
                            if (dist < closestDist)
                            {
                                closestDist = dist;
                                closestPos = pos;
                            }
                        }
                    }
                }

                // ============================================================
                // STEP 2b: Ballistic radar + proximity singleton
                // Moved from STEP 6 (after ballistic job schedule) to eliminate sync point:
                // SystemAPI.Query<Ballistic,ThreatPosition> after ScheduleParallel forced
                // main-thread Complete of async ballistic job every 6 frames.
                // Here, previous frame's ballistic is already completed (STEP 1) в†’
                // positions are fresh, no pending writers, zero sync point.
                // ============================================================
                if (updateRadar)
                {
                    using (PerformanceProfiler.Measure("SP:TMS.BallisticRadar"))
                    {
                    // Add ballistic missiles to radar (separate query вЂ" they're few)
                    foreach (var (ballistic, bisState, threatPos, entity) in
                        SystemAPI.Query<RefRO<Ballistic>, RefRO<BallisticInterceptState>, RefRO<ThreatPosition>>()
                        .WithAll<ActiveThreat>()
                        .WithEntityAccess())
                    {
                        // Coast gate: keep an awaiting (coasting) ballistic on radar so the player
                        // still sees the incoming missile until the interceptor reaches it.
                        if (bisState.ValueRO.IsIntercepted && !bisState.ValueRO.AwaitingInterceptorImpact) continue;

                        float3 pos = threatPos.ValueRO.Position;
                        if (math.any(math.isnan(pos)) || math.any(math.isinf(pos))) continue;

                        float speed = ballistic.ValueRO.Speed;
                        if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f) speed = FALLBACK_BALLISTIC_SPEED;

                        radarBuffer.Add(new RadarThreatBuffer
                        {
                            EntityIndex = entity.Index,
                            EntityVersion = entity.Version,
                            Position = pos,
                            TargetPosition = ballistic.ValueRO.TargetPosition,
                            Speed = speed,
                            Type = RadarThreatType.Ballistic,
                            MissedShotsCount = 0
                        });

                        float dist = math.distance(cameraPos, pos);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestPos = pos;
                        }
                    }

                    // Write camera-closest data to singleton for ThreatAudioOrchestrator
                    if (TryGetProximitySingleton(out var proximityEntity)
                        && m_ProximityLookupRW.HasComponent(proximityEntity))
                    {
                        var proximity = m_ProximityLookupRW[proximityEntity];
                        bool hasClosestThreat = closestDist < float.MaxValue;
                        proximity.ClosestDistance = hasClosestThreat ? closestDist : float.MaxValue;
                        proximity.ClosestPosition = hasClosestThreat ? closestPos : float3.zero;
                        m_ProximityLookupRW[proximityEntity] = proximity;
                    }
                    } // end SP:TMS.BallisticRadar
                }

                // ============================================================
                // STEP 3: Schedule MOVEMENT job from the published collect snapshot.
                // PERF-LOCK: movement reads the async-collected list (CollectShahedInputsJob),
                // NOT m_ShahedQuery.ToArchetypeChunkArray. That main-thread chunk materialisation
                // (+ ActiveThreat write-dependency drain) is exactly what this refactor moved to
                // a Burst worker — reverting to a main-thread collect here brings the per-cycle
                // sync point back. The collect job completed at STEP 1a, so m_CollectedPub.Length
                // is known on the main thread (no deferred scheduling needed). Double-buffer:
                // MovementJob writes the current write buffer, ApplyJob reads the other.
                // ============================================================
                if (m_CollectedPub.Length > 0 && !SkipMovement)
                {
                    if (Log.IsDebugEnabled)
                        PerformanceProfiler.RecordJobSchedule("ThreatMovement.MovementSchedule", m_CollectedPub.Length);

                    using (PerformanceProfiler.Measure("ThreatMovement.MovementSchedule"))
                    {
                        // Select write buffer (ApplyJob is reading from the OTHER buffer)
                        ref var writeEntities = ref (m_UseBufferA ? ref m_EntitiesA : ref m_EntitiesB);
                        ref var writeOutputs = ref (m_UseBufferA ? ref m_OutputsA : ref m_OutputsB);

                        // Snapshot the apply entity list parallel to the outputs (next frame).
                        // Kept separate from m_CollectedPub: that buffer is swapped away next
                        // cycle while apply still needs this exact ordering.
                        //
                        // Refresh the apply-mutated input fields from the live components before
                        // scheduling: the collect snapshot was gathered BEFORE this cycle's apply
                        // (STEP 1b), so feeding it to movement as-is makes every OVERWRITTEN field
                        // (waypoint / obstacle refs / cooldown / direction) evolve from the state
                        // two cycles back. A lag-2 chain with overwrite semantics splits into two
                        // independent even/odd-cycle states — each holding its OWN avoidance
                        // waypoint, often on opposite sides of an obstacle, so the drone flips
                        // target every cycle and twitches in place (entity 495792 et al,
                        // 2026-06-10). Position is incremental (+=) and only lagged, but refreshing
                        // it too removes the residual one-step input error for free. Lookups were
                        // updated this cycle (before STEP 1b); a NaN/Inf guard mirrors the collect
                        // job so corrupted live data cannot bypass its filter.
                        writeEntities.Clear();
                        for (int ci = 0; ci < m_CollectedPub.Length; ci++)
                        {
                            var rec = m_CollectedPub[ci];
                            if (m_ShahedLookupRW.HasComponent(rec.Entity))
                            {
                                var sh = m_ShahedLookupRW[rec.Entity];
                                var input = rec.Input;
                                if (!math.any(math.isnan(sh.TargetPosition)) && !math.any(math.isinf(sh.TargetPosition)))
                                {
                                    input.TargetPosition = sh.TargetPosition;
                                    input.TargetBuilding = sh.TargetBuilding;
                                }
                                input.CurrentDistance = sh.CurrentDistance;
                                input.IsAvoiding = sh.IsAvoiding;
                                input.AvoidanceWaypoint = sh.AvoidanceWaypoint;
                                input.AvoidanceObstacle = sh.AvoidanceObstacle;
                                input.PreviousAvoidanceObstacle = sh.PreviousAvoidanceObstacle;
                                input.AvoidanceCooldown = sh.AvoidanceCooldown;
                                input.TimeSinceCheckpoint = sh.TimeSinceCheckpoint;
                                input.LastCheckpointPos = sh.LastCheckpointPos;
                                input.CurrentDirection = sh.CurrentDirection;
                                input.CurrentBankAngle = sh.BankAngle;
                                if (m_ThreatPositionLookupRW.HasComponent(rec.Entity))
                                {
                                    float3 livePos = m_ThreatPositionLookupRW[rec.Entity].Position;
                                    if (!math.any(math.isnan(livePos)) && !math.any(math.isinf(livePos)))
                                        input.CurrentPosition = livePos;
                                }
                                if (m_FlightProgressLookupRW.HasComponent(rec.Entity))
                                {
                                    var fp = m_FlightProgressLookupRW[rec.Entity];
                                    input.MinDistanceToTarget = fp.MinDistanceToTarget;
                                    input.MinDistanceTime = fp.MinDistanceTime;
                                }
                                rec.Input = input;
                                m_CollectedPub[ci] = rec;
                            }
                            writeEntities.Add(rec.Entity);
                        }

                        EnsureOutputCapacity(ref writeOutputs, m_CollectedPub.Length);

                        var terrainData = m_TerrainSystem.GetHeightData();
                        var job = new ShahedMovementJob
                        {
                            DeltaTime = FIXED_TIME_STEP,
                            ElapsedTime = SystemAPI.Time.ElapsedTime,
                            TerrainData = terrainData,
                            Collected = m_CollectedPub.AsArray(),
                            Outputs = writeOutputs,
                            Buildings = m_CachedBuildings,
                            BuildingGrid = m_BuildingGrid,
                            DoObstacleCheck = doObstacleCheck && !SkipObstacles && m_CachedBuildings.Length > 0,
                            ObstacleOobLog = m_ObstacleOobLog.AsParallelWriter()
                        };

                        // BURSTMARK crash-1 candidate (worker). Container state catches an uncreated
                        // backing buffer before the AV; worker attribution is approximate (3 jobs/frame).
                        if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                            CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ShahedMovementJob.Schedule inputs={m_CollectedPub.Length} outputs={writeOutputs.IsCreated} buildings={m_CachedBuildings.IsCreated}/{m_CachedBuildings.Length} grid={m_BuildingGrid.IsCreated}");
                        // PERF-LOCK: coarse active-wave marker only; ClearIfCurrent is phase-exit only, never per-frame after Complete.
                        Core.Diagnostics.NativeCrashBreadcrumb.Mark(Core.Diagnostics.NativeCrashMarkers.ThreatMovementPipeline);
                        // Schedule WITHOUT Complete - results applied next frame
                        m_MovementJobHandle = job.Schedule(m_CollectedPub.Length, 64);
                        if (terrainData.isCreated)
                            m_TerrainSystem.AddCPUHeightReader(m_MovementJobHandle);
                        if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                            CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post ShahedMovementJob.Schedule");
                        m_HasPendingMovementJob = true;

                        // Swap buffers: next frame ApplyJob will read from this buffer
                        m_UseBufferA = !m_UseBufferA;
                    }
                }

                // ============================================================
                // STEP 3b: Schedule next cycle's async input collection.
                // Reads the freshly-applied ThreatPosition (apply ran at STEP 1b this cycle) into
                // m_CollectedBack on a Burst worker; completed + published at STEP 1a next cycle.
                // ============================================================
                if (!SkipCollect && !m_ShahedQuery.IsEmptyIgnoreFilter)
                {
                    using (PerformanceProfiler.Measure("TMS.CollectSchedule"))
                        ScheduleCollectInputsJob();
                }

                // ============================================================
                // STEP 4: Ballistic movement (IJobEntity, async pattern)
                // PERF: Schedule without Complete() вЂ" results apply next frame (1-frame latency)
                // NOTE: Arrival detection for ballistics is in ThreatArrivalSystem
                // ============================================================
                if (!m_BallisticQuery.IsEmpty && !SkipBallistic)
                {
                    // Fixed timestep (not real delta) — fixed step keeps the lofted-arc
                    // trajectory (climb → high cruise → terminal dive) deterministic regardless
                    // of framerate.
                    var ballisticJob = new BallisticMovementJobEntity
                    {
                        DeltaTime = FIXED_TIME_STEP,
                            ElapsedTime = SystemAPI.Time.ElapsedTime
                        };
                    if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                        CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BallisticMovementJobEntity.ScheduleParallel queryEmpty={m_BallisticQuery.IsEmpty} hasPendingMovement={m_HasPendingMovementJob}");
                    // PERF-LOCK: same coarse TMS marker as Shahed movement so same-frame job alternation does not write a new marker file.
                    Core.Diagnostics.NativeCrashBreadcrumb.Mark(Core.Diagnostics.NativeCrashMarkers.ThreatMovementPipeline);
                    m_BallisticJobHandle = ballisticJob.ScheduleParallel(m_BallisticQuery, Dependency);
                    if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                        CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post BallisticMovementJobEntity.ScheduleParallel");
                    m_HasPendingBallisticJob = true;
                    // SAFETY-LOCK: folding the ballistic handle into Dependency is the SOLE gate
                    // protecting structural changes against this in-flight Ballistic/ThreatPosition
                    // writer. Once reported here, the ECS ComponentDependencyManager tracks it as the
                    // writer of those types, so any structural change (e.g. InterceptRequest creation
                    // played back on InterceptBarrier) completes it via BeforeStructuralChange →
                    // CompleteAllJobsAndInvalidateArrays (decompile: EntityDataAccess.cs:391-396).
                    // Results still apply next frame in STEP 1 (1-frame latency). Do NOT remove this
                    // combine and do NOT re-add a piggy-back AddJobHandleForProducer on any ECB barrier:
                    // the old InterceptBarrier piggy-back force-completed this job on every frame
                    // (including no-intercept frames where the barrier flush early-returns), which was
                    // a pure main-thread stall once Patriot put ballistic missiles in the air.
                    // Like the render handle (now folded IN Dependency, Branch B), ballistic is IN
                    // Dependency, so ECS self-completes it — no barrier needed.
                    Dependency = JobHandle.CombineDependencies(Dependency, m_BallisticJobHandle);
                }

                // ============================================================
                // STEP 5-RENDER: Burst job writes Transform/Moving/TransformFrame.
                // Scheduled AFTER ballistic so render reads updated ThreatPosition.
                //
                // Handle kept out of TMS.Dependency so downstream systems (TIS, TAS,
                // DebrisSystem) don't inherit the vanilla 12-22ms Transform/Moving/
                // TransformFrame writer chain through per-type dependency tracking.
                //
                // Render-job completion is NOT forced in this frame any more. The old
                // ThreatLifecycleBarrier.m_RenderJobHandle.Complete() ran in the same
                // GameSimulation as the schedule (workers got no time → main-thread block
                // 185ms/window → GPU starvation 73→88%). Replaced by:
                //   1. render→render self-sync (m_PrevRenderJobHandle folded into the next
                //      render job's input dep below) — render N+1 never races render N.
                //   2. render-completion gate in Modification4 of frame N+1
                //      (ThreatSpawnApplySystem / ThreatDeletionApplySystem call
                //      RenderWriteBarrier.Consume before any drone structural change) — by
                //      then render N has had a whole frame of worker time, so the drain is
                //      near-free, and no structural change lands in a live render chunk.
                // The render handle is therefore no longer registered on ThreatLifecycleBarrier. It is now
                // folded INTO system.Dependency so ECS — not a manual Modification4 drain — fences vanilla
                // job readers; RenderWriteBarrier.Publish (below) now fences only main-thread readers
                // (CameraTracking, spawn/delete structural Consume).
                // ============================================================
                // DIAG: chunk count of the live ActiveThreat set drives SP:TMS.RenderSchedule.
                // Logged before the SkipRender gate, time-throttled (~2s) so it fires reliably
                // regardless of frame alignment. skipRender=manual t:render toggle (normally false).
                if (Log.IsDebugEnabled)
                {
                    double nowRender = SystemAPI.Time.ElapsedTime;
                    if (nowRender >= m_NextRenderLogTime)
                    {
                        m_NextRenderLogTime = nowRender + 2.0d;
                        Log.Debug($"[TMS:RENDER] livePublished={(m_CollectedPub.IsCreated ? m_CollectedPub.Length : -1)} shahedChunks={m_ShahedQuery.CalculateChunkCount()} skipRender={SkipRender}");
                    }
                }
                if (!SkipRender)
                {
                    using (PerformanceProfiler.Measure("SP:TMS.RenderSchedule"))
                    {
                        int tfSlot = (int)((m_SimulationSystem.frameIndex >> 4) & 3);
                        var renderJob = new DroneRenderWriteJob { Slot = tfSlot };

                        // Chain render on ballistic for correct ThreatPosition reads
                        var renderInputDep = m_HasPendingBallisticJob
                            ? JobHandle.CombineDependencies(Dependency, m_BallisticJobHandle)
                            : Dependency;
                        // PERF-LOCK: render→render self-sync — fold the previous frame's render
                        // handle in so render N+1 cannot start writing the same Transform/Moving/
                        // TransformFrame chunks while render N is still in flight. Replaces the
                        // removed same-frame barrier Complete that used to serialize them. Without
                        // this, multi-tick GameSimulation (2x-8x) self-races the render writer.
                        renderInputDep = JobHandle.CombineDependencies(renderInputDep, m_PrevRenderJobHandle);
                        // BURSTMARK crash-1 candidate (worker render write — prior null-chunk-ptr history).
                        if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                            CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre DroneRenderWriteJob.ScheduleParallel slot={tfSlot} hasBallistic={m_HasPendingBallisticJob}");
                        m_RenderJobHandle = renderJob.ScheduleParallel(renderInputDep);
                        if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                            CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post DroneRenderWriteJob.ScheduleParallel");
                        // Still published: system.Dependency (below) fences other ECS jobs, but our
                        // MAIN-THREAD render-component readers are not ECS jobs and the barrier is their
                        // only fence — CameraTrackingSystem.Consume(ThreatTransformFrame) (the camera's
                        // main-thread TransformFrame read, Axiom 14) and the spawn/delete structural
                        // Consume in ThreatSpawnApplySystem / ThreatDeletionApplySystem. Keep Publish.
                        m_RenderWriteBarrier.Publish(m_RenderJobHandle, GetType(), RenderWriteMask);
                        m_PrevRenderJobHandle = m_RenderJobHandle;
                        // PERF-LOCK: render writer folded INTO system.Dependency (Branch B) so ECS fences
                        // ALL vanilla job readers of Transform/Moving/TransformFrame — same-frame AND
                        // next-frame — closing the torn-read data race (c0000005 in vanilla Burst at
                        // lib_burst+0x9525c0) that the Modification4 manual drain left open on the producing
                        // frame. NOT a same-frame Complete (that force-complete was the GPU-starvation
                        // source). ECS resolves it at the natural sync point. Removing the fold reopens the
                        // race. Perf (Transform/Moving worker-graph ordering) MUST be wave-measured before ship.
                        Dependency = JobHandle.CombineDependencies(Dependency, m_RenderJobHandle);
                    }
                }
                else
                {
                    // H4 FIX: Clear stale handle when render skipped (DevTools path)
                    m_RenderJobHandle = default;
                }

            }
        }

#pragma warning restore CIVIC187

        private void RefreshBallisticSnapshots()
        {
            if (!m_BallisticInfoList.IsCreated)
                return;

            m_BallisticInfoList.Clear();
            if (m_BallisticQuery.IsEmpty)
                return;

            using (PerformanceProfiler.Measure("TMS.BallisticSnapshot"))
            {
#pragma warning disable CIVIC343 // Ballistic snapshot wants active, not enabled-for-destruction, threats.
                foreach (var (ballistic, interceptState, threatPos, entity) in
                    SystemAPI.Query<RefRO<Ballistic>, RefRO<BallisticInterceptState>, RefRO<ThreatPosition>>()
                    .WithAll<ActiveThreat>()
                    .WithNone<Deleted, Destroyed, PendingDestruction>()
                    .WithEntityAccess())
#pragma warning restore CIVIC343
                {
                    var pos = threatPos.ValueRO.Position;
                    if (math.any(math.isnan(pos)) || math.any(math.isinf(pos)))
                        continue;

                    m_BallisticInfoList.Add(BallisticSnapshotInfo.From(
                        entity, pos, ballistic.ValueRO.IsArrived,
                        interceptState.ValueRO.IsIntercepted, ballistic.ValueRO.ThreatGeneration));
                }
            }
        }

        // NOTE: ApplyJobOutputs replaced by ApplyMovementJob (Burst + parallel)

        private void EnsureOutputCapacity(ref NativeArray<ShahedMovementOutput> array, int requiredLength)
        {
            if (!array.IsCreated || array.Length < requiredLength)
            {
                if (array.IsCreated) array.Dispose();
                array = default;
                array = new NativeArray<ShahedMovementOutput>(requiredLength + 32, Allocator.Persistent);
            }
        }

        /// <summary>
        /// Schedule the async drone-input collector into m_CollectedBack. Sizes the back buffer
        /// to the live Shahed count inside the dependency chain (ToEntityListAsync +
        /// ResizeShahedCollectJob — no main-thread sync), then runs CollectShahedInputsJob in
        /// parallel. The handle is owned by m_HasPendingCollectJob and completed unconditionally
        /// at STEP 1a next cycle (never gated on the movement/ballistic flags).
        /// </summary>
        private void ScheduleCollectInputsJob()
        {
            m_ShahedTypeHandle.Update(this);
            m_CombatStateTypeHandle.Update(this);
            m_ThreatPositionTypeHandle.Update(this);
            m_FlightProgressTypeHandle.Update(this);
            m_ActiveThreatTypeHandle.Update(this);
            m_EntityTypeHandle.Update(this);
            // m_IdentifiedTargetLookupRO is already Updated at the top of OnUpdateImpl.

            // 1) Async-list the matching entities. The async family carries the ActiveThreat
            //    enableable dependency into the gather job (GetDependency), NOT a main-thread
            //    CompleteDependency — so no sync point comes back here.
            NativeList<Entity> shahedList = m_ShahedQuery.ToEntityListAsync(
                World.UpdateAllocator.ToAllocator, out JobHandle listHandle);

            // 2) Size the back buffer to count*2 inside the chain (collect uses AddNoResize).
            var resizeJob = new ResizeShahedCollectJob
            {
                Entities = shahedList.AsDeferredJobArray(),
                Back = m_CollectedBack
            };
            JobHandle resizeHandle = resizeJob.Schedule(JobHandle.CombineDependencies(Dependency, listHandle));

            // 3) Parallel collect into the sized back buffer.
            var collectJob = new CollectShahedInputsJob
            {
                ShahedHandle = m_ShahedTypeHandle,
                CombatHandle = m_CombatStateTypeHandle,
                PositionHandle = m_ThreatPositionTypeHandle,
                FlightProgressHandle = m_FlightProgressTypeHandle,
                ActiveThreatHandle = m_ActiveThreatTypeHandle,
                EntityHandle = m_EntityTypeHandle,
                IdentifiedLookup = m_IdentifiedTargetLookupRO,
                Output = m_CollectedBack.AsParallelWriter(),
                InvalidIndices = m_InvalidShahedIndices.AsParallelWriter()
            };

            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CollectShahedInputsJob.ScheduleParallel back={m_CollectedBack.IsCreated} pub={m_CollectedPub.IsCreated}");
            m_CollectJobHandle = collectJob.ScheduleParallel(m_ShahedQuery, resizeHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post CollectShahedInputsJob.ScheduleParallel");

            // Collect reads Shahed/ThreatPosition/etc RO — fold into Dependency so a later RW
            // writer (ballistic this cycle, apply next cycle) waits on a worker, not the main
            // thread. Movement (native-output only) intentionally stays out of Dependency.
            Dependency = JobHandle.CombineDependencies(Dependency, m_CollectJobHandle);
            m_HasPendingCollectJob = true;
        }

        /// <summary>
        /// Drain NaN/Inf-skipped Shahed indices recorded by the Burst collect job and warn on
        /// the main thread. Severity stays Warn (a NaN position is an upstream bug); only the
        /// call site moved off the worker, since Burst can't log. Usually empty — cheap.
        /// </summary>
        private void DrainInvalidShahedLog()
        {
            if (!m_InvalidShahedIndices.IsCreated || m_InvalidShahedIndices.Count == 0)
                return;
            int count = m_InvalidShahedIndices.Count;
            int sample = -1;
            while (m_InvalidShahedIndices.TryDequeue(out int idx))
            {
                if (sample < 0) sample = idx;
            }
            Log.Warn($" SKIP NaN/Inf Shahed kept out of movement: count={count} sampleEntityIndex={sample}");
        }

        // Bound per-session obstacle-OOB telemetry: a persistent grid/cache desync would
        // otherwise emit every cycle. The first handful already carry the magnitude needed.
        private const int OBSTACLE_OOB_EMIT_CAP = 16;

        private void DrainObstacleOobLog()
        {
            if (!m_ObstacleOobLog.IsCreated || m_ObstacleOobLog.Count == 0)
                return;

            int count = m_ObstacleOobLog.Count;
            int worstIdx = -1;
            while (m_ObstacleOobLog.TryDequeue(out int idx))
            {
                if (idx > worstIdx) worstIdx = idx;
            }

            if (m_ObstacleOobEmitsThisSession >= OBSTACLE_OOB_EMIT_CAP)
                return;
            m_ObstacleOobEmitsThisSession++;

            int buildingsLength = m_CachedBuildings.IsCreated ? m_CachedBuildings.Length : 0;
            int gridCount = m_BuildingGrid.IsCreated ? m_BuildingGrid.Count() : 0;

            m_EventBus.SafePublish(new ObstacleIndexOobEvent(worstIdx, buildingsLength, gridCount, count));
            Log.Warn($" Obstacle grid index out of range: worstIdx={worstIdx} buildings={buildingsLength} grid={gridCount} count={count} (read skipped, no crash)");
        }

        // NOTE: ProcessObstacleAvoidance removed вЂ" merged into ShahedMovementJob (Burst, parallel)

        /// <summary>
        /// Refresh ECS handles/lookups immediately before scheduling a building-cache job.
        /// Count and fill may be separated by one or more system ticks, so each phase
        /// must own this refresh instead of relying on a previous phase's handles.
        /// </summary>
        private void RefreshBuildingCacheScheduleInputs()
        {
            m_EntityTypeHandle.Update(this);
            m_TransformTypeHandle.Update(this);
            m_PrefabRefTypeHandle.Update(this);
            m_PrefabGeometryLookup.Update(this);
            m_PrefabBuildingDataLookup.Update(this);
        }

        /// <summary>
        /// First async cache phase: count expanded grid entries without blocking the main thread.
        /// </summary>
        private void ScheduleBuildingGridCountJob()
        {
            m_PendingGridStats[CountExpandedBuildingGridEntriesJob.TOTAL_ENTRIES_INDEX] = 0;
            m_PendingGridStats[CountExpandedBuildingGridEntriesJob.MAX_ENTRIES_PER_BUILDING_INDEX] = 0;
            m_PendingGridStats[CountExpandedBuildingGridEntriesJob.BUILDING_COUNT_INDEX] = 0;
            m_PendingGridStats[CountExpandedBuildingGridEntriesJob.FILL_OVERFLOW_INDEX] = 0;

            RefreshBuildingCacheScheduleInputs();

            var job = new CountExpandedBuildingGridEntriesJob
            {
                EntityHandle = m_EntityTypeHandle,
                TransformHandle = m_TransformTypeHandle,
                PrefabRefHandle = m_PrefabRefTypeHandle,
                GeometryLookup = m_PrefabGeometryLookup,
                BuildingDataLookup = m_PrefabBuildingDataLookup,
                Stats = m_PendingGridStats,
                DefaultSize = new float3(CacheBuildingsChunkJob.DEFAULT_BUILDING_WIDTH, CacheBuildingsChunkJob.DEFAULT_BUILDING_HEIGHT, CacheBuildingsChunkJob.DEFAULT_BUILDING_DEPTH),
                LotUnitSize = CacheBuildingsChunkJob.LOT_UNIT_SIZE,
                MinBuildingRadius = CacheBuildingsChunkJob.MIN_BUILDING_RADIUS,
                GridCellSize = ObstacleAvoidanceHelper.GRID_CELL_SIZE,
                GridExpansionMargin = ObstacleAvoidanceHelper.GRID_EXPANSION_MARGIN
            };

            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CountExpandedBuildingGridEntriesJob.Schedule buildingQueryEmpty={m_BuildingQuery.IsEmpty} stats={m_PendingGridStats.IsCreated}/{m_PendingGridStats.Length}");
            m_BuildingGridCountJobHandle = JobChunkExtensions.Schedule(job, m_BuildingQuery, Dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post CountExpandedBuildingGridEntriesJob.Schedule");
            PublishBuildingCacheReadDependency(m_BuildingGridCountJobHandle);
            m_HasPendingBuildingGridCount = true;
        }

        /// <summary>
        /// Second async cache phase: fill the pre-sized building list and expanded grid.
        /// PERF: Reads Transform/PrefabRef directly from chunks; main thread only schedules.
        /// </summary>
        private void ScheduleBuildingCacheFillJob()
        {
            int countedGridEntries = m_PendingGridStats[CountExpandedBuildingGridEntriesJob.TOTAL_ENTRIES_INDEX];
            int countedBuildings = m_PendingGridStats[CountExpandedBuildingGridEntriesJob.BUILDING_COUNT_INDEX];
            int maxEntriesPerBuilding = math.max(
                m_PendingGridStats[CountExpandedBuildingGridEntriesJob.MAX_ENTRIES_PER_BUILDING_INDEX],
                16);
            m_PendingGridStats[CountExpandedBuildingGridEntriesJob.FILL_OVERFLOW_INDEX] = 0;

            long requestedBuildingCapacity = math.max(
                INITIAL_BUILDING_CAPACITY,
                (long)countedBuildings * 2L + BUILDING_CAPACITY_REBUILD_HEADROOM);
            int buildingCapacity = requestedBuildingCapacity >= int.MaxValue
                ? int.MaxValue
                : checked((int)requestedBuildingCapacity);
            long requestedGridCapacity = math.max(
                (long)countedGridEntries * 2L + GRID_CAPACITY_REBUILD_HEADROOM,
                (long)countedGridEntries + (long)BUILDING_CAPACITY_REBUILD_HEADROOM * maxEntriesPerBuilding + GRID_CAPACITY_REBUILD_HEADROOM);
            int gridCapacity = requestedGridCapacity >= int.MaxValue
                ? int.MaxValue
                : checked((int)requestedGridCapacity);

            m_PendingBuildings.Clear();
            m_PendingGrid.Clear();
            if (m_PendingBuildings.Capacity < buildingCapacity)
                m_PendingBuildings.Capacity = buildingCapacity;
            if (m_PendingGrid.Capacity < gridCapacity)
                m_PendingGrid.Capacity = gridCapacity;
            // Reclaim memory if the city shrank significantly (e.g. mass demolition).
            // Threshold 4x avoids thrashing on normal fluctuations.
            else if (gridCapacity > INITIAL_GRID_CAPACITY && m_PendingGrid.Capacity > gridCapacity * 4)
                m_PendingGrid.Capacity = gridCapacity * 2;

            RefreshBuildingCacheScheduleInputs();

            var job = new CacheBuildingsChunkJob
            {
                EntityHandle = m_EntityTypeHandle,
                TransformHandle = m_TransformTypeHandle,
                PrefabRefHandle = m_PrefabRefTypeHandle,
                GeometryLookup = m_PrefabGeometryLookup,
                BuildingDataLookup = m_PrefabBuildingDataLookup,
                Buildings = m_PendingBuildings,
                Grid = m_PendingGrid,
                Stats = m_PendingGridStats,
                DefaultSize = new float3(CacheBuildingsChunkJob.DEFAULT_BUILDING_WIDTH, CacheBuildingsChunkJob.DEFAULT_BUILDING_HEIGHT, CacheBuildingsChunkJob.DEFAULT_BUILDING_DEPTH),
                LotUnitSize = CacheBuildingsChunkJob.LOT_UNIT_SIZE,
                MinBuildingRadius = CacheBuildingsChunkJob.MIN_BUILDING_RADIUS,
                GridCellSize = ObstacleAvoidanceHelper.GRID_CELL_SIZE,
                GridExpansionMargin = ObstacleAvoidanceHelper.GRID_EXPANSION_MARGIN
            };

            // Schedule (sequential, single worker thread) вЂ" ECS chains dependency on Transform writers
            // automatically. Main thread does NOT block; worker thread waits for writers to finish.
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CacheBuildingsChunkJob.Schedule buildingQueryEmpty={m_BuildingQuery.IsEmpty} buildings={m_PendingBuildings.IsCreated}/{m_PendingBuildings.Length}/{m_PendingBuildings.Capacity} grid={m_PendingGrid.IsCreated}/{m_PendingGrid.Count()} stats={m_PendingGridStats.IsCreated}/{m_PendingGridStats.Length}");
            m_BuildingCacheJobHandle = JobChunkExtensions.Schedule(job, m_BuildingQuery, Dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post CacheBuildingsChunkJob.Schedule");
            PublishBuildingCacheReadDependency(m_BuildingCacheJobHandle);
            m_HasPendingBuildingCache = true;
        }

        private void PublishBuildingCacheReadDependency(JobHandle jobHandle)
        {
            m_BuildingQuery.AddDependency(jobHandle);
            m_ObjectGeometryQuery.AddDependency(jobHandle);
            m_BuildingDataQuery.AddDependency(jobHandle);
        }

        // NOTE: ProcessArrivedThreats, ProcessExhaustedThreats, ProcessBallisticImpacts
        // moved to ThreatArrivalSystem

        // NOTE: UpdateRadarBufferAndClosest removed вЂ" merged into OnUpdate STEP 2 (single query)

        // WriteRenderingComponents removed вЂ" replaced by DroneRenderWriteJob (Burst IJobEntity, zero sync points)

        private float3 GetCameraPosition(float elapsed)
        {
            // PERF: Cache Camera object (eliminates FindObjectWithTag per call)
            // Update position only every ~0.5s вЂ" camera doesn't move that fast
            m_CameraCacheTimer += elapsed;
            if (m_CameraCacheTimer >= 0.5f)
            {
                m_CameraCacheTimer = 0f;
                if (m_CachedCamera == null)
                    m_CachedCamera = UnityEngine.Camera.main;
                if (m_CachedCamera != null)
                {
                    var camPos = m_CachedCamera.transform.position;
                    m_CachedCameraPos = new float3(camPos.x, camPos.y, camPos.z);
                }
                else
                {
                    m_CachedCameraPos = new float3(0, FALLBACK_CAMERA_HEIGHT, 0);
                }
            }
            return m_CachedCameraPos;
        }

        // NOTE: ProcessInterceptRequests and ExecuteIntercept removed
        // InterceptRequest processing moved to InterceptProcessingSystem (Core)
        // Single consumer pattern - avoids race conditions from dual consumers

        /// <summary>
        /// Narrow [HotPathSystem] carveout: materialise arrived-ballistic entities for
        /// Step1c iteration. The ballistic movement job has already joined into
        /// <c>Dependency</c> by this point (STEP 1 completes before STEP 1c runs), so
        /// <c>ToEntityArray</c>'s dependency-sync is a no-op; the remaining cost is the
        /// per-frame <c>Allocator.Temp</c> materialisation, accepted as bounded by
        /// arrived-ballistic count (typically &lt;10).
        /// </summary>
        [CompletesDependency("Step1c: ballistic movement job already joined into Dependency in STEP 1; ToEntityArray is a Temp materialisation of arrived-ballistic entities only (typically <10), bounded cost")]
        private NativeArray<Entity> SnapshotArrivedBallistics()
            => m_BallisticQuery.ToEntityArray(Allocator.Temp);

    }
}
