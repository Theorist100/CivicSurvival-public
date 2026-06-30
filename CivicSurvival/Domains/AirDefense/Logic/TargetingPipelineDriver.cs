using System;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Jobs;
using CivicSurvival.Domains.AirDefense.Systems;

namespace CivicSurvival.Domains.AirDefense.Logic
{
    /// <summary>
    /// Drives the async N-1 targeting pipeline (Match → Residential → Scoring).
    /// Wraps <see cref="TargetingBufferStore"/>, <see cref="ThrottleHelper"/>, and the
    /// residential cache / policy / render-barrier dependencies that the orchestrator
    /// used to manage inline.
    ///
    /// Not a system — owned by <see cref="AirDefenseOrchestrator"/>. The orchestrator owns the
    /// ECS queries/handles/lookups (created in OnCreate, Updated each frame) and the main-thread
    /// AA collection; this driver owns scheduling (including the async CollectThreatJob), the
    /// buffer state machine, and the read/promote dance.
    /// </summary>
#pragma warning disable CA1001 // m_Buffers disposed in Dispose() — driver owns it
    internal sealed class TargetingPipelineDriver : IDisposable
#pragma warning restore CA1001
    {
        private readonly TargetingBufferStore m_Buffers = new();
        private ResidentialCacheSystem m_ResidentialCache = null!;
        private AirDefensePolicySystem m_PolicySystem = null!;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;
        private EntityQuery m_ThreatQuery;
        private int m_ThrottleFrames;
        private ThrottleHelper m_Throttle;

        public TargetingPipelineDriver(int throttleFrames)
        {
            m_ThrottleFrames = throttleFrames;
            m_Throttle = new ThrottleHelper(throttleFrames);
            m_Buffers.Initialize();
        }

        public void Wire(
            ResidentialCacheSystem residentialCache,
            AirDefensePolicySystem policySystem,
            IRenderWriteBarrier renderWriteBarrier,
            EntityQuery threatQuery)
        {
            m_ResidentialCache = residentialCache;
            m_PolicySystem = policySystem;
            m_RenderWriteBarrier = renderWriteBarrier;
            m_ThreatQuery = threatQuery;
        }

        public void Dispose()
        {
            m_Buffers.Dispose();
            CivicSurvival.Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(
                CivicSurvival.Core.Diagnostics.NativeCrashMarkers.AirDefenseTargetingPipeline);
        }

        public int AAWriteCount => m_Buffers.AAWriteCount;
        public bool HasPendingJob => m_Buffers.HasPendingJob;

        /// <summary>
        /// Upper bound on the live threat count, computed WITHOUT a sync point from chunk
        /// metadata: chunkCount * 128 (TypeManager.MaximumChunkCapacity — the hard per-chunk
        /// capacity ceiling). CalculateChunkCountWithoutFiltering does NOT call SyncFilterTypes
        /// (unlike CalculateEntityCount / IsEmpty, which drain the moving TMS graph on the main
        /// thread), so this is free of the spike the jobify removes. The multiplier is the hard
        /// 128 ceiling, never a single chunk's capacity — m_ThreatQuery is multi-archetype, so
        /// (sum of chunks over all archetypes) * 128 >= actual is a provable upper bound at any
        /// fragmentation. Buffers sized by this can over-allocate (harmless memory) but never
        /// under-allocate (which would be an AddNoResize AV).
        /// </summary>
        public int UpperBoundThreatCount => m_ThreatQuery.CalculateChunkCountWithoutFiltering() * 128;

        /// <summary>
        /// Consume the N-1 frame's targeting result without blocking the main thread.
        /// Returns false when no fire-control snapshot is available. <paramref name="completed"/>
        /// distinguishes "job finished but snapshot is empty" from "job is still pending".
        /// Caller must call <see cref="PromoteAfterProcess"/> only when
        /// <paramref name="completed"/> is true.
        /// </summary>
        public bool TryConsumePreviousSnapshot(out TargetingSnapshot snapshot, out bool completed)
        {
            snapshot = default;
            completed = false;
            if (!m_Buffers.HasPendingJob) return false;

            if (!m_Buffers.TryCompletePendingJobNonBlocking())
                return false;

            completed = true;
            snapshot = m_Buffers.GetSnapshot();
            return !snapshot.IsEmpty;
        }

        /// <summary>
        /// Always call after <see cref="TryConsumePreviousSnapshot"/> reports
        /// <c>completed == true</c> (whether or not fire control had work) — promotes
        /// the write frame into the read slot so the next schedule fills a fresh buffer.
        /// </summary>
        public void PromoteAfterProcess() => m_Buffers.PromoteWriteFrame();

        /// <summary>
        /// Refresh residential cache if its internal timer says so. Must be called
        /// in the safe window: after the prior job completed, before any new fill.
        /// </summary>
        public void RefreshResidentialIfPending() => m_ResidentialCache.RefreshIfPending();

        /// <summary>
        /// Throttle check + non-empty threat query check. True only when this frame is
        /// allowed to schedule a new targeting pipeline.
        /// </summary>
        public bool ShouldScheduleThisFrame()
        {
            // Use the no-sync chunk-count gate, NOT m_ThreatQuery.IsEmpty: IsEmpty calls
            // SyncFilterTypes (enableable ActiveThreat/PendingDestruction) → CompleteWriteDependency
            // = main-thread drain of the moving TMS graph. After the collect jobify this gate would
            // become the SOLE main-thread drain (the foreach that used to drain it is gone), so the
            // spike would just move here. CalculateChunkCountWithoutFiltering reads chunk metadata
            // without a sync. The gate is coarser (counts non-enabled/intercepted threats too) but
            // it only decides "schedule the pipeline?"; the precise filter (enabled mask +
            // IsIntercepted) runs inside CollectThreatJob, which yields an empty list when no real
            // target exists → deferred match/residential run on 0 elements (no-op).
            if (m_ThreatQuery.CalculateChunkCountWithoutFiltering() == 0)
            {
                CivicSurvival.Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(
                    CivicSurvival.Core.Diagnostics.NativeCrashMarkers.AirDefenseTargetingPipeline);
                return false;
            }

            return m_Throttle.ShouldUpdate();
        }

        /// <summary>
        /// Residential cache is populated on first frame after start; pipeline waits.
        /// </summary>
        public bool IsResidentialReady => m_ResidentialCache.IsReady;

        /// <summary>
        /// Open the fill phase: clear write buffers, consume a render-write ticket for
        /// building Transform reads. Caller invokes <see cref="AddAA"/> using the returned
        /// ticket as evidence of safe transform access; threats are collected asynchronously.
        /// <para>Ticket scope is <see cref="RenderWriteComponentMask.BuildingTransform"/>:
        /// there are no current civic out-of-band producers for building Transform, so
        /// <see cref="IRenderWriteBarrier.Consume"/> completes instantly (zero handles,
        /// no <c>Complete()</c> wait) while keeping the read behind the barrier contract
        /// for future producers.</para>
        /// </summary>
        public RenderWriteTicket BeginFill(Type ownerType)
        {
            m_Buffers.ClearWriteFrame();
            return m_RenderWriteBarrier.Consume(ownerType, RenderWriteComponentMask.BuildingTransform);
        }

        public void AddAA(in AAData aa) => m_Buffers.AddAA(in aa);

        /// <summary>
        /// Size the candidate buffer (and the collect/residential buffers) for the worst-case
        /// upperBound*A matches. Returns false when the buffer cannot be sized (overflow / failed
        /// grow); the caller must then abort the fill instead of scheduling, or the match job's
        /// AddNoResize would overrun. <paramref name="upperBoundThreatCount"/> is the no-sync
        /// chunk-metadata ceiling (see <see cref="UpperBoundThreatCount"/>) — the actual collected
        /// count is &lt;= this bound, so sizing here is always sufficient.
        /// </summary>
        public bool PrepareCandidates(int upperBoundThreatCount, int aaCount) =>
            m_Buffers.PrepareCandidateCapacity(upperBoundThreatCount, aaCount);

        /// <summary>
        /// Schedule the Match → Residential → Scoring chain on the current write frame
        /// using N-1 read buffers for scoring. Registers the final handle with the
        /// buffer store (Idle → Pending) and returns it for the caller to combine
        /// into its system Dependency.
        ///
        /// MUST schedule on the incoming ECS dependency, NOT `default`. The jobs touch
        /// only native containers, so it *looks* like default is safe — but in the
        /// production Burst build (DOTS_SAFETY stripped) there is no automatic
        /// safety-handle tracking for manually-created NativeContainers, so the passed
        /// handle is the ONLY ordering against the buffer-store's own producers/consumers
        /// across frames. Passing `default` raced and crashed as a native AV in
        /// lib_burst_generated (null backing pointer) — same failure mode as the earlier
        /// ToComponentDataListAsync `default` attempt. Consume is kept non-blocking by
        /// polling the final handle before Complete(), not by dropping this ordering.
        /// </summary>
        public JobHandle ScheduleJobChain(JobHandle dependency, in CollectThreatJobHandles collectHandles)
        {
            var views = m_Buffers.GetScheduleViews();
            var threatWriteList = m_Buffers.ThreatWriteList;

            // ================================================================
            // Phase 1: Collect Job (Burst IJobChunk) — fills threatWriteList on a worker.
            // The moving TMS writers reach this job through ADO's reader/writer lists (query +
            // lookups), so scheduling on the incoming `dependency` fences them on a worker, not
            // the main thread. Capacity was pre-sized to the upper bound in PrepareCandidates,
            // so AddNoResize never overruns.
            // ================================================================
            // PERF-LOCK: one coarse marker for the whole active targeting phase; no per-frame Clear after Complete.
            CivicSurvival.Core.Diagnostics.NativeCrashBreadcrumb.Mark(
                CivicSurvival.Core.Diagnostics.NativeCrashMarkers.AirDefenseTargetingPipeline);
            var collect = new CollectThreatJob
            {
                ShahedHandle = collectHandles.ShahedHandle,
                CombatHandle = collectHandles.CombatHandle,
                PositionHandle = collectHandles.PositionHandle,
                EntityHandle = collectHandles.EntityHandle,
                PriorityLookup = collectHandles.PriorityLookup,
                Threats = m_Buffers.ThreatWriteParallelWriter
            };
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre CollectThreatJob.ScheduleParallel threatCapacity={threatWriteList.Capacity} aaArr={views.AAArray.IsCreated}/{views.AAArray.Length} residentialFlags={views.ThreatIsOverResidentialWrite.IsCreated}/{views.ThreatIsOverResidentialWrite.Length} lastCandidates={m_Buffers.LastCandidateCount}");
            JobHandle collectHandle = collect.ScheduleParallel(m_ThreatQuery, dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info("[BURSTMARK] post CollectThreatJob.ScheduleParallel");

            // ================================================================
            // Phase 2: Match Job (Burst Parallel) — DEFERRED count over the collected threats.
            // count is resolved from threatWriteList.Length on the worker after collect finishes;
            // data is the same list as a deferred array.
            // ================================================================
            var matchJob = new ThreatAAMatchJob
            {
                Threats = threatWriteList.AsDeferredJobArray(),
                AAs = views.AAArray,
                Candidates = views.CandidateParallelWriter
            };
            // BURSTMARK crash-1 candidate (worker targeting). Container state before the AV.
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ThreatAAMatchJob.Schedule threatCapacity={threatWriteList.Capacity} aaArr={views.AAArray.IsCreated}/{views.AAArray.Length} residentialFlags={views.ThreatIsOverResidentialWrite.IsCreated}/{views.ThreatIsOverResidentialWrite.Length} lastCandidates={m_Buffers.LastCandidateCount}");
            var matchHandle = matchJob.Schedule(threatWriteList, 8, collectHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ThreatAAMatchJob.Schedule aaArr={views.AAArray.IsCreated}/{views.AAArray.Length}");

            // ================================================================
            // Phase 3: Residential Check (Burst Parallel) — DEFERRED count, reads ThreatData.Position
            // ================================================================
            var config = BalanceConfig.Current;
            float radiusSq = config.Threats.DebrisCheckRadius * config.Threats.DebrisCheckRadius;
            var residentialPositions = m_ResidentialCache.ResidentialPositions;

            var residentialJob = new ResidentialCheckJob
            {
                Threats = threatWriteList.AsDeferredJobArray(),
                ResidentialPositions = residentialPositions,
                RadiusSquared = radiusSq,
                BallisticSkipAltitude = config.AirDefense.BallisticSkipAltitude,
                Results = views.ThreatIsOverResidentialWrite
            };
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre ResidentialCheckJob.Schedule threatCapacity={threatWriteList.Capacity} residentialPositions={residentialPositions.IsCreated}/{residentialPositions.Length} results={views.ThreatIsOverResidentialWrite.IsCreated}/{views.ThreatIsOverResidentialWrite.Length} radiusSq={radiusSq}");
            var residentialHandle = residentialJob.Schedule(threatWriteList, 8, matchHandle);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post ResidentialCheckJob.Schedule residentialPositions={residentialPositions.IsCreated}/{residentialPositions.Length} results={views.ThreatIsOverResidentialWrite.IsCreated}/{views.ThreatIsOverResidentialWrite.Length}");

            // ================================================================
            // Phase 4: Scoring Job (Burst Parallel) — DEFERRED COUNT
            // Uses LastCandidateCount from N-1 frame to avoid blocking.
            // Trade-off: +1 frame latency, but zero blocking in hot path.
            // ================================================================
            // H11: cache once — CompleteAndSwap between two reads would give mismatched count/array size → crash
            int candidateCount = m_Buffers.LastCandidateCount;
            if (candidateCount == 0)
            {
                m_Buffers.ClearScoredCandidates();
                m_Buffers.BeginPending(residentialHandle);
                return residentialHandle;
            }

            // Copy N-1 candidates into scored buffer; ScoringJob will score them in-place
            m_Buffers.CommitScoredCandidates(candidateCount);

            var scoringConfig = new ScoringConfig
            {
                ResidentialRadiusSq = radiusSq,
                Policy = m_PolicySystem?.CurrentPolicy ?? DefensePolicy.HumanitarianShield,
                CriticalDistance = config.AirDefense.CriticalDistance
            };

            // C10 FIX: read ScoredCandidatesArray AFTER CommitScoredCandidates — Commit may
            // Dispose+realloc m_ScoredCandidates, invalidating the pointer captured in views.
            var scoredCandidates = m_Buffers.ScoredCandidatesArray;
            var scoringJob = new EngagementScoringJob
            {
                Threats = views.ThreatDataRead,
                ThreatIsOverResidential = views.ThreatIsOverResidentialRead,
                Config = scoringConfig,
                Candidates = scoredCandidates,
                CandidateCount = candidateCount // actual count, not Candidates.Length
            };

            // Scoring reads N-1 buffers only — no dependency on match/residential handles.
            // However, TryCompletePendingJobNonBlocking() completes finalHandle before
            // fire-control consume and PromoteWriteFrame() swaps afterward. If finalHandle
            // excluded residentialHandle, match+residential jobs may still be writing to
            // m_CandidatesWrite / m_ThreatIsOverResidentialWrite when the swap occurs — next
            // frame reads partially-written data (data race).
            // Fix: combine so the swap only runs once both chains have finished.
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] pre EngagementScoringJob.Schedule candidates={candidateCount} threatsRead={views.ThreatDataRead.IsCreated}/{views.ThreatDataRead.Length} residentialRead={views.ThreatIsOverResidentialRead.IsCreated}/{views.ThreatIsOverResidentialRead.Length} scoredCandidates={scoredCandidates.IsCreated}/{scoredCandidates.Length}");
            var scoringHandle = scoringJob.Schedule(candidateCount, 16, dependency);
            if (Core.Diagnostics.BurstLogBootstrap.MarkersEnabled)
                CivicSurvival.Core.Diagnostics.BurstLogBootstrap.Info($"[BURSTMARK] post EngagementScoringJob.Schedule candidates={candidateCount} threatsRead={views.ThreatDataRead.IsCreated}/{views.ThreatDataRead.Length} residentialRead={views.ThreatIsOverResidentialRead.IsCreated}/{views.ThreatIsOverResidentialRead.Length} scoredCandidates={scoredCandidates.IsCreated}/{scoredCandidates.Length}");

            var finalHandle = JobHandle.CombineDependencies(residentialHandle, scoringHandle);
            m_Buffers.BeginPending(finalHandle);
            return finalHandle;
        }

        public void ResetThrottle() => m_Throttle = new ThrottleHelper(m_ThrottleFrames);

        public void ForceComplete()
        {
            m_Buffers.ForceComplete();
            CivicSurvival.Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(
                CivicSurvival.Core.Diagnostics.NativeCrashMarkers.AirDefenseTargetingPipeline);
        }
        public void ClearGenerations() => m_Buffers.ClearGenerations();
        public void AbortFillAndInvalidate()
        {
            m_Buffers.ForceComplete();
            m_Buffers.ClearGenerations();
            CivicSurvival.Core.Diagnostics.NativeCrashBreadcrumb.ClearIfCurrent(
                CivicSurvival.Core.Diagnostics.NativeCrashMarkers.AirDefenseTargetingPipeline);
        }
        public void ClearScoredCandidates() => m_Buffers.ClearScoredCandidates();

        /// <summary>
        /// Validate that an incoming ticket covers the components the caller is about
        /// to access. Helper exposed so the orchestrator's main-thread AA collection
        /// (which reads building Transform) can guard its reads.
        /// </summary>
        public static void EnsureRenderTicket(RenderWriteTicket ticket, RenderWriteComponentMask requiredMask)
        {
            if (!ticket.Covers(requiredMask))
                throw new InvalidOperationException($"Render write ticket does not cover {requiredMask}");
        }
    }

    /// <summary>
    /// Inputs the orchestrator hands to <see cref="TargetingPipelineDriver.ScheduleJobChain"/>
    /// for the async <see cref="CollectThreatJob"/>: the ECS type handles (updated this frame in
    /// the orchestrator) plus the PriorityTarget lookup. Kept on the orchestrator (the system)
    /// because handles/lookups must be created in OnCreate and Updated in OnUpdate; the driver is
    /// not a system and cannot own them.
    /// </summary>
    internal readonly struct CollectThreatJobHandles
    {
        public readonly ComponentTypeHandle<Shahed> ShahedHandle;
        public readonly ComponentTypeHandle<ShahedCombatState> CombatHandle;
        public readonly ComponentTypeHandle<ThreatPosition> PositionHandle;
        public readonly EntityTypeHandle EntityHandle;
        public readonly ComponentLookup<PriorityTarget> PriorityLookup;

        public CollectThreatJobHandles(
            ComponentTypeHandle<Shahed> shahedHandle,
            ComponentTypeHandle<ShahedCombatState> combatHandle,
            ComponentTypeHandle<ThreatPosition> positionHandle,
            EntityTypeHandle entityHandle,
            ComponentLookup<PriorityTarget> priorityLookup)
        {
            ShahedHandle = shahedHandle;
            CombatHandle = combatHandle;
            PositionHandle = positionHandle;
            EntityHandle = entityHandle;
            PriorityLookup = priorityLookup;
        }
    }
}
