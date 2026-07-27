using Game.Simulation;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Shared scheduling barrier for intercept ECB playback.
    ///
    /// AirDefense-domain producers (AirDefenseOrchestrator, BallisticDefenseSystem)
    /// emit InterceptRequest commands into this barrier; InterceptProcessingSystem
    /// consumes them via RegisterAfter(typeof(InterceptBarrier))]. Lives in
    /// CoreKernel because it is a cross-feature scheduling anchor — owning it from
    /// any gated coordinator (e.g. ThreatsAirDefense) would create an implicit
    /// runtime cycle: AirDefense systems would Require a system whose registration
    /// depends on AirDefense itself being open. Per FEATURE_MODULE_ARCHITECTURE
    /// Rule 4, scheduling anchors stay in Core so dependent features schedule
    /// deterministically regardless of coordinator state.
    ///
    /// Mirrors the vanilla <c>Game.EndFrameBarrier</c> protection: the base
    /// <see cref="EntityCommandBufferSystem"/> only completes its internal producer
    /// handle when <c>PendingBuffers</c> is non-empty, so callers that register a
    /// handle on a tick where no producer wrote an ECB would leak a growing
    /// <c>JobHandle.CombineDependencies</c> chain. Intercept producers are gated
    /// on rare events (m_InterceptFiredThisFrame, result.InterceptCommands &gt; 0),
    /// so PendingBuffers is typically empty — this protection is required.
    /// </summary>
    [FrameworkSystem]
    public partial class InterceptBarrier : EntityCommandBufferSystem
    {
        private JobHandle m_LocalProducerHandle;

        public new void AddJobHandleForProducer(JobHandle producerJob)
        {
            // PERF-LOCK: do NOT add `base.AddJobHandleForProducer(producerJob);` here.
            // Vanilla EntityCommandBufferSystem.FlushPendingBuffers (decompile
            // Unity.Entities/EntityCommandBufferSystem.cs:56-64) early-returns when
            // PendingBuffers is empty, so base.m_ProducerHandle leaks on lazy ticks.
            // Intercept producers fire only on rare events (m_InterceptFiredThisFrame,
            // result.InterceptCommands > 0) → PendingBuffers is empty most ticks.
            // Mirror vanilla Game.EndFrameBarrier (Game/EndFrameBarrier.cs:45-48).
            // Enforced by CIVIC488.
            m_LocalProducerHandle = JobHandle.CombineDependencies(m_LocalProducerHandle, producerJob);
        }

        protected override void OnUpdate()
        {
            // PERF-LOCK: unconditional Complete — see AddJobHandleForProducer above. This now
            // drains ONLY the intercept-ECB producers' own Dependency (AirDefenseOrchestrator,
            // BallisticDefenseSystem — cheap lookup-update jobs). The ThreatMovementSystem
            // ballistic-movement piggy-back was removed: that job is folded into TMS.Dependency,
            // so the ECS dependency manager self-completes it on any structural change
            // (EntityDataAccess.BeforeStructuralChange → CompleteAllJobsAndInvalidateArrays). The
            // old piggy-back force-completed the ballistic writer here every frame — including
            // no-intercept frames where base.OnUpdate early-returns on empty PendingBuffers —
            // which became an 18-24ms/frame main-thread stall once Patriots flew. Do NOT re-add a
            // movement/ballistic handle to this barrier; keep such writers in their system's
            // Dependency instead.
            m_LocalProducerHandle.Complete();
            m_LocalProducerHandle = default;
            base.OnUpdate();
        }
    }
}
