using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Spotters.Systems
{
    /// <summary>
    /// Receives resolved budget results for spotter operations and applies the
    /// typed durable intent on the retained budget entity.
    ///
    /// Query-driven — queries BudgetDeductRequest + BudgetDeductResult +
    /// SpotterBudgetIntent entities. Source text is diagnostic only.
    /// </summary>
    [ActIndependent]
    public partial class SpotterBudgetIngressSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("SpotterBudgetIngress");

        private SpotterAggregateSystem? m_Aggregate;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;
        private EntityQuery m_ResolvedRequestQuery;
        private ComponentLookup<RequestMeta> m_RequestMetaLookup;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResolvedRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<BudgetDeductResult>());
            m_RequestMetaLookup = GetComponentLookup<RequestMeta>(true);
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_DependencyWire = new CivicDependencyWire(nameof(SpotterBudgetIngressSystem));

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Aggregate ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<SpotterAggregateSystem>());
        }

        protected override void OnUpdateImpl()
        {
            if (m_Aggregate == null || m_ResolvedRequestQuery.IsEmpty)
                return;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            bool anyProcessed = false;
            m_RequestMetaLookup.Update(this);

            foreach (var (_, result, intent, phase, entity) in
                SystemAPI.Query<RefRO<BudgetDeductRequest>, RefRO<BudgetDeductResult>, RefRW<SpotterBudgetIntent>, RefRW<PendingPhase>>()
                .WithEntityAccess())
            {
                if (ProcessSpotterBudgetIntent(ecb, result.ValueRO, ref intent.ValueRW, ref phase.ValueRW, entity))
                    ecb.DestroyEntity(entity);
                anyProcessed = true;
            }

            if (anyProcessed)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private bool ProcessSpotterBudgetIntent(
            EntityCommandBuffer ecb,
            in BudgetDeductResult result,
            ref SpotterBudgetIntent intent,
            ref PendingPhase phase,
            Entity budgetEntity)
        {
            if (phase.Value == PendingPhaseValue.Applied || phase.Value == PendingPhaseValue.Confirmed)
                return true;

            if (intent.Action == AirDefenseActionType.PerformSBUVisit)
            {
                if (result.Succeeded)
                {
                    if (!intent.DomainApplied && !m_Aggregate!.ApplyConfirmedBudgetIntent(intent))
                    {
                        intent.DomainRejected = true;
                        return DrainOrQueueRefund(ecb, ref intent, ref phase, result.Amount, "SpotterSBURefund", budgetEntity, emitFailure: true);
                    }

                    intent.DomainApplied = true;
                    phase.Value = PendingPhaseValue.Applied;
                    EmitIntentSuccess(ecb, budgetEntity);
                }
                else
                {
                    intent.ChargeFailed = true;
                    phase.Value = PendingPhaseValue.Confirmed;
                    Log.Warn($"SBU budget deduction of ${result.Amount} failed — no domain mutation was applied");
                    EmitIntentFailure(ecb, budgetEntity);
                }
                return true;
            }

            if (intent.Action == AirDefenseActionType.PerformEvacuation)
            {
                if (result.Succeeded)
                {
                    if (!intent.DomainApplied && !m_Aggregate!.ApplyConfirmedBudgetIntent(intent))
                    {
                        intent.DomainRejected = true;
                        return DrainOrQueueRefund(ecb, ref intent, ref phase, result.Amount, "SpotterEvacRefund", budgetEntity, emitFailure: true);
                    }

                    intent.DomainApplied = true;
                    phase.Value = PendingPhaseValue.Applied;
                    EmitIntentSuccess(ecb, budgetEntity);
                }
                else
                {
                    intent.ChargeFailed = true;
                    phase.Value = PendingPhaseValue.Confirmed;
                    Log.Warn($"Evacuation budget deduction of ${result.Amount} failed — no domain mutation was applied");
                    EmitIntentFailure(ecb, budgetEntity);
                }
                return true;
            }

            if (intent.Action == AirDefenseActionType.ToggleCounterOSINT)
            {
                if (result.Succeeded)
                {
                    if (!intent.DomainApplied && !m_Aggregate!.ApplyConfirmedBudgetIntent(intent))
                    {
                        intent.DomainRejected = true;
                        return DrainOrQueueRefund(ecb, ref intent, ref phase, result.Amount, "SpotterCounterOSINTRefund", budgetEntity, emitFailure: true);
                    }

                    intent.DomainApplied = true;
                    phase.Value = PendingPhaseValue.Applied;
                    Log.Info("Counter-OSINT budget confirmed — enable queued");
                    EmitIntentSuccess(ecb, budgetEntity);
                }
                else
                {
                    intent.ChargeFailed = true;
                    phase.Value = PendingPhaseValue.Confirmed;
                    Log.Warn("Counter-OSINT budget failed — activation cancelled");
                    EmitIntentFailure(ecb, budgetEntity);
                }
                return true;
            }

            if (intent.Action == AirDefenseActionType.CounterOSINTDailyCost)
            {
                if (result.Succeeded)
                {
                    if (!intent.DomainApplied && !m_Aggregate!.ApplyConfirmedBudgetIntent(intent))
                    {
                        intent.DomainRejected = true;
                        return DrainOrQueueRefund(ecb, ref intent, ref phase, result.Amount, "SpotterCounterOSINTDailyRefund", budgetEntity, emitFailure: false);
                    }

                    intent.DomainApplied = true;
                    phase.Value = PendingPhaseValue.Applied;
                }
                else
                {
                    intent.ChargeFailed = true;
                    phase.Value = PendingPhaseValue.Confirmed;
                    m_Aggregate!.RollbackFailedBudgetIntent(intent);
                    Log.Warn("Counter-OSINT daily cost budget failed — disable queued");
                }
                return true;
            }

            Log.Error($"SpotterBudgetIngress: unrecognized typed spotter budget intent action={intent.Action}");
            intent.DomainRejected = true;
            if (result.Succeeded && result.Amount > 0)
                return DrainOrQueueRefund(ecb, ref intent, ref phase, result.Amount, "SpotterUnknownRefund", budgetEntity, emitFailure: true);

            phase.Value = PendingPhaseValue.Confirmed;
            EmitIntentFailure(ecb, budgetEntity);
            return true;
        }

        private bool DrainOrQueueRefund(
            EntityCommandBuffer ecb,
            ref SpotterBudgetIntent intent,
            ref PendingPhase phase,
            long amount,
            string source,
            Entity budgetEntity,
            bool emitFailure)
        {
            if (amount <= 0)
            {
                phase.Value = PendingPhaseValue.Confirmed;
                if (emitFailure) EmitIntentFailure(ecb, budgetEntity);
                return true;
            }

            string operationKey = ResolveRefundOperationKey(ref intent);
            if (intent.RefundResolved)
            {
                if (TryCleanupRefundResult(ecb, operationKey, ref intent))
                    return false;

                phase.Value = PendingPhaseValue.Confirmed;
                if (emitFailure) EmitIntentFailure(ecb, budgetEntity);
                return true;
            }

            foreach (var (requestRef, resultRef, refundEntity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                intent.RefundQueued = true;
                intent.RefundResolved = true;
                intent.RefundFailed = !resultRef.ValueRO.Succeeded;
                intent.RefundCleanupQueued = true;
                ecb.DestroyEntity(refundEntity);
                return false;
            }

            foreach (var requestRef in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (requestRef.ValueRO.OperationKey.ToString() == operationKey)
                {
                    intent.RefundQueued = true;
                    return false;
                }
            }

            if (BudgetEmitter.TryQueueAddFunds(
                    ecb,
                    amount,
                    source,
                    BudgetIncomeKind.Refund,
                    operationKey,
                    out _,
                    BudgetResultMode.RetainResult))
            {
                intent.RefundQueued = true;
            }

            return false;
        }

        private bool TryCleanupRefundResult(EntityCommandBuffer ecb, string operationKey, ref SpotterBudgetIntent intent)
        {
            foreach (var (requestRef, _, refundEntity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                ecb.DestroyEntity(refundEntity);
                intent.RefundCleanupQueued = true;
                return true;
            }

            intent.RefundCleanupQueued = false;
            return false;
        }

        private void EmitIntentSuccess(EntityCommandBuffer ecb, Entity budgetEntity)
        {
            if (m_RequestMetaLookup.TryGetComponent(budgetEntity, out var meta))
                RequestResultEmitter.EmitSuccess(ecb, meta, RequestKind.SpotterAction, SystemAPI.Time.ElapsedTime);
        }

        private void EmitIntentFailure(EntityCommandBuffer ecb, Entity budgetEntity)
        {
            if (m_RequestMetaLookup.TryGetComponent(budgetEntity, out var meta))
                RequestResultEmitter.Emit(ecb, meta, RequestKind.SpotterAction, RequestStatus.Failed, ReasonIds.SpotterInsufficientFunds, SystemAPI.Time.ElapsedTime);
        }

        private static string ResolveRefundOperationKey(ref SpotterBudgetIntent intent)
        {
            string stored = intent.RefundOperationKey.ToString();
            if (!string.IsNullOrEmpty(stored))
                return stored;

            string fallback = $"SpotterRefund:Legacy:{(int)intent.Action}:{intent.Cost}:{intent.Days}:{intent.CoveredUntilGameHour:R}";
            intent.RefundOperationKey = new Unity.Collections.FixedString128Bytes(fallback);
            return fallback;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Log.Info("Destroyed");
        }
    }
}
