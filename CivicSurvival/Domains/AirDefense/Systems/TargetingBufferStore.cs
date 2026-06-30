using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Diagnostics;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Jobs;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Owns the double-buffered targeting memory and N-1 pipeline state machine.
    /// Owned exclusively by AirDefenseOrchestrator.
    ///
    /// API is split by phase:
    /// - Fill phase: ClearWriteFrame / AddAA / PrepareCandidateCapacity. Threats are filled
    ///   asynchronously by CollectThreatJob into ThreatWriteList; the orchestrator never adds
    ///   threats on the main thread.
    /// - Schedule phase (narrow): GetScheduleViews() → TargetingScheduleViews.
    ///   All AsArray() / AsParallelWriter() calls are concentrated in GetScheduleViews().
    /// - Consume phase: GetSnapshot() → TargetingSnapshot (zero-copy readonly view).
    /// </summary>
    internal sealed class TargetingBufferStore : IDisposable
    {
        private static readonly LogContext Log = new("TargetingBufferStore");

        // Count of fill passes refused because the candidate buffer could not be sized to
        // cover the worst-case T*A matches. 0 in normal play; non-zero means a regression
        // (formula change / overflow / stale count) tripped the release-safe capacity guard.
        private static int s_AbortedCapacityCount;
        public static int AbortedCapacityCount => System.Threading.Volatile.Read(ref s_AbortedCapacityCount);
        public static void ResetCounters() => System.Threading.Interlocked.Exchange(ref s_AbortedCapacityCount, 0);
        private const int CAPACITY_ABORT_LOG_THROTTLE = 60;

        // Double-buffered lists (eager allocation in Initialize)
#pragma warning disable CIVIC023 // Dispose() called in IDisposable.Dispose() — not an ECS system with OnDestroy
        private NativeList<AAData> m_AADataWrite;
        private NativeList<AAData> m_AADataRead;
        private NativeList<ThreatData> m_ThreatDataWrite;
        private NativeList<ThreatData> m_ThreatDataRead;
        private NativeList<EngagementCandidate> m_CandidatesWrite;
        private NativeList<EngagementCandidate> m_CandidatesRead;

        // Lazy-allocated arrays (size determined at first PrepareCandidateCapacity call)
#pragma warning disable CIVIC278 // NativeArray has no Clear(); guarded by count fields and lazy reallocation
        private NativeArray<bool> m_ThreatIsOverResidentialWrite;
        private NativeArray<bool> m_ThreatIsOverResidentialRead;

        // Scored candidates (lazy, outside swap cycle — ScoringJob writes here in-place)
        private NativeArray<EngagementCandidate> m_ScoredCandidates;
#pragma warning restore CIVIC278
#pragma warning restore CIVIC023

        // Counts
        private int m_LastCandidateCount;
        private int m_ScoredCandidateCount;

        // Pending job state
        private JobHandle m_TargetingJobHandle;
        private bool m_HasPendingJob;

        // ============================================================================
        // STATE
        // ============================================================================

        public bool HasPendingJob => m_HasPendingJob;
        public int LastCandidateCount => m_LastCandidateCount;

        /// <summary>
        /// Current scored candidates array. Must be read AFTER CommitScoredCandidates
        /// to avoid use-after-free when Commit reallocates the buffer.
        /// </summary>
        public NativeArray<EngagementCandidate> ScoredCandidatesArray => m_ScoredCandidates;

        // ============================================================================
        // FILL PHASE (closed — orchestrator never gets raw NativeList)
        // Must not be called when HasPendingJob.
        // ============================================================================

        /// <summary>Clear all write-side lists. Call once at the start of each fill pass.</summary>
        public void ClearWriteFrame()
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "ClearWriteFrame called while job pending");
            m_AADataWrite.Clear();
            m_ThreatDataWrite.Clear();
        }

        /// <summary>Append an AA entry to the write buffer.</summary>
        public void AddAA(in AAData aa)
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "AddAA called while job pending");
            m_AADataWrite.Add(aa);
        }

        /// <summary>
        /// Threat write buffer for the async collector. CollectThreatJob writes here via
        /// <see cref="ThreatWriteParallelWriter"/> (AddNoResize), and the deferred match/
        /// residential jobs read its length on the worker — the main thread never reads it.
        /// Capacity is pre-sized to the upper bound in <see cref="PrepareCandidateCapacity"/>.
        /// </summary>
        public NativeList<ThreatData> ThreatWriteList => m_ThreatDataWrite;

        /// <summary>Parallel writer for CollectThreatJob (AddNoResize into the pre-sized list).</summary>
        public NativeList<ThreatData>.ParallelWriter ThreatWriteParallelWriter => m_ThreatDataWrite.AsParallelWriter();

        /// <summary>Current AA write count. Valid after AddAA calls, before BeginPending.</summary>
        public int AAWriteCount => m_AADataWrite.Length;

        /// <summary>
        /// Finalize the fill phase: grow NativeArrays to fit threatCount, copy threat positions
        /// from the filled write buffer, and resize the candidates list.
        /// Must be called after all AddThreat / AddAA calls and before GetScheduleViews.
        /// Must not be called when HasPendingJob.
        /// Read buffers are never resized here — they grow implicitly on next swap.
        /// Capacity = T*A*2 — 2x safety factor so AddNoResize never throws.
        ///
        /// Returns false when the candidate buffer cannot be sized to cover the worst-case
        /// T*A matches (negative inputs, int overflow, or a capacity grow that did not take).
        /// The caller MUST abort scheduling on false — proceeding would let the match job's
        /// AddNoResize write past the buffer. This is the release-safe replacement for the
        /// old Debug.Assert, which is stripped from production builds and guards nothing there.
        /// </summary>
        public bool PrepareCandidateCapacity(int upperBoundThreatCount, int aaCount)
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "PrepareCandidateCapacity called while job pending");

            // upperBoundThreatCount is the chunk-metadata ceiling (chunkCount * 128), computed
            // without a sync point. The async CollectThreatJob fills m_ThreatDataWrite with
            // actual <= upperBound threats AFTER this sizing, so every buffer here is sized to
            // the upper bound: match's AddNoResize on candidates (upperBound * aaCount), the
            // residential output, and the collect output itself. The main thread cannot know the
            // actual count at sizing time — over-sizing is harmless (memory), under-sizing is an AV.

            // Release-safe input guard: worst-case matches is T*A (one AddNoResize per
            // threat-AA pair). Negative inputs or a product past int.MaxValue/2 cannot be
            // covered by an int-capacity NativeList, so refuse the pass rather than overrun.
            if (upperBoundThreatCount < 0 || aaCount < 0)
                return AbortCapacity(upperBoundThreatCount, aaCount, 0);
            long required = (long)upperBoundThreatCount * aaCount;
            if (required > int.MaxValue / 2)
                return AbortCapacity(upperBoundThreatCount, aaCount, required);

            // Collect output must hold every collected threat (AddNoResize in CollectThreatJob).
            // Capacity >= upperBound guarantees the worker never overruns.
            if (m_ThreatDataWrite.Capacity < upperBoundThreatCount)
                m_ThreatDataWrite.Capacity = upperBoundThreatCount;

            if (!m_ThreatIsOverResidentialWrite.IsCreated || m_ThreatIsOverResidentialWrite.Length < upperBoundThreatCount)
            {
                if (m_ThreatIsOverResidentialWrite.IsCreated) m_ThreatIsOverResidentialWrite.Dispose();
                m_ThreatIsOverResidentialWrite = default;
                m_ThreatIsOverResidentialWrite = new NativeArray<bool>(upperBoundThreatCount + 32, Allocator.Persistent, NativeArrayOptions.ClearMemory); // H9: zero trailing slots — stale bools cause wrong engagement scores
            }

            m_CandidatesWrite.Clear();
            // Capacity must be ≥ T*A (maximum possible matches, one per threat-AA pair).
            // 2x safety factor handles NativeList internal alignment; clamp guards int overflow
            // for pathological inputs (T*A > ~536M is unreachable in CS2, but invariant is verified).
            int maxCandidates = math.max((int)math.min(required * 2, int.MaxValue / 2), 64);
            if (m_CandidatesWrite.Capacity < maxCandidates)
                m_CandidatesWrite.Capacity = maxCandidates;
            // Release-safe invariant verification: capacity must cover worst-case AddNoResize
            // in ThreatAAMatchJob. With the overflow guard above this can only fail if a grow
            // silently did not take; treat it as a hard abort, not an assert that disappears
            // in production.
            if (m_CandidatesWrite.Capacity < required)
                return AbortCapacity(upperBoundThreatCount, aaCount, required);

            // Footprint diagnostic: publish the (never-shrinking) write-buffer byte sizes. Reads
            // .Capacity only — main-thread allocation metadata, no job-completing sync point.
            NativeFootprintTracker.ReportTargetingThreat(
                (long)m_ThreatDataWrite.Capacity * UnsafeUtility.SizeOf<ThreatData>());
            NativeFootprintTracker.ReportTargetingCandidate(
                (long)m_CandidatesWrite.Capacity * UnsafeUtility.SizeOf<EngagementCandidate>());

            return true;
        }

        /// <summary>
        /// Log (throttled) and refuse a fill pass whose candidate buffer cannot cover the
        /// worst-case T*A matches. The caller treats false as "skip scheduling this frame".
        /// </summary>
        private static bool AbortCapacity(int threatCount, int aaCount, long required)
        {
            int total = System.Threading.Interlocked.Increment(ref s_AbortedCapacityCount);
            if (total == 1 || total % CAPACITY_ABORT_LOG_THROTTLE == 0)
                Log.Error($"Targeting candidate capacity cannot cover worst case: threats={threatCount} aa={aaCount} required={required} (int.MaxValue/2={int.MaxValue / 2}) — skipping pass to avoid AddNoResize overrun. total={AbortedCapacityCount}");
            return false;
        }

        // ============================================================================
        // SCHEDULE PHASE — narrow view via TargetingScheduleViews
        // All AsArray() / AsParallelWriter() calls are concentrated in GetScheduleViews().
        // ============================================================================

        /// <summary>
        /// Return a narrow view of the current write + N-1 read buffers for job scheduling.
        /// All AsArray() and AsParallelWriter() escape hatches are concentrated here.
        /// Valid only between PrepareCandidateCapacity() and BeginPending().
        /// Must not be called when HasPendingJob.
        /// Do not store in fields — use as local variable only.
        /// </summary>
        public TargetingScheduleViews GetScheduleViews()
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "GetScheduleViews called while job pending");
            return new TargetingScheduleViews(
                m_AADataWrite.AsArray(),
                m_CandidatesWrite.AsParallelWriter(),
                m_ThreatIsOverResidentialWrite,
                m_ThreatDataRead.AsArray(),
                m_ThreatIsOverResidentialRead,
                m_ScoredCandidates.IsCreated ? m_ScoredCandidates : default
            );
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        public void Initialize()
        {
            m_AADataWrite = new NativeList<AAData>(64, Allocator.Persistent);
            m_AADataRead = new NativeList<AAData>(64, Allocator.Persistent);
            m_ThreatDataWrite = new NativeList<ThreatData>(256, Allocator.Persistent);
            m_ThreatDataRead = new NativeList<ThreatData>(256, Allocator.Persistent);
            m_CandidatesWrite = new NativeList<EngagementCandidate>(1024, Allocator.Persistent);
            m_CandidatesRead = new NativeList<EngagementCandidate>(1024, Allocator.Persistent);
            // NativeArrays are lazy-allocated in PrepareCandidateCapacity
        }

        public void Dispose()
        {
            if (m_HasPendingJob) m_TargetingJobHandle.Complete();

            if (m_AADataWrite.IsCreated) m_AADataWrite.Dispose();
            if (m_AADataRead.IsCreated) m_AADataRead.Dispose();
            if (m_ThreatDataWrite.IsCreated) m_ThreatDataWrite.Dispose();
            if (m_ThreatDataRead.IsCreated) m_ThreatDataRead.Dispose();
            if (m_CandidatesWrite.IsCreated) m_CandidatesWrite.Dispose();
            if (m_CandidatesRead.IsCreated) m_CandidatesRead.Dispose();
            if (m_ThreatIsOverResidentialWrite.IsCreated) m_ThreatIsOverResidentialWrite.Dispose();
            if (m_ThreatIsOverResidentialRead.IsCreated) m_ThreatIsOverResidentialRead.Dispose();
            if (m_ScoredCandidates.IsCreated) m_ScoredCandidates.Dispose();
        }

        /// <summary>
        /// Targeting zone reset only. Completes pending job, clears lists, resets counts.
        /// Non-targeting state (stats, timers, residential) stays in orchestrator.
        /// m_ScoredCandidates is disposed (not just cleared) — matches pre-refactor ResetState semantics.
        /// NativeArrays retain capacity and are not disposed on reset.
        /// </summary>
        public void ResetFrameState()
        {
            if (m_HasPendingJob)
            {
                m_TargetingJobHandle.Complete();
                m_HasPendingJob = false;
            }

            m_AADataWrite.Clear();
            m_ThreatDataWrite.Clear();
            m_CandidatesWrite.Clear();
            m_AADataRead.Clear();
            m_ThreatDataRead.Clear();
            m_CandidatesRead.Clear();

            m_LastCandidateCount = 0;
            m_ScoredCandidateCount = 0;

            if (m_ScoredCandidates.IsCreated) m_ScoredCandidates.Dispose();
        }

        // ============================================================================
        // PHASE OPERATIONS
        // ============================================================================

        /// <summary>
        /// Copy N-1 read-side candidates into scored storage and record count.
        /// count must equal LastCandidateCount (completed read-side count after CompleteAndSwap).
        /// ScoringJob will write scores in-place into TargetingScheduleViews.ScoredCandidatesWritable.
        /// Must not be called when HasPendingJob.
        /// </summary>
        public void CommitScoredCandidates(int count)
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "CommitScoredCandidates called while job pending");
            UnityEngine.Debug.Assert(count >= 0, "CommitScoredCandidates: count < 0");
            UnityEngine.Debug.Assert(count <= m_CandidatesRead.Length, "CommitScoredCandidates: count > CandidatesRead.Length");

            int targetLength = count + 32;
            // R9-M4: Shrink when using < 25% of capacity (avoids session-long waste after large waves)
            bool needsRealloc = !m_ScoredCandidates.IsCreated
                || m_ScoredCandidates.Length < count
                || (m_ScoredCandidates.Length > 64 && count < m_ScoredCandidates.Length / 4);
            if (needsRealloc)
            {
                if (m_ScoredCandidates.IsCreated) m_ScoredCandidates.Dispose();
                m_ScoredCandidates = default;
                m_ScoredCandidates = new NativeArray<EngagementCandidate>(targetLength, Allocator.Persistent);
            }

            int copyCount = math.min(count, m_CandidatesRead.Length); // guard OOB if read buffer was reallocated
            NativeArray<EngagementCandidate>.Copy(m_CandidatesRead.AsArray(), m_ScoredCandidates, copyCount);
            m_ScoredCandidateCount = copyCount;
        }

        /// <summary>
        /// Reset scored candidate count when no scoring job is scheduled this frame.
        /// Called on the no-candidates-last-frame path before BeginPending.
        /// </summary>
        public void ClearScoredCandidates() => m_ScoredCandidateCount = 0;

        /// <summary>
        /// Register a scheduled targeting job handle. Idle → Pending.
        /// </summary>
        public void BeginPending(JobHandle handle)
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "BeginPending called while already pending");
            m_TargetingJobHandle = handle;
            m_HasPendingJob = true;
        }

        /// <summary>
        /// Complete pending job without swap. Used in early-exit, ResetFrameState, Dispose paths.
        /// No-op if no job is pending.
        /// </summary>
        public void ForceComplete()
        {
            if (!m_HasPendingJob) return;
            m_TargetingJobHandle.Complete();
            m_HasPendingJob = false;
        }

        /// <summary>
        /// Drop both generations after an early-exit boundary. Used when no AA exists:
        /// old read-side candidates are no longer semantically valid for future AA.
        /// </summary>
        public void ClearGenerations()
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "ClearGenerations called while job pending");

            m_AADataWrite.Clear();
            m_ThreatDataWrite.Clear();
            m_CandidatesWrite.Clear();
            m_AADataRead.Clear();
            m_ThreatDataRead.Clear();
            m_CandidatesRead.Clear();

            m_LastCandidateCount = 0;
            m_ScoredCandidateCount = 0;
        }

        /// <summary>
        /// Complete pending targeting jobs only after workers have already finished.
        /// Returns false while the chain is still draining; callers must not consume or
        /// promote buffers in that case.
        /// </summary>
        public bool TryCompletePendingJobNonBlocking()
        {
            UnityEngine.Debug.Assert(m_HasPendingJob, "TryCompletePendingJobNonBlocking called but no pending job");

            if (!m_TargetingJobHandle.IsCompleted)
                return false;

            m_TargetingJobHandle.Complete(); // release safety state; should not block after IsCompleted
            m_HasPendingJob = false;
            return true;
        }

        /// <summary>
        /// Capture LastCandidateCount and swap the completed write frame into read.
        /// Call after fire-control consumes the pre-swap read frame.
        /// LastCandidateCount is captured from CandidatesWrite.Length BEFORE swap.
        /// </summary>
        public void PromoteWriteFrame()
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "PromoteWriteFrame called while job pending");
            // Capture N-1 count BEFORE swap (CandidatesWrite holds current-frame filled data)
            m_LastCandidateCount = m_CandidatesWrite.Length;

            (m_AADataRead, m_AADataWrite) = (m_AADataWrite, m_AADataRead);
            (m_ThreatDataRead, m_ThreatDataWrite) = (m_ThreatDataWrite, m_ThreatDataRead);
            (m_CandidatesRead, m_CandidatesWrite) = (m_CandidatesWrite, m_CandidatesRead);
            (m_ThreatIsOverResidentialRead, m_ThreatIsOverResidentialWrite) =
                (m_ThreatIsOverResidentialWrite, m_ThreatIsOverResidentialRead);
        }

        /// <summary>
        /// Get read-only snapshot of completed targeting result for fire-control consume phase.
        /// Valid only until next PromoteWriteFrame() or ResetFrameState().
        /// Do not store in fields — use as local variable only.
        /// Must be called when !HasPendingJob.
        /// </summary>
        public TargetingSnapshot GetSnapshot()
        {
            UnityEngine.Debug.Assert(!m_HasPendingJob, "GetSnapshot called while job pending");
            return new TargetingSnapshot(
                m_ThreatDataRead.AsArray().AsReadOnly(),
                m_AADataRead.AsArray().AsReadOnly(),
                m_ScoredCandidates.IsCreated ? m_ScoredCandidates : default,
                m_ScoredCandidateCount
            );
        }
    }

    /// <summary>
    /// Narrow view of write + N-1 read buffers for job scheduling.
    /// All AsArray() and AsParallelWriter() calls are concentrated in
    /// TargetingBufferStore.GetScheduleViews() — not scattered across the orchestrator.
    /// Valid only between PrepareCandidateCapacity() and BeginPending().
    /// Do not store in fields — use as local variable only.
    /// </summary>
    internal readonly struct TargetingScheduleViews
    {
        /// <summary>Write-side AA data for ThreatAAMatchJob.</summary>
        public readonly NativeArray<AAData> AAArray;

        /// <summary>Write-side candidates parallel writer for ThreatAAMatchJob output.</summary>
        public readonly NativeList<EngagementCandidate>.ParallelWriter CandidateParallelWriter;

        /// <summary>Write-side residential flags for ResidentialCheckJob output.</summary>
        public readonly NativeArray<bool> ThreatIsOverResidentialWrite;

        /// <summary>N-1 threat data as mutable array for EngagementScoringJob.</summary>
        public readonly NativeArray<ThreatData> ThreatDataRead;

        /// <summary>N-1 residential flags for EngagementScoringJob.</summary>
        public readonly NativeArray<bool> ThreatIsOverResidentialRead;

        /// <summary>Scored candidates for EngagementScoringJob (writes scores in-place).</summary>
        public readonly NativeArray<EngagementCandidate> ScoredCandidatesWritable;

        public TargetingScheduleViews(
            NativeArray<AAData> aaArray,
            NativeList<EngagementCandidate>.ParallelWriter candidateParallelWriter,
            NativeArray<bool> threatIsOverResidentialWrite,
            NativeArray<ThreatData> threatDataRead,
            NativeArray<bool> threatIsOverResidentialRead,
            NativeArray<EngagementCandidate> scoredCandidatesWritable)
        {
            AAArray = aaArray;
            CandidateParallelWriter = candidateParallelWriter;
            ThreatIsOverResidentialWrite = threatIsOverResidentialWrite;
            ThreatDataRead = threatDataRead;
            ThreatIsOverResidentialRead = threatIsOverResidentialRead;
            ScoredCandidatesWritable = scoredCandidatesWritable;
        }
    }

    /// <summary>
    /// Zero-copy view of completed targeting result for one frame of fire control.
    /// Valid only until next PromoteWriteFrame() or ResetFrameState() on the owning TargetingBufferStore.
    /// Do not store in fields — use as local variable only.
    /// </summary>
    internal readonly struct TargetingSnapshot
    {
        /// <summary>N-1 threat data (read buffer after swap).</summary>
        public readonly NativeArray<ThreatData>.ReadOnly Threats;

        /// <summary>N-1 AA data (read buffer after swap).</summary>
        public readonly NativeArray<AAData>.ReadOnly AAs;

        /// <summary>Scored engagement candidates (written by ScoringJob in-place).</summary>
        public readonly NativeArray<EngagementCandidate> ScoredCandidates;

        /// <summary>Actual candidate count (semantic count, not buffer length).</summary>
        public readonly int CandidateCount;

        public bool IsEmpty => CandidateCount == 0;

        public TargetingSnapshot(
            NativeArray<ThreatData>.ReadOnly threats,
            NativeArray<AAData>.ReadOnly aas,
            NativeArray<EngagementCandidate> scoredCandidates,
            int candidateCount)
        {
            Threats = threats;
            AAs = aas;
            ScoredCandidates = scoredCandidates;
            CandidateCount = candidateCount;
        }
    }
}
