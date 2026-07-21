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

namespace CivicSurvival.Domains.Mobilization.Systems
{
    /// <summary>
    /// Pause-safe intake for player mobilization commands (AXIOM 14): conscription
    /// toggle and Call To Arms. Runs in ModificationEnd (ticks while paused) and
    /// applies each request via
    /// <see cref="MobilizationSystem.ApplyMobilizationActionImmediate"/> — all
    /// mobilization command state is managed (fields/cooldowns/reputation), no money,
    /// so the apply is synchronous. The manpower/casualty simulation stays in
    /// GameSimulation.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.Mobilization)]
    [HandlesRequestKind(RequestKind.ConscriptionToggle)]
    [TransientConsumerReconcile(typeof(MobilizationRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: conscription/call-to-arms state changes are committed only while this consumer runs, so pre-consume load loss is reissuable.")]
    public partial class MobilizationRequestIntakeSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("MobilizationRequestIntake");

        private EntityQuery m_RequestQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private MobilizationSystem? m_Mobilization;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<MobilizationRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            RequireForUpdate(m_RequestQuery);

            Log.Info("Created (pause-safe mobilization intake)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Mobilization ??= FeatureRegistry.Instance.Require<MobilizationSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_RequestQuery.IsEmpty || m_Mobilization == null)
                return;

            var ecb = m_ModificationEndBarrier.CreateCommandBuffer();
            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<MobilizationRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                bool success = m_Mobilization.ApplyMobilizationActionImmediate(request.ValueRO.Action, out var kind, out var failReason);
                if (success)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, kind, World.Time.ElapsedTime);
                else
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, kind, RequestStatus.Failed, ReasonId.FromRuntime(failReason), World.Time.ElapsedTime);

                if (Log.IsDebugEnabled) Log.Debug($"Processed {request.ValueRO.Action}: {(success ? "Success" : "Failed")}");
                ecb.DestroyEntity(entity);
            }

            m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }
    }
}
