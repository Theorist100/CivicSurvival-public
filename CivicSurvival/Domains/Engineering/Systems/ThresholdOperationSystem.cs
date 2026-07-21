using Game;
using Game.Common;
using Game.Simulation;
using Game.Buildings;
using Game.Areas;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using Game.Net;

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// Burst job for threshold cutoff processing.
    /// Single-threaded (.Run()) because processing + stats must happen in same pass.
    /// Burst compilation provides SIMD + zero GC even without parallelism.
    ///
    /// S13a-7 FIX: Protected services (critical or battery-priority BlackoutState)
    /// are always exempt from threshold cutoff. During active load shedding (AutoDispatch),
    /// threshold is reduced from 90% to 50% so partial power is preserved.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct ThresholdOperationJob : IJobEntity
    {
        public float Threshold;

        // Stats output (NativeReference for single values)
        public NativeReference<int> CutoffCount;
        public NativeReference<int> CutoffKW;

        // Per-district tracking (NativeHashMap for Burst compatibility)
        // FIX CIVIC097: Use Entity key to prevent index recycling issues
        public NativeHashMap<Entity, int> PerDistrictCounts;
        public NativeHashMap<Entity, int> PerDistrictKW;

        // S13a-7 FIX: Critical infrastructure lookup
        [Unity.Collections.ReadOnly]
        public ComponentLookup<BlackoutState> BlackoutStateLookup;

        // Per-entity violation streak (debounce against vanilla flow jitter).
        // Vanilla ElectricityFlowSystem emits transient sub-threshold values on single
        // ticks (iterative solver convergence, graph reroute, voltage drop recompute).
        // Cut is only valid for a building below threshold for MinStreakForCut consecutive
        // ticks — otherwise we'd convert vanilla noise into gameplay events (BackupPower
        // drain, mood penalty, vanilla efficiency reset).
        public NativeHashMap<Entity, int> ViolationStreak;
        public int MinStreakForCut;

        public void Execute(Entity entity, ref ElectricityConsumer consumer, in CurrentDistrict district)
        {
            // Skip if no wanted consumption
            if (consumer.m_WantedConsumption <= 0)
                return;

            // Skip if already at zero (blackout or no connection)
            if (consumer.m_FulfilledConsumption <= 0)
                return;

            if (BlackoutStateLookup.TryGetComponent(entity, out var blackoutState)
                && ShouldBypassThresholdCut(blackoutState))
                return;

            // Calculate fulfillment ratio
            float ratio = (float)consumer.m_FulfilledConsumption / consumer.m_WantedConsumption;

            bool belowThreshold = ratio > 0f && ratio < Threshold;

            if (!belowThreshold)
            {
                // Recovered (or above threshold from the start) — drop streak entry so
                // future violations start counting from zero, not from a stale count.
                if (ViolationStreak.IsCreated)
                    ViolationStreak.Remove(entity);
                return;
            }

            // Increment violation streak for this entity.
            int streak = 1;
            if (ViolationStreak.IsCreated)
            {
                if (ViolationStreak.TryGetValue(entity, out int prevStreak))
                {
                    streak = prevStreak + 1;
                    ViolationStreak[entity] = streak;
                }
                else
                {
                    ViolationStreak.TryAdd(entity, streak);
                }
            }

            // Only apply cut after sustained violation — protects against vanilla
            // ElectricityFlowSystem single-tick jitter.
            if (streak < MinStreakForCut)
                return;

            int lostKW = consumer.m_FulfilledConsumption;
            consumer.m_FulfilledConsumption = 0;
            CutoffCount.Value++;
            CutoffKW.Value += lostKW;

            // Track per-district (FIX CIVIC097: use Entity key)
            Entity districtEntity = district.m_District;
            if (PerDistrictCounts.TryGetValue(districtEntity, out int current))
                PerDistrictCounts[districtEntity] = current + 1;
            else
                PerDistrictCounts.TryAdd(districtEntity, 1);

            if (PerDistrictKW.TryGetValue(districtEntity, out int currentKW))
                PerDistrictKW[districtEntity] = currentKW + lostKW;
            else
                PerDistrictKW.TryAdd(districtEntity, lostKW);
        }

        private static bool ShouldBypassThresholdCut(BlackoutState state)
            => state.IsCritical || state.HasBatteryPriority;
    }

    /// <summary>
    /// Threshold Operation System - enforces minimum power threshold.
    ///
    /// BURST OPTIMIZED: Single-threaded Burst job for processing + stats.
    /// Zero GC, SIMD optimizations, ~10x faster than main thread C#.
    ///
    /// Vanilla CS2 distributes power proportionally during deficit:
    /// - If 85% power available, ALL buildings get 85%
    ///
    /// This system changes that behavior:
    /// - Buildings must get >= 90% power to function
    /// - If a building gets less than 90%, it gets NOTHING
    ///
    /// Why this matters:
    /// - Critical infrastructure (hospitals) needs FULL power to work
    /// - Partial power = useless
    /// - Forces player to use Load Shedding to protect essential services
    ///
    /// S13a-12 ACCEPTED: Operates on post-flow capacity data from vanilla ElectricityFlowSystem; cross-check N/A.
    /// </summary>
    [ActIndependent]
    public partial class ThresholdOperationSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("ThresholdOperationSystem");

        // PERF: 500ms interval instead of 4 frames (~67ms) - threshold changes slowly
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        private EntityQuery m_ConsumerQuery;
        private EntityQuery m_ThresholdSingletonQuery;
        private EntityQuery m_AutoDispatchQuery;
        // Gate: threshold is a deficit-distribution mechanism; under surplus + Normal
        // zone vanilla flow jitter is not a gameplay signal. See OnThrottledUpdate.
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_StressQuery;
        private ComponentLookup<ThresholdStateSingleton> m_ThresholdSingletonLookup;
        private BufferLookup<ThresholdCutBuffer> m_ThresholdCutBufferLookup;
        // S13a-7 FIX: Critical infrastructure lookup for job
        private ComponentLookup<BlackoutState> m_BlackoutStateLookup;

        private int m_LastCutoffCount = 0;

        // Persistent Native collections (zero GC)
        private NativeReference<int> m_CutoffCount;
        private NativeReference<int> m_CutoffKW;
        private NativeHashMap<Entity, int> m_PerDistrictCounts;
        private NativeHashMap<Entity, int> m_PerDistrictKW;

        // Debounce: a building must be sub-threshold for N consecutive ticks before
        // we cut it. At UPDATE_INTERVAL_500_MS that's 1.5s of sustained pressure —
        // long enough to filter vanilla solver convergence noise, short enough that
        // a real deficit still cuts within ~2s.
        private const int CUT_DEBOUNCE_TICKS = 3;
        private NativeHashMap<Entity, int> m_ViolationStreak;

        // M2 FIX: Cache ModSettings (avoid lookup in hot path)
        private ModSettings? m_Settings;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            ThresholdStateSingleton.EnsureExists(EntityManager);

            Log.Info("Created (Burst optimized)");

            m_ConsumerQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadWrite<ElectricityConsumer>(),
                ComponentType.ReadOnly<CurrentDistrict>(),
                ComponentType.Exclude<OutsideConnection>(),
                ComponentType.Exclude<Deleted>()
            );

            m_ThresholdSingletonQuery = GetEntityQuery(
                ComponentType.ReadWrite<ThresholdStateSingleton>()
            );

            m_ThresholdSingletonLookup = GetComponentLookup<ThresholdStateSingleton>(false);
            m_ThresholdCutBufferLookup = GetBufferLookup<ThresholdCutBuffer>(false);
            // S13a-7 FIX: Lookup for IsCritical check in job
            m_BlackoutStateLookup = GetComponentLookup<BlackoutState>(true);
            m_AutoDispatchQuery = GetEntityQuery(ComponentType.ReadOnly<AutoDispatchData>());
            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_StressQuery = GetEntityQuery(ComponentType.ReadOnly<GridStressData>());

            // Allocate persistent Native collections
            m_CutoffCount = new NativeReference<int>(Allocator.Persistent);
            m_CutoffKW = new NativeReference<int>(Allocator.Persistent);
            m_PerDistrictCounts = new NativeHashMap<Entity, int>(64, Allocator.Persistent);
            m_PerDistrictKW = new NativeHashMap<Entity, int>(64, Allocator.Persistent);
            m_ViolationStreak = new NativeHashMap<Entity, int>(64, Allocator.Persistent);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            ThresholdStateSingleton.EnsureExists(EntityManager);
        }

        protected override bool ShouldSkipUpdate()
        {
            return m_Settings == null || !m_Settings.ThresholdOperationEnabled;
        }

        /// <summary>
        /// Clear stale ThresholdStateSingleton when feature disabled.
        /// Prevents UI from showing phantom "threshold active" state.
        /// </summary>
        protected override void OnBecameDisabled()
        {
            m_ThresholdSingletonLookup.Update(this);
            if (m_ThresholdSingletonQuery.TryGetSingletonEntity<ThresholdStateSingleton>(out var entity))
            {
                m_ThresholdSingletonLookup[entity] = default;
                m_ThresholdCutBufferLookup.Update(this);
                if (m_ThresholdCutBufferLookup.TryGetBuffer(entity, out var buffer))
                    buffer.Clear();
            }
            m_LastCutoffCount = 0;
            Log.Info("ThresholdOperation disabled — cleared singleton");
        }

        protected override void OnThrottledUpdate()
        {
            using (PerformanceProfiler.MeasureDebug("SP:TOS.LookupSync"))
            {
                m_ThresholdSingletonLookup.Update(this);
                m_ThresholdCutBufferLookup.Update(this);
                m_BlackoutStateLookup.Update(this);
            }

            // Early exit if no consumers
            if (m_ConsumerQuery.IsEmpty)
            {
                m_CutoffCount.Value = 0;
                m_CutoffKW.Value = 0;
                m_PerDistrictCounts.Clear();
                m_PerDistrictKW.Clear();
                m_ViolationStreak.Clear();
                m_LastCutoffCount = 0;
                UpdateSingleton(0, 0);
                return;
            }

            // Reset stats
            m_CutoffCount.Value = 0;
            m_CutoffKW.Value = 0;
            m_PerDistrictCounts.Clear();
            m_PerDistrictKW.Clear();

            // S13a-7 FIX: Reduce threshold during active load shedding.
            // When AutoDispatch has shed districts, remaining buildings may get <90% power
            // because vanilla flow graph still wastes allocation on blackout buildings.
            // Using 50% threshold during crisis preserves partial power instead of cutting to zero.
            //
            // NOTE: AutoDispatchData is written by AutoDispatchSystem later in the
            // power-capacity chain. This read is always 1 frame stale on the
            // transition frame when shedding first activates. Impact: wrong threshold for 1
            // ThrottledUpdate tick (~500ms), then self-corrects. Structural ordering constraint —
            // not fixable without introducing a cross-group dependency.
            bool isCrisis = false;
            if (m_AutoDispatchQuery.TryGetSingleton<AutoDispatchData>(out var dispatchData))
                isCrisis = dispatchData.AutoSheddedCount > 0;

            float threshold = isCrisis
                ? Engine.PowerGrid.GRID_POWER_THRESHOLD_CRISIS  // 0.5 = 50%
                : Engine.PowerGrid.GRID_POWER_THRESHOLD;        // 0.9 = 90%

            // Global gate: threshold is a *deficit-distribution* mechanism. Under
            // healthy surplus + Normal stress zone, any sub-threshold reading is
            // vanilla ElectricityFlowSystem iteration noise, not gameplay. Cutting
            // those buildings (Fulfilled := 0) cascades into BackupPower drain,
            // mood penalty, vanilla efficiency reset, and UI badge flicker — all
            // for noise. Skip the job entirely when the grid isn't under pressure.
            //
            // CIVIC070 suppression: this system already RegisterAfter<BlackoutReadyMarker>
            // (Fulfilled is fresh). A second RegisterAfter<PowerDataReadyMarker> for
            // PowerGridSingleton freshness would trip CIVIC470 (single registration per
            // system, marker chain via middleman not supported). The gate tolerates 1
            // throttle tick (~500ms) of stale Balance: thresholds are coarse (50 MW)
            // and grid state changes on multi-second timescales. If neither singleton
            // resolves, gridUnderPressure stays true → threshold behaves as before the
            // gate was added (no regression).
#pragma warning disable CIVIC070
            bool hasPg = m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var pgGate);
