using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.AirDefense;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Despawns interceptor missiles by lifetime. Runs in Modification4 (render-safe phase, mirror of
    /// the threat deletion consumer): a missile older than its lifetime gets vanilla <c>Deleted</c>
    /// added so the vanilla CleanUp pipeline tears down its GPU batch.
    ///
    /// <para><b>Render-completion gate (CIVIC508).</b> <c>AddComponent&lt;Deleted&gt;</c> migrates the
    /// interceptor chunk. Since InterceptorMovementSystem writes Transform/Moving/TransformFrame from a
    /// Burst <c>InterceptorRenderWriteJob</c> whose handle is kept out of <c>Dependency</c>, that worker
    /// may still be reading the chunk — migrating it underneath would be a null-chunk-ptr crash. So this
    /// system drains the published render handle via <c>IRenderWriteBarrier.Consume</c> before recording
    /// any <c>Deleted</c> command (same contract as the threat deletion consumer).</para>
    ///
    /// Restored-on-load purge is NOT here — it lives in <c>InterceptorLoadPurgeSystem</c>
    /// (ModificationEnd one-shot, before the first PreCulling, keyed on the surviving InterceptorTag),
    /// because a load-time purge in GameSimulation (IPostLoadValidation, frame +2) runs after the
    /// render crash and queries the stripped Interceptor component.
    /// </summary>
    [ActIndependent]
    public partial class InterceptorCleanupSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("InterceptorCleanupSystem");

        // ~12 s at 60 sim fps. Sized to cover the full Patriot engagement envelope (4 km at 450 m/s ≈
        // 9-10 s to close on a fleeing drone) now that the missile's ARRIVAL drives the threat explosion
        // (deferred-intercept): a shorter timer would age the missile out before it reaches a far target,
        // firing the explosion at despawn (missile already gone) instead of at visual contact. Acts as a
        // safety cap for the rare stern-chase that never closes (e.g. a ballistic at max range).
        // Measured in sim frames (frameIndex), NOT wall-clock: this system ticks in Modification4 (which
        // runs in pause), but frameIndex is frozen in pause (the sim does not tick), so a paused,
        // visually-frozen missile does not age out and vanish (pause-safe, Axiom 14).
        private const uint INTERCEPTOR_LIFETIME_FRAMES = 720u;

        private EntityQuery m_InterceptorQuery;
        private ModificationBarrier4 m_ModificationBarrier = null!;
        private SimulationSystem m_SimulationSystem = null!;
        private IRenderWriteBarrier m_RenderWriteBarrier = null!;

        // Coast-resolution reads. Sync-free: this system runs in Modification4, where none of these
        // components have an in-flight Burst writer (all TMS jobs live in GameSimulation). Identity
        // (generation + isBallistic) is carried on the Interceptor → no Shahed/Ballistic lookup.
        private ComponentLookup<ShahedCombatState> m_CombatStateLookupRO;
        private ComponentLookup<BallisticInterceptState> m_BallisticInterceptLookupRO;
        private ComponentLookup<ThreatPosition> m_ThreatPosLookupRO;
        private ComponentLookup<Deleted> m_DeletedLookupRO;
        private ComponentLookup<PendingDestruction> m_PendingDestructionLookupRO;
        [System.NonSerialized] private IThreatTerminalizationSink m_TerminalizationQueue = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_InterceptorQuery = GetEntityQuery(
                ComponentType.ReadOnly<Interceptor>(),
                ComponentType.ReadOnly<InterceptorTag>(),
                ComponentType.Exclude<Deleted>());
            m_ModificationBarrier = World.GetOrCreateSystemManaged<ModificationBarrier4>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CombatStateLookupRO = GetComponentLookup<ShahedCombatState>(true);
            m_BallisticInterceptLookupRO = GetComponentLookup<BallisticInterceptState>(true);
            m_ThreatPosLookupRO = GetComponentLookup<ThreatPosition>(true);
            m_DeletedLookupRO = GetComponentLookup<Deleted>(true);
            m_PendingDestructionLookupRO = GetComponentLookup<PendingDestruction>(true);
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
            uint now = m_SimulationSystem.frameIndex;

            m_CombatStateLookupRO.Update(this);
            m_BallisticInterceptLookupRO.Update(this);
            m_ThreatPosLookupRO.Update(this);
            m_DeletedLookupRO.Update(this);
            m_PendingDestructionLookupRO.Update(this);
            m_TerminalizationQueue ??= ServiceRegistry.Instance.Require<IThreatTerminalizationSink>();

            // CIVIC508: drain the in-flight interceptor render job before any AddComponent<Deleted> can
            // migrate its chunk under the worker. The barrier self-prunes completed handles, so on frames
            // with no live render job this is a no-op; mirrors the threat deletion consumer's gate.
            m_RenderWriteBarrier.Consume(GetType(), RenderWriteComponentMask.InterceptorRender);

            EntityCommandBuffer? ecb = null;
            int despawned = 0;
            foreach (var (interceptor, entity) in
                     SystemAPI.Query<RefRO<Interceptor>>()
                         .WithAll<InterceptorTag>()
                         .WithNone<Deleted>()
                         .WithEntityAccess())
            {
                ref readonly var ic = ref interceptor.ValueRO;
                bool reached = ic.HasReachedTarget;
                // Unsigned subtraction handles frameIndex wraparound; LaunchFrame <= now in practice.
                bool aged = now - ic.LaunchFrame >= INTERCEPTOR_LIFETIME_FRAMES;
                if (!reached && !aged)
                    continue;

                // Resolution trigger #2: if the chased threat is still coasting when this missile
                // leaves the airspace (reached its target, or aged out on a tail-chase before
                // arriving), terminalize the threat at its live position. The sink dedups by entity,
                // so a same-frame GameSimulation arrival queue (trigger #1) collapses with this one.
                // Cross-domain via the Core sink + factory (Axiom 5).
                ResolveCoastIfAwaiting(
                    new Entity { Index = ic.ThreatIndex, Version = ic.ThreatVersion },
                    ic.IsBallistic, ic.ThreatGeneration);

                ecb ??= m_ModificationBarrier.CreateCommandBuffer();
                ecb.Value.AddComponent<Deleted>(entity);
                despawned++;
            }

            if (despawned > 0 && Log.IsDebugEnabled)
                Log.Debug($"[Interceptor] despawned {despawned} expired/reached missile(s)");
        }

        private void ResolveCoastIfAwaiting(Entity threat, bool isBallistic, int generation)
        {
            if (threat == Entity.Null)
                return;
            if (m_DeletedLookupRO.HasComponent(threat)
                || (m_PendingDestructionLookupRO.HasComponent(threat) && m_PendingDestructionLookupRO.IsComponentEnabled(threat)))
                return;

            float3 pos = m_ThreatPosLookupRO.TryGetComponent(threat, out var tp) ? tp.Position : float3.zero;

            bool awaiting = isBallistic
                ? (m_BallisticInterceptLookupRO.TryGetComponent(threat, out var bis) && bis.AwaitingInterceptorImpact)
                : (m_CombatStateLookupRO.TryGetComponent(threat, out var cs) && cs.AwaitingInterceptorImpact);
            if (!awaiting)
                return;

            m_TerminalizationQueue.Queue(ThreatTerminalOutcome.Intercept(
                threat, pos, isBallistic, generation,
                debrisFallTime: isBallistic ? 0f : BalanceConfig.Current.Threats.DebrisFallTime));
        }
    }
}
