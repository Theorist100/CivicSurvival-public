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

namespace CivicSurvival.Domains.Cognitive.Ops.Systems
{
    /// <summary>
    /// Pause-safe intake for buckwheat aid distribution (AXIOM 14). Runs in
    /// ModificationEnd (ticks while paused) and applies each request via
    /// <see cref="BuckwheatSystem.DistributeToDistrictImmediate"/> — the reserve,
    /// cooldowns and penalty buffer are managed/main-thread state, no money, so the
    /// apply is synchronous. Procurement, expiry and the reserve simulation stay in
    /// the throttled GameSimulation tick.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.AidDistribution)]
    [TransientConsumerReconcile(typeof(AidDistributionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: buckwheat reserve, penalties, trust request and result event are emitted only after this consumer runs; pre-consume load loss is reissuable.")]
    public partial class BuckwheatAidIntakeSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("BuckwheatAidIntake");

        private EntityQuery m_RequestQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private BuckwheatSystem? m_Buckwheat;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<AidDistributionRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            RequireForUpdate(m_RequestQuery);

            Log.Info("Created (pause-safe aid intake)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Buckwheat ??= FeatureRegistry.Instance.Require<BuckwheatSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty || m_Buckwheat == null)
                return;

            var ecb = m_ModificationEndBarrier.CreateCommandBuffer();
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<AidDistributionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                bool success = m_Buckwheat.DistributeToDistrictImmediate(request.ValueRO.DistrictIndex, ecb, out FixedString64Bytes failReason);
                if (success)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.AidDistribution, World.Time.ElapsedTime);
                else
                    RequestResultEmitter.EmitFixedReason(ecb, meta.ValueRO, RequestKind.AidDistribution, RequestStatus.Failed, failReason, World.Time.ElapsedTime);

                if (Log.IsDebugEnabled) Log.Debug($"Processed AidDistributionRequest: district {request.ValueRO.DistrictIndex} = {(success ? "Success" : "Failed")}");
                ecb.DestroyEntity(entity);
            }

            m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }
    }
}
