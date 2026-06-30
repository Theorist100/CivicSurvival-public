using CivicSurvival.Core.Attributes;
using Game;
using Unity.Jobs;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// GameSimulation ECB playback barrier for threat-entity non-structural-on-drone operations.
    ///
    /// Hosts ECB producers that mutate threat-entity enable-bits / lifecycle from GameSimulation:
    /// SetComponentEnabled&lt;ActiveThreat&gt;(false), SetComponentEnabled&lt;PendingDestruction&gt;
    /// (true), SetComponentEnabled&lt;PendingThreatDeletion&gt;(true), plus renderless creates.
    /// These flips are render-safe to play back here for two reasons: (1) an enable-bit flip
    /// does NOT migrate a chunk, so it cannot pull a chunk out from under the in-flight render
    /// job; (2) <c>DroneRenderWriteJob</c> queries only <c>ThreatPosition</c> — it does NOT read
    /// any of these enableable tags (RACE-SAFETY INVARIANT in DroneRenderWriteJob.cs, CIVIC508),
    /// so the playback write and the render read touch disjoint chunk slots. The render-archetype
    /// structural changes (drone spawn, drone Add&lt;Deleted&gt;) DO migrate chunks and therefore
    /// live in the Modification4 consumers behind RenderWriteBarrier.Consume instead.
    ///
    /// The render handle (<c>ThreatMovementSystem.m_RenderJobHandle</c>) is NO LONGER registered
    /// here. Render-job completion moved to render→render self-sync (TMS STEP 5-RENDER) plus the
    /// Modification4 render-completion gate (<c>ThreatSpawnApplySystem</c> /
    /// <c>ThreatDeletionApplySystem</c> call <c>RenderWriteBarrier.Consume</c> before any drone
    /// structural change). The same-frame barrier <c>Complete()</c> that used to force the render
    /// job in GameSimulation is gone — it starved the GPU (workers got no time before the
    /// main-thread block). See Docs/Plans/DroneRenderAsync/COMMIT_PLAN.md §4.
    ///
    /// The shadow handle below remains: it mirrors the vanilla <c>Game.EndFrameBarrier</c>
    /// protection so any sync-point-only producer (a job whose handle is registered without
    /// writing an ECB on the same tick) is completed before ECB playback. With the render handle
    /// gone it now drains only the cheap enable-bit / renderless ECB producers, so its
    /// <c>Complete()</c> is near-free.
    /// </summary>
    [FrameworkSystem]
    public partial class ThreatLifecycleBarrier : SafeCommandBufferSystem
    {
        private JobHandle m_LocalProducerHandle;

        public new void AddJobHandleForProducer(JobHandle producerJob)
        {
            // PERF-LOCK: do NOT add `base.AddJobHandleForProducer(producerJob);` here.
            // Vanilla EntityCommandBufferSystem.FlushPendingBuffers (decompile
            // Unity.Entities/EntityCommandBufferSystem.cs:56-64) early-returns when
            // PendingBuffers is empty, so base.m_ProducerHandle leaks on lazy ticks
            // (sync-point-only producers register without CreateCommandBuffer).
            // Mirror vanilla Game.EndFrameBarrier (Game/EndFrameBarrier.cs:45-48).
            // Enforced by CIVIC488.
            m_LocalProducerHandle = JobHandle.CombineDependencies(m_LocalProducerHandle, producerJob);
        }

        protected override void OnUpdate()
        {
            // Complete the accumulated shadow producer handle before ECB playback. With the render
            // handle removed (it is now drained in Modification4, not here), this drains only cheap
            // enable-bit / renderless ECB producers, so it is near-free. The shadow handle is still
            // required because the base EntityCommandBufferSystem only completes its internal
            // producer handle when PendingBuffers is non-empty.
            m_LocalProducerHandle.Complete();
            m_LocalProducerHandle = default;
            base.OnUpdate();
        }
    }
}
