using System;
using System.Collections.Generic;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Debt category constants for tracking.
    /// </summary>
    public static class DebtCategory
    {
        public const string WarDamage = "WarDamage";
        public const string Infrastructure = "Infrastructure";
        public const string IMFLoan = "IMFLoan";
    }

    /// <summary>
    /// Shared service for city debt operations.
    /// Tracks debt by category, processes monthly payments, applies interest.
    ///
    /// Used by: WarDamageDebtSystem, CityDebtTrackingSystem, FinanceUIPanel
    ///
    /// Thread-safety: All debt operations protected by s_DebtLock.
    /// </summary>
    public static class CityDebtService
    {
        private static readonly LogContext Log = new("CityDebtService");
        private static readonly object s_DebtLock = new();
        private static Dictionary<string, long> s_DebtByCategory = new();
        private static volatile bool s_RestructureActive;
        private static volatile bool s_WarningPublishedThisCycle;
        private static long s_IncomeAtPeriodStart;
        private static volatile bool s_IncomePeriodInitialized;
        private static int s_LastAppliedBillingDay;

        // FIX S6-04: Accessors for serialization (CityDebtTrackingSystem)
        internal static long GetIncomeAtPeriodStart() => s_IncomeAtPeriodStart;
        internal static void SetIncomeAtPeriodStart(long value) => s_IncomeAtPeriodStart = value;
        internal static bool GetIncomePeriodInitialized() => s_IncomePeriodInitialized;
        internal static void SetIncomePeriodInitialized(bool value) => s_IncomePeriodInitialized = value;

        // FIX S1-02: Persist restructure state across save/load
        internal static bool GetRestructureActive() => s_RestructureActive;
        internal static void SetRestructureActive(bool value) => s_RestructureActive = value;
        internal static bool GetWarningPublishedThisCycle() => s_WarningPublishedThisCycle;
        internal static void SetWarningPublishedThisCycle(bool value) => s_WarningPublishedThisCycle = value;
        internal static bool IsBillingDayApplied(int billingDay) => billingDay <= s_LastAppliedBillingDay;
        internal static int GetLastAppliedBillingDay() => s_LastAppliedBillingDay;
        internal static void SetLastAppliedBillingDay(int billingDay)
        {
            if (billingDay > s_LastAppliedBillingDay)
                s_LastAppliedBillingDay = billingDay;
        }
        internal static void RestoreLastAppliedBillingDayFromLoad(int billingDay)
        {
            s_LastAppliedBillingDay = Math.Max(0, billingDay);
        }

        /// <summary>
        /// Whether debt is currently restructured (interest frozen, reduced rate).
        /// Used by UI to show restructure banner.
        /// </summary>
        public static bool IsRestructured => s_RestructureActive;

        public readonly struct DebtSnapshot : IEquatable<DebtSnapshot>
        {
            public DebtSnapshot(long totalDebt, float debtToIncomeRatio, Dictionary<string, long> breakdown, bool debtRestructured)
            {
                TotalDebt = totalDebt;
                DebtToIncomeRatio = debtToIncomeRatio;
                Breakdown = breakdown;
                DebtRestructured = debtRestructured;
            }

            public long TotalDebt { get; }
            public float DebtToIncomeRatio { get; }
            public Dictionary<string, long> Breakdown { get; }
            public bool DebtRestructured { get; }

            public bool Equals(DebtSnapshot other)
                => TotalDebt == other.TotalDebt
                    && DebtToIncomeRatio.Equals(other.DebtToIncomeRatio)
                    && DebtRestructured == other.DebtRestructured
                    && DictionaryEquals(Breakdown, other.Breakdown);

            public override bool Equals(object? obj)
                => obj is DebtSnapshot other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    TotalDebt,
                    DebtToIncomeRatio,
                    DebtRestructured,
                    Breakdown is null ? 0 : Breakdown.Count);

            public static bool operator ==(DebtSnapshot left, DebtSnapshot right)
                => left.Equals(right);

            public static bool operator !=(DebtSnapshot left, DebtSnapshot right)
                => !left.Equals(right);

            private static bool DictionaryEquals(Dictionary<string, long>? left, Dictionary<string, long>? right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null) return false;
                if (left.Count != right.Count) return false;
                foreach (var pair in left)
                {
                    if (!right.TryGetValue(pair.Key, out var otherValue))
                        return false;
                    if (pair.Value != otherValue)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Add debt to city. Use when budget is insufficient for expenses.
        /// </summary>
        /// <param name="amount">Debt amount to add (must be positive)</param>
        /// <param name="category">Debt category (use DebtCategory constants)</param>
        public static void AddDebt(long amount, string category = DebtCategory.WarDamage)
        {
            if (amount <= 0)
            {
                Log.Error($" AddDebt called with invalid amount: {amount} (must be > 0)");
                return;
            }

            bool debtOverflowed = false;
            long debtTotal;
            lock (s_DebtLock)
            {
                if (!s_DebtByCategory.TryGetValue(category, out long current))
                    current = 0;

                // Overflow guard
                if (amount > long.MaxValue - current)
                {
                    debtOverflowed = true;
                    s_DebtByCategory[category] = long.MaxValue;
                }
                else
                {
                    s_DebtByCategory[category] = current + amount;
                }

                debtTotal = GetTotalDebtUnsafe();
            }

            if (debtOverflowed)
                Log.Warn($" Debt overflow prevented for {category}");
            Log.Info($" Added ${amount:N0} debt [{category}], total: ${debtTotal:N0}");

            // Publish event (outside lock)
            PublishDebtEvent(DebtEventType.DebtAdded, amount, category);
        }

        /// <summary>
        /// Get total debt across all categories.
        /// Thread-safe via lock.
        /// </summary>
        public static long GetTotalDebt()
        {
            lock (s_DebtLock)
            {
                return GetTotalDebtUnsafe();
            }
        }

        /// <summary>
        /// Get debt breakdown by category.
        /// Thread-safe via lock.
        /// </summary>
        public static Dictionary<string, long> GetDebtBreakdown()
        {
            lock (s_DebtLock)
            {
                return new(s_DebtByCategory);
            }
        }

        /// <summary>
        /// Compute debt-to-income ratio using last billing period income.
        /// Ratio = totalDebt / periodIncome. Uses period income (not cumulative)
        /// to ensure the ratio reflects current financial health.
        /// </summary>
        public static float GetDebtToIncomeRatio()
        {
            long totalDebt = GetTotalDebt();
            long periodIncome = GetPeriodIncome();
            return totalDebt / (float)Math.Max(1, periodIncome);
        }

        public static DebtSnapshot GetSnapshot()
        {
            long totalDebt;
            Dictionary<string, long> breakdown;
            bool isRestructured;
            lock (s_DebtLock)
            {
                totalDebt = GetTotalDebtUnsafe();
                breakdown = new Dictionary<string, long>(s_DebtByCategory);
                isRestructured = s_RestructureActive;
            }

            long periodIncome = GetPeriodIncome();
            float ratio = totalDebt / (float)Math.Max(1, periodIncome);
            return new DebtSnapshot(totalDebt, ratio, breakdown, isRestructured);
        }

        public static bool ShouldShowDebtWarning(DebtSnapshot snapshot, float warningRatio)
        {
            return snapshot.TotalDebt > 0
                && GetRawPeriodIncome() > 0
                && snapshot.DebtToIncomeRatio > warningRatio;
        }

        /// <summary>
        /// Get income earned since last billing period snapshot.
        /// Falls back to cumulative income if no snapshot yet (first period).
        /// FIX S6-03: Uses recurring income (excludes DonorAid) to prevent
        /// one-time donor windfalls from inflating debt-to-income ratio.
        /// </summary>
        public static long GetPeriodIncome()
        {
            return Math.Max(1, GetRawPeriodIncome());
        }

        internal static long GetRawPeriodIncome()
        {
            long cumulative = CityBudgetService.GetRecurringIncome();
            if (!s_IncomePeriodInitialized)
                return Math.Max(0, cumulative);

            long delta = cumulative - s_IncomeAtPeriodStart;
            return Math.Max(0, delta);
        }

        /// <summary>
        /// Snapshot current cumulative income as period start.
        /// Called by CityDebtTrackingSystem at each billing cycle.
        /// FIX S6-03: Uses recurring income to match GetPeriodIncome.
        /// </summary>
        internal static void SnapshotPeriodIncome()
        {
            s_IncomeAtPeriodStart = CityBudgetService.GetRecurringIncome();
            s_IncomePeriodInitialized = true;
        }

        /// <summary>
        /// Process monthly debt payment.
        /// Called by CityDebtTrackingSystem on day change.
        /// Includes auto-restructure logic when debt-to-income ratio exceeds threshold.
        /// </summary>
        /// <param name="world">ECS world for budget access</param>
        /// <param name="rate">Monthly payment rate (0.10 = 10%)</param>
        /// <param name="minimum">Minimum payment amount</param>
        /// <param name="interestRate">Interest rate on missed/partial payments</param>
        /// <param name="warningRatio">Debt-to-income ratio for UI warning</param>
        /// <param name="restructureRatio">Debt-to-income ratio for auto-restructure</param>
        /// <param name="restructuredRate">Reduced interest rate during restructure</param>
        internal static void ProcessMonthlyPayment(
            World world, float rate, long minimum, float interestRate,
            float warningRatio, float restructureRatio, float restructuredRate)
            => ProcessMonthlyPayment(
                world, rate, minimum, interestRate, warningRatio, restructureRatio,
                restructuredRate, GetTotalDebt(), GetRawPeriodIncome());

        internal static void ProcessMonthlyPayment(
            World world, float rate, long minimum, float interestRate,
            float warningRatio, float restructureRatio, float restructuredRate,
            long decisionDebt, long periodIncome)
        {
            long totalDebt;
            lock (s_DebtLock)
            {
                totalDebt = GetTotalDebtUnsafe();
            }

            if (totalDebt <= 0)
            {
                s_RestructureActive = false;
                s_WarningPublishedThisCycle = false;
                return;
            }

            // Debt-to-income ratio check
            // FIX S1-06: Skip ratio-based restructure when no recurring income yet —
            // zero income produces ratio = totalDebt/1 which always exceeds threshold
            long ratioDebt = decisionDebt > 0 ? decisionDebt : totalDebt;
            float ratio = CalculateDebtRatio(ratioDebt, periodIncome);

            if (ratio > restructureRatio && periodIncome > 0)
            {
                // Auto-restructure: reduced rate, no standard interest
                s_RestructureActive = true;
                float effectiveRate = restructuredRate;

                // R4-S5-03 FIX: Try-first pattern for restructured path too.
                // FIX H76: Clamp to totalDebt — prevents overpayment when minimum > debt
                long payment = Math.Min(totalDebt, Math.Max(minimum, (long)Math.Round(totalDebt * rate)));

                if (CityBudgetService.TryDeduct(world, payment, BudgetCategory.DebtPayment) == BudgetResult.Ok)
                {
                    lock (s_DebtLock) { ReduceDebtUnsafe(payment); }
                    Log.Info($" Restructured payment: ${payment:N0} (full), ratio={ratio:F1}x");
                    PublishDebtEvent(DebtEventType.MonthlyPaymentMade, payment, null);
                }
                else
                {
                    long available = CityBudgetService.TryDeductUpTo(world, payment, BudgetCategory.DebtPayment);
                    if (available >= minimum)
                    {
                        lock (s_DebtLock)
                        {
                            ReduceDebtUnsafe(available);
                            ApplyInterestUnsafe(effectiveRate);
                        }
                        Log.Info($" Restructured payment: ${available:N0} (partial), rate={effectiveRate:P0}");
                        PublishDebtEvent(DebtEventType.PartialPaymentMade, available, null);
                    }
                    else
                    {
                        // Missed — apply reduced interest only
                        lock (s_DebtLock) { ApplyInterestUnsafe(effectiveRate); }
                        Log.Warn($" Restructured: payment missed, reduced interest. Debt: ${GetTotalDebt():N0}");
                        PublishDebtEvent(DebtEventType.PaymentMissed, 0, null);
                    }
                }

                PublishDebtEvent(DebtEventType.DebtRestructured, totalDebt, null);
                return;
            }

            s_RestructureActive = false;

            // Warning threshold (publish once per payment cycle)
            if (ratio > warningRatio && periodIncome > 0 && !s_WarningPublishedThisCycle)
            {
                s_WarningPublishedThisCycle = true;
                PublishDebtEvent(DebtEventType.DebtWarning, totalDebt, null);
            }

            // R4-S5-03 FIX: Try-first pattern — eliminates TOCTOU between GetBalance and TryDeduct.
            // Previously: read balance → branch → deduct. Balance could change between read and deduct
            // (synchronous DayChangedEvent handlers mutate budget), silently skipping the payment.
            // FIX H76: Clamp to totalDebt — prevents overpayment when minimum > debt
            long stdPayment = Math.Min(totalDebt, Math.Max(minimum, (long)Math.Round(totalDebt * rate)));

            if (CityBudgetService.TryDeduct(world, stdPayment, BudgetCategory.DebtPayment) == BudgetResult.Ok)
            {
                // Full payment succeeded atomically
                lock (s_DebtLock) { ReduceDebtUnsafe(stdPayment); }
                Log.Info($" Monthly payment: ${stdPayment:N0} (full)");
                PublishDebtEvent(DebtEventType.MonthlyPaymentMade, stdPayment, null);
            }
            else
            {
                // Full failed — try partial with current balance
                long stdAvailable = CityBudgetService.TryDeductUpTo(world, stdPayment, BudgetCategory.DebtPayment);
                if (stdAvailable >= minimum)
                {
                    // Partial payment + interest
                    lock (s_DebtLock)
                    {
                        ReduceDebtUnsafe(stdAvailable);
                        ApplyInterestUnsafe(interestRate);
                    }
                    Log.Info($" Monthly payment: ${stdAvailable:N0} (partial), interest applied");
                    PublishDebtEvent(DebtEventType.PartialPaymentMade, stdAvailable, null);
                }
                else
                {
                    // Missed payment + interest
                    lock (s_DebtLock) { ApplyInterestUnsafe(interestRate); }
                    Log.Warn($" Payment missed, interest applied. Debt: ${GetTotalDebt():N0}");
                    PublishDebtEvent(DebtEventType.PaymentMissed, 0, null);
                }
            }

            // Reset warning flag for next payment cycle
            s_WarningPublishedThisCycle = false;
        }

        /// <summary>
        /// Restore debt tracking data (used by serialization system).
        /// Thread-safe via lock.
        /// </summary>
        internal static void SetDebtByCategory(Dictionary<string, long> debt)
        {
            if (debt == null)
            {
                Mod.Log.Warn("[CityDebtService] SetDebtByCategory: null input, resetting");
                ResetTracking();
                return;
            }

            // FIX: Validate input - prevent corrupted/malicious data
            const int MAX_CATEGORIES = 20;
            if (debt.Count > MAX_CATEGORIES)
            {
                Log.Warn($" SetDebtByCategory: too many categories ({debt.Count}), capping to {MAX_CATEGORIES}");
            }

            lock (s_DebtLock)
            {
                s_DebtByCategory.Clear();
                int count = 0;
                foreach (var kvp in debt)
                {
                    if (count >= MAX_CATEGORIES) break;
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    if (kvp.Value < 0) continue;  // Skip negative debt (invalid)
                    s_DebtByCategory[kvp.Key] = kvp.Value;
                    count++;
                }
            }
        }

        /// <summary>
        /// Reset all debt tracking (call on new game).
        /// Thread-safe via lock.
        /// </summary>
        public static void ResetTracking()
        {
            lock (s_DebtLock)
            {
                s_DebtByCategory.Clear();
            }
            s_RestructureActive = false;
            s_WarningPublishedThisCycle = false;
            s_IncomeAtPeriodStart = 0;
            s_IncomePeriodInitialized = false;
            s_LastAppliedBillingDay = 0;
            Log.Debug(" Tracking reset");
        }

        /// <summary>
        /// Apply debt relief: forgive a percentage of total debt across all categories.
        /// Thread-safe. Returns amount forgiven.
        /// </summary>
        /// <param name="percent">Fraction to forgive (0.30 = 30%)</param>
        public static long ApplyDebtRelief(float percent)
        {
            if (percent <= 0f || percent > 1f)
            {
                Log.Error($" ApplyDebtRelief: invalid percent {percent:P0}");
                return 0;
            }

            long forgiven;
            long totalAfter;
            lock (s_DebtLock)
            {
                long totalBefore = GetTotalDebtUnsafe();
                if (totalBefore <= 0) return 0;

                forgiven = (long)Math.Round(totalBefore * percent);
                if (forgiven <= 0) return 0;

                ReduceDebtUnsafe(forgiven);
                totalAfter = GetTotalDebtUnsafe();
            }

            Log.Info($" Debt relief: ${forgiven:N0} forgiven ({percent:P0}), remaining: ${totalAfter:N0}");

            PublishDebtEvent(DebtEventType.DebtRelief, forgiven, null);
            return forgiven;
        }

        // =========================================
        // Private helpers
        // =========================================

        private static long GetTotalDebtUnsafe()
        {
            long total = 0;
            foreach (var kvp in s_DebtByCategory)
            {
                total += kvp.Value;
                // FIX #228: Overflow guard — cap at long.MaxValue
                if (total < 0) return long.MaxValue;
            }
            return total;
        }

        private static float CalculateDebtRatio(long debt, long income)
        {
            if (income <= 0)
                return 0f;

            return (float)(debt / (double)Math.Max(1L, income));
        }

        /// <summary>
        /// Reduce debt proportionally. Requires caller to hold s_DebtLock.
        /// </summary>
        [CallerHoldsLock(nameof(s_DebtLock))]
        private static void ReduceDebtUnsafe(long amount)
        {
            long totalDebt = GetTotalDebtUnsafe();
            if (totalDebt <= 0 || amount <= 0)
                return;

            amount = Math.Min(amount, totalDebt);
            var toReduce = new Dictionary<string, long>();
            var fractions = new List<(string Key, double Fraction, long Debt)>();
            long totalReduction = 0;

            foreach (var kvp in s_DebtByCategory)
            {
                if (kvp.Value <= 0) continue;

                double exact = amount * ((double)kvp.Value / totalDebt);
                long reduction = Math.Min(kvp.Value, (long)Math.Floor(exact));
                toReduce[kvp.Key] = reduction;
                totalReduction += reduction;
                fractions.Add((kvp.Key, exact - reduction, kvp.Value));
            }

            long remainder = amount - totalReduction;
            fractions.Sort((a, b) =>
            {
                int cmp = b.Fraction.CompareTo(a.Fraction);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Key, b.Key);
            });

            for (int i = 0; i < fractions.Count && remainder > 0; i++)
            {
                var item = fractions[i];
                long currentReduction = toReduce[item.Key];
                if (currentReduction >= item.Debt)
                    continue;

                toReduce[item.Key] = currentReduction + 1;
                remainder--;
            }

            // Apply updates
            foreach (var kvp in toReduce)
            {
                long newValue = Math.Max(0, s_DebtByCategory[kvp.Key] - kvp.Value);
                if (newValue <= 0)
                    s_DebtByCategory.Remove(kvp.Key);
                else
                    s_DebtByCategory[kvp.Key] = newValue;
            }
        }

        /// <summary>
        /// Apply interest to all debt categories. Requires caller to hold s_DebtLock.
        /// </summary>
        [CallerHoldsLock(nameof(s_DebtLock))]
        private static void ApplyInterestUnsafe(float rate)
        {
            var toUpdate = new Dictionary<string, long>();
            foreach (var kvp in s_DebtByCategory)
            {
                if (kvp.Value <= 0) continue;

                long interest = (long)Math.Round(kvp.Value * rate);
                long newValue = kvp.Value + interest;

                // Overflow guard
                if (newValue < kvp.Value)
                    newValue = long.MaxValue;

                toUpdate[kvp.Key] = newValue;
            }

            foreach (var kvp in toUpdate)
                s_DebtByCategory[kvp.Key] = kvp.Value;
        }

        private static void PublishDebtEvent(DebtEventType type, long amount, string? category)
        {
            var eventBus = ServiceRegistry.Instance.Require<IEventBus>();
            eventBus.SafePublish(new DebtEvent(type, amount, GetTotalDebt(), category), "CityDebtService");
        }
    }
}
