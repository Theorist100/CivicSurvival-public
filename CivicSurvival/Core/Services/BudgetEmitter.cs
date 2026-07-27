using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Utils;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Single entry point for budget request entities. Owns same-frame affordability
    /// reservation for normal deductions while preserving debt-fallback semantics.
    /// </summary>
    public static class BudgetEmitter
    {
        private static readonly LogContext Log = new("BudgetEmitter");

        public static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeduct(world, ecb, amount, category, priority, source, out _, out _, default, false, resultMode, false);

        public static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            RequestMeta requestMeta,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeduct(world, ecb, amount, category, priority, source, out _, out _, requestMeta, true, resultMode, false);

        public static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            out Entity entity,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeduct(world, ecb, amount, category, priority, source, out entity, out _, default, false, resultMode, false);

        public static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            out Entity entity,
            RequestMeta requestMeta,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeduct(world, ecb, amount, category, priority, source, out entity, out _, requestMeta, true, resultMode, false);

        public static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            out Entity entity,
            out long queuedAmount,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeduct(world, ecb, amount, category, priority, source, out entity, out queuedAmount, default, false, resultMode, false);

        public static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            out Entity entity,
            out long queuedAmount,
            RequestMeta requestMeta,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeduct(world, ecb, amount, category, priority, source, out entity, out queuedAmount, requestMeta, true, resultMode, false);

        public static bool TryQueueDeductOnEntity(
            World world,
            EntityCommandBuffer ecb,
            Entity entity,
            long amount,
            string category,
            byte priority,
            string source,
            out long queuedAmount,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeductOnEntity(
                world,
                ecb,
                entity,
                amount,
                category,
                priority,
                source,
                out queuedAmount,
                default,
                false,
                resultMode);

        public static bool TryQueueDeductOnEntity(
            World world,
            EntityCommandBuffer ecb,
            Entity entity,
            long amount,
            string category,
            byte priority,
            string source,
            out long queuedAmount,
            RequestMeta requestMeta,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => TryQueueDeductOnEntity(
                world,
                ecb,
                entity,
                amount,
                category,
                priority,
                source,
                out queuedAmount,
                requestMeta,
                true,
                resultMode);

        private static bool TryQueueDeduct(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            out Entity entity,
            out long queuedAmount,
            RequestMeta requestMeta,
            bool attachRequestMeta,
            BudgetResultMode resultMode,
            bool preferImmediateEntity)
        {
            entity = Entity.Null;
            queuedAmount = 0;
            if (amount <= 0)
                return false;
            if (world == null || !world.IsCreated)
                return false;

            bool shadowOps = category == BudgetCategory.ShadowOps;
            long effectiveAmount = amount;
            IShadowWalletService wallet = NullShadowWalletService.Instance;
            if (shadowOps)
            {
                wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                var aff = wallet.CanAffordWithPending(amount);
                if (!aff.Affordable)
                    return false;
                effectiveAmount = aff.EffectiveCost;
            }
            else
            {
                if (!CityBudgetService.CanAffordWithPending(world, amount))
                    return false;
            }

            try
            {
                uint createdFrame = GetSimulationFrame(world);
                if (preferImmediateEntity)
                {
                    entity = CreateDeductEntityImmediate(world, effectiveAmount, category, priority, source, default!, resultMode, effectiveAmount, requestMeta, attachRequestMeta, createdFrame);
                    if (shadowOps)
                        wallet.RegisterPendingDeduction(effectiveAmount);
                    else
                        CityBudgetService.RegisterPendingDeduction(amount);
                }
                else
                {
                    entity = CreateDeductEntity(ecb, effectiveAmount, category, priority, source, default!, resultMode, effectiveAmount, requestMeta, attachRequestMeta, createdFrame);
                    if (shadowOps)
                        wallet.RegisterPendingDeduction(effectiveAmount);
                    else
                        CityBudgetService.RegisterPendingDeduction(amount);
                }
                queuedAmount = effectiveAmount;
                return true;
            }
            catch (System.Exception ex)
            {
                if (entity != Entity.Null && entity.Index >= 0 && world.EntityManager.Exists(entity))
                    world.EntityManager.DestroyEntity(entity);

                Log.Error($"Failed to queue budget deduct ${amount:N0} [{category}] from {source}: {ex}");
                entity = Entity.Null;
                queuedAmount = 0;
                return false;
            }
        }

        private static bool TryQueueDeductOnEntity(
            World world,
            EntityCommandBuffer ecb,
            Entity entity,
            long amount,
            string category,
            byte priority,
            string source,
            out long queuedAmount,
            RequestMeta requestMeta,
            bool attachRequestMeta,
            BudgetResultMode resultMode)
        {
            queuedAmount = 0;
            if (amount <= 0 || entity == Entity.Null)
                return false;
            if (world == null || !world.IsCreated)
                return false;

            bool shadowOps = category == BudgetCategory.ShadowOps;
            long effectiveAmount = amount;
            IShadowWalletService wallet = NullShadowWalletService.Instance;
            if (shadowOps)
            {
                wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                var aff = wallet.CanAffordWithPending(amount);
                if (!aff.Affordable)
                    return false;
                effectiveAmount = aff.EffectiveCost;
            }
            else
            {
                if (!CityBudgetService.CanAffordWithPending(world, amount))
                    return false;
            }

            try
            {
                uint createdFrame = GetSimulationFrame(world);
                if (entity.Index >= 0 && world.EntityManager.Exists(entity))
                {
                    AddDeductComponentsImmediate(world.EntityManager, entity, effectiveAmount, category, priority, source, default!, resultMode, effectiveAmount, requestMeta, attachRequestMeta, createdFrame);
                    if (shadowOps)
                        wallet.RegisterPendingDeduction(effectiveAmount);
                    else
                        CityBudgetService.RegisterPendingDeduction(amount);
                }
                else
                {
                    AddDeductComponents(ecb, entity, effectiveAmount, category, priority, source, default!, resultMode, effectiveAmount, requestMeta, attachRequestMeta, createdFrame);
                    if (shadowOps)
                        wallet.RegisterPendingDeduction(effectiveAmount);
                    else
                        CityBudgetService.RegisterPendingDeduction(amount);
                }
                queuedAmount = effectiveAmount;
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to queue budget deduct ${amount:N0} [{category}] on existing entity from {source}: {ex}");
                queuedAmount = 0;
                return false;
            }
        }

        public static bool QueueDeductWithDebtFallback(
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            string debtFallbackCategory,
            BudgetResultMode resultMode = BudgetResultMode.FireAndForget)
            => QueueDeductWithDebtFallback(
                ecb, amount, category, priority, source, debtFallbackCategory, out _, resultMode);

        public static bool QueueDeductWithDebtFallback(
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            string debtFallbackCategory,
            out Entity entity,
            BudgetResultMode resultMode = BudgetResultMode.FireAndForget)
        {
            entity = Entity.Null;
            if (amount <= 0)
                return false;
            if (category == BudgetCategory.ShadowOps)
            {
                Log.Error($"Rejected ambiguous ShadowOps debt fallback request from {source}");
                return false;
            }

            entity = CreateDeductEntity(ecb, amount, category, priority, source, debtFallbackCategory, resultMode, 0, default, false, 0u);
            return true;
        }

        public static void QueueAddFunds(EntityCommandBuffer ecb, long amount, string source)
            => QueueAddFunds(ecb, amount, source, CityBudgetService.ClassifyIncomeSource(source));

        public static void QueueAddFunds(EntityCommandBuffer ecb, long amount, string source, BudgetIncomeKind incomeKind)
            => QueueAddFunds(ecb, amount, source, incomeKind, BudgetResultMode.FireAndForget, string.Empty, out _);

        public static bool TryQueueAddFunds(
            EntityCommandBuffer ecb,
            long amount,
            string source,
            BudgetIncomeKind incomeKind,
            string operationKey,
            out Entity entity,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult)
            => QueueAddFunds(ecb, amount, source, incomeKind, resultMode, operationKey, out entity);

        public static bool TryQueueAddFundsImmediate(
            World world,
            long amount,
            string source,
            BudgetIncomeKind incomeKind,
            string operationKey,
            out Entity entity,
            BudgetResultMode resultMode = BudgetResultMode.RetainResult,
            double createdTime = double.PositiveInfinity)
        {
            entity = Entity.Null;
            if (amount <= 0)
                return false;
            if (world == null || !world.IsCreated)
                return false;

            var entityManager = world.EntityManager;
            entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new BudgetAddFundsRequest
            {
                Amount = amount,
                Source = new FixedString32Bytes(source ?? string.Empty),
                IncomeKind = incomeKind,
                ResultMode = resultMode,
                OperationKey = new FixedString128Bytes(operationKey ?? string.Empty)
            });
            RequestMetaWriter.AddInternal(
                entityManager,
                entity,
                nameof(BudgetAddFundsRequest),
                operationKey ?? source ?? string.Empty,
                createdTime,
                GetSimulationFrame(world));
            return true;
        }

        private static bool QueueAddFunds(
            EntityCommandBuffer ecb,
            long amount,
            string source,
            BudgetIncomeKind incomeKind,
            BudgetResultMode resultMode,
            string operationKey,
            out Entity entity)
        {
            entity = Entity.Null;
            if (amount <= 0)
                return false;

            entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new BudgetAddFundsRequest
            {
                Amount = amount,
                Source = new FixedString32Bytes(source ?? string.Empty),
                IncomeKind = incomeKind,
                ResultMode = resultMode,
                OperationKey = new FixedString128Bytes(operationKey ?? string.Empty)
            });
            RequestMetaWriter.AddInternal(ecb, entity, nameof(BudgetAddFundsRequest), operationKey ?? source ?? string.Empty);
            return true;
        }

        private static Entity CreateDeductEntity(
            EntityCommandBuffer ecb,
            long amount,
            string category,
            byte priority,
            string source,
            string debtFallbackCategory,
            BudgetResultMode resultMode,
            long reservationAmount,
            RequestMeta requestMeta,
            bool attachRequestMeta,
            uint createdFrame)
        {
            var entity = ecb.CreateEntity();
            AddDeductComponents(ecb, entity, amount, category, priority, source, debtFallbackCategory, resultMode, reservationAmount, requestMeta, attachRequestMeta, createdFrame);
            return entity;
        }

        private static Entity CreateDeductEntityImmediate(
            World world,
            long amount,
            string category,
            byte priority,
            string source,
            string debtFallbackCategory,
            BudgetResultMode resultMode,
            long reservationAmount,
            RequestMeta requestMeta,
            bool attachRequestMeta,
            uint createdFrame)
        {
            var entityManager = world.EntityManager;
            var entity = entityManager.CreateEntity();
            AddDeductComponentsImmediate(entityManager, entity, amount, category, priority, source, debtFallbackCategory, resultMode, reservationAmount, requestMeta, attachRequestMeta, createdFrame);
            return entity;
        }

        private static void AddDeductComponents(
            EntityCommandBuffer ecb,
            Entity entity,
            long amount,
            string category,
            byte priority,
            string source,
            string debtFallbackCategory,
            BudgetResultMode resultMode,
            long reservationAmount,
            RequestMeta requestMeta,
            bool attachRequestMeta,
            uint createdFrame)
        {
            ecb.AddComponent(entity, new BudgetDeductRequest
            {
                Amount = amount,
                Category = new FixedString32Bytes(category ?? BudgetCategory.Other),
                Priority = priority,
                Source = new FixedString64Bytes(source ?? string.Empty),
                DebtFallbackCategory = new FixedString32Bytes(debtFallbackCategory ?? string.Empty),
                ResultMode = resultMode,
                ReservationAmount = reservationAmount > 0 ? reservationAmount : 0
            });
            if (attachRequestMeta)
            {
                requestMeta.CreatedFrame = createdFrame;
                ecb.AddComponent(entity, requestMeta);
            }
            else
            {
                RequestMetaWriter.AddInternal(ecb, entity, nameof(BudgetDeductRequest), source ?? string.Empty, createdFrame: createdFrame);
            }
        }

        private static void AddDeductComponentsImmediate(
            EntityManager entityManager,
            Entity entity,
            long amount,
            string category,
            byte priority,
            string source,
            string debtFallbackCategory,
            BudgetResultMode resultMode,
            long reservationAmount,
            RequestMeta requestMeta,
            bool attachRequestMeta,
            uint createdFrame)
        {
            entityManager.AddComponentData(entity, new BudgetDeductRequest
            {
                Amount = amount,
                Category = new FixedString32Bytes(category ?? BudgetCategory.Other),
                Priority = priority,
                Source = new FixedString64Bytes(source ?? string.Empty),
                DebtFallbackCategory = new FixedString32Bytes(debtFallbackCategory ?? string.Empty),
                ResultMode = resultMode,
                ReservationAmount = reservationAmount > 0 ? reservationAmount : 0
            });
            if (attachRequestMeta)
            {
                requestMeta.CreatedFrame = createdFrame;
                entityManager.AddComponentData(entity, requestMeta);
            }
            else
            {
                RequestMetaWriter.AddInternal(entityManager, entity, nameof(BudgetDeductRequest), source ?? string.Empty, createdFrame: createdFrame);
            }
        }

        private static uint GetSimulationFrame(World world)
        {
            var simulationSystem = world?.GetExistingSystemManaged<SimulationSystem>();
            return simulationSystem != null ? simulationSystem.frameIndex : 0u;
        }
    }
}