#pragma warning restore CIVIC070
            bool hasStress = m_StressQuery.TryGetSingleton<GridStressData>(out var stressGate);

            bool gridUnderPressure = isCrisis
                || !(hasPg || hasStress)
                || (hasPg && pgGate.Balance < Engine.PowerGrid.SURPLUS_THRESHOLD)
                || (hasStress && stressGate.Zone != GridStressZone.Normal);

            if (!gridUnderPressure)
            {
                // Reset state so we don't carry stale streak into the next pressure window.
                m_ViolationStreak.Clear();
                m_LastCutoffCount = 0;
                UpdateSingleton(0, 0);
                return;
            }

            // Run Burst job (single-threaded via .Run() - immediate results)
            int cutoffCount;
            int cutoffKW;
            using (PerformanceProfiler.MeasureDebug("TOS.BurstJob"))
            {
                var job = new ThresholdOperationJob
                {
                    Threshold = threshold,
                    CutoffCount = m_CutoffCount,
                    CutoffKW = m_CutoffKW,
                    PerDistrictCounts = m_PerDistrictCounts,
                    PerDistrictKW = m_PerDistrictKW,
                    BlackoutStateLookup = m_BlackoutStateLookup,
                    ViolationStreak = m_ViolationStreak,
                    MinStreakForCut = CUT_DEBOUNCE_TICKS
                };
                // BURSTMARK crash-2 candidate (main-thread .Run). pre/post is reliable here:
                // if BurstDiag.log ends on "pre" with no "post", this job's static init AV'd.
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ThresholdOperationJob.Run cutoff={m_CutoffCount.IsCreated} kw={m_CutoffKW.IsCreated} pdc={m_PerDistrictCounts.IsCreated}/count={m_PerDistrictCounts.Count}/capacity={m_PerDistrictCounts.Capacity} pdk={m_PerDistrictKW.IsCreated}/count={m_PerDistrictKW.Count}/capacity={m_PerDistrictKW.Capacity} streak={m_ViolationStreak.IsCreated}/count={m_ViolationStreak.Count}/capacity={m_ViolationStreak.Capacity} threshold={threshold:F3}");
                job.Run(m_ConsumerQuery);
                if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                    CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ThresholdOperationJob.Run cutoff={m_CutoffCount.Value} kw={m_CutoffKW.Value} pdc={m_PerDistrictCounts.Count} pdk={m_PerDistrictKW.Count} streak={m_ViolationStreak.Count}");
                cutoffCount = m_CutoffCount.Value;
                cutoffKW = m_CutoffKW.Value;
            }

            // Log only when count changes significantly
            if (math.abs(cutoffCount - m_LastCutoffCount) >= 10 && cutoffCount > 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"ThresholdOperation: Cut off {cutoffCount} buildings ({cutoffKW} kW) below {threshold:P0} fulfillment (crisis={isCrisis})");
            }

            m_LastCutoffCount = cutoffCount;

            // Write to ECS singleton and buffer
            UpdateSingleton(cutoffCount, cutoffKW);
        }

        /// <summary>
        /// Write threshold state to ECS singleton and buffer for UI.
        /// </summary>
        private void UpdateSingleton(int cutoffCount, int cutoffKW)
        {
            // TOCTOU FIX: Use TryGetSingletonEntity instead of IsEmpty + GetSingletonEntity
            if (!m_ThresholdSingletonQuery.TryGetSingletonEntity<ThresholdStateSingleton>(out var entity))
                return;

            var prev = m_ThresholdSingletonLookup[entity];
            bool stateChanged = prev.IsActive != (cutoffCount > 0)
                || prev.CutoffCount != cutoffCount
                || prev.CutoffKW != cutoffKW
                || ThresholdBufferChanged(entity);
            if (!stateChanged)
                return;

            m_ThresholdSingletonLookup[entity] = new ThresholdStateSingleton
            {
                IsActive = cutoffCount > 0,
                CutoffCount = cutoffCount,
                CutoffKW = cutoffKW
            };

            // Update per-district buffer from NativeHashMap
            if (!m_ThresholdCutBufferLookup.TryGetBuffer(entity, out var buffer)) return;
            buffer.Clear();

            var enumerator = m_PerDistrictCounts.GetEnumerator();
            while (enumerator.MoveNext())
            {
                Entity districtEntity = enumerator.Current.Key;
                int kw = m_PerDistrictKW.TryGetValue(districtEntity, out int v) ? v : 0;
                buffer.Add(new ThresholdCutBuffer
                {
                    District = DistrictRef.FromEntity(districtEntity),
                    CutCount = enumerator.Current.Value,
                    CutKW = kw
                });
            }
            enumerator.Dispose();
        }

        private bool ThresholdBufferChanged(Entity entity)
        {
            if (!m_ThresholdCutBufferLookup.TryGetBuffer(entity, out var buffer))
                return m_PerDistrictCounts.Count != 0;

            if (buffer.Length != m_PerDistrictCounts.Count)
                return true;

            for (int i = 0; i < buffer.Length; i++)
            {
                Entity districtEntity = buffer[i].District.ToEntity();
                if (!m_PerDistrictCounts.TryGetValue(districtEntity, out int count)
                    || count != buffer[i].CutCount)
                {
                    return true;
                }

                int kw = m_PerDistrictKW.TryGetValue(districtEntity, out int value) ? value : 0;
                if (kw != buffer[i].CutKW)
                    return true;
            }

            return false;
        }

        protected override void OnDestroy()
        {
            // Dispose persistent Native collections
            if (m_CutoffCount.IsCreated) m_CutoffCount.Dispose();
            if (m_CutoffKW.IsCreated) m_CutoffKW.Dispose();
            if (m_PerDistrictCounts.IsCreated) m_PerDistrictCounts.Dispose();
            if (m_PerDistrictKW.IsCreated) m_PerDistrictKW.Dispose();
            if (m_ViolationStreak.IsCreated) m_ViolationStreak.Dispose();

            Log.Info("Destroyed");
            base.OnDestroy();
        }
    }
}
