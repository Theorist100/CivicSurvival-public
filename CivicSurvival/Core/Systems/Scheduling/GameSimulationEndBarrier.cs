using CivicSurvival.Core.Attributes;
using Game;
using Unity.Jobs;

namespace CivicSurvival.Core.Systems.Scheduling
{
    /// <summary>
    /// Shared GameSimulation-phase ECB playback barrier.
    ///
    /// Mirrors the vanilla <c>Game.EndFrameBarrier</c> pattern: the base
    /// <see cref="Unity.Entities.EntityCommandBufferSystem"/> only completes its
    /// internal producer handle inside <c>FlushPendingBuffers</c>, which early-returns
    /// when <c>PendingBuffers</c> is empty. Callers that register a job handle
    /// without also creating an ECB on the same tick would otherwise accumulate an
    /// uncompleted <c>JobHandle.CombineDependencies</c> chain in the base private
    /// field across ticks, eventually pointing into recycled Unity Job allocator
    /// slots.
    ///
    /// The override here keeps a separate <c>m_LocalProducerHandle</c> and force-
    /// completes it before <c>base.OnUpdate</c>, unconditionally — identical to
    /// vanilla's protection in <c>EndFrameBarrier.OnUpdate</c>.
    /// </summary>
    [FrameworkSystem]
    public partial class GameSimulationEndBarrier : SafeCommandBufferSystem
    {
        private JobHandle m_LocalProducerHandle;

        public new void AddJobHandleForProducer(JobHandle producerJob)
        {
            // PERF-LOCK: do NOT add `base.AddJobHandleForProducer(producerJob);` here.
            // Vanilla EntityCommandBufferSystem.FlushPendingBuffers (decompile
            // Unity.Entities/EntityCommandBufferSystem.cs:56-64) early-returns when
            // PendingBuffers is empty, so base.m_ProducerHandle never completes on
            // lazy ticks (handle registered without CreateCommandBuffer) and grows
            // an unbounded JobHandle.CombineDependencies chain into recycled job
            // allocator slots. Pattern mirrors vanilla Game.EndFrameBarrier shadow
            // (Game/EndFrameBarrier.cs:45-48). Enforced by CIVIC488.
            m_LocalProducerHandle = JobHandle.CombineDependencies(m_LocalProducerHandle, producerJob);
        }

        protected override void OnUpdate()
        {
            // PERF-LOCK: unconditional Complete — see AddJobHandleForProducer above.
            // base.OnUpdate's FlushPendingBuffers will not complete this handle.
            m_LocalProducerHandle.Complete();
            m_LocalProducerHandle = default;
            base.OnUpdate();
        }
    }
}
