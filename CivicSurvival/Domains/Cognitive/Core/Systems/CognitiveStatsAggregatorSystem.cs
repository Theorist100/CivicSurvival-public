using Game;
using Game.Buildings;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Aggregates household-level PsyImpact statistics for UI display.
    /// Runs throttled (~1 second) to avoid per-frame overhead.
    ///
    /// Uses ASYNC pattern: schedule this frame, read results next frame.
    /// This eliminates sync point (.Complete()) that was blocking main thread.
    ///
    /// Provides:
    /// - Total households tracked
    /// - Average infection level, resistance, trauma
    /// - Counts of households affected by each stress source
    ///
    /// Data stored in CognitiveStatsState singleton, read by CognitiveUIPanel.
    ///
    /// S17b-6 ACCEPTED: District-level integrity is in CognitiveState (different system's responsibility).
    /// S17b-9 ACCEPTED: Default zeros for BlackoutVulnerabilityMult — standard ECS zero-init; recalculated on first update.
    /// </summary>
    [ActIndependent]
    public partial class CognitiveStatsAggregatorSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("CognitiveStatsAggregatorSystem");

        private const float FALLBACK_BLACKOUT_VULN_MAX_BONUS = 0.30f;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        private EntityQuery m_HouseholdQuery;
        private ComponentLookup<PropertyRenter> m_PropertyRenterLookup;

        // Async pattern: schedule job, read results next update
        private JobHandle m_PendingJobHandle;
        private NativeReference<CognitiveStatsAggregation> m_PendingAggregation;
        private bool m_HasPendingJob;

        // DidFire latch REMOVED: CogAgg now reads only persistent fields (BlackoutHours,
        // Trauma, InfectionLevel, Resistance_Value) — valid at any time, not just on MHR
        // fire frames. CogAgg fires on its own 1-second throttle independently of MHR.

        // FIX H2: Cache config for ApplyAggregationResults (same snapshot as job scheduling)
        private float m_PendingVulnThreshold;
        private float m_PendingVulnMaxHours;
        private float m_PendingVulnMaxBonus;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Ensure singleton exists
            CognitiveStatsState.EnsureExists(EntityManager);

            // Query all mod entities — persistent fields are valid on all entities.
            m_HouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<HouseholdPsyState>()
            );

            // Cache lookup for homeless filter
            m_PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true);

            // Allocate persistent aggregation buffer
            m_PendingAggregation = new NativeReference<CognitiveStatsAggregation>(Allocator.Persistent);

            // Pressure pipeline registration: consume channels for stats UI
            PressureRegistry.RegisterConsumer(PressureChannel.Blackout, nameof(CognitiveStatsAggregatorSystem));
            PressureRegistry.RegisterConsumer(PressureChannel.Envy, nameof(CognitiveStatsAggregatorSystem));
            PressureRegistry.RegisterConsumer(PressureChannel.Impact, nameof(CognitiveStatsAggregatorSystem));

            Log.Info("Created (async pattern, uses HouseholdPsyState)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            CognitiveStatsState.EnsureExists(EntityManager);
        }

        protected override void OnDestroy()
        {
            // Complete any pending job before disposal
            m_PendingJobHandle.Complete();
            m_HasPendingJob = false;
            m_PendingJobHandle = default;

            if (m_PendingAggregation.IsCreated)
                m_PendingAggregation.Dispose();

            // FIX L5: Symmetric deregister (matches OnCreate registrations)
            PressureRegistry.DeregisterConsumer(PressureChannel.Blackout, nameof(CognitiveStatsAggregatorSystem));
            PressureRegistry.DeregisterConsumer(PressureChannel.Envy, nameof(CognitiveStatsAggregatorSystem));
            PressureRegistry.DeregisterConsumer(PressureChannel.Impact, nameof(CognitiveStatsAggregatorSystem));

            base.OnDestroy();
        }

        // ShouldSkipUpdate REMOVED: no longer depends on MHR.DidFire latch.
        // Persistent fields are always valid — standard ThrottledSystemBase timing is sufficient.

        protected override void OnThrottledUpdate()
        {
            // ============================================================
            // STEP 1: Complete PREVIOUS job and apply results
            // ============================================================
            if (m_HasPendingJob)
            {
                m_PendingJobHandle.Complete();
                m_HasPendingJob = false;
                ApplyAggregationResults();
            }

            // DidFire latch removed: CogAgg now reads persistent fields only.
            // Persistent fields are valid at any time — no dependency on MHR fire frame.

            // ============================================================
            // STEP 2: Schedule NEW job (no .Complete() - runs async)
            // ============================================================
            if (m_HouseholdQuery.IsEmptyIgnoreFilter)
            {
                // Reset stats if no households
                if (SystemAPI.TryGetSingletonRW<CognitiveStatsState>(out var stateRef))
                {
                    stateRef.ValueRW = default;
                    // FIX M5: BlackoutVulnerabilityMult neutral is 1.0f, not 0f
                    stateRef.ValueRW.BlackoutVulnerabilityMult = 1.0f;
                }
                return;
            }

            m_PropertyRenterLookup.Update(this);
            var balance = BalanceConfig.Current;
            if (balance == null) return;
            var cwCfg = balance.Cognitive;

            // FIX H2: Cache config snapshot — ApplyAggregationResults must use same values as job
            m_PendingVulnThreshold = cwCfg.BlackoutVulnThresholdHours;
            m_PendingVulnMaxHours = cwCfg.BlackoutVulnMaxHours;
            m_PendingVulnMaxBonus = cwCfg.BlackoutVulnMaxBonus;

            var job = new AggregateStatsJob
            {
                Aggregation = m_PendingAggregation,
                PropertyRenterLookup = m_PropertyRenterLookup,
                StressThreshold = cwCfg.StressThreshold,
                InfectionThreshold = cwCfg.InfectionThreshold,
                BlackoutVulnThreshold = cwCfg.BlackoutVulnThresholdHours
            };

            // Schedule WITHOUT .Complete() - main thread is free
            // Uses Dependency as input (waits for upstream writes to HouseholdPsyState)
            // Must assign back to Dependency so PsyTransientResetSystem waits for our read
            // Reset immediately before scheduling because the job read-modify-writes Aggregation.Value.
            m_PendingAggregation.Value = default;
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre AggregateStatsJob.Schedule queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} aggregation={m_PendingAggregation.IsCreated}");
            Dependency = m_PendingJobHandle = job.Schedule(m_HouseholdQuery, Dependency);
            if (CivicSurvival.Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post AggregateStatsJob.Schedule queryEmpty={m_HouseholdQuery.IsEmptyIgnoreFilter} aggregation={m_PendingAggregation.IsCreated}");
            m_HasPendingJob = true;
        }

        private void ApplyAggregationResults()
        {
            if (!SystemAPI.TryGetSingletonRW<CognitiveStatsState>(out var state))
                return;

            var agg = m_PendingAggregation.Value;
            int count = agg.HouseholdCount;

            state.ValueRW.TotalHouseholds = count;

#pragma warning disable CIVIC190 // Division guarded by count > 0 check above
            if (count > 0)
            {
                state.ValueRW.AvgInfectionLevel = agg.TotalInfection / count;
                state.ValueRW.AvgResistance = agg.TotalResistance / count;
                state.ValueRW.AvgTrauma = agg.TotalTrauma / count;
            }
#pragma warning restore CIVIC190
            else
            {
                state.ValueRW.AvgInfectionLevel = 0f;
                state.ValueRW.AvgResistance = 0f;
                state.ValueRW.AvgTrauma = 0f;
            }

            state.ValueRW.HouseholdsUnderBlackout = agg.BlackoutCount;
            state.ValueRW.HouseholdsWithEnvy = agg.EnvyCount;
            state.ValueRW.HouseholdsUnderImpact = agg.ImpactCount;
            state.ValueRW.HouseholdsInfected = agg.InfectedCount;

            // CDI-7: Calculate blackout vulnerability stats
            state.ValueRW.VulnerableHouseholds = agg.VulnerableHouseholdCount;
            if (agg.VulnerableHouseholdCount > 0)
            {
                // Average blackout hours among vulnerable households
                float avgBlackoutHours = agg.TotalBlackoutHours / agg.VulnerableHouseholdCount;
                state.ValueRW.AvgBlackoutHours = avgBlackoutHours;

                // Calculate vulnerability multiplier (must match CognitiveCalculator.cs formula):
                // FIX W2-M4 + W3-M3: Use config divisor + cap (was hardcoded 24f, no cap)
                // FIX H2: Use cached config from scheduling frame (not fresh BalanceConfig.Current)
                float vulnThreshold = m_PendingVulnThreshold > 0f ? m_PendingVulnThreshold : 4f;
                float vulnMaxHours = m_PendingVulnMaxHours > 0f ? m_PendingVulnMaxHours : GameRate.HOURS_PER_DAY;
                float vulnBonus = m_PendingVulnMaxBonus > 0f ? m_PendingVulnMaxBonus : FALLBACK_BLACKOUT_VULN_MAX_BONUS;
                float excessHours = Unity.Mathematics.math.max(0f, avgBlackoutHours - vulnThreshold);
                float safeMaxHours = Unity.Mathematics.math.max(vulnMaxHours, 0.001f);
                float vulnRatio = Unity.Mathematics.math.min(excessHours / safeMaxHours * vulnBonus, vulnBonus);
                state.ValueRW.BlackoutVulnerabilityMult = 1.0f + vulnRatio;
            }
            else
            {
                state.ValueRW.AvgBlackoutHours = 0f;
                state.ValueRW.BlackoutVulnerabilityMult = 1.0f;
            }
        }
    }

    /// <summary>
    /// Burst job for aggregating PsyImpact statistics.
    /// Runs single-threaded to avoid race conditions on shared aggregation.
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct AggregateStatsJob : IJobEntity
    {
        public NativeReference<CognitiveStatsAggregation> Aggregation;

        // Lookup to skip homeless households (mod entity exists but household lost PropertyRenter)
        [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenterLookup;

        // Config values (from BalanceConfig.Cognitive)
        public float StressThreshold;
        public float InfectionThreshold;
        // FIX W2-M4: Was hardcoded 4f
        public float BlackoutVulnThreshold;

        public void Execute(in HouseholdPsyState psy)
        {
            // Skip homeless: mod entity exists but household no longer has PropertyRenter
            Entity household = psy.GetHouseholdEntity();
            // FIX L3: Entity.Null guard — HasComponent(Entity.Null) is undefined in Burst
            if (household == Entity.Null || !PropertyRenterLookup.HasComponent(household))
                return;

            // NOTE M4: NativeReference read-modify-write — safe only with Schedule (single-thread).
            // If changed to ScheduleParallel, must use atomic ops or per-chunk aggregation.
            var agg = Aggregation.Value;

            agg.HouseholdCount++;
            agg.TotalInfection += psy.InfectionLevel;
            agg.TotalResistance += psy.Resistance_Value;
            agg.TotalTrauma += psy.Trauma;

            // Switched from transient fields to persistent fields:
            // With PsySlot(16), only 1/16 entities have non-zero transients at any time.
            // Persistent fields are valid across all slots.
            if (psy.BlackoutHours > 0.001f)
                agg.BlackoutCount++;

            if (psy.HasImpactPressure)
                agg.ImpactCount++;

            if (psy.HasEnvyPressure)
                agg.EnvyCount++;

            if (psy.InfectionLevel > InfectionThreshold)
                agg.InfectedCount++;

            // CDI-7: Track blackout vulnerability
            // FIX W2-M4: Use config threshold (was hardcoded 4f)
            if (psy.BlackoutHours > BlackoutVulnThreshold)
            {
                agg.TotalBlackoutHours += psy.BlackoutHours;
                agg.VulnerableHouseholdCount++;
            }

            Aggregation.Value = agg;
        }
    }

    /// <summary>
    /// Intermediate aggregation struct for job.
    /// </summary>
    public struct CognitiveStatsAggregation
    {
        public int HouseholdCount;
        public float TotalInfection;
        public float TotalResistance;
        public float TotalTrauma;
        public int BlackoutCount;
        public int EnvyCount;
        public int ImpactCount;
        public int InfectedCount;

        // CDI-7: Blackout vulnerability aggregation
        public float TotalBlackoutHours;       // Sum of BlackoutHours for households > 4h
        public int VulnerableHouseholdCount;   // Count of households with BlackoutHours > 4h
    }

    /// <summary>
    /// Singleton component storing aggregated cognitive stats for UI.
    /// </summary>
    public struct CognitiveStatsState : IComponentData
    {
        public int TotalHouseholds;
        public float AvgInfectionLevel;
        public float AvgResistance;
        public float AvgTrauma;
        public int HouseholdsUnderBlackout;
        public int HouseholdsWithEnvy;
        public int HouseholdsUnderImpact;
        public int HouseholdsInfected;

        // CDI-7: Blackout vulnerability stats
        /// <summary>
        /// Average blackout hours for vulnerable households (those with >4h blackout).
        /// 0 if no households are vulnerable.
        /// </summary>
        public float AvgBlackoutHours;

        /// <summary>
        /// Number of households with extended blackout (>4h), making them propaganda-vulnerable.
        /// </summary>
        public int VulnerableHouseholds;

        /// <summary>
        /// Current blackout vulnerability multiplier (1.0 = no effect, 1.3 = +30% propaganda effectiveness).
        /// Calculated from average excess blackout hours of vulnerable households.
        /// </summary>
        public float BlackoutVulnerabilityMult;

        /// <summary>
        /// Ensure singleton entity exists.
        /// </summary>
        public static void EnsureExists(EntityManager em)
        {
            CivicSingleton.Ensure(em, new CognitiveStatsState { BlackoutVulnerabilityMult = 1.0f });
        }
    }
}
