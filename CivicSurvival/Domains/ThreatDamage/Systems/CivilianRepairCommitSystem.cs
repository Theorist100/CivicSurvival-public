using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Applies or rejects resolved civilian repair intents in ModificationEnd.
    /// CivilianDamageSystem remains the sole writer of CivilianWarDamage.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.CivilianRepair)]
    public partial class CivilianRepairCommitSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("CivilianRepairCommitSystem");

        private EntityQuery m_IntentQuery;
        private ModificationEndBarrier m_Barrier = null!;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private readonly HashSet<long> m_ProcessedBuildings = new();
        [System.NonSerialized] private int m_ProcessedBuildingsFrame = -1;
#pragma warning disable CIVIC229 // System reference — same-domain owner write boundary.
        private CivilianDamageSystem m_DamageSystem = null!;
#pragma warning restore CIVIC229
        private CivicDependencyWire m_DependencyWire = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<CivilianRepairIntent>());
            m_Barrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_DependencyWire = new CivicDependencyWire(nameof(CivilianRepairCommitSystem));
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_DamageSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<CivilianDamageSystem>());
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            m_StorageInfoLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);

            int frame = UnityEngine.Time.frameCount;
            if (frame != m_ProcessedBuildingsFrame)
            {
                m_ProcessedBuildings.Clear();
                m_ProcessedBuildingsFrame = frame;
            }

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            foreach (var (intentRef, entity) in
                SystemAPI.Query<RefRW<CivilianRepairIntent>>()
                .WithEntityAccess())
            {
                var intent = intentRef.ValueRO;
                if (intent.Applied || !intent.BudgetResolved)
                    continue;

                if (!ecbCreated)
                {
                    ecb = m_Barrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                intentRef.ValueRW.Applied = true;

                if (!intent.BudgetSucceeded)
                {
                    RejectIntent(ecb, entity, intent, ReasonIds.CivilianRepairInsufficientFunds, refund: false);
                    continue;
                }

                if (!BuildingExists(intent.Building.Index, intent.Building.Version))
                {
                    RejectIntent(ecb, entity, intent, ReasonIds.CivilianRepairNotFound, refund: true);
                    continue;
                }

                if (!m_ProcessedBuildings.Add(BuildingIdentityKey.Pack(intent.Building.Index, intent.Building.Version)))
                {
                    RejectIntent(ecb, entity, intent, ReasonIds.CivilianRepairBudgetPending, refund: true);
                    continue;
                }

                // Distinguish "target busy" from "target missing" so the toast
                // tells the player something actionable. Without this split a
                // building that is already being repaired would surface the
                // misleading "not found" reason. The query path uses the same
                // damage-map lookup that ApplyRepairStart re-runs internally.
                if (m_DamageSystem.IsRepairTargetUnderRepair(in intent))
                {
                    if (intent.BudgetSucceeded && intent.Cost > 0)
                    {
                        Log.Warn($"Civilian repair replay confirmed for building {intent.Building.Index}:{intent.Building.Version}; already under repair, suppressing refund for paid intent.");
                        ApplyIntent(ecb, entity, intent);
                    }
                    else
                    {
                        RejectIntent(ecb, entity, intent, ReasonIds.CivilianRepairAlreadyActive, refund: true);
                    }
                    continue;
                }

                if (!m_DamageSystem.ApplyRepairStart(ref intent, ecb))
                {
                    RejectIntent(ecb, entity, intent, ReasonIds.CivilianRepairNotFound, refund: true);
                    continue;
                }

                ApplyIntent(ecb, entity, intent);
            }

            if (ecbCreated)
                m_Barrier.AddJobHandleForProducer(Dependency);
        }

        private bool BuildingExists(int buildingIndex, int buildingVersion)
        {
            var building = new Entity { Index = buildingIndex, Version = buildingVersion };
            return m_StorageInfoLookup.Exists(building)
                && !m_DeletedLookup.HasComponent(building)
                && !m_DestroyedLookup.HasComponent(building);
        }

        private void ApplyIntent(EntityCommandBuffer ecb, Entity intentEntity, CivilianRepairIntent intent)
        {
            if (intent.RequestId != 0)
            {
                EmitReportedRepairResult(ecb, intent.RequestId, RequestStatus.Success, ReasonId.None);
                RequestResultBridge.PublishTerminalForBegun(
                    RequestResultBridge.CivilianRepair,
                    intent.RequestId,
                    RequestStatus.Success);
            }

            ecb.DestroyEntity(intentEntity);
            Log.Info($"Civilian repair confirmed: building={intent.Building.Index}, type={intent.RepairType}, cost={intent.Cost}");
        }

        private void RejectIntent(
            EntityCommandBuffer ecb,
            Entity intentEntity,
            CivilianRepairIntent intent,
            ReasonId reasonId,
            bool refund)
        {
            // If a refund was owed and the refund itself failed, the player has
            // been bilked silently — surface this as a distinct reason so the
            // toast tells them refund failed, not "not found". Mirrors
            // PlantRepairCommitSystem.RefundRepairPayment / PlantRepairRefundFailed.
            var terminalReason = reasonId;
            if (refund && !m_DamageSystem.RefundRepair(in intent, ecb))
                terminalReason = ReasonIds.CivilianRepairRefundFailed;

            if (intent.RequestId != 0)
            {
                EmitReportedRepairResult(ecb, intent.RequestId, RequestStatus.Failed, terminalReason);
                RequestResultBridge.PublishTerminalForBegun(
                    RequestResultBridge.CivilianRepair,
                    intent.RequestId,
                    RequestStatus.Failed,
                    terminalReason.ToString());
            }

            ecb.DestroyEntity(intentEntity);
            Log.Warn($"Civilian repair rejected: building={intent.Building.Index}, reason={terminalReason}");
        }

        private void EmitReportedRepairResult(
            EntityCommandBuffer ecb,
            int requestId,
            RequestStatus status,
            ReasonId reasonId)
        {
            var resultEntity = status == RequestStatus.Success
                ? RequestResultEmitter.EmitSuccess(
                    ecb,
                    requestId,
                    RequestKind.CivilianRepair,
                    SystemAPI.Time.ElapsedTime)
                : RequestResultEmitter.Emit(
                    ecb,
                    requestId,
                    RequestKind.CivilianRepair,
                    status,
                    reasonId,
                    SystemAPI.Time.ElapsedTime);
            ecb.AddComponent<Reported>(resultEntity);
        }
    }
}
