using Unity.Entities;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Public entry point for arsenal replenishment via procurement intent entities.
    /// This is the async/budget-gated channel — the counterpart to the synchronous
    /// <c>ICounterAttackArsenalService.Replenish</c>. Mirrors how <c>AAAmmoSystem</c>
    /// builds an <c>AAResupplyBatchIntent</c> + <c>AAResupplyBudgetLink</c>, drained by
    /// <c>CounterAttackArsenalSystem</c>.
    ///
    /// Lives in <c>Core/Services</c> (Axiom 5 / CIVIC179): the procurement channels
    /// are in other domains (ShadowEconomy import, Diplomacy donors), and Domain→Domain
    /// import is banned — the producer API must sit in Core for them to reach it. It
    /// operates only on Core types (intents, BudgetEmitter, BudgetCategory, wallet
    /// service) — no GridWarfare-domain dependency.
    ///
    /// Two channels feed it:
    /// - <see cref="QueuePaidProcurement"/> — channel (a), import/donor paid purchase.
    ///   Shadow-import callers pass <c>BudgetCategory.ShadowOps</c> so SanctionsMarkup +
    ///   shadow-wallet pending reservation are applied inside <c>BudgetEmitter</c>.
    /// - <see cref="QueueFreeGrant"/> — donor aid already paid for diplomatically.
    /// - Hidden-factory production (channel b, Phase-30b) should call the synchronous
    ///   <c>ICounterAttackArsenalService.Replenish</c> directly once a unit is produced
    ///   (no budget gate — production already paid the upkeep). It is exposed there, not
    ///   here, because factory output is not a budget transaction.
    /// </summary>
    public static class ArsenalProcurementEmitter
    {
        /// <summary>
        /// Queue a paid arsenal procurement batch. Returns false (and queues nothing)
        /// if the budget request could not be created (insufficient funds / no wallet).
        /// On success the batch is gated and applied by
        /// <c>CounterAttackArsenalSystem</c>, which grants the stock when the
        /// budget result resolves.
        /// </summary>
        /// <param name="world">Owning ECS world (for budget affordability + pending reservation).</param>
        /// <param name="ecb">Command buffer from a barrier (GameSimulationEndBarrier / EndFrameBarrier).</param>
        /// <param name="batchId">Stable non-zero correlation id (from <c>ICounterAttackArsenalService.AllocateProcurementBatchId</c>).</param>
        /// <param name="kind">Munition kind purchased.</param>
        /// <param name="count">Units purchased (must be &gt; 0).</param>
        /// <param name="totalCost">Base cost; markup is applied downstream for ShadowOps.</param>
        /// <param name="budgetCategory">Budget category — <c>BudgetCategory.ShadowOps</c> for shadow import.</param>
        /// <param name="sourceLabel">Diagnostic label for the budget request source string.</param>
        public static bool QueuePaidProcurement(
            World world,
            EntityCommandBuffer ecb,
            long batchId,
            ArsenalKind kind,
            int count,
            long totalCost,
            string budgetCategory,
            string sourceLabel)
        {
            if (count <= 0 || totalCost <= 0)
                return false;

            // Eligibility precheck on the producer boundary (CIVIC410): do not even
            // create the batch if the funding source can't cover it. BudgetEmitter
            // re-checks authoritatively, but emitting an intent we know will fail leaves
            // an orphan the pipeline has to drop. Mirrors AAAmmoSystem's CanPay precheck.
            if (!CanAffordProcurement(world, totalCost, budgetCategory))
                return false;

            var batchEntity = ecb.CreateEntity();
            ecb.AddComponent(batchEntity, new ArsenalProcurementBatchIntent
            {
                BatchId = batchId,
                Kind = kind,
                Count = count,
                TotalCost = totalCost,
                RequiresBudget = true,
                BudgetResolved = false,
                BudgetSucceeded = false
            });

#pragma warning disable CIVIC022 // Source string built once per procurement batch, not per frame
            bool budgetQueued = BudgetEmitter.TryQueueDeduct(
                world,
                ecb,
                totalCost,
                budgetCategory,
                BudgetPriority.PlayerAction,
                $"ArsenalProcurement:{batchId}:{sourceLabel}",
                out var budgetEntity,
                BudgetResultMode.RetainResult);
#pragma warning restore CIVIC022

            if (!budgetQueued)
            {
                // Mark the batch resolved+failed so the pipeline drops it next tick
                // (UI sees the failed outcome) instead of leaving an orphan.
                ecb.SetComponent(batchEntity, new ArsenalProcurementBatchIntent
                {
                    BatchId = batchId,
                    Kind = kind,
                    Count = count,
                    TotalCost = totalCost,
                    RequiresBudget = true,
                    BudgetResolved = true,
                    BudgetSucceeded = false
                });
                return false;
            }

            ecb.AddComponent(budgetEntity, new ArsenalProcurementBudgetLink
            {
                BatchId = batchId
            });
            return true;
        }

        /// <summary>
        /// Affordability precheck for the funding source behind <paramref name="budgetCategory"/>.
        /// ShadowOps draws the shadow wallet (with sanctions markup + pending reservation);
        /// everything else draws the city budget. Same predicate BudgetEmitter applies
        /// internally — surfaced here so the producer can reject before emitting an intent.
        /// </summary>
        public static bool CanAffordProcurement(World world, long totalCost, string budgetCategory)
        {
            if (totalCost <= 0)
                return false;
            if (world == null || !world.IsCreated)
                return false;

            if (budgetCategory == BudgetCategory.ShadowOps)
            {
                var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                return wallet.CanAffordWithPending(totalCost).Affordable;
            }

            return CityBudgetService.CanAffordWithPending(world, totalCost);
        }

        /// <summary>
        /// Queue a free arsenal grant batch (no budget gate). For aid sources that are
        /// already paid for diplomatically (donors). Always queues; applied next tick.
        /// </summary>
        public static void QueueFreeGrant(
            EntityCommandBuffer ecb,
            long batchId,
            ArsenalKind kind,
            int count)
        {
            if (count <= 0)
                return;

            var batchEntity = ecb.CreateEntity();
            ecb.AddComponent(batchEntity, new ArsenalProcurementBatchIntent
            {
                BatchId = batchId,
                Kind = kind,
                Count = count,
                TotalCost = 0,
                RequiresBudget = false,
                BudgetResolved = true,
                BudgetSucceeded = true
            });
        }
    }
}
