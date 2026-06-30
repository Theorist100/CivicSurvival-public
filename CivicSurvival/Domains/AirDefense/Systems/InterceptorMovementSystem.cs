using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.AirDefense;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Constants;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Jobs;
using Game;
using Game.Common;
using Game.Objects;
using Game.Simulation;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Flies each interceptor missile toward the threat its launching shot engaged, refining the aim
    /// each tick from the threat's live <see cref="ThreatPosition"/> (chase). The main thread resolves
    /// the pose (chase target, smoothed heading, arrival) into the mod-only <see cref="Interceptor"/>
    /// fields (<c>CurrentPosition</c>/<c>RenderRotation</c>/<c>RenderVelocity</c>); a Burst
    /// <c>InterceptorRenderWriteJob</c> copies that pose into the vanilla render components
    /// (<c>Transform</c>/<c>Moving</c>/<c>TransformFrame</c>) so the BRG pipeline interpolates the
    /// flight. Render-only: the missile mirrors the HIT/MISS the fire-control formula already
    /// resolved — it never touches gameplay state, so it is PvP-safe.
    ///
    /// <para><b>Cadence.</b> Ticks once per <see cref="UPDATE_INTERVAL"/> sim frames at the TMS
    /// sub-frame offset (same as the threats), so the TransformFrame ring slot advances exactly once
    /// per tick — the OIS Pattern-B contract vanilla's Bezier interpolation expects. Movement uses the
    /// fixed sub-frame step, not per-frame DeltaTime. (The engine exhaust is re-attached on a separate
    /// per-frame cadence by <c>InterceptorExhaustSystem</c> — the 16-frame movement tick is too slow to
    /// re-inject a record EffectControlSystem may drop, which would make the flame flicker.)</para>
    ///
    /// <para><b>Burst render-write (mirror of the threat pipeline).</b> Writing
    /// <c>Transform</c>/<c>Moving</c>/<c>TransformFrame</c> from a main-thread
    /// <c>SystemAPI.Query&lt;RefRW&lt;Transform&gt;,RefRW&lt;Moving&gt;&gt;</c> forced a
    /// <c>CompleteDependencyBeforeRW</c> universal sync EVERY tick (the city-wide transform job drain
    /// — 33→283 ms spikes under a Patriot salvo). Now only the pose MATH runs main-thread (chase, turn,
    /// arrival, the managed terminalization sink) and a Burst <c>InterceptorRenderWriteJob</c> applies it.
    /// The render handle is folded INTO <c>Dependency</c> (Branch B) so ECS fences vanilla job readers of
    /// <c>Transform</c>/<c>Moving</c>/<c>TransformFrame</c> against the worker — closing the torn-read race
    /// (lib_burst+0x9525c0) the manual Modification4 drain left open — without a same-frame Complete. It is
    /// ALSO still published through <c>IRenderWriteBarrier</c> for the MAIN-THREAD structural consumers that
    /// <c>Dependency</c> cannot fence: the Consume before the Modification4 structural ops in
    /// InterceptorCleanupSystem (AddComponent&lt;Deleted&gt;) and InterceptorSpawnApplySystem (CreateEntity)
    /// so neither migrates a chunk under the in-flight worker (CIVIC508 class). The job
    /// reads no <c>ThreatPosition</c>, so it adds no ordering edge to ThreatMovementSystem (BUG-005),
    /// and the <c>Interceptor</c>/<c>InterceptorTag</c> identity is non-enableable (no enable-mask
    /// race).</para>
    ///
    /// Pause-safe: runs in GameSimulation, which does not tick in pause, so airborne missiles freeze
    /// alongside the frozen AA — consistent with tracers.
    /// </summary>
    [ActIndependent]
    public partial class InterceptorMovementSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("InterceptorMovementSystem");

        // Match the threat render cadence: tick once per 16 sim frames at the TMS sub-frame offset, so
        // slot = (frameIndex>>4)&3 advances exactly once per tick (OIS Pattern B). Ticking every frame
        // would rewrite one TF slot 16× and leave the other 3 stale → backward-Bezier jitter.
        private const int UPDATE_INTERVAL = 16;
        private const float FIXED_TIME_STEP = 4f / 15f; // sub-frame step (UPDATE_INTERVAL / 60), vanilla AircraftMoveSystem constant

        // Max heading change per tick (0.267 s). Higher than the Shahed's 15° — a fast missile chasing
        // a moving threat needs a tighter turn to avoid arcing past it, but still smooth enough to read
        // as a curve rather than an instant snap. Tune by eye.
        private const float MAX_TURN_RADIANS_PER_STEP = 35f * math.PI / 180f;

        // Heading treated as "unset" below this squared length → first tick after spawn aims straight.
        private const float MIN_DIR_SQ = 1e-6f;

        private EntityQuery m_InterceptorQuery;
        private ComponentLookup<ThreatPosition> m_ThreatPosLookup;
        private SimulationSystem m_SimulationSystem = null!;

        // Render job handle. Kept OUT of Dependency (folding it back re-adds the Transform/Moving/TF
        // universal sync this split removed). Drained a frame later in Modification4 by
        // InterceptorCleanupSystem / InterceptorSpawnApplySystem via RenderWriteBarrier.Consume, where
        // the worker has had a whole frame of time. Mirror of ThreatMovementSystem.m_RenderJobHandle.
        private JobHandle m_RenderJobHandle;

        // Previous frame's render handle, folded into the next render job's input dep so render N+1
        // cannot start writing the same Transform/Moving/TransformFrame chunks while render N is still
        // in flight (the render→render self-race the removed same-frame Complete used to mask).
        // NonSerialized: reset to default in OnStopRunning/OnDestroy so a previous city's handle does
        // not leak into the first render schedule of the loaded city (Interceptor is stripped on load →
        // the RequireForUpdate gate drops → OnStopRunning fires).
        [System.NonSerialized] private JobHandle m_PrevRenderJobHandle;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;

        // Resolution-trigger reads (all sync-free: the await-bit components are RO-only in the
        // in-flight TMS Burst jobs, Deleted/PendingDestruction have no in-flight Burst writer here).
        // Identity (generation + isBallistic) is carried on the Interceptor itself, so NO Shahed/
        // Ballistic lookup is needed — that read would drain the in-flight ballistic Burst job (perf H1).
        private ComponentLookup<ShahedCombatState> m_CombatStateLookupRO;
        private ComponentLookup<BallisticInterceptState> m_BallisticInterceptLookupRO;
        private ComponentLookup<Deleted> m_DeletedLookupRO;
        private ComponentLookup<PendingDestruction> m_PendingDestructionLookupRO;
        [System.NonSerialized] private IThreatTerminalizationSink m_TerminalizationQueue = null!;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => UPDATE_INTERVAL;
        public override int GetUpdateOffset(SystemUpdatePhase phase) => ThreatConstants.TMS_SUB_FRAME;

        protected override void OnCreate()
        {
            base.OnCreate();
            // Gate query only (RequireForUpdate). Transform is RO here — the per-tick Transform write now
            // happens in InterceptorRenderWriteJob (TypeHandle, no main-thread RW), not on this query.
            m_InterceptorQuery = GetEntityQuery(
                ComponentType.ReadWrite<Interceptor>(),
                ComponentType.ReadOnly<InterceptorTag>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>());
            m_ThreatPosLookup = GetComponentLookup<ThreatPosition>(isReadOnly: true);
            m_CombatStateLookupRO = GetComponentLookup<ShahedCombatState>(true);
            m_BallisticInterceptLookupRO = GetComponentLookup<BallisticInterceptState>(true);
            m_DeletedLookupRO = GetComponentLookup<Deleted>(true);
            m_PendingDestructionLookupRO = GetComponentLookup<PendingDestruction>(true);
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            RequireForUpdate(m_InterceptorQuery);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // CIVIC403: resolve infrastructure services in OnStartRunning (??=), not OnCreate.
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
        }

        protected override void OnUpdateImpl()
        {
            m_ThreatPosLookup.Update(this);
            m_CombatStateLookupRO.Update(this);
            m_BallisticInterceptLookupRO.Update(this);
            m_DeletedLookupRO.Update(this);
            m_PendingDestructionLookupRO.Update(this);
            m_TerminalizationQueue ??= ServiceRegistry.Instance.Require<IThreatTerminalizationSink>();

            // Fixed sub-frame step (this system ticks once per UPDATE_INTERVAL frames), not per-frame
            // DeltaTime — matches the threat movement timestep so flight speed is frame-rate independent.
            const float dt = FIXED_TIME_STEP;

            // TransformFrame ring slot — advances once per tick because the system ticks once per 16
            // frames; matches the threat render-write cadence so OIS interpolation reads it correctly.
            int slot = (int)((m_SimulationSystem.frameIndex >> 4) & 3u);

            // MAIN THREAD: pose math only. Reads/writes the mod-only Interceptor component (sync-free —
            // no universal-component RW, so no CompleteDependencyBeforeRW). The current position comes
            // from Interceptor.CurrentPosition (seeded to SpawnPos at spawn, advanced each tick here),
            // NOT a Game.Objects.Transform RO read — reading Transform here would re-add the city-wide
            // transform-job drain this split exists to remove. The Burst job below applies the resolved
            // pose into Transform/Moving/TransformFrame.
            foreach (var interceptorRef in
                     SystemAPI.Query<RefRW<Interceptor>>()
                         .WithAll<InterceptorTag>()
                         .WithNone<Deleted>())
            {
                ref Interceptor interceptor = ref interceptorRef.ValueRW;

                // Chase: read the engaged threat's live position; fall back to the last-known target
                // when the threat no longer exists (HIT despawned it, or version recycled). ThreatPosition
                // is read RO on the MAIN THREAD (not in the job) — so the render job carries no ordering
                // edge to ThreatMovementSystem's in-flight ballistic job (BUG-005).
                float3 target = interceptor.TargetPos;
                var threat = new Entity { Index = interceptor.ThreatIndex, Version = interceptor.ThreatVersion };
                if (m_ThreatPosLookup.HasComponent(threat))
                {
                    target = m_ThreatPosLookup[threat].Position;
                    interceptor.TargetPos = target; // retain last-known for the frame the threat dies
                }

                float3 current = interceptor.CurrentPosition;
                float3 toTarget = target - current;
                float dist = math.length(toTarget);
                float step = interceptor.Speed * dt;

                // Smooth turn (Shahed-style): rotate the persisted heading toward the target by a capped
                // angle per tick — an arc, not an instant snap. Zero heading on spawn → aim straight.
                float3 desiredDir = math.normalizesafe(toTarget, math.forward());
                float3 currentDir = interceptor.CurrentDirection;
                float3 dir = math.lengthsq(currentDir) < MIN_DIR_SQ
                    ? desiredDir
                    : RotateTowards(currentDir, desiredDir, MAX_TURN_RADIANS_PER_STEP);
                interceptor.CurrentDirection = dir;

                quaternion rot = quaternion.LookRotationSafe(dir, math.up());
                bool reached = dist <= step;
                float3 newPos = reached ? target : current + dir * step;     // snap on arrival
                float3 velocity = reached ? float3.zero : dir * interceptor.Speed;

                // Resolved pose → mod-only Interceptor fields. InterceptorRenderWriteJob copies these
                // into Transform/Moving/TransformFrame on a Burst worker; InterceptorExhaustSystem reads
                // CurrentPosition as the VFX seed. All sync-free (only our systems touch these fields).
                interceptor.CurrentPosition = newPos;
                interceptor.RenderRotation = rot;
                interceptor.RenderVelocity = velocity;

                if (reached && !interceptor.HasReachedTarget)
                {
                    // The missile met its target. If the chased threat is still coasting
                    // (AwaitingInterceptorImpact), terminalize it HERE at the meet point via the Core
                    // sink (explosion + render delete + intercept sound, no damage; drained by
                    // ThreatTerminalizationSystem later this GameSimulation frame — ≤1-frame, pure
                    // render). The missile itself is despawned from the render-safe Modification4 phase
                    // by InterceptorCleanupSystem (HasReachedTarget) — never AddComponent<Deleted> here
                    // (out-of-phase render chunk-cache crash class). This managed sink call stays
                    // main-thread (impossible in Burst) — it is rare (only the arrival tick).
                    TryQueueCoastResolution(threat, newPos, interceptor.IsBallistic, interceptor.ThreatGeneration);
                    interceptor.HasReachedTarget = true;
                }
            }

            // BURST WORKER: apply the resolved pose into the vanilla render components. IJobEntity uses
            // TypeHandles (not ComponentLookup) → NO CompleteDependencyBeforeRW universal sync. The main-
            // thread RW of Transform/Moving on the Interceptor query was the 33→283ms/call drain.
            var renderJob = new InterceptorRenderWriteJob { Slot = slot };
            // PERF-LOCK: render handle folded INTO system.Dependency so ECS fences ALL vanilla job readers
            // of Transform/Moving/TransformFrame against this worker — same-frame AND next-frame — closing
            // the torn-read data race (c0000005 in vanilla Burst at lib_burst+0x9525c0) that the
            // Modification4 manual drain left open on the producing frame. This is NOT a same-frame Complete
            // (that force-complete was the GPU-starvation source); ECS resolves the dependency at the natural
            // sync point. The render→render self-sync (m_PrevRenderJobHandle) still guards intra-frame
            // multi-tick. Removing the Dependency fold reopens the race.
            // NOTE (Axiom 15): this REPLACES the prior "keep OUT of Dependency" PERF-LOCK. That marker cited
            // a 33→283ms main-thread Transform/Moving drain — but that was the OLD main-thread RW, not a
            // worker handle in Dependency. The perf cost of THIS (worker-graph ordering, not a main-thread
            // wait) MUST be wave-measured before ship; fallback = narrow the fold to TransformFrame only.
            JobHandle renderInputDep = JobHandle.CombineDependencies(Dependency, m_PrevRenderJobHandle);
            m_RenderJobHandle = renderJob.ScheduleParallel(renderInputDep);
            // Still published: system.Dependency fences other ECS jobs, but our MAIN-THREAD render-component
            // readers (InterceptorCleanup/Spawn structural Consume; AA building-transform consumers) are not
            // ECS jobs — the barrier is their only fence. Keep Publish.
            m_RenderWriteBarrier.Publish(m_RenderJobHandle, GetType(), RenderWriteComponentMask.InterceptorRender);
            m_PrevRenderJobHandle = m_RenderJobHandle;
            Dependency = JobHandle.CombineDependencies(Dependency, m_RenderJobHandle);
        }

        protected override void OnStopRunning()
        {
            // Drain the in-flight render job before the system goes idle so no worker survives into a
            // structural change. Fires on load too: Interceptor is stripped on load → the
            // RequireForUpdate gate drops → OnStopRunning. Reset the self-sync latch so a previous
            // city's handle does not leak into the loaded city's first render schedule. (Mirror of
            // ThreatMovementSystem teardown; not reachable from OnUpdate, so the RenderWriteBarrier
            // manual-Complete ban does not apply.)
            m_RenderJobHandle.Complete();
            m_PrevRenderJobHandle = default;
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            m_RenderJobHandle.Complete();
            m_PrevRenderJobHandle = default;
            base.OnDestroy();
        }

        /// <summary>
        /// Queues the intercept terminal outcome for a coasting threat the missile just reached.
        /// Uses the identity carried on the Interceptor (isBallistic + generation) — no Shahed/Ballistic
        /// RO read (perf H1). Only the await-bit component (RO-only in the in-flight Burst jobs → no
        /// sync) + liveness are read here. Cross-domain via the Core sink + Core factory (Axiom 5).
        /// </summary>
        private void TryQueueCoastResolution(Entity threat, float3 position, bool isBallistic, int generation)
        {
            if (threat == Entity.Null)
                return;
            if (m_DeletedLookupRO.HasComponent(threat)
                || (m_PendingDestructionLookupRO.HasComponent(threat) && m_PendingDestructionLookupRO.IsComponentEnabled(threat)))
                return;

            bool awaiting = isBallistic
                ? (m_BallisticInterceptLookupRO.TryGetComponent(threat, out var bis) && bis.AwaitingInterceptorImpact)
                : (m_CombatStateLookupRO.TryGetComponent(threat, out var cs) && cs.AwaitingInterceptorImpact);
            if (!awaiting)
                return;

            m_TerminalizationQueue.Queue(ThreatTerminalOutcome.Intercept(
                threat, position, isBallistic, generation,
                debrisFallTime: isBallistic ? 0f : BalanceConfig.Current.Threats.DebrisFallTime));
        }

        /// <summary>
        /// Rotates normalized vector <paramref name="from"/> toward <paramref name="to"/> by at most
        /// <paramref name="maxRadians"/> (Slerp, with an antiparallel guard). Mirror of the Shahed
        /// movement helper — kept local to avoid a cross-domain import (Axiom 5).
        /// </summary>
        private static float3 RotateTowards(float3 from, float3 to, float maxRadians)
        {
            float dot = math.clamp(math.dot(from, to), -1f, 1f);
            const float epsilon = 0.0001f;
            const float antiparallelThreshold = -0.9999f;

            // Antiparallel guard: sin(π) = 0 → Slerp division by zero. Rotate in the XZ plane instead.
            if (dot < antiparallelThreshold)
            {
                float perpX = -from.z;
                float perpZ = from.x;
                float3 rawPerp = new float3(perpX, 0f, perpZ);
                float3 perp = math.lengthsq(rawPerp) < epsilon
                    ? new float3(1f, 0f, 0f)
                    : math.normalizesafe(rawPerp);
                return math.normalizesafe(from + perp * math.sin(maxRadians));
            }

            float angle = math.acos(dot);
            if (angle <= maxRadians || angle < epsilon)
                return to;
#pragma warning disable CIVIC073 // angle > maxRadians guard above guarantees t < 1
            float t = maxRadians / math.max(angle, epsilon);
#pragma warning disable CIVIC021 // sinAngle > 0 guaranteed: dot > -0.9999 + angle > epsilon guards above
            float sinAngle = math.sin(angle);
            float3 result = (math.sin((1f - t) * angle) * from + math.sin(t * angle) * to) / sinAngle;
#pragma warning restore CIVIC021
            return math.normalizesafe(result);
#pragma warning restore CIVIC073
        }
    }
}
