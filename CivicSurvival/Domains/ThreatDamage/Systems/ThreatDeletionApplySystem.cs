using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Game.Common;
using Unity.Entities;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Consumer half of the drone-deletion producer/consumer split (mirror of vanilla
    /// <c>IgniteSystem</c> and the mod's own <c>ModFireApplySystem</c> /
    /// <c>PowerIndexApplySystem</c>). Runs in Modification4: reads drones whose
    /// <see cref="PendingThreatDeletion"/> enable-bit was flipped by the threat-lifecycle
    /// producers (<c>ThreatTerminalizationSystem</c>, <c>ThreatDebugSystem</c>) in
    /// GameSimulation and performs the structural <c>AddComponent&lt;Deleted&gt;</c> on the
    /// vanilla render drone from THIS phase — where vanilla's render batch pipeline
    /// (<c>RequiredBatchesSystem</c> ModificationEnd, <c>PreCullingSystem</c>,
    /// <c>BatchManagerSystem</c>, all later in the same MainLoop) expects the archetype
    /// migration.
    ///
    /// Why the producer cannot add <c>Deleted</c> itself: the structural add migrates the
    /// drone's render chunk, and doing that from GameSimulation (LateUpdate, end of frame)
    /// lands the migration out of phase with the render pass → the main-thread vanilla Burst
    /// batch job reads a stale, zeroed render chunk-cache and crashes (the render-chunk-cache
    /// crash class). Modification4 (MainLoop) runs before GameSimulation (LateUpdate) in the
    /// frame, and Modification4 &lt; ModificationEnd &lt; PreCulling &lt; Rendering, so the
    /// migration flushed here is in phase with the render pass.
    ///
    /// Frame semantics + render-completion gate: the producer flips the enable-bit on tick N
    /// (GameSimulation), where the bit playback is render-safe because it is not a structural
    /// change. This consumer adds <c>Deleted</c> on tick N+1 (Modification4). The render-job
    /// completion that used to be forced by <c>ThreatLifecycleBarrier</c> in GameSimulation is
    /// gone (it starved the GPU), so this consumer drains the render writer itself at the TOP of
    /// <c>OnUpdateImpl</c> via <c>RenderWriteBarrier.Consume</c> BEFORE creating the ECB — so the
    /// render job is never in flight when the <c>Deleted</c> migration flushes. By Modification4
    /// of frame N+1 the render job scheduled in GameSimulation of frame N has had a whole frame of
    /// worker time, so the drain is near-free. (Second of two drone-structural Modification4 sites
    /// that depend on this gate; the other is <c>ThreatSpawnApplySystem</c>.)
    ///
    /// Pause-safe: the producers live in GameSimulation, which does not tick while paused, so no
    /// drone is ever signalled in pause; this consumer (Modification4, which DOES tick in pause)
    /// just sees an empty query and does nothing. "Drones are not deleted while paused" holds.
    /// </summary>
    [ActIndependent]
    public partial class ThreatDeletionApplySystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ThreatDeletionApplySystem");

        // Same render-write scope DroneRenderWriteJob publishes under. Full mask (all 3
        // components) — one published handle, drained regardless of any future sub-mask split.
        private const RenderWriteComponentMask RenderWriteMask =
            RenderWriteComponentMask.ThreatTransform |
            RenderWriteComponentMask.ThreatMoving |
            RenderWriteComponentMask.ThreatTransformFrame;

        private EntityQuery m_SignalQuery;
        private ModificationBarrier4 m_ModificationBarrier = null!;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;
        private ComponentLookup<Deleted> m_DeletedLookup;

        protected override void OnCreate()
        {
            base.OnCreate();
            // Drones whose PendingThreatDeletion bit is enabled. IEnableableComponent queries
            // match only enabled entities by default — the bulk of drones (bit disabled) never
            // enter this query.
            m_SignalQuery = GetEntityQuery(ComponentType.ReadOnly<PendingThreatDeletion>());
            m_ModificationBarrier = World.GetOrCreateSystemManaged<ModificationBarrier4>();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            RequireForUpdate(m_SignalQuery);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_RenderWriteBarrier ??= ServiceRegistry.Instance.Require<IRenderWriteBarrier>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_SignalQuery.IsEmpty)
                return;

            // RENDER-COMPLETION GATE (CRITICAL): drain the in-flight DroneRenderWriteJob BEFORE the
            // structural Add<Deleted> below migrates the drone's render chunk. The render handle is
            // kept out of ThreatMovementSystem.Dependency and is no longer force-completed by
            // ThreatLifecycleBarrier in GameSimulation, so without this the migration could land in
            // a chunk the render job is still iterating → native AV (9db2bedf).
            m_RenderWriteBarrier.Consume(GetType(), RenderWriteMask);

            m_DeletedLookup.Update(this);

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;
            int applied = 0;

            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<PendingThreatDeletion>>()
                .WithEntityAccess())
            {
                if (!ecbCreated)
                {
                    ecb = m_ModificationBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                // Clear the signal so a drone that survives one tick (e.g. paused playback
                // straddling ticks) is not re-queued. The drone leaves via Deleted → vanilla
                // Cleanup anyway; this guards the in-between window.
                ecb.SetComponentEnabled<PendingThreatDeletion>(entity, false);

                // Idempotency: skip if already Deleted (a second producer flip this frame, or a
                // restored drone that re-terminalized). The deferred add below is not yet
                // visible via the lookup, but disabling the bit above prevents the same entity
                // re-entering this query before the Deleted migration flushes.
                if (m_DeletedLookup.HasComponent(entity))
                    continue;

                // Structural add — archetype migration. Legal here because this system runs in
                // Modification4, where the render batch pipeline consumes the change later in
                // the same MainLoop (mirror of vanilla IgniteSystem / ModFireApplySystem).
                // RENDER-SAFE-DELETE: drone Deleted add is in-phase here (Modification4, before
                // the render batch pass); CIVIC498 allow-marker — never move this to GameSimulation.
                ecb.AddComponent<Deleted>(entity);
                applied++;
            }

            if (ecbCreated)
                m_ModificationBarrier.AddJobHandleForProducer(Dependency);

            if (applied > 0 && Log.IsDebugEnabled)
                Log.Debug($"applied {applied} deletion(s)");
        }
    }
}
