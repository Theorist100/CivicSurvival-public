using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using Game.Common;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Placement
{
    /// <summary>
    /// Pause-safe building-placement commit. Converts resolved placement intents into mod
    /// entities in ModificationEnd, so UI placement does not depend on GameSimulation ticks.
    /// The two-phase, save-safe transaction state machine (apply/reject, refund settlement,
    /// terminal-emit idempotency) is generic; the per-kind work (create the entity, refund the
    /// credit, queue the budget refund, map the reason) is delegated to the intent kind's handler.
    /// </summary>
    [ActIndependent]
    public partial class BuildingPlacementCommitSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("BuildingPlacementCommit");

        private EntityQuery m_IntentQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private readonly System.Collections.Generic.HashSet<long> m_ProcessedBuildings = new();
        [System.NonSerialized] private int m_ProcessedBuildingsFrame = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<BuildingPlacementIntent>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            Log.Info("Created");
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
                SystemAPI.Query<RefRW<BuildingPlacementIntent>>()
                .WithEntityAccess())
            {
                ref var intent = ref intentRef.ValueRW;

                if (!BuildingPlacementHandlerRegistry.TryGet(intent.Kind, out var handler))
                    continue;

                // A terminal reject (ApplyResolved && !ApplySucceeded — both serialized) must NEVER
                // re-enter the apply path. SettleRejectRefund is re-entrant: it retries any still-owed
                // refund, then emits the terminal result and destroys the intent. A terminal APPLY keeps
                // ApplySucceeded=true, so it falls through to the Applied=false reprocess that re-creates
                // an installation lost to a pre-playback save — that save-safe path must stay intact.
                if (intent.ApplyResolved && !intent.ApplySucceeded)
                {
                    if (!ecbCreated)
                    {
                        ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                        ecbCreated = true;
                    }
                    SettleRejectRefund(handler, ecb, entity, ref intent);
                    continue;
                }

                if (intent.Applied)
                    continue;

                if (intent.RequiresBudget && !intent.BudgetResolved)
                    continue;

                if (intent.ReservedCreditKind != 0 && !intent.CreditResolved)
                    continue;

                if (!ecbCreated)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                intentRef.ValueRW.Applied = true;

                if (intent.RequiresBudget && !intent.BudgetSucceeded)
                {
                    RejectIntent(handler, ecb, entity, ref intent, "BudgetFailed");
                    continue;
                }

                if (intent.ReservedCreditKind != 0 && !intent.CreditSucceeded)
                {
                    RejectIntent(handler, ecb, entity, ref intent, "CreditFailed");
                    continue;
                }

                if (!BuildingExists(intent.Building.Index, intent.Building.Version))
                {
                    RejectIntent(handler, ecb, entity, ref intent, "BuildingMissingBeforeApply");
                    continue;
                }

                if (!m_ProcessedBuildings.Add(BuildingIdentityKey.Pack(intent.Building.Index, intent.Building.Version)))
                {
                    RejectIntent(handler, ecb, entity, ref intent, "DuplicateBuilding");
                    continue;
                }

                ApplyIntent(handler, ecb, entity, ref intent);
            }

            if (ecbCreated)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private bool BuildingExists(int buildingIndex, int buildingVersion)
        {
            var building = new Entity { Index = buildingIndex, Version = buildingVersion };
            return m_StorageInfoLookup.Exists(building)
                && !m_DeletedLookup.HasComponent(building)
                && !m_DestroyedLookup.HasComponent(building);
        }

        private void ApplyIntent(IBuildingPlacementHandler handler, EntityCommandBuffer ecb, Entity intentEntity, ref BuildingPlacementIntent intent)
        {
            intent.ApplyResolved = true;
            intent.ApplySucceeded = true;
            intent.TerminalReason = BuildingPlacementTerminalReason.Applied;

            handler.Commit(World, ecb, intentEntity, in intent);

            if (intent.RequestId != 0)
            {
                EmitReportedPlacementResult(handler, ecb, intent.RequestId, RequestStatus.Success, ReasonId.None);
                RequestResultBridge.PublishTerminalForBegun(
                    handler.ResultBridgeKey,
                    intent.RequestId,
                    RequestStatus.Success);
            }

            ecb.DestroyEntity(intentEntity);
            Log.Info($"Placement confirmed: kind={intent.Kind}, building={intent.Building.Index}, cost={intent.Cost}");
        }

        private void RejectIntent(IBuildingPlacementHandler handler, EntityCommandBuffer ecb, Entity intentEntity, ref BuildingPlacementIntent intent, string reason)
        {
            intent.ApplyResolved = true;
            intent.ApplySucceeded = false;
            intent.TerminalReason = MapTerminalReason(reason);

            if (ShouldDeletePlacedObjectOnReject(intent.TerminalReason))
                DeletePlacedObjectIfPresent(ecb, intent);

            SettleRejectRefund(handler, ecb, intentEntity, ref intent);
        }

        /// <summary>
        /// Settles a terminal reject: refunds the compensating value (a credit counter for
        /// credit-only intents, the paid budget for paid ones — a purse-marker credit kind on a
        /// paid intent refunds through the budget leg), emits the player-facing result, and
        /// destroys the intent. Re-entrant
        /// across the OnUpdateImpl top-of-loop retry and the 2x-3x same-frame replays. The terminal emit
        /// is synchronous and pause-safe; the budget refund drains later through GameSimulation and the
        /// intent is destroyed only once BOTH the terminal answer is emitted AND the refund has settled.
        /// </summary>
        private void SettleRejectRefund(IBuildingPlacementHandler handler, EntityCommandBuffer ecb, Entity intentEntity, ref BuildingPlacementIntent intent)
        {
            // Credit and budget are orthogonal (ReservedCreditKind counts a granted credit,
            // PayingPurse routes money) and mutually exclusive by construction. The
            // !RequiresBudget gate keeps that invariant load-bearing: if an intent ever carried
            // both, the credit refund must not swallow the single RefundResolved slot and starve
            // the budget refund below.
            if (!intent.RefundResolved
                && intent.ReservedCreditKind != 0
                && !intent.RequiresBudget
                && intent.CreditSucceeded)
            {
                intent.RefundSucceeded = handler.RefundCredit(World, intent.ReservedCreditKind, intent.PlacementId);
                intent.RefundResolved = true;
                if (!intent.RefundSucceeded)
                    Log.Error($"Placement credit refund failed: owner singleton unavailable (kind={intent.Kind}, building={intent.Building.Index}, reason={intent.TerminalReason})");
            }

            // Budget refund is owed for ANY terminal reject once the payment actually landed
            // (BudgetSucceeded) — the terminal reason does not matter: a charged player whose
            // placement did not happen is always owed the money back. BudgetFailed never paid
            // (BudgetSucceeded=false), so it never reaches this.
            bool budgetRefundOwed =
                intent.RequiresBudget
                && intent.BudgetSucceeded
                && intent.Cost > 0;

            if (!intent.RefundResolved && budgetRefundOwed)
            {
                // A purse without a deferred request pipeline (shadow wallet) settles
                // synchronously in the handler; the queued city-budget path would refund
                // the wrong purse for it.
                if (handler.TryRefundBudgetSync(World, in intent, out var syncSucceeded))
                {
                    intent.RefundSucceeded = syncSucceeded;
                    intent.RefundResolved = true;
                    if (syncSucceeded)
                        Log.Info($"Budget refund ${intent.Cost} settled synchronously for building {intent.Building.Index} (reason: {intent.TerminalReason})");
                    else
                        Log.Error($"Synchronous budget refund failed: purse unavailable (kind={intent.Kind}, building={intent.Building.Index}, reason={intent.TerminalReason})");
                }
                else if (TryDrainBudgetRefundResult(handler, ecb, ref intent, out var refundSucceeded))
                {
                    intent.RefundSucceeded = refundSucceeded;
                    intent.RefundResolved = true;
                    if (refundSucceeded)
                        Log.Info($"Budget refund ${intent.Cost} for building {intent.Building.Index} (reason: {intent.TerminalReason})");
                    else
                        Log.Warn($"Budget refund terminal failure for building {intent.Building.Index} (reason: {intent.TerminalReason})");
                }
                else
                {
                    // Not drained yet — queue once and leave RefundResolved=false; the top-of-loop
                    // retry drains it next tick. The terminal emit below is deliberately NOT gated on
                    // this — the player sees the reject immediately (in pause), money settles later.
                    QueueBudgetRefundIfMissing(handler, ecb, in intent);
                }
            }

            if (!intent.RefundResolved && !budgetRefundOwed)
            {
                intent.RefundResolved = true;
                intent.RefundSucceeded = true;
            }

            if (!intent.TerminalEmitted)
            {
                if (intent.RequestId != 0)
                {
                    var reasonId = handler.MapTerminalReasonToReasonId(intent.TerminalReason);
                    EmitReportedPlacementResult(handler, ecb, intent.RequestId, RequestStatus.Failed, reasonId);
                    RequestResultBridge.PublishTerminalForBegun(
                        handler.ResultBridgeKey,
                        intent.RequestId,
                        RequestStatus.Failed,
                        reasonId.ToString());
                }

                intent.TerminalEmitted = true;
                Log.Warn($"Placement rejected: kind={intent.Kind}, building={intent.Building.Index}, reason={intent.TerminalReason}");
            }

            if (intent.RefundResolved && intent.TerminalEmitted)
                ecb.DestroyEntity(intentEntity);
        }

        /// <summary>
        /// Observes the retained BudgetAddFundsResult for this intent's refund request (matched by the
        /// per-placement handler operation key) and destroys the result entity. Returns false until
        /// BudgetResolutionSystem has drained the request — in pause it never ticks, so the refund settles
        /// after unpause while the terminal emit has already answered the UI.
        /// </summary>
        private bool TryDrainBudgetRefundResult(IBuildingPlacementHandler handler, EntityCommandBuffer ecb, ref BuildingPlacementIntent intent, out bool succeeded)
        {
            succeeded = false;
            string operationKey = handler.BudgetRefundOperationKey(intent.PlacementId);
            foreach (var (requestRef, resultRef, entity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                succeeded = resultRef.ValueRO.Succeeded;
                ecb.DestroyEntity(entity);
                return true;
            }

            return false;
        }

        private void QueueBudgetRefundIfMissing(IBuildingPlacementHandler handler, EntityCommandBuffer ecb, in BuildingPlacementIntent intent)
        {
            string operationKey = handler.BudgetRefundOperationKey(intent.PlacementId);
            foreach (var requestRef in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (requestRef.ValueRO.OperationKey.ToString() == operationKey)
                    return;
            }

            handler.QueueBudgetRefund(ecb, intent.Cost, operationKey);
        }

        private void DeletePlacedObjectIfPresent(EntityCommandBuffer ecb, in BuildingPlacementIntent intent)
        {
            var building = intent.Building.ToEntity();
            if (!m_StorageInfoLookup.Exists(building)
                || m_DeletedLookup.HasComponent(building)
                || m_DestroyedLookup.HasComponent(building))
            {
                return;
            }

            ecb.AddComponent<Deleted>(building);
        }

        private static bool ShouldDeletePlacedObjectOnReject(BuildingPlacementTerminalReason reason)
        {
            // DuplicateBuilding rejects a second intent that targets an already accepted placement;
            // the visible object belongs to the earlier successful intent.
            return reason != BuildingPlacementTerminalReason.DuplicateBuilding;
        }

        private void EmitReportedPlacementResult(
            IBuildingPlacementHandler handler,
            EntityCommandBuffer ecb,
            int requestId,
            RequestStatus status,
            ReasonId reasonId)
        {
            var resultEntity = status == RequestStatus.Success
                ? RequestResultEmitter.EmitSuccess(
                    ecb,
                    requestId,
                    handler.RequestKind,
                    SystemAPI.Time.ElapsedTime)
                : RequestResultEmitter.Emit(
                    ecb,
                    requestId,
                    handler.RequestKind,
                    status,
                    reasonId,
                    SystemAPI.Time.ElapsedTime);
            ecb.AddComponent<Reported>(resultEntity);
        }

        private static BuildingPlacementTerminalReason MapTerminalReason(string raw)
        {
            if (raw == "BudgetFailed")
                return BuildingPlacementTerminalReason.BudgetFailed;
            if (raw == "CreditFailed")
                return BuildingPlacementTerminalReason.CreditFailed;
            if (raw == "BuildingMissingBeforeApply")
                return BuildingPlacementTerminalReason.BuildingMissingBeforeApply;
            if (raw == "DuplicateBuilding")
                return BuildingPlacementTerminalReason.DuplicateBuilding;
            return BuildingPlacementTerminalReason.PlacementFailed;
        }
    }
}
