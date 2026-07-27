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
    /// terminalization sink, the job is a data-copy plus the one-shot full-ring init on the first
    /// movement tick (consuming <see cref="Interceptor.NeedsRingInit"/> — hence <c>ref</c>).
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

        // One-shot ring-init "emergence" history: total backward span (m) of the synthesized flight
        // history. Kept under the 12 m launch-height offset so no history point sinks below the
        // launcher. Per-slot velocities equal the per-slot position step over the tick (4/15 s) —
        // position/velocity consistency is what keeps vanilla's Bezier from looping backward
        // (BACKWARD_JITTER.md: tangent > gap → backward burst).
        private const float RING_INIT_BACKSPAN = 9f;
        private const float FIXED_TIME_STEP = 4f / 15f; // sub-frame step, mirror of the movement system

        private void Execute(
            ref Interceptor ic,
            ref Transform transform,
            ref Moving moving,
            DynamicBuffer<TransformFrame> tfBuf)
        {
            // Last-hop NaN/Inf backstop before the vanilla render pipeline (see DroneRenderWriteJob):
            // a non-finite pose would reach ObjectInterpolateSystem → CullingInfo bounds (AV).
            // Guards ALL THREE written fields — a NaN quaternion survives vanilla's interpolation
            // normalize just as lethally as a NaN position (crash-hunt 2026-07-21, lens 3). Skip
            // the write and keep the last-good pose for this frame.
            if (math.any(math.isnan(ic.CurrentPosition)) || math.any(math.isinf(ic.CurrentPosition))
                || math.any(math.isnan(ic.RenderRotation.value)) || math.any(math.isinf(ic.RenderRotation.value))
                || math.any(math.isnan(ic.RenderVelocity)) || math.any(math.isinf(ic.RenderVelocity)))
                return;

            float3 oldPos = transform.m_Position;
            transform = new Transform { m_Position = ic.CurrentPosition, m_Rotation = ic.RenderRotation };
            moving.m_Velocity = ic.RenderVelocity;

            if (ic.NeedsRingInit && tfBuf.Length >= 4)
            {
                // First movement tick: rewrite the FULL ring with a moving "emergence" history —
                // slot (Slot-k)&3 holds the pose k ticks back, receding from the current position
                // toward the launcher along the flight heading. Without this the ring keeps vanilla
                // UpdateGroupSystem's Created-frame fill (4 static zero-velocity copies of the spawn
                // transform, decompile UpdateGroupSystem.cs:471-483) and the interpolation replays a
                // ~0.5-0.8 s standstill at the launcher. A one-shot full-ring write is the same
                // operation vanilla itself performs on the Created frame — the February "all 4 slots"
                // seam (BACKWARD_JITTER.md) came from doing it EVERY tick; after this tick the write
                // pattern stays strictly 1 slot per tick.
                float3 dir = math.normalizesafe(ic.RenderVelocity, math.forward(ic.RenderRotation));
                const float backStep = RING_INIT_BACKSPAN / 3f;
                float3 histVelocity = dir * (backStep / FIXED_TIME_STEP);
                for (int k = 0; k < 4; k++)
                {
                    tfBuf[(Slot - k + 4) & 3] = new TransformFrame
                    {
                        m_Position = ic.CurrentPosition - dir * (backStep * k),
                        m_Velocity = histVelocity,
                        m_Rotation = ic.RenderRotation,
                        m_Flags = TransformFlags.Flying
                    };
                }
                ic.NeedsRingInit = false;
                return;
            }

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
