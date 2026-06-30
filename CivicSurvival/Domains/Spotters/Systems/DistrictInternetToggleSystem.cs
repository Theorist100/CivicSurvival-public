using Game.Common;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Spotters.Systems
{
    /// <summary>
    /// Pause-safe district internet toggle consumer. Validates the request in
    /// ModificationEnd and delegates the mutation to SpotterAggregateSystem,
    /// preserving the SpotterCountermeasuresState owner boundary.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.DistrictInternetToggle)]
    [TransientConsumerReconcile(typeof(DistrictInternetToggleRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: durable InternetDisabledBuffer state is mutated by the SpotterAggregateSystem owner during this ModificationEnd consumer, so pre-consume load loss is reissuable.")]
    public partial class DistrictInternetToggleSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("DistrictInternetToggleSystem");
        private const int MAX_DISTRICT_INDEX = 10000;

        private EntityQuery m_RequestQuery;
        private EntityQuery m_CurrentActQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
#pragma warning disable CIVIC229 // System reference — internet buffer mutation stays with the singleton owner.
        [System.NonSerialized] private SpotterAggregateSystem? m_Aggregate;
#pragma warning restore CIVIC229

        protected override void OnCreate()
        {
            base.OnCreate();
            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<DistrictInternetToggleRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();

            RequireForUpdate(m_RequestQuery);
            RequireForUpdate<CurrentActSingleton>();

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Aggregate ??= FeatureRegistry.Instance.Require<SpotterAggregateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            // Request entity is destroyed synchronously via EntityManager so duplicate
            // ticks at >1x sim speed see an empty query (no per-frame dedup state needed).
            // The ModificationEndBarrier ECB stays for the *result* entity (Reported tag
            // flows into RequestResultCollectorSystem on the next GameSim tick).
            // Audit-verified: no scheduled jobs read DistrictInternetToggleRequest, so
            // the sync destroy creates no sync point.
            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;
            var seenDistricts = new NativeHashSet<int>(4, Allocator.Temp);

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<DistrictInternetToggleRequest>, RefRO<RequestMeta>>()
                    .WithEntityAccess())
            {
                if (!ecbCreated)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                var districtIndex = request.ValueRO.DistrictEntityIndex;
                RequestStatus status;
                string failReason = "";

                if (!SpotterEligibility.CanToggleInternetForDistrict(
                        GetCurrentAct(),
                        districtIndex,
                        MAX_DISTRICT_INDEX,
                        out failReason))
                {
                    status = RequestStatus.Failed;
                }
                else if (!seenDistricts.Add(districtIndex))
                {
                    failReason = ReasonIds.SpotterDuplicateAction;
                    status = RequestStatus.Failed;
                }
                else
                {
                    m_Aggregate!.ToggleInternetForDistrict(districtIndex);
                    status = RequestStatus.Success;
                }

                EmitTerminal(ecb, meta.ValueRO, status, failReason);
#pragma warning disable CIVIC006, CIVIC208 // Single-shot UI command consumer: no scheduled jobs read DistrictInternetToggleRequest (audit-verified), so synchronous destroy creates no sync point.
                EntityManager.DestroyEntity(entity);
#pragma warning restore CIVIC006, CIVIC208
            }

            seenDistricts.Dispose();
            if (ecbCreated)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private Act GetCurrentAct()
        {
            return m_CurrentActQuery.GetSingleton<CurrentActSingleton>().CurrentAct;
        }

        private void EmitTerminal(
            EntityCommandBuffer ecb,
            in RequestMeta meta,
            RequestStatus status,
            string reasonId)
        {
            Entity resultEntity;
            if (status == RequestStatus.Success)
            {
                resultEntity = RequestResultEmitter.EmitSuccess(
                    ecb,
                    meta,
                    RequestKind.DistrictInternetToggle,
                    SystemAPI.Time.ElapsedTime);
            }
            else
            {
                string resultReason = string.IsNullOrEmpty(reasonId)
                    ? ReasonIds.SpotterActionFailed
                    : reasonId;
                resultEntity = RequestResultEmitter.Emit(
                    ecb,
                    meta,
                    RequestKind.DistrictInternetToggle,
                    status,
                    ReasonId.FromRuntime(resultReason),
                    SystemAPI.Time.ElapsedTime);
                reasonId = resultReason;
            }

            ecb.AddComponent<Reported>(resultEntity);

            RequestResultBridge.PublishTerminalForBegun(
                RequestResultBridge.DistrictInternetToggle,
                meta.RequestId,
                status,
                reasonId,
                discriminatorKind: meta.DiscriminatorKind.Length > 0 ? meta.DiscriminatorKind.ToString() : "none",
                discriminatorValue: meta.DiscriminatorValue.ToString());
        }
    }
}
