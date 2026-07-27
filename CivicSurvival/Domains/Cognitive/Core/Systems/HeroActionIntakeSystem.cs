using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Common;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Pause-safe intake for player hero commands (AXIOM 14): deploy, recall and
    /// set-mode. Runs in ModificationEnd (ticks while paused) and applies each
    /// request via <see cref="HeroDeploymentSystem.TryApplyHeroActionImmediate"/> —
    /// payment is a synchronous BudgetTransactionResolver deduct with every
    /// eligibility check before money moves, so no retained budget intent or refund
    /// path exists. The Arestovych debt accrual stays in the owner's throttled
    /// GameSimulation tick.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.HeroAction)]
    [TransientConsumerReconcile(typeof(HeroActionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: hero state and payment are committed only while this consumer runs; a pre-consume load has no side-effect and the click is reissuable.")]
    public partial class HeroActionIntakeSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("HeroActionIntake");

        private EntityQuery m_RequestQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private HeroDeploymentSystem? m_HeroDeployment;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<HeroActionRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            RequireForUpdate(m_RequestQuery);

            Log.Info("Created (pause-safe hero intake)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_HeroDeployment ??= FeatureRegistry.Instance.Require<HeroDeploymentSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty || m_HeroDeployment == null)
                return;

            var ecb = m_ModificationEndBarrier.CreateCommandBuffer();
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<HeroActionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                bool success = m_HeroDeployment.TryApplyHeroActionImmediate(
                    request.ValueRO.Action,
                    request.ValueRO.Mode,
                    out FixedString64Bytes failReason);
                if (success)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.HeroAction, World.Time.ElapsedTime);
                else
                    RequestResultEmitter.EmitFixedReason(ecb, meta.ValueRO, RequestKind.HeroAction, RequestStatus.Failed, failReason, World.Time.ElapsedTime);

                if (Log.IsDebugEnabled) Log.Debug($"Processed {request.ValueRO.Action}: {(success ? "Success" : "Failed")}");
                ecb.DestroyEntity(entity);
            }

            m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }
    }
}
