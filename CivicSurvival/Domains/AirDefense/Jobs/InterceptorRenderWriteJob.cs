using CivicSurvival.Core.Components.AirDefense;
using Game.Common;
using Game.Objects;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Jobs
{
    /// <summary>
    /// Burst-compiled <c>IJobEntity</c> — applies the pose InterceptorMovementSystem resolved on the
    /// main thread (<see cref="Interceptor.CurrentPosition"/>/<see cref="Interceptor.RenderRotation"/>/
    /// <see cref="Interceptor.RenderVelocity"/>) into the vanilla render components
    /// (<c>Transform</c>/<c>Moving</c>/<c>TransformFrame</c>) so the BRG pipeline interpolates the flight.
    /// Sibling of <c>DroneRenderWriteJob</c>; the main thread keeps chase/turn math + the managed
    /// terminalization sink, the job is a pure data-copy.
    ///
    /// <para>Writing those vanilla render components from the system's main-thread
    /// <c>SystemAPI.Query&lt;RefRW&lt;Transform&gt;,RefRW&lt;Moving&gt;&gt;</c> forced a
    /// <c>CompleteDependencyBeforeRW</c> universal sync EVERY tick (the city-wide transform job drain —
    /// 33→283 ms spikes under a Patriot salvo). <c>IJobEntity</c> uses TypeHandles, not ComponentLookup,
    /// so it carries no universal sync.</para>
    ///
    /// <para><strong>RACE-SAFETY INVARIANT — do NOT add an enableable lifecycle tag</strong>
    /// (<c>Game.Common.Deleted</c> as a read parameter, or any future enableable interceptor tag) to the
    /// <c>Execute</c> signature.</strong> This handle is now folded INTO the system's <c>Dependency</c>
    /// at the schedule site (RenderDrainFence — ECS fences vanilla job readers of Transform/Moving/
    /// TransformFrame), published to the Modification4 render-completion gate (<c>RenderWriteBarrier.Consume</c>
    /// in InterceptorCleanupSystem / InterceptorSpawnApplySystem), and self-synced render→render
    /// (<c>m_PrevRenderJobHandle</c>). But none of those fence a structural barrier's enable-mask ECB
    /// playback against this worker (Dependency orders jobs, not ECB playback). If the job read an
    /// enableable tag's enable-mask, that playback could write the mask while this worker
    /// reads it — the exact CIVIC508 write-while-read. The identity here is <see cref="Interceptor"/> +
    /// <see cref="InterceptorTag"/> (both plain, non-enableable) and the job reads NO
    /// <c>ThreatPosition</c> — so it adds no ordering edge to ThreatMovementSystem either (BUG-005).
    /// A terminalized-but-not-yet-deleted missile is excluded structurally via
    /// <c>[WithNone(typeof(Deleted))]</c> (a structural filter, not an enable-mask read). Enforced by
    /// CIVIC508.</para>
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    [WithAll(typeof(InterceptorTag))]
    [WithNone(typeof(Deleted))]
    public partial struct InterceptorRenderWriteJob : IJobEntity
    {
        public int Slot;

        private void Execute(
            in Interceptor ic,
            ref Transform transform,
            ref Moving moving,
            DynamicBuffer<TransformFrame> tfBuf)
        {
            // Last-hop NaN/Inf backstop before the vanilla render pipeline (see DroneRenderWriteJob):
            // a non-finite pose would reach ObjectInterpolateSystem → CullingInfo bounds (AV). Skip
            // the write and keep the last-good pose for this frame.
            if (math.any(math.isnan(ic.CurrentPosition)) || math.any(math.isinf(ic.CurrentPosition)))
                return;

            float3 oldPos = transform.m_Position;
            transform = new Transform { m_Position = ic.CurrentPosition, m_Rotation = ic.RenderRotation };
            moving.m_Velocity = ic.RenderVelocity;

            if (Slot < tfBuf.Length)
            {
                tfBuf[Slot] = new TransformFrame
                {
                    m_Position = math.lerp(oldPos, ic.CurrentPosition, 0.5f),
                    m_Velocity = ic.RenderVelocity,
                    m_Rotation = ic.RenderRotation,
                    m_Flags = TransformFlags.Flying
                };
            }
        }
    }
}
