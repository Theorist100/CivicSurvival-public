using Game;
using Game.Buildings;
using Game.Common;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.PowerBackup.Jobs;
using Game.Areas;

namespace CivicSurvival.Domains.PowerBackup.Systems
{
    /// <summary>
    /// Runtime system for backup power - handles charge/discharge logic.
    ///
    /// ASYNC PATTERN: Processing job runs async, results applied next frame.
    /// Stats have 1 frame latency (fine for UI).
    ///
    /// When building has grid power: charges battery
    /// When building has no grid power: discharges battery to keep building running
    ///
    /// S13a-9 ACCEPTED: Backup power operates independently from grid collapse — by design (batteries/generators).
    /// S13a-10 ACCEPTED: FuelHours clamped to 0 in BackupPowerJob (floor check exists).
    /// </summary>
    [SingletonOwner(typeof(BackupPowerStateSingleton))]
    [SingletonOwner(typeof(ChargeRateRegistry))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class BackupPowerRuntimeSystem : ThrottledSystemBase, IBackupPowerPolicyWriter, ICivicSingletonOwner<BackupPowerStateSingleton>, ICivicSingletonOwner<ChargeRateRegistry>, IActGatedSystem
    {
        private const int LOW_CHARGE_THRESHOLD = 20;
        private const int RECHARGE_THRESHOLD = 80;
        private static readonly LogContext Log = new("BackupPowerRuntimeSystem");

        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        private EntityQuery m_BackupQuery;
        private EntityQuery m_EfficiencyQuery;
        private EntityQuery m_BackupPowerSingletonQuery;
        private EntityQuery m_CounterfeitQuery;
        private EntityQuery m_CoverageAggQuery;

        // ComponentLookups for atomic TryGetComponent (avoids TOCTOU race)
        private ComponentLookup<ElectricityConsumer> m_ConsumerLookup;

        // Single source of truth for battery priority (BlackoutState.HasBatteryPriority)
        private ComponentLookup<BlackoutState> m_BlackoutStateLookup;

        // Three-layer: lookups for district coverage aggregation and economy modulation
        private ComponentLookup<BatteryLayerTag> m_LayerTagLookup;
        private ComponentLookup<CurrentDistrict> m_CurrentDistrictLookup;
        private EntityQuery m_EconomyStateQuery;

        // Frame-local map for CounterfeitBattery mod entities by building index
        [NonEntityIndex] private NativeHashMap<long, CounterfeitBattery> m_CounterfeitByBuilding;

        // Stats for UI — recalculated each update, not persisted
        [System.NonSerialized] private BackupPowerStats m_Stats;
        [System.NonSerialized] private BackupPowerStateSingleton m_UiState;
        [System.NonSerialized] private BackupPolicy m_CurrentPolicy = BackupPolicy.Reserve;

        // Async job state
        private Unity.Jobs.JobHandle m_PendingJobHandle;
        private bool m_HasPendingJob;
        private bool m_HasPendingSingletonWrite;
        private bool m_HasStats;
        private bool m_IsFirstTick = true;
        [System.NonSerialized] private ActGateController m_Gate = null!;

        // CDI-12: Track depletion to avoid spam
        private bool m_DepletionNotified;

        // T11-4 FIX: Track battery tier to avoid spamming BatteryLow every 500ms
        // Also fixes false "Battery Low" after load (was m_PreviousBatteryPercent=100 in resolver)
        private int m_LastBatteryTier = int.MaxValue;
        private bool m_RechargeNotified;
        private bool m_GeneratorDepletionNotified;

        // Three-layer: per-layer stats (city-wide) — recalculated each update, not persisted
        [System.NonSerialized] private int m_HospitalsPowered;
        [System.NonSerialized] private int m_HospitalsTotal;
        [System.NonSerialized] private int m_SchoolsPowered;
        [System.NonSerialized] private int m_SchoolsTotal;

        // Three-layer: per-district coverage data (rebuilt every update)
        [NonEntityIndex] private NativeHashMap<int, DistrictBatteryCoverage> m_DistrictCoverageMap;

        // Singleton write job lookups (read-write — used in WriteSingletonJob)
#pragma warning disable CIVIC269 // Write via IJob, not direct indexer
        private ComponentLookup<BackupPowerStateSingleton> m_SingletonWriteLookup;
#pragma warning restore CIVIC269
        private BufferLookup<DistrictBatteryCoverage> m_CoverageBufferLookup;

        // Stats aggregation (persistent NativeReferences — re-populated every frame from job)
        private ComponentTypeHandle<BackupPower> m_BackupPowerTypeHandle;
        [System.NonSerialized] private NativeReference<int> m_StatsProtectedBuildings;
        [System.NonSerialized] private NativeReference<int> m_StatsDischargingCount;
        [System.NonSerialized] private NativeReference<int> m_StatsGeneratorsRunning;
        [System.NonSerialized] private NativeReference<long> m_StatsTotalCapacityWh;
        [System.NonSerialized] private NativeReference<long> m_StatsTotalChargeWh;
#pragma warning disable CIVIC023, CIVIC236 // Disposed in OnDestroy — false positive
        [System.NonSerialized] private NativeReference<int> m_StatsGeneratorsTotal;
        [System.NonSerialized] private NativeReference<int> m_StatsGeneratorsFueled;
#pragma warning restore CIVIC023, CIVIC236

        // District coverage aggregation (persistent NativeReferences for Burst job — re-populated every frame)
        [System.NonSerialized] private NativeReference<int> m_CovHospitalsPowered;
        [System.NonSerialized] private NativeReference<int> m_CovHospitalsTotal;
        [System.NonSerialized] private NativeReference<int> m_CovSchoolsPowered;
        [System.NonSerialized] private NativeReference<int> m_CovSchoolsTotal;

        // R3-C-9: System reads Policy via TryGetSingleton (always fresh) and fuel indirectly
        // via GeneratorEfficiency singleton (also fresh). The 500ms staleness only affects
        // charge/discharge stats visible to UI — acceptable for display purposes.
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;
        public BackupPowerStats Stats => m_Stats;
        public BackupPowerStateSingleton UiState => m_UiState;

        // M2 FIX: Cache ModSettings (avoid lookup in hot path)
        private ModSettings? m_Settings;

        // Cache counterfeit map — only rebuild on structural changes (entity add/remove)
        // Not serialized: always starts -1 on load → forces rebuild on first update (correct behavior)
        [EntityQueryOrderCursor("Invalidates the counterfeit-battery lookup map when the underlying query's archetype set changes.")]
        [System.NonSerialized] private int m_LastCounterfeitVersion = -1;

        private EntityQuery m_CurrentActQuery;

        // Game time tracking for realistic charge/discharge
        private double m_LastGameHour = 0.0;
        [System.NonSerialized] private float m_DeltaHours = 0f; // recalculated every tick

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            BackupPowerStateSingleton.EnsureExists(EntityManager);
            ChargeRateRegistry.EnsureExists(EntityManager);
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IBackupPowerPolicyWriter>(this);

            Log.Info($"{nameof(BackupPowerRuntimeSystem)} created (async pattern, Crisis-only)");

            // Initialize ComponentLookups for atomic component access
            m_ConsumerLookup = GetComponentLookup<ElectricityConsumer>(true);

            // Single source of truth for battery priority
            m_BlackoutStateLookup = GetComponentLookup<BlackoutState>(true);

            // Three-layer: district coverage aggregation and economy modulation
            m_LayerTagLookup = GetComponentLookup<BatteryLayerTag>(true);
            m_CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_EconomyStateQuery = GetEntityQuery(ComponentType.ReadOnly<EconomySingleton>());

            // Three-layer: per-district coverage
            m_DistrictCoverageMap = new NativeHashMap<int, DistrictBatteryCoverage>(32, Allocator.Persistent);
            // Frame-local map for CounterfeitBattery lookups
            m_CounterfeitByBuilding = new NativeHashMap<long, CounterfeitBattery>(64, Allocator.Persistent);

            // Allocate persistent NativeReferences for stats (zero GC)
            m_StatsProtectedBuildings = new NativeReference<int>(Allocator.Persistent);
            m_StatsDischargingCount = new NativeReference<int>(Allocator.Persistent);
            m_StatsGeneratorsRunning = new NativeReference<int>(Allocator.Persistent);
            m_StatsTotalCapacityWh = new NativeReference<long>(Allocator.Persistent);
            m_StatsTotalChargeWh = new NativeReference<long>(Allocator.Persistent);
            m_StatsGeneratorsTotal = new NativeReference<int>(Allocator.Persistent);
            m_StatsGeneratorsFueled = new NativeReference<int>(Allocator.Persistent);

            m_CovHospitalsPowered = new NativeReference<int>(Allocator.Persistent);
            m_CovHospitalsTotal = new NativeReference<int>(Allocator.Persistent);
            m_CovSchoolsPowered = new NativeReference<int>(Allocator.Persistent);
            m_CovSchoolsTotal = new NativeReference<int>(Allocator.Persistent);

            // Query for BackupPower mod entities (not vanilla buildings)
            m_BackupQuery = GetEntityQuery(
                ComponentType.ReadWrite<BackupPower>(),
                ComponentType.Exclude<Deleted>()
            );

            m_EfficiencyQuery = GetEntityQuery(ComponentType.ReadOnly<GeneratorEfficiency>());
            m_BackupPowerSingletonQuery = GetEntityQuery(ComponentType.ReadWrite<BackupPowerStateSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            InitializeGate();

            m_CounterfeitQuery = GetEntityQuery(
                ComponentType.ReadOnly<CounterfeitBattery>(),
                ComponentType.Exclude<Deleted>()
            );
            m_CoverageAggQuery = GetEntityQuery(
                ComponentType.ReadOnly<BackupPower>(),
                ComponentType.Exclude<Deleted>()
            );

            // WriteSingletonJob lookups (read-write for background write)
            m_SingletonWriteLookup = GetComponentLookup<BackupPowerStateSingleton>(false);
            m_CoverageBufferLookup = GetBufferLookup<DistrictBatteryCoverage>(false);
            m_BackupPowerTypeHandle = GetComponentTypeHandle<BackupPower>(true);

            // Ensure buffer exists on singleton entity (structural change — must be main thread, once)
            if (m_BackupPowerSingletonQuery.TryGetSingletonEntity<BackupPowerStateSingleton>(out var singletonEntity))
            {
                if (!EntityManager.HasBuffer<DistrictBatteryCoverage>(singletonEntity))
                    EntityManager.AddBuffer<DistrictBatteryCoverage>(singletonEntity);
            }

        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            BackupPowerStateSingleton.EnsureExists(EntityManager);
            ChargeRateRegistry.EnsureExists(EntityManager);
        }

        public bool TrySetBackupPolicy(BackupPolicy policy)
        {
            BackupPowerStateSingleton.EnsureExists(EntityManager);
            if (!m_BackupPowerSingletonQuery.TryGetSingletonEntity<BackupPowerStateSingleton>(out var singleton))
                return false;

            var state = EntityManager.GetComponentData<BackupPowerStateSingleton>(singleton);
            state.Policy = policy;
            EntityManager.SetComponentData(singleton, state);
            m_CurrentPolicy = policy;
            m_UiState.Policy = policy;
            return true;
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);
            return m_Settings == null
                || !m_Settings.BackupPowerEnabled
                || m_Gate.State != ActGateState.Active;
        }

        /// <summary>
        /// FIX S3-06: Clear stale coverage when feature disabled.
        /// Prevents MentalHealthResolver from reading phantom battery mitigation.
        /// </summary>
        protected override void OnBecameDisabled()
        {
            if (m_HasPendingJob || m_HasPendingSingletonWrite)
            {
                m_PendingJobHandle.Complete();
                m_HasPendingJob = false;
                m_HasPendingSingletonWrite = false;
            }
            ClearRuntimeStatsState(clearHasStats: true);
            ClearSingletonStats();
            Log.Info("BackupPower disabled — cleared stats and coverage data");
        }

        protected override void OnThrottledUpdate()
        {
            using (PerformanceProfiler.Measure("BPRS.Lookups"))
            {
                m_ConsumerLookup.Update(this);
                m_BlackoutStateLookup.Update(this);
                m_LayerTagLookup.Update(this);
                m_CurrentDistrictLookup.Update(this);
            }

            // ASYNC PATTERN: Complete previous job chain FIRST (before any NativeContainer access)
            // Fix: Complete BEFORE touching m_CounterfeitByBuilding — job may still be reading it
            if (m_HasPendingJob)
            {
                using (PerformanceProfiler.Measure("BPRS.Complete"))
                {
                    m_PendingJobHandle.Complete();
                    m_HasPendingJob = false;
                }

                // If all backup entities removed since job was scheduled — skip stale results
                if (m_BackupQuery.IsEmpty)
                {
                    ClearRuntimeStatsState(clearHasStats: false);
                    ClearSingletonStats();
                    // Reset notification flags — prevent stale state leaking to next battery population
                    m_LastBatteryTier = int.MaxValue;
                    m_DepletionNotified = false;
                    m_RechargeNotified = false;
                    m_GeneratorDepletionNotified = false;
                    Log.Info("[BackupPower] All entities removed — notification state reset");
                }
                else
                {
                    // Read results from async stats + coverage jobs (just NativeReference reads — zero cost)
                    ReadAsyncResults();
                    m_HasStats = true;
                }
            }

            if (m_BackupQuery.IsEmpty)
            {
                bool hadStats = m_HasStats;
                ClearRuntimeStatsState(clearHasStats: true);
                ClearSingletonStats();
                if (hadStats)
                    Log.Info("[BackupPower] No backup entities — cleared stale singleton stats");
                return;
            }

            // Build counterfeit map only on structural changes (entity add/remove)
            // GetCombinedComponentOrderVersion reads archetype metadata — no sync point
            int counterfeitVersion = m_CounterfeitQuery.GetCombinedComponentOrderVersion(true);
            if (counterfeitVersion != m_LastCounterfeitVersion)
            {
                m_LastCounterfeitVersion = counterfeitVersion;
                EnsureCounterfeitCapacity();
                m_CounterfeitByBuilding.Clear();
                foreach (var counterfeit in
                    SystemAPI.Query<RefRO<CounterfeitBattery>>()
                    .WithNone<Deleted>())
                {
                    long key = counterfeit.ValueRO.Building.Packed;
                    if (!m_CounterfeitByBuilding.TryAdd(key, counterfeit.ValueRO))
                    {
                        if (m_CounterfeitByBuilding.TryGetValue(key, out var existing))
                        {
                            if (counterfeit.ValueRO.FireRiskMultiplier > existing.FireRiskMultiplier)
                                m_CounterfeitByBuilding[key] = counterfeit.ValueRO;
                        }
                    }
                }
            }

            UpdateGameTime();

            // Write singleton BEFORE scheduling new chain (reads m_DistrictCoverageMap which new chain clears)
            // Skip when no stats yet (avoids writing zero-stats to singleton on first post-load tick)
            if (m_HasStats)
            {
                using (PerformanceProfiler.Measure("BPRS.SingletonWrite"))
                {
                    ScheduleSingletonWrite();
                }
            }

            // Schedule new async job chain (if entities exist):
            // BackupPowerJob (parallel) → BackupPowerStatsJob (single) → DistrictCoverageJob (single)
            if (!m_BackupQuery.IsEmpty)
            {
                using (PerformanceProfiler.Measure("BPRS.Schedule"))
                {
                    ScheduleAsyncProcessing();
                }
            }
        }

        /// <summary>
        /// Schedule singleton write on a worker thread via WriteSingletonJob.
        /// Eliminates main-thread sync point: EntityManager.SetComponentData would trigger
        /// CompleteDependencyBeforeRW, stalling for any job reading the same component type.
        /// IJob.Schedule() returns immediately — dependency resolution happens on worker threads.
        /// </summary>
        private void ScheduleSingletonWrite()
        {
            if (!m_BackupPowerSingletonQuery.TryGetSingletonEntity<BackupPowerStateSingleton>(out var entity))
                return;

            m_SingletonWriteLookup.Update(this);
            m_CoverageBufferLookup.Update(this);

            var coverageData = m_DistrictCoverageMap.GetValueArray(Allocator.TempJob);

            var writeJob = new WriteSingletonJob
            {
                SingletonLookup = m_SingletonWriteLookup,
                CoverageLookup = m_CoverageBufferLookup,
                SingletonEntity = entity,
                ChargePercent = m_Stats.ChargePercent,
                ProtectedBuildings = m_Stats.ProtectedBuildings,
                TotalCapacityKWh = (int)(m_Stats.TotalCapacityWh / 1000),
                DischargingCount = m_Stats.DischargingCount,
                HospitalsPowered = m_HospitalsPowered,
                HospitalsTotal = m_HospitalsTotal,
                SchoolsPowered = m_SchoolsPowered,
                SchoolsTotal = m_SchoolsTotal,
                CoverageData = coverageData
            };

            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre WriteSingletonJob.Schedule entity={entity.Index} coverage={coverageData.IsCreated}/{coverageData.Length} map={m_DistrictCoverageMap.IsCreated}/count={m_DistrictCoverageMap.Count}");
            var writeHandle = writeJob.Schedule(Dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post WriteSingletonJob.Schedule entity={entity.Index} coverage={coverageData.IsCreated}/{coverageData.Length}");
            if (coverageData.IsCreated)
                Dependency = coverageData.Dispose(writeHandle);
            else
                Dependency = writeHandle;
            m_PendingJobHandle = writeHandle;
            m_HasPendingSingletonWrite = true;
        }

        private void UpdateGameTime()
        {
            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null) return;
            float currentHour = timeProvider.Current.TotalGameHours;

            if (m_IsFirstTick)
            {
                m_IsFirstTick = false;
                m_LastGameHour = currentHour;
                m_DeltaHours = 0f;
                return;
            }

            float rawDelta = (float)(currentHour - m_LastGameHour);
            m_LastGameHour = currentHour;

            // H18: Warn only for genuine save/load time gaps (normal gameplay never exceeds ~2h per tick)
            if (rawDelta > 2f)
                Log.Warn($"DeltaHours jump: {rawDelta:F2}h — clamped to 1h (lost {math.max(0f, rawDelta - 1f):F2}h of simulation)");
            else if (rawDelta < -0.01f)
                Log.Warn($"DeltaHours backward: {rawDelta:F2}h — clamped to 0");

            // H14: Safety cap for save/load time gaps (rawDelta can spike to hundreds of hours).
            // Normal gameplay: rawDelta << 1h, cap never triggers.
            // Battery rate IS proportional to game speed (4x drain at 4x) — by design.
            // Max 1 game-hour per tick (covers realistic speeds, blocks save/load spikes)
            m_DeltaHours = math.clamp(rawDelta, 0f, 1f);
        }

        /// <summary>
        /// Schedule async Burst job chain for charge/discharge + stats + coverage.
        /// All three jobs run on worker threads — results applied next frame.
        /// Chain: BackupPowerJob (parallel) → BackupPowerStatsJob (single) → DistrictCoverageJob (single)
        /// </summary>
        private void ScheduleAsyncProcessing()
        {
            if (m_BackupQuery.IsEmpty) return;

            float generatorEfficiency = GetGeneratorEfficiencyMultiplier();
            var cfg = BalanceConfig.Current;

            // BUG-4 FIX: Get current discharge policy from singleton
            var policy = m_BackupPowerSingletonQuery
                .TryGetSingleton<BackupPowerStateSingleton>(out var backupState)
                    ? backupState.Policy
                    : BackupPowerStateSingleton.Default.Policy;
            m_CurrentPolicy = policy;

            // Three-layer: compute private charge rate multiplier from economy state
            var econ = m_EconomyStateQuery.TryGetSingleton<EconomySingleton>(out var ec)
                ? ec : EconomySingleton.Default;
            float economyMult = econ.State switch
            {
                PopulationState.Loyal => cfg.BackupPower.ChargeRateMultLoyal,
                PopulationState.Anxious => cfg.BackupPower.ChargeRateMultAnxious,
                PopulationState.Rebellious => cfg.BackupPower.ChargeRateMultRebellious,
                PopulationState.Brainwashed => cfg.BackupPower.ChargeRateMultBrainwashed,
                PopulationState.Zombie => cfg.BackupPower.ChargeRateMultZombie,
                _ => 1.0f
            };

            // Registry guaranteed by EnsureExists in OnCreate + OnStartRunning; floor=0.10/ceiling=2.0 live in Default.
#pragma warning disable CIVIC055 // Registry existence is a lifecycle invariant, not a runtime query.
            ref var chargeReg = ref SystemAPI.GetSingletonRW<ChargeRateRegistry>().ValueRW;
#pragma warning restore CIVIC055
            chargeReg.Rate.Set(ChargeRateRegistry.Source.EconomyState, economyMult);
            float privateChargeRate = chargeReg.Rate.Resolve(1.0f);

            var job = new BackupPowerJob
            {
                DeltaHours = m_DeltaHours,
                GeneratorEfficiency = generatorEfficiency,
                GridPowerThreshold = cfg.PowerGrid.GridPowerThreshold,
                DegradationPerHour = cfg.BackupPower.DegradationPerHour,
                IdleDegradationFraction = cfg.BackupPower.IdleDegradationFraction,
                CounterfeitIdlePenalty = cfg.BackupPower.CounterfeitIdlePenalty,
                CounterfeitByBuilding = m_CounterfeitByBuilding,
                ConsumerLookup = m_ConsumerLookup,
                Policy = policy,
                BlackoutStateLookup = m_BlackoutStateLookup,
                // Three-layer: economy charge modulation
                LayerTagLookup = m_LayerTagLookup,
                PrivateChargeRateMultiplier = privateChargeRate
            };

            // 1. BackupPowerJob — parallel on workers (charge/discharge)
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BackupPowerJob.ScheduleParallel counterfeit={m_CounterfeitByBuilding.IsCreated}/count={m_CounterfeitByBuilding.Count}/capacity={m_CounterfeitByBuilding.Capacity} deltaHours={m_DeltaHours:F3} policy={policy}");
            var backupHandle = job.ScheduleParallel(m_BackupQuery, Dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post BackupPowerJob.ScheduleParallel");

            // 2. BackupPowerStatsJob — single-threaded on worker (depends on BackupPowerJob)
            m_StatsProtectedBuildings.Value = 0;
            m_StatsDischargingCount.Value = 0;
            m_StatsGeneratorsRunning.Value = 0;
            m_StatsTotalCapacityWh.Value = 0;
            m_StatsTotalChargeWh.Value = 0;
            m_StatsGeneratorsTotal.Value = 0;
            m_StatsGeneratorsFueled.Value = 0;
            m_BackupPowerTypeHandle.Update(this);

            var statsJob = new BackupPowerStatsJob
            {
                BackupPowerHandle = m_BackupPowerTypeHandle,
                ProtectedBuildings = m_StatsProtectedBuildings,
                DischargingCount = m_StatsDischargingCount,
                GeneratorsRunning = m_StatsGeneratorsRunning,
                TotalCapacityWh = m_StatsTotalCapacityWh,
                TotalChargeWh = m_StatsTotalChargeWh,
                GeneratorsTotal = m_StatsGeneratorsTotal,
                GeneratorsFueled = m_StatsGeneratorsFueled
            };
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BackupPowerStatsJob.Schedule refs={m_StatsProtectedBuildings.IsCreated}/{m_StatsDischargingCount.IsCreated}/{m_StatsGeneratorsRunning.IsCreated}/{m_StatsTotalCapacityWh.IsCreated}/{m_StatsTotalChargeWh.IsCreated}/{m_StatsGeneratorsTotal.IsCreated}/{m_StatsGeneratorsFueled.IsCreated}");
            var statsHandle = statsJob.Schedule(m_BackupQuery, backupHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post BackupPowerStatsJob.Schedule");

            // 3. DistrictCoverageJob — single-threaded on worker (depends on StatsJob)
            EnsureDistrictCoverageCapacity();
            m_DistrictCoverageMap.Clear();
            m_CovHospitalsPowered.Value = 0;
            m_CovHospitalsTotal.Value = 0;
            m_CovSchoolsPowered.Value = 0;
            m_CovSchoolsTotal.Value = 0;

            var coverageJob = new DistrictCoverageJob
            {
                DistrictLookup = m_CurrentDistrictLookup,
                LayerTagLookup = m_LayerTagLookup,
                CoverageMap = m_DistrictCoverageMap,
                HospitalsPowered = m_CovHospitalsPowered,
                HospitalsTotal = m_CovHospitalsTotal,
                SchoolsPowered = m_CovSchoolsPowered,
                SchoolsTotal = m_CovSchoolsTotal
            };
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre DistrictCoverageJob.Schedule map={m_DistrictCoverageMap.IsCreated}/count={m_DistrictCoverageMap.Count}/capacity={m_DistrictCoverageMap.Capacity} refs={m_CovHospitalsPowered.IsCreated}/{m_CovHospitalsTotal.IsCreated}/{m_CovSchoolsPowered.IsCreated}/{m_CovSchoolsTotal.IsCreated}");
            m_PendingJobHandle = coverageJob.Schedule(m_CoverageAggQuery, statsHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post DistrictCoverageJob.Schedule map={m_DistrictCoverageMap.IsCreated}/count={m_DistrictCoverageMap.Count}");
            m_HasPendingJob = true;
            m_HasPendingSingletonWrite = false;
            Dependency = m_PendingJobHandle;
        }

        /// <summary>
        /// Read results from async stats + coverage jobs after completion.
        /// All heavy lifting (iteration, aggregation) happened on worker threads —
        /// this just reads NativeReferences + publishes events on main thread.
        /// </summary>
        private void ReadAsyncResults()
        {
            // Read stats from NativeReferences (populated by async BackupPowerStatsJob)
            m_Stats = new BackupPowerStats
            {
                ProtectedBuildings = m_StatsProtectedBuildings.Value,
                DischargingCount = m_StatsDischargingCount.Value,
                GeneratorsRunning = m_StatsGeneratorsRunning.Value,
                TotalCapacityWh = m_StatsTotalCapacityWh.Value,
                TotalChargeWh = m_StatsTotalChargeWh.Value,
                GeneratorsTotal = m_StatsGeneratorsTotal.Value,
                GeneratorsFueled = m_StatsGeneratorsFueled.Value
            };

            // Read coverage from NativeReferences (populated by async DistrictCoverageJob)
            m_HospitalsPowered = m_CovHospitalsPowered.Value;
            m_HospitalsTotal = m_CovHospitalsTotal.Value;
            m_SchoolsPowered = m_CovSchoolsPowered.Value;
            m_SchoolsTotal = m_CovSchoolsTotal.Value;
            PublishUiState();

            // Diagnostic logging (errors only)
            if (m_Stats.ChargePercent < 0 || float.IsNaN(m_Stats.ChargePercent) || float.IsInfinity(m_Stats.ChargePercent))
            {
                Log.Error($"[DIAGNOSTIC] Invalid ChargePercent={m_Stats.ChargePercent}% | TotalChargeWh={m_Stats.TotalChargeWh} TotalCapacityWh={m_Stats.TotalCapacityWh} ProtectedBuildings={m_Stats.ProtectedBuildings}");
            }

            // Notify events (tier-based dedup: publish only on state transitions, not every 500ms tick)
            // FIX S3-07: Removed DischargingCount > 0 gate — Reserve policy prevents discharge
            // but player still needs depletion warnings (batteries can deplete from aging/counterfeit)
            if (m_Stats.TotalCapacityWh > 0)
            {
                // Clear generator-only flag when batteries appear (generator→mixed transition)
                m_GeneratorDepletionNotified = false;

                int chargePercent = (int)m_Stats.ChargePercent;
                int tier = chargePercent / 10;

                // Re-arm depletion only after a meaningful recharge tier to avoid 0%/1% oscillation spam.
                if (chargePercent > LOW_CHARGE_THRESHOLD && m_DepletionNotified)
                {
                    m_DepletionNotified = false;
                    m_LastBatteryTier = int.MaxValue; // re-arm BatteryLow detection for next cycle
                }

                if (chargePercent > LOW_CHARGE_THRESHOLD && chargePercent < RECHARGE_THRESHOLD && tier > m_LastBatteryTier)
                    m_LastBatteryTier = int.MaxValue;

                // CDI-12: Battery fully depleted (0%) - critical notification
                if (chargePercent == 0 && !m_DepletionNotified)
                {
                    EventBus?.SafePublish(new InfraEvent(InfraEventType.BatteryDepleted), "BackupPowerRuntimeSystem");
                    m_DepletionNotified = true;
                    m_LastBatteryTier = 0;
                    m_RechargeNotified = false;
                }
                else if (chargePercent <= LOW_CHARGE_THRESHOLD && tier < m_LastBatteryTier)
                {
                    EventBus?.SafePublish(new InfraEvent(InfraEventType.BatteryLow, BatteryPercent: chargePercent), "BackupPowerRuntimeSystem");
                    m_LastBatteryTier = tier;
                    m_RechargeNotified = false;
                }
                // FIX S4-01: Moved recharge check inside TotalCapacityWh > 0 block.
                // Was in dead else-branch (ChargePercent requires TotalCapacityWh > 0).
                else if (m_Stats.ChargePercent >= RECHARGE_THRESHOLD && !m_RechargeNotified)
                {
                    EventBus?.SafePublish(new InfraEvent(InfraEventType.BatteryRecharged), "BackupPowerRuntimeSystem");
                    m_DepletionNotified = false;
                    m_RechargeNotified = true;
                    m_LastBatteryTier = int.MaxValue;
                }
            }

            // Generator-only notification path (when no batteries exist)
            if (m_Stats.TotalCapacityWh == 0 && m_Stats.GeneratorsTotal > 0)
            {
                if (m_Stats.GeneratorsFueled == 0 && !m_GeneratorDepletionNotified)
                {
                    EventBus?.SafePublish(new InfraEvent(InfraEventType.BatteryDepleted), "BackupPowerRuntimeSystem");
                    m_GeneratorDepletionNotified = true;
                }
                else if (m_Stats.GeneratorsFueled > 0 && m_GeneratorDepletionNotified)
                {
                    m_GeneratorDepletionNotified = false;
                }
            }
        }

        private float GetGeneratorEfficiencyMultiplier()
        {
            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_EfficiencyQuery.TryGetSingleton<GeneratorEfficiency>(out var efficiency))
                return 1f;

            return efficiency.Value;
        }

        private void PublishUiState()
        {
            m_UiState = new BackupPowerStateSingleton
            {
                ChargePercent = m_Stats.ChargePercent,
                ProtectedBuildings = m_Stats.ProtectedBuildings,
                TotalCapacityKWh = (int)(m_Stats.TotalCapacityWh / 1000),
                DischargingCount = m_Stats.DischargingCount,
                Policy = m_CurrentPolicy,
                HospitalsPowered = m_HospitalsPowered,
                HospitalsTotal = m_HospitalsTotal,
                SchoolsPowered = m_SchoolsPowered,
                SchoolsTotal = m_SchoolsTotal
            };
        }

        private void ClearRuntimeStatsState(bool clearHasStats)
        {
            m_Stats = default;
            m_UiState = new BackupPowerStateSingleton { Policy = m_CurrentPolicy };
            m_HospitalsPowered = 0;
            m_HospitalsTotal = 0;
            m_SchoolsPowered = 0;
            m_SchoolsTotal = 0;
            if (m_DistrictCoverageMap.IsCreated)
                m_DistrictCoverageMap.Clear();
            if (clearHasStats)
                m_HasStats = false;
        }

        private void ClearSingletonStats()
        {
            if (!m_BackupPowerSingletonQuery.TryGetSingletonEntity<BackupPowerStateSingleton>(out var singleton))
                return;

            m_SingletonWriteLookup.Update(this);
            m_CoverageBufferLookup.Update(this);
            if (m_SingletonWriteLookup.TryGetComponent(singleton, out var state)
                && m_SingletonWriteLookup.HasComponent(singleton))
            {
                m_SingletonWriteLookup[singleton] = new BackupPowerStateSingleton { Policy = state.Policy };
            }

            if (m_CoverageBufferLookup.HasBuffer(singleton))
                m_CoverageBufferLookup[singleton].Clear();
        }

        private void EnsureCounterfeitCapacity()
        {
            int required = CountForCapacity(m_CounterfeitQuery);
            if (m_CounterfeitByBuilding.Capacity < required)
                m_CounterfeitByBuilding.Capacity = math.ceilpow2(required);
        }

        private void EnsureDistrictCoverageCapacity()
        {
            int required = CountForCapacity(m_CoverageAggQuery);
            if (m_DistrictCoverageMap.Capacity < required)
                m_DistrictCoverageMap.Capacity = math.ceilpow2(required);
        }

        private void InitializeGate()
        {
            m_Gate = new ActGateController(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: HandleGateTransition);
        }

        protected override void OnBecameEnabled()
        {
            m_DepletionNotified = false;
            m_GeneratorDepletionNotified = false;
            m_RechargeNotified = false;
            m_LastBatteryTier = int.MaxValue;
            m_IsFirstTick = true;
            ClearRuntimeStatsState(clearHasStats: true);
        }

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
            {
                if (!isInitial)
                {
                    ResetThrottleCounter();
                    ForceNextUpdate();
                    Log.Info("[BackupPowerRuntime] Gate opened");
                }
                return;
            }

            if (next == ActGateState.Inactive && !isInitial)
            {
                m_DepletionNotified = false;
                m_GeneratorDepletionNotified = false;
                m_RechargeNotified = false;
                m_LastBatteryTier = int.MaxValue;
                Log.Info("[BackupPowerRuntime] Gate closed");
            }
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IBackupPowerPolicyWriter>();

            // Complete any pending async job before disposal
            if (m_HasPendingJob || m_HasPendingSingletonWrite)
            {
                m_PendingJobHandle.Complete();
                m_HasPendingJob = false;
                m_HasPendingSingletonWrite = false;
            }

            // Dispose persistent NativeReferences
            if (m_StatsProtectedBuildings.IsCreated) m_StatsProtectedBuildings.Dispose();
            if (m_StatsDischargingCount.IsCreated) m_StatsDischargingCount.Dispose();
            if (m_StatsGeneratorsRunning.IsCreated) m_StatsGeneratorsRunning.Dispose();
            if (m_StatsTotalCapacityWh.IsCreated) m_StatsTotalCapacityWh.Dispose();
            if (m_StatsTotalChargeWh.IsCreated) m_StatsTotalChargeWh.Dispose();
            if (m_StatsGeneratorsTotal.IsCreated) m_StatsGeneratorsTotal.Dispose();
            if (m_StatsGeneratorsFueled.IsCreated) m_StatsGeneratorsFueled.Dispose();
            if (m_CovHospitalsPowered.IsCreated) m_CovHospitalsPowered.Dispose();
            if (m_CovHospitalsTotal.IsCreated) m_CovHospitalsTotal.Dispose();
            if (m_CovSchoolsPowered.IsCreated) m_CovSchoolsPowered.Dispose();
            if (m_CovSchoolsTotal.IsCreated) m_CovSchoolsTotal.Dispose();
            if (m_CounterfeitByBuilding.IsCreated) m_CounterfeitByBuilding.Dispose();
            if (m_DistrictCoverageMap.IsCreated) m_DistrictCoverageMap.Dispose();

            Log.Info($"{nameof(BackupPowerRuntimeSystem)} destroyed");
            base.OnDestroy();
        }
    }
}
