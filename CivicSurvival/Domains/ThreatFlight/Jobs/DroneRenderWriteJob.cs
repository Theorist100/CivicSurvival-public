using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Game.Objects;
using Game.Rendering;
using CivicSurvival.Core.Components.Threats;

namespace CivicSurvival.Domains.ThreatFlight.Jobs
{
    /// <summary>
    /// Burst-compiled IJobEntity — writes Transform/Moving/TransformFrame
    /// for threat entities (Shahed + Ballistic). <c>ThreatPosition</c> alone
    /// uniquely identifies our drones (vanilla Aircraft lack it).
    ///
    /// Replaces main-thread WriteRenderingComponents with zero sync points.
    /// IJobEntity uses TypeHandle internally (not ComponentLookup) — no
    /// CompleteDependencyBeforeRW on universal components.
    ///
    /// <para><strong>RACE-SAFETY INVARIANT — do NOT add <c>ActiveThreat</c> (or any
    /// enableable threat tag: <c>PendingDestruction</c>, <c>PendingThreatDeletion</c>)
    /// to this query.</strong> This handle is now folded INTO <c>system.Dependency</c> at the
    /// schedule site (RenderDrainFence Branch B — ECS fences vanilla job readers of Transform/
    /// Moving/TransformFrame) and published to <c>RenderWriteBarrier</c> (fences main-thread
    /// structural/camera consumers). But NEITHER fence covers the GameSimulation barrier's
    /// main-thread <c>SetComponentEnabled&lt;ActiveThreat&gt;</c> ECB playback (ThreatTerminalization /
    /// ThreatDebug) against this worker — Dependency orders jobs, not a barrier's ECB playback. If
    /// the job read an enableable tag, that playback would write the chunk's enable-mask while this worker reads it —
    /// a write-while-read the safety system flags and a 1-frame non-determinism in
    /// production. Filtering by liveness here is impossible without either a
    /// main-thread <c>Complete()</c> (GPU starvation, the very thing the refactor
    /// removed) or that race. Instead the job renders every threat entity, including
    /// a terminalized-but-not-yet-deleted drone for ≤1 frame (harmless: vanilla
    /// renders it via Transform until the <c>Deleted</c> migration anyway, position is
    /// already final). Enforced by CIVIC508.</para>
    /// </summary>
#if ENABLE_BURST
    [BurstCompile]
#endif
    public partial struct DroneRenderWriteJob : IJobEntity
    {
        public int Slot;

        void Execute(
            in ThreatPosition tp,
            ref Transform transform,
            ref Moving moving,
            DynamicBuffer<TransformFrame> tfBuf)
        {
            // Last-hop NaN/Inf backstop before the vanilla render pipeline. A non-finite position
            // flows unchecked through ObjectInterpolateSystem → InterpolatedTransform → CullingInfo
            // bounds (AV in culling/BRG). The known Inf source is already retired upstream
            // (BallisticMovementJobEntity guards NaN/Inf Speed/TargetPosition); this is a cheap
            // final guard — skip the write and leave the last-good pose for this frame.
            if (math.any(math.isnan(tp.Position)) || math.any(math.isinf(tp.Position)))
                return;

            float3 oldPos = transform.m_Position;
            transform = new Transform { m_Position = tp.Position, m_Rotation = tp.Rotation };
            moving.m_Velocity = tp.Velocity;

            if (Slot < tfBuf.Length)
            {
                float3 midPos = math.lerp(oldPos, tp.Position, 0.5f);
                tfBuf[Slot] = new TransformFrame
                {
                    m_Position = midPos,
                    m_Velocity = tp.Velocity,
                    m_Rotation = tp.Rotation,
                    m_Flags = TransformFlags.Flying
                };
            }
        }
    }
}
