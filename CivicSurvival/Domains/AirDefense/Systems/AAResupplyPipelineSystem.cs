using Game;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Logic;
using System.Threading;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Gates paid AA resupply on one retained budget result per frozen resupply batch.
    /// </summary>
    [ActIndependent]
    public partial class AAResupplyPipelineSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("AAResupplyPipeline");
        private static int s_EcbCommandCount;
        public static int EcbCommandCount => Volatile.Read(ref s_EcbCommandCount);
        public static void ResetCounters() => Interlocked.Exchange(ref s_EcbCommandCount, 0);
        private static void IncrementEcbCount() => Interlocked.Increment(ref s_EcbCommandCount);

        private EntityQuery m_BatchQuery;
        private EntityQuery m_ResolvedRequestQuery;
        private EntityQuery m_ResolvedRefundQuery;
        private EntityQuery m_BudgetRequestQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private ComponentLookup<Simulate> m_SimulateLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<AirDefenseInstallation> m_AALookup;
        private ComponentLookup<BudgetDeductExpiredOnLoad> m_ExpiredOnLoadLookup;
        private EntityStorageInfoLookup m_StorageInfoLookup;

        private readonly struct BatchApplyResult
        {
            public readonly long AccountedCost;
            public readonly long AcceptedCost;
            public readonly int AcceptedRounds;

            public BatchApplyResult(long accountedCost, long acceptedCost, int acceptedRounds)
            {
                AccountedCost = accountedCost;
                AcceptedCost = acceptedCost;
                AcceptedRounds = acceptedRounds;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_BatchQuery = GetEntityQuery(ComponentType.ReadWrite<AAResupplyBatchIntent>());
            m_ResolvedRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<BudgetDeductResult>(),
                ComponentType.ReadOnly<AAResupplyBudgetLink>());
            m_ResolvedRefundQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetAddFundsRequest>(),
                ComponentType.ReadOnly<BudgetAddFundsResult>(),
                ComponentType.ReadOnly<AAResupplyRefundIntent>());
            m_BudgetRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<AAResupplyBudgetLink>());

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            m_SimulateLookup = GetComponentLookup<Simulate>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_AALookup = GetComponentLookup<AirDefenseInstallation>(true);
            m_ExpiredOnLoadLookup = GetComponentLookup<BudgetDeductExpiredOnLoad>(true);
            m_StorageInfoLookup = GetEntityStorageInfoLookup();

            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            bool hasResolved = !m_ResolvedRequestQuery.IsEmpty;
            bool hasRefunds = !m_ResolvedRefundQuery.IsEmpty;
            bool hasBatches = !m_BatchQuery.IsEmpty;
            if (!hasResolved && !hasRefunds && !hasBatches) return;

            m_SimulateLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_AALookup.Update(this);
            m_ExpiredOnLoadLookup.Update(this);
            m_StorageInfoLookup.Update(this);

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            if (hasResolved)
                ResolveBudgetResults(ref ecb, ref ecbCreated);
            if (hasRefunds)
                DrainResolvedRefunds(ref ecb, ref ecbCreated);

            if (!m_BatchQuery.IsEmpty)
            {
                FinalizeReadyBatches(ref ecb, ref ecbCreated);
            }

            if (ecbCreated)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void ResolveBudgetResults(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            foreach (var (result, link, requestEntity) in
                SystemAPI.Query<RefRO<BudgetDeductResult>, RefRW<AAResupplyBudgetLink>>()
                .WithEntityAccess())
            {
                if (link.ValueRO.Retired) continue;
                if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }

                long batchId = link.ValueRO.BatchId;
                if (TryFindBatchRW(batchId, out var batchRef))
                {
                    link.ValueRW.Retired = true;
                    if (!result.ValueRO.Succeeded
                        && m_ExpiredOnLoadLookup.HasComponent(requestEntity)
                        && !batchRef.ValueRO.IsEmergency)
                    {
                        batchRef.ValueRW.BudgetResolved = false;
                        batchRef.ValueRW.BudgetSucceeded = false;
                        QueueBudgetRetry(ecb, batchRef.ValueRO);
                        Log.Info($"AAResupply batch {batchId}: retained budget expired on load; re-queued automatic resupply charge");
                    }
                    else
                    {
                        batchRef.ValueRW.BudgetResolved = true;
                        batchRef.ValueRW.BudgetSucceeded = result.ValueRO.Succeeded;
                    }
                }
                else
                {
                    link.ValueRW.Retired = true;
                    if (result.ValueRO.Succeeded && result.ValueRO.Amount > 0)
                    {
                        Refund(ecb, result.ValueRO.Amount, $"AAResupplyRefund:OrphanBudget:{batchId}:{result.ValueRO.Amount}");
                        Log.Warn($"AAResupply budget result BatchId={batchId} has no batch — refunded ${result.ValueRO.Amount:N0}");
                    }
                    else
                    {
                        Log.Warn($"AAResupply budget result BatchId={batchId} has no batch");
                    }
                }

                ecb.DestroyEntity(requestEntity);
                IncrementEcbCount();
            }
        }

        private void FinalizeReadyBatches(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            foreach (var (batchRef, batchEntity) in
                SystemAPI.Query<RefRW<AAResupplyBatchIntent>>()
                .WithEntityAccess())
            {
                var batch = batchRef.ValueRO;

                // Terminal guard (AAPlacementIntent doctrine): the destroy below is a
                // deferred ECB command that plays back only after ALL sim ticks. At
                // 2x-3x this batch is still alive on later ticks of the same frame —
                // skip it once its outcome was decided so ammo+budget are not
                // re-charged and the line ResupplyAARequests are not re-emitted.
                if (batch.Applied) continue;

                if (batch.RequiresBudget && !batch.BudgetResolved && !HasBudgetRequestForBatch(batch.BatchId))
                {
                    if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
                    DestroyBatchLines(ecb, batch.BatchId);
                    batchRef.ValueRW.Applied = true;
                    ecb.DestroyEntity(batchEntity);
                    IncrementEcbCount();
                    PublishFailedBatch(ecb, batch);
                    Log.Warn($"AAResupply batch {batch.BatchId}: budget request missing, dropped unresolved batch");
                    continue;
                }

                bool ready = !batch.RequiresBudget || batch.BudgetResolved;
                if (!ready) continue;

                if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }

                if (batch.RequiresBudget && !batch.BudgetSucceeded)
                {
                    DestroyBatchLines(ecb, batch.BatchId);
                    batchRef.ValueRW.Applied = true;
                    ecb.DestroyEntity(batchEntity);
                    IncrementEcbCount();
                    PublishFailedBatch(ecb, batch);
                    Log.Info($"AAResupply batch {batch.BatchId}: budget failed, dropped");
                    continue;
                }

                var applyResult = ProcessSuccessfulBatchLines(ecb, batch.BatchId);
                if (batch.RequiresBudget && batch.BudgetSucceeded && applyResult.AccountedCost < batch.TotalCost)
                {
                    long missingRefund = batch.TotalCost - applyResult.AccountedCost;
                    Refund(ecb, missingRefund, $"AAResupplyRefund:MissingLines:{batch.BatchId}:{missingRefund}");
                    Log.Warn($"AAResupply batch {batch.BatchId}: missing lines, refunded ${missingRefund:N0}");
                }

                PublishAcceptedBatch(ecb, batch, applyResult.AcceptedRounds, applyResult.AcceptedCost);

                batchRef.ValueRW.Applied = true;
                ecb.DestroyEntity(batchEntity);
                IncrementEcbCount();
            }
        }

        private BatchApplyResult ProcessSuccessfulBatchLines(EntityCommandBuffer ecb, long batchId)
        {
            long accountedCost = 0;
            long acceptedCost = 0;
            int acceptedRounds = 0;
            foreach (var (lineRef, lineEntity) in
                SystemAPI.Query<RefRO<AAResupplyLineIntent>>()
                .WithEntityAccess())
            {
                var line = lineRef.ValueRO;
                if (line.BatchId != batchId) continue;

                long lineCost = line.Cost;
                accountedCost += lineCost;

                var aaEntity = new Entity { Index = line.AAEntityIndex, Version = line.AAEntityVersion };
                if (AirDefenseLifecycle.TryGetActiveInstallation(
                        aaEntity,
                        m_AALookup,
                        m_StorageInfoLookup,
                        m_SimulateLookup,
                        m_DeletedLookup,
                        m_DestroyedLookup,
                        out _))
                {
                    var req = ecb.CreateEntity();
                    ecb.AddComponent(req, new ResupplyAARequest
                    {
                        AAEntityIndex = line.AAEntityIndex,
                        AAEntityVersion = line.AAEntityVersion,
                        NewAmmo = line.NewAmmo,
                        RoundsAdded = line.RoundsAdded,
                        CostPerRound = line.CostPerRound,
                        AllocatedCost = line.AllocatedCost
                    });
                    RequestMetaWriter.AddInternal(ecb, req, nameof(ResupplyAARequest), line.AAEntityIndex.ToString());
                    IncrementEcbCount();
                    acceptedCost += lineCost;
                    acceptedRounds += line.RoundsAdded;
                }
                else if (lineCost > 0)
                {
                    Refund(ecb, lineCost, $"AAResupplyRefund:DeadLine:{batchId}:{line.AAEntityIndex}:{line.AAEntityVersion}:{lineCost}");
                    Log.Warn($"AAResupply refund: AA {line.AAEntityIndex} dead after charge — refunding ${lineCost:N0}");
                }

                ecb.DestroyEntity(lineEntity);
                IncrementEcbCount();
            }

            return new BatchApplyResult(accountedCost, acceptedCost, acceptedRounds);
        }

        private void PublishAcceptedBatch(EntityCommandBuffer ecb, in AAResupplyBatchIntent batch, int acceptedRounds, long acceptedCost)
        {
            if (acceptedRounds <= 0)
            {
                PublishFailedBatch(ecb, batch, cost: 0);
                return;
            }

            EmitEmergencyRequestResult(ecb, batch, RequestStatus.Success, ReasonId.None);

            // Trickle batches are silent: a per-tick Partial/Full would spam the
            // narrative + telemetry listeners. AAAmmoSystem publishes the single
            // terminal Full when the city-wide deficit actually reaches zero.
            if (batch.Trickle)
                return;

            if (batch.IsEmergency)
            {
                EventBus?.SafePublish(new AAResupplyEvent(
                    AAResupplyResult.Emergency,
                    Rounds: acceptedRounds,
                    Cost: acceptedCost
                ), "AAResupplyPipelineSystem");
                return;
            }

            int neededRounds = batch.NeededRounds > 0 ? batch.NeededRounds : batch.RequestedRounds;
            bool fulfilledRequested = batch.RequestedRounds <= 0 || acceptedRounds >= batch.RequestedRounds;
            if (batch.IsFullResupply && fulfilledRequested)
            {
                EventBus?.SafePublish(new AAResupplyEvent(
                    AAResupplyResult.Full,
                    Rounds: acceptedRounds,
                    Cost: acceptedCost
                ), "AAResupplyPipelineSystem");
                return;
            }

            EventBus?.SafePublish(new AAResupplyEvent(
                AAResupplyResult.Partial,
                Rounds: acceptedRounds,
                Needed: neededRounds,
                Cost: acceptedCost
            ), "AAResupplyPipelineSystem");
        }

        private void PublishFailedBatch(EntityCommandBuffer ecb, in AAResupplyBatchIntent batch, long? cost = null)
        {
            EmitEmergencyRequestResult(ecb, batch, RequestStatus.Failed, ReasonIds.AirDefenseActionFailed);

            // Trickle batches are silent on failure too — AAAmmoSystem owns the single
            // terminal Failed for the auto refill cycle (city broke), not the per-tick batch.
            if (batch.Trickle)
                return;

            EventBus?.SafePublish(new AAResupplyEvent(
                AAResupplyResult.Failed,
                Cost: cost ?? batch.TotalCost
            ), "AAResupplyPipelineSystem");
        }

        private void EmitEmergencyRequestResult(
            EntityCommandBuffer ecb,
            in AAResupplyBatchIntent batch,
            RequestStatus status,
            ReasonId reason)
        {
            if (!batch.IsEmergency || batch.RequestId == 0)
                return;

            if (status == RequestStatus.Success)
                RequestResultEmitter.EmitSuccess(ecb, batch.RequestId, RequestKind.EmergencyResupply, SystemAPI.Time.ElapsedTime);
            else
                RequestResultEmitter.Emit(ecb, batch.RequestId, RequestKind.EmergencyResupply, status, reason, SystemAPI.Time.ElapsedTime);

            RequestResultBridge.PublishTerminalForBegun(
                RequestResultBridge.EmergencyResupply,
                batch.RequestId,
                status,
                status == RequestStatus.Success ? "" : reason.ToString());
        }

        private void DestroyBatchLines(EntityCommandBuffer ecb, long batchId)
        {
            foreach (var (lineRef, lineEntity) in
                SystemAPI.Query<RefRO<AAResupplyLineIntent>>()
                .WithEntityAccess())
            {
                if (lineRef.ValueRO.BatchId != batchId) continue;
                ecb.DestroyEntity(lineEntity);
                IncrementEcbCount();
            }
        }

        private void DrainResolvedRefunds(ref EntityCommandBuffer ecb, ref bool ecbCreated)
        {
            foreach (var (result, intent, entity) in
                SystemAPI.Query<RefRO<BudgetAddFundsResult>, RefRO<AAResupplyRefundIntent>>()
                .WithAll<BudgetAddFundsRequest>()
                .WithEntityAccess())
            {
                if (!ecbCreated) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); ecbCreated = true; }
                if (!result.ValueRO.Succeeded)
                    Log.Warn($"AAResupply retained refund failed: op={intent.ValueRO.OperationKey.ToString()} amount=${intent.ValueRO.Amount:N0}");
                ecb.DestroyEntity(entity);
                IncrementEcbCount();
            }
        }

        private void Refund(EntityCommandBuffer ecb, long amount, string operationKey)
        {
            if (amount <= 0) return;
            if (HasRefundRequest(operationKey))
                return;

            if (BudgetEmitter.TryQueueAddFunds(
                    ecb,
                    amount,
                    BudgetSource.ResupplyRefund,
                    BudgetIncomeKind.Refund,
                    operationKey,
                    out var refundEntity,
                    BudgetResultMode.RetainResult))
            {
                ecb.AddComponent(refundEntity, new AAResupplyRefundIntent
                {
                    Amount = amount,
                    OperationKey = new FixedString128Bytes(operationKey)
                });
                IncrementEcbCount();
            }
        }

        private bool HasRefundRequest(string operationKey)
        {
            foreach (var requestRef in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (requestRef.ValueRO.OperationKey.ToString() == operationKey)
                    return true;
            }

            return false;
        }

        private void QueueBudgetRetry(EntityCommandBuffer ecb, in AAResupplyBatchIntent batch)
        {
            if (batch.TotalCost <= 0)
                return;

#pragma warning disable CIVIC022 // Source string built once per resupply batch retry, not per frame
#pragma warning disable CIVIC410 // False positive: post-load reconciliation retry of a committed auto-resupply charge whose retained budget expired on load (see ResolveBudgetResults). The deduction MUST be re-issued unconditionally for accounting consistency — the resupply was already delivered — so an eligibility/affordability precheck would wrongly drop a committed charge. Non-affordability is handled downstream via the RetainResult result entity.
            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    batch.TotalCost,
                    BudgetCategory.AirDefense,
                    BudgetPriority.Operational,
                    $"AAResupplyBatch:{batch.BatchId}:PostLoadRetry",
                    out var budgetEntity,
                    BudgetResultMode.RetainResult))
#pragma warning restore CIVIC410
#pragma warning restore CIVIC022
            {
                Log.Warn($"AAResupply batch {batch.BatchId}: post-load retry could not queue budget request");
                return;
            }

            ecb.AddComponent(budgetEntity, new AAResupplyBudgetLink
            {
                BatchId = batch.BatchId
            });
            IncrementEcbCount();
        }

        private bool HasBudgetRequestForBatch(long batchId)
        {
            if (m_BudgetRequestQuery.IsEmpty) return false;

            foreach (var link in SystemAPI.Query<RefRO<AAResupplyBudgetLink>>())
            {
                if (link.ValueRO.BatchId == batchId)
                    return true;
            }

            return false;
        }

        private bool TryFindBatchRW(long batchId, out RefRW<AAResupplyBatchIntent> result)
        {
            foreach (var batchRef in SystemAPI.Query<RefRW<AAResupplyBatchIntent>>())
            {
                if (batchRef.ValueRO.BatchId == batchId)
                {
                    result = batchRef;
                    return true;
                }
            }

            result = default;
            return false;
        }
    }
}
