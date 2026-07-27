using CivicSurvival.Core.Attributes;
using Game;
using Unity.Jobs;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// PostSimulation ECB playback barrier for mod cleanup and delayed bookkeeping.
    /// SafeCommandBufferSystem closes the gate at playback; AllowBarrier&lt;ModCleanupBarrier&gt;
    /// must reopen it before approved PostSimulation producers run.
    /// Keeps the producer handle local and intentionally does not forward to
    /// EntityCommandBufferSystem.AddJobHandleForProducer because the base handle
    /// is only completed when PendingBuffers is non-empty.
    /// </summary>
    [FrameworkSystem]
    public partial class ModCleanupBarrier : SafeCommandBufferSystem
    {
        private JobHandle m_LocalProducerHandle;

        public new void AddJobHandleForProducer(JobHandle producerJob)
        {
            // PERF-LOCK: do NOT add `base.AddJobHandleForProducer(producerJob);` here.
            // Vanilla EntityCommandBufferSystem.FlushPendingBuffers (decompile
            // Unity.Entities/EntityCommandBufferSystem.cs:56-64) early-returns when
            // PendingBuffers is empty, so base.m_ProducerHandle leaks on lazy ticks.
            // Mirror vanilla Game.EndFrameBarrier (Game/EndFrameBarrier.cs:45-48).
            // Enforced by CIVIC488.
            m_LocalProducerHandle = JobHandle.CombineDependencies(m_LocalProducerHandle, producerJob);
        }

        protected override void OnUpdate()
        {
            // PERF-LOCK: unconditional Complete — see AddJobHandleForProducer above.
            m_LocalProducerHandle.Complete();
            m_LocalProducerHandle = default;
            base.OnUpdate();
        }
    }
}
