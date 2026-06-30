using Game;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.PowerBackup.Jobs;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.PowerBackup.Systems
{
    /// <summary>
    /// Effects system for backup power - handles side effects.
    ///
    /// Effects:
    /// 1. Noise from diesel generators (tracked for UI)
    /// 2. Battery degradation over discharge cycles
    /// 3. Fire risk (random chance when discharging)
    /// 4. Fuel consumption for generators
    ///
    /// Architecture: Async Burst IJobEntity — NO hot-path sync points.
    /// Frame N: apply finished fire intents + schedule NEW job.
    /// Later tick: consume ECS fire intents after the scheduled handle reports complete.
    /// </summary>
    public partial class BackupPowerEffectsSystem : ThrottledSystemBase, IActGatedSystem
    {
        private const int DEFAULT_RANDOM_SEED = 54321;
        private const float EFFECT_SLOT_SECONDS = 2f;

        private static readonly LogContext Log = new("BackupPowerEffectsSystem");

        // Building fire creates a ModFireIntent on this barrier in GameSimulation;
        // ModFireApplySystem does the OnFire/BatchesUpdated structural add in ModificationEnd.
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private ComponentLookup<OnFire> m_OnFireLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        // V_REGRESSION Pattern 8 (Phase 8): per-system m_IgniteQueuedThisFrame
        // replaced by shared cross-system IFrameMutationDedup (resolved in
        // OnStartRunning). Frame-end clear runs in FrameMutationDedupClearSystem.
        private IFrameMutationDedup m_FrameMutationDedup = null!;

        // S12b-1 FIX: Query for counterfeit batteries (fire risk multiplier)
        private EntityQuery m_CounterfeitQuery;
        private EntityQuery m_BackupQuery;

        // Stats for UI — recalculated each update, not persisted
        [System.NonSerialized] private BackupEffectsStats m_Stats;
        [System.NonSerialized] private float m_LastGameTimeForEffects = -1f;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_2_SECONDS;
        public BackupEffectsStats Stats => m_Stats;
        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        // Random for fire events (deterministic seed)
        private Random m_Random;
        private EntityQuery m_CurrentActQuery;
        [System.NonSerialized] private ActGateController m_Gate = null!;

        // M2 FIX: Cache ModSettings (avoid lookup in hot path)
        private ModSettings? m_Settings;

        private JobHandle m_PendingEffectsHandle;
        private uint m_EffectTick;
        private bool m_HasPendingResults;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_OnFireLookup = GetComponentLookup<OnFire>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);

            m_CounterfeitQuery = GetEntityQuery(
                ComponentType.ReadOnly<CounterfeitBattery>(),
                ComponentType.Exclude<Deleted>());
            m_BackupQuery = GetEntityQuery(
                ComponentType.ReadOnly<BackupPower>(),
                ComponentType.Exclude<Deleted>());
            m_Random = new Random(DEFAULT_RANDOM_SEED);

            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());

            InitializeGate();

            Log.Info($"{nameof(BackupPowerEffectsSystem)} created (gated until Crisis)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            m_FrameMutationDedup ??= ServiceRegistry.Instance.Require<IFrameMutationDedup>();
            // Dedup reader is resolved lazily in ProcessFireCandidate so devtools
            // toggling Corruption on/off mid-run picks up the change naturally.
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);
            return m_Settings == null
                || !m_Settings.BackupPowerEnabled
                || m_Gate.State != ActGateState.Active;
        }

        protected override void OnBecameDisabled()
        {
            Dependency.Complete();
            if (m_HasPendingResults)
            {
                ProcessFinishedEffects();
                m_HasPendingResults = false;
                m_PendingEffectsHandle = default;
            }
            ClearPendingFireIntents();
            m_Stats = default;
            RebaseEffectBaseline();
        }

        protected override void OnThrottledUpdate()
        {
            using (PerformanceProfiler.Measure("BPEffects.ProcessEffects"))
            {
                if (m_HasPendingResults)
                {
                    if (!m_PendingEffectsHandle.IsCompleted)
                        return;
                    m_HasPendingResults = false;
                }

                ProcessFinishedEffects();

                if (!m_BackupQuery.IsEmpty)
                {
                    ScheduleNewJob();
                    m_HasPendingResults = true;
                }
            }
        }

        private void ProcessFinishedEffects()
        {
            m_Stats = default;
            m_OnFireLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_DeletedLookup.Update(this);

            var bpConfig = BalanceConfig.Current.BackupPower;

            foreach (var (backupRef, _) in
                SystemAPI.Query<RefRW<BackupPower>>()
                .WithNone<Deleted>()
                .WithEntityAccess())
            {
                var backup = backupRef.ValueRO;
                if (backup.Type == BackupPowerType.None)
                    continue;

                if (backup.Degradation > bpConfig.DegradationWarningThreshold)
                    m_Stats.DegradedUnits++;

                if (backup.Type == BackupPowerType.DieselGenerator && backup.IsDischarging && backup.FuelHours > 0)
                {
                    m_Stats.GeneratorsRunning++;
                    m_Stats.TotalNoiseLevel += bpConfig.NoiseLevelDiesel;
                }

                if (backup.PendingFireType == BackupPowerType.None)
                    continue;

                if (ProcessFireCandidate(backup.GetBuildingEntity(), backup.PendingFireType))
                    m_Stats.FiresTriggered++;
                backup.PendingFireType = BackupPowerType.None;
                backupRef.ValueRW = backup;
            }

            if (m_Stats.GeneratorsRunning > 0 || m_Stats.FiresTriggered > 0)
                Log.Info($"BackupEffects: {m_Stats.GeneratorsRunning} generators, noise={m_Stats.TotalNoiseLevel}dB, fires={m_Stats.FiresTriggered}");
        }

        [CompletesDependency("ScheduleNewJob: counterfeit count for NativeHashMap capacity pre-allocation before Burst job schedule; throttled 2s tick, CounterfeitBattery written infrequently by DistrictModernizationSystem (no pending jobs typical)")]
        private void ScheduleNewJob()
        {
            if (m_BackupQuery.IsEmpty)
                return;

            // Use game-time delta (not wall-clock) so fire probability scales correctly with game speed.
            // At 3x speed, ThrottledDeltaSeconds is ~0.67s but 2s of game-time passed.
            // GameRate.HoursToSeconds(TotalGameHours) persists across save/load (unlike ElapsedTime which resets).
            // LOAD-INVARIANT: effects jobs must not schedule from a throwing GameTime read.
            if (!TryGetCurrentGameTimeSeconds(out var gameTimeSeconds))
                return;
            float gameTimeDelta = ResolveEffectGameTimeDelta(gameTimeSeconds, m_LastGameTimeForEffects);
            m_LastGameTimeForEffects = gameTimeSeconds;
            float dtScale = gameTimeDelta / EFFECT_SLOT_SECONDS;

            // Cache managed config values for Burst job
            var bpConfig = BalanceConfig.Current.BackupPower;

            // S12b-1 FIX: Build counterfeit fire risk map (BuildingKey → multiplier)
            // R4-S3-08 FIX: Use (Index,Version) composite key to prevent entity slot recycling false positives.
            int capacity = CountForCapacity(m_CounterfeitQuery, out int counterfeitCount);
            var counterfeitMap = new NativeHashMap<long, float>(capacity, Allocator.TempJob);
            bool counterfeitMapDisposed = false;
            try
            {
            if (counterfeitCount > 0)
            {
                var counterfeitData = m_CounterfeitQuery.ToComponentDataArray<CounterfeitBattery>(Allocator.Temp);
                for (int i = 0; i < counterfeitData.Length; i++)
                {
                    long key = counterfeitData[i].Building.Packed;
                    float multiplier = counterfeitData[i].FireRiskMultiplier;
                    if (counterfeitMap.TryGetValue(key, out float existing))
                        counterfeitMap[key] = math.max(existing, multiplier);
                    else
                        counterfeitMap.TryAdd(key, multiplier);
                }
                if (counterfeitData.IsCreated) counterfeitData.Dispose();
            }

            uint randomSeed = m_Random.state;
            m_Random.NextUInt();
            m_EffectTick = m_EffectTick == uint.MaxValue ? 1u : m_EffectTick + 1u;

            var job = new BackupPowerEffectsJob
            {
                DtScale = dtScale,
                RandomSeed = randomSeed,
                EffectTick = m_EffectTick,
                FireRiskHomeBattery = bpConfig.FireRiskHomeBattery,
                FireRiskBusinessUps = bpConfig.FireRiskBusinessUps,
                FireRiskIndustrialBattery = bpConfig.FireRiskIndustrialBattery,
                FireRiskDieselGenerator = bpConfig.FireRiskDieselGenerator,
                CounterfeitFireRiskMap = counterfeitMap
            };

            // Schedule — NO Complete(). Job runs on worker thread.
            // Results consumed next throttled update (2 seconds later).
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre BackupPowerEffectsJob.Schedule counterfeitCount={counterfeitCount} map={counterfeitMap.IsCreated}/count={counterfeitMap.Count}/capacity={counterfeitMap.Capacity} dtScale={dtScale:F3} tick={m_EffectTick}");
            var jobHandle = job.Schedule(Dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post BackupPowerEffectsJob.Schedule map={counterfeitMap.IsCreated}/count={counterfeitMap.Count}");
            m_PendingEffectsHandle = jobHandle;
            // Dispose TempJob map after job completes
            if (counterfeitMap.IsCreated)
                Dependency = counterfeitMap.Dispose(jobHandle);
            else
                Dependency = jobHandle;
            counterfeitMapDisposed = true;
            }
            finally
            {
                if (!counterfeitMapDisposed && counterfeitMap.IsCreated) counterfeitMap.Dispose();
            }
        }

        /// <summary>
        /// Process a fire intent written by the Burst job.
        /// Runs on main thread: ECB commands + EventBus (not Burst-accessible).
        /// </summary>
        private bool ProcessFireCandidate(Entity buildingEntity, BackupPowerType type)
        {
            if (!SystemAPI.HasComponent<Building>(buildingEntity))
                return false;

            Log.Warn($"BackupPower FIRE triggered on building {buildingEntity.Index}!");

            if (type == BackupPowerType.DieselGenerator)
            {
                EventBus?.SafePublish(new InfraEvent(InfraEventType.GeneratorFire), "BackupPowerEffectsSystem");
            }

            // POT-03 FIX: Trigger actual fire on vanilla building
            // L-76: Skip if CounterfeitBatteryFireSystem already fired this building today.
            // Null object returns WasFiredToday=false when Corruption closed → fire proceeds.
            var fireDedup = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCounterfeitFireDedupReader.Instance);
            if (m_OnFireLookup.HasComponent(buildingEntity)
                || fireDedup.WasFiredToday(buildingEntity))
                return false;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            // TryApplyModFire is the producer half: it creates a ModFireIntent.
            // ModFireApplySystem builds the real OnFire.m_Event from the vanilla fire prefab
            // and applies OnFire + BatchesUpdated + upgrade propagation in ModificationEnd, in
            // phase with the render pass; vanilla FireSimulationSystem then drives escalation,
            // spread and structural damage off the fire event.
            if (!BuildingDamageHelper.TryApplyModFire(
                ecb,
                buildingEntity,
                m_FrameMutationDedup,
                m_OnFireLookup,
                m_DestroyedLookup,
                m_DeletedLookup))
                return false;

            // ECB written on main thread — no AddJobHandleForProducer needed
            return true;
        }

        private void ClearPendingFireIntents()
        {
            foreach (var backupRef in
                SystemAPI.Query<RefRW<BackupPower>>()
                .WithNone<Deleted>())
            {
                if (backupRef.ValueRO.PendingFireType == BackupPowerType.None)
                    continue;
                var backup = backupRef.ValueRO;
                backup.PendingFireType = BackupPowerType.None;
                backupRef.ValueRW = backup;
            }
        }

        /// <summary>
        /// Save/load-safe variant. Returns false when GameTimeSystem hasn't activated yet
        /// (vanilla calls SetDefaults / Deserialize before OnGameLoaded). Callers should
        /// fall back to the -1f sentinel; <see cref="RepairEffectBaselineAfterDeserialize"/>
        /// will rebase the baseline once activation completes.
        /// </summary>
        private static bool TryGetCurrentGameTimeSeconds(out float seconds)
        {
            if (!GameTimeSystem.TryGetGameHours(out var h))
            {
                seconds = -1f;
                return false;
            }
            seconds = GameRate.HoursToSeconds(h);
            return true;
        }

        private static bool IsValidEffectBaseline(float gameTimeSeconds) =>
            math.isfinite(gameTimeSeconds) && gameTimeSeconds >= 0f;

        private static float ResolveEffectGameTimeDelta(float currentGameTimeSeconds, float lastGameTimeSeconds)
        {
            if (!IsValidEffectBaseline(lastGameTimeSeconds))
                return EFFECT_SLOT_SECONDS;

            return math.clamp(currentGameTimeSeconds - lastGameTimeSeconds, 0f, EFFECT_SLOT_SECONDS);
        }

        private void RebaseEffectBaseline()
        {
            if (TryGetCurrentGameTimeSeconds(out var seconds))
                m_LastGameTimeForEffects = seconds;
            else
                m_LastGameTimeForEffects = -1f;
        }

        private void InitializeGate()
        {
            m_Gate = new ActGateController(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: HandleGateTransition);
        }

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
            {
                if (!isInitial)
                {
                    ResetThrottleCounter();
                    ForceNextUpdate();
                    RebaseEffectBaseline();
                    Log.Info("[BackupPowerEffects] Gate opened");
                }
                return;
            }

            if (next == ActGateState.Inactive && !isInitial)
            {
                Log.Info("[BackupPowerEffects] Gate closed");
            }
        }

        protected override void OnBecameEnabled()
        {
            m_Stats = default;
            m_HasPendingResults = false;
            m_PendingEffectsHandle = default;
            RebaseEffectBaseline();
        }

        protected override void OnDestroy()
        {
            // Complete any pending job before disposing
            Dependency.Complete();
            if (m_HasPendingResults)
            {
                m_PendingEffectsHandle.Complete();
                m_HasPendingResults = false;
                m_PendingEffectsHandle = default;
            }
            // FrameMutationDedup is a process-lifetime singleton owned by Mod —
            // do not dispose here. Per-system m_IgniteQueuedThisFrame retired
            // by V_REGRESSION Phase 8.

            Log.Info($"{nameof(BackupPowerEffectsSystem)} destroyed");
            base.OnDestroy();
        }
    }

    /// <summary>
    /// Statistics from effects system for UI.
    /// </summary>
    public struct BackupEffectsStats
    {
        public int GeneratorsRunning;
        public int TotalNoiseLevel;      // dB total
        public int FiresTriggered;
        public int DegradedUnits;    // Backup power units (batteries and generators) with >20% degradation
    }
}
