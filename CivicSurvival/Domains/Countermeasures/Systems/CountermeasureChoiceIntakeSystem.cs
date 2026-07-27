using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using Game.Common;
using Unity.Entities;

namespace CivicSurvival.Domains.Countermeasures.Systems
{
    /// <summary>
    /// Pause-safe intake for the Investigation/Police decision modal (AXIOM 14).
    ///
    /// The modal is a forced choice: while it is open there is no other way to
    /// close it, and players routinely pause to think it over. The FSM tick
    /// (heat decay, phase timers) stays in GameSimulation, but the *choice*
    /// must apply at selectedSpeed==0 — so this thin system runs in
    /// ModificationEnd and drains CountermeasureChoiceRequest entities by
    /// calling the FSM owner's synchronous drain
    /// (<see cref="CountermeasuresUpdateSystem.DrainChoiceRequestsImmediate"/>):
    /// investigation bribe pays the shadow wallet synchronously, police
    /// branches were already sync, results emit on the ModificationEndBarrier.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.CountermeasureChoice)]
    [TransientConsumerReconcile(typeof(CountermeasureChoiceRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI choice: durable FSM changes are applied synchronously while this consumer processes the request, so pre-consume load loss is reissuable.")]
    public partial class CountermeasureChoiceIntakeSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("CountermeasureChoiceIntake");

        private EntityQuery m_RequestQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private CountermeasuresUpdateSystem? m_Fsm;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<CountermeasureChoiceRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            RequireForUpdate(m_RequestQuery);

            Log.Info("Created (pause-safe choice intake)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Fsm ??= FeatureRegistry.Instance.Require<CountermeasuresUpdateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty || m_Fsm == null)
                return;

            var ecb = m_ModificationEndBarrier.CreateCommandBuffer();
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<CountermeasureChoiceRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                // ApplyChoiceImmediate mutates the FSM singletons via EntityManager.Set —
                // a data write on a different archetype, not a structural change, so the
                // request iteration stays valid; the request entity dies via ECB.
                var status = m_Fsm.ApplyChoiceImmediate(request.ValueRO.ChoiceType, request.ValueRO.ChoiceValue, ecb, out var reasonId);
                if (status == RequestStatus.Success)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.CountermeasureChoice, World.Time.ElapsedTime);
                else
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.CountermeasureChoice, RequestStatus.Failed, ReasonId.FromRuntime(reasonId), World.Time.ElapsedTime);

                if (Log.IsDebugEnabled) Log.Debug($"Processed {request.ValueRO.ChoiceType}: {status}");
                ecb.DestroyEntity(entity);
            }

            m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }
    }
}
