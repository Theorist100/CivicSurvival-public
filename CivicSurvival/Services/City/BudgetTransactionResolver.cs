using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Services.City
{
    /// <summary>
    /// Phase-neutral budget mutation core. Systems that need immediate, vanilla-style
    /// apply-time payment can call this directly; BudgetResolutionSystem uses the same
    /// logic when draining deferred request entities.
    /// </summary>
    public static class BudgetTransactionResolver
    {
        private static readonly LogContext Log = new("BudgetTransactionResolver");

        public static BudgetDeductResult Deduct(
            World world,
            IShadowWalletService walletService,
            long amount,
            string category,
            string source,
            string debtFallbackCategory = "")
        {
            var result = new BudgetDeductResult
            {
                Succeeded = false,
                Amount = amount,
                PaidAmount = 0,
                DebtAmount = 0
            };

            if (amount <= 0)
                return result;

            bool hasDebtFallback = !string.IsNullOrEmpty(debtFallbackCategory);

            if (hasDebtFallback && category == BudgetCategory.ShadowOps)
            {
                Log.Error($"Rejected ambiguous ShadowOps debt fallback request amount=${amount:N0} from {source}");
                return result;
            }

            if (hasDebtFallback)
            {
                var outcome = PayWithDebtFallback(world, amount, category, debtFallbackCategory, source);
                result.Succeeded = outcome.Succeeded;
                result.PaidAmount = outcome.PaidAmount;
                result.DebtAmount = outcome.DebtAmount;
                return result;
            }

            if (category == BudgetCategory.ShadowOps)
            {
                bool success = walletService.TryDeduct(amount, source);
                result.Succeeded = success;
                result.PaidAmount = success ? amount : 0;

                if (!success && Log.IsDebugEnabled)
                    Log.Debug($"Shadow wallet deduction failed: ${amount:N0} from {source}");
                return result;
            }

            var deductResult = CityBudgetService.TryDeduct(world, amount, category);
            result.Succeeded = deductResult == BudgetResult.Ok;
            result.PaidAmount = result.Succeeded ? amount : 0;

            if (!result.Succeeded && Log.IsDebugEnabled)
                Log.Debug($"Deduction failed: ${amount:N0} [{category}] from {source} (reason: {deductResult})");

            return result;
        }

        public static bool QueueRefund(EntityCommandBuffer ecb, long amount, string source, BudgetIncomeKind incomeKind)
        {
            if (amount <= 0)
                return false;

            BudgetEmitter.QueueAddFunds(ecb, amount, source, incomeKind);
            return true;
        }

        private static DebtFallbackOutcome PayWithDebtFallback(
            World world,
            long amount,
            string budgetCategory,
            string debtCategory,
            string source)
        {
            var fullResult = CityBudgetService.TryDeduct(world, amount, budgetCategory);
            if (fullResult == BudgetResult.Ok)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Paid ${amount:N0} [{budgetCategory}] from {source}");
                return new DebtFallbackOutcome(true, amount, 0);
            }

            if (fullResult != BudgetResult.InsufficientFunds)
            {
                // ACCOUNTING-INVARIANT: debt fallback is a policy for real lack of
                // city funds. Budget host/load infrastructure failures are retryable
                // failures and must not become permanent debt.
                Log.Warn($"Debt fallback deferred ${amount:N0} [{budgetCategory}] from {source} — reason: {fullResult}");
                return new DebtFallbackOutcome(false, 0, 0);
            }

            long actualPaid = CityBudgetService.TryDeductUpTo(world, amount, budgetCategory);
            long debtAmount = amount - actualPaid;
            if (debtAmount > 0)
                CityDebtService.AddDebt(debtAmount, debtCategory);

            if (debtAmount > 0)
                Log.Warn($"Damage cost ${amount:N0}: paid ${actualPaid:N0}, debt ${debtAmount:N0} ({debtCategory}) from {source}");
            else if (Log.IsDebugEnabled)
                Log.Debug($"Damage cost ${amount:N0}: paid ${actualPaid:N0}, no debt remainder ({debtCategory}) from {source}");

            return new DebtFallbackOutcome(true, actualPaid, debtAmount);
        }

        private readonly struct DebtFallbackOutcome
        {
            public readonly bool Succeeded;
            public readonly long PaidAmount;
            public readonly long DebtAmount;

            public DebtFallbackOutcome(bool succeeded, long paidAmount, long debtAmount)
            {
                Succeeded = succeeded;
                PaidAmount = paidAmount;
                DebtAmount = debtAmount;
            }
        }
    }
}
