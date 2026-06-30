using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Single entry point for emitting shadow-economy wallet requests.
    /// Owns the wallet-presence gate so callers do not create orphan requests.
    /// </summary>
    public static class ShadowEconomyEmitter
    {
        private static readonly LogContext Log = new("ShadowEconomyEmitter");

        public static bool TryQueueIncome(World world, EntityCommandBuffer ecb, long amount, string reason, string operationKey)
            => TryQueueIncome(
                world,
                ecb,
                amount,
                new FixedString64Bytes(reason ?? string.Empty),
                new FixedString128Bytes(operationKey ?? string.Empty));

        public static bool TryQueueIncome(
            World world,
            EntityCommandBuffer ecb,
            long amount,
            FixedString64Bytes reason,
            FixedString128Bytes operationKey)
        {
            if (amount <= 0)
                return false;

            if (operationKey.Length == 0)
            {
                Log.Warn($"Income dropped (${amount:N0} {reason.ToString()}): missing operation key");
                return false;
            }

            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            if (!wallet.IsOperational)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"Income dropped (${amount:N0} {reason.ToString()}): wallet not operational");
                return false;
            }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ShadowIncomeRequest
            {
                Amount = amount,
                Reason = reason,
                OperationKey = operationKey
            });
            RequestMetaWriter.AddInternal(ecb, entity, nameof(ShadowIncomeRequest), operationKey.ToString());
            return true;
        }

        public static bool TryApplyRefund(World world, long amount, string reason, string operationKey)
        {
            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            return wallet.TryApplyRefund(amount, reason ?? string.Empty, operationKey ?? string.Empty);
        }

        public static bool TryQueueDeduct(World world, EntityCommandBuffer ecb, long baseCost, string reason)
            => TryQueueDeduct(world, ecb, baseCost, new FixedString64Bytes(reason ?? string.Empty));

        public static bool TryQueueDeduct(World world, EntityCommandBuffer ecb, long baseCost, FixedString64Bytes reason)
        {
            if (baseCost <= 0)
                return false;
            if (world == null || !world.IsCreated)
                return false;

            var wallet = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            var snapshot = wallet.GetWalletSnapshot();
            if (!snapshot.Exists)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"Deduct dropped (${baseCost:N0} {reason.ToString()}): wallet unavailable");
                return false;
            }

            var affordability = wallet.CanAffordWithPending(baseCost);
            if (!affordability.Affordable)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"Deduct dropped (${baseCost:N0} {reason.ToString()}): wallet cannot afford effective cost ${affordability.EffectiveCost:N0}");
                return false;
            }

            long effectiveCost = affordability.EffectiveCost;
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ShadowWalletDeductRequest
            {
                Amount = effectiveCost,
                Reason = reason,
                ReservationAmount = effectiveCost
            });
            RequestMetaWriter.AddInternal(ecb, entity, nameof(ShadowWalletDeductRequest), reason.ToString());
            wallet.RegisterPendingDeduction(effectiveCost);
            return true;
        }

        public static bool TryQueueSetImportMW(
            EntityCommandBuffer ecb,
            int requestedMW,
            int expectedMaxMW,
            long expectedDailyCost,
            float createdTime,
            RequestToken token,
            int presetPercent = ShadowTradeRequest.NoPresetPercent)
        {
            if (requestedMW < 0 || expectedMaxMW < 0 || requestedMW > expectedMaxMW || expectedDailyCost < 0)
                return false;
            if (presetPercent != ShadowTradeRequest.NoPresetPercent && (presetPercent < 0 || presetPercent > 100))
                return false;

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ShadowTradeRequest
            {
                TradeType = ShadowTradeType.SetImportMW,
                Value = requestedMW,
                ExpectedMaxMW = expectedMaxMW,
                ExpectedDailyCost = expectedDailyCost,
                HasPriceLock = 1,
                PresetPercent = presetPercent
            });
            RequestMetaWriter.Add(ecb, entity, token, createdTime);
            return true;
        }

        public static bool TryQueueSetExportPercent(
            EntityCommandBuffer ecb,
            int percent,
            float createdTime,
            RequestToken token)
        {
            if (percent < 0 || percent > 100)
                return false;

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ShadowTradeRequest
            {
                TradeType = ShadowTradeType.SetExportPercent,
                Value = percent
            });
            RequestMetaWriter.Add(ecb, entity, token, createdTime);
            return true;
        }
    }
}
