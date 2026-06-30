using System;
using System.Collections.Generic;
using Unity.Entities;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Result classification for budget mutation operations (TryDeduct, AddFunds).
    /// Distinguishes legitimate denial from systemic unavailability so callers can
    /// react appropriately (retry, skip, log).
    /// </summary>
    public enum BudgetResult
    {
        /// <summary>Default uninitialised value — never returned by operations.</summary>
        None = 0,
        /// <summary>Operation succeeded.</summary>
        Ok = 1,
        /// <summary>
        /// AddFunds reached the vanilla money cap and applied only part of the
        /// requested amount. This is not a transient host failure.
        /// </summary>
        Capped = 2,
        /// <summary>Not enough funds (TryDeduct) — legitimate denial, retry with smaller amount possible.</summary>
        InsufficientFunds = 3,
        /// <summary>amount &lt;= 0 — programming error, fix the caller.</summary>
        InvalidAmount = 4,
        /// <summary>
        /// CityBudgetHost is not attached to a world (boot window, between-worlds transition,
        /// or façade missing from ServiceRegistry). Operation skipped. Caller can retry later.
        /// </summary>
        SystemUnavailable = 5,
        /// <summary>PlayerMoney singleton not found (city not loaded yet, or save mid-load).</summary>
        BudgetEntityMissing = 6
    }

    /// <summary>
    /// Budget expense categories for tracking.
    /// </summary>
    public static class BudgetCategory
    {
        public const string AirDefense = "AirDefense";
        public const string Repairs = "Repairs";
        public const string SpotterOps = "SpotterOps";
        public const string CognitiveOps = "CognitiveOps";
        public const string Procurement = "Procurement";
        public const string Penalties = "Penalties";
        public const string DebtPayment = "DebtPayment";
        public const string RefugeeSupport = "RefugeeSupport";
        public const string ShadowOps = "ShadowOps";
        public const string Other = "Other";
    }

    /// <summary>
    /// Budget income sources for tracking.
    /// </summary>
    public static class BudgetSource
    {
        public const string DonorAid = "DonorAid";
        public const string EmergencyFunding = "EmergencyFunding";
        public const string ResupplyRefund = "ResupplyRefund";
        public const string RepairRefund = "RepairRefund";
        public const string CivRepairRefund = "CivRepairRefund";
        public const string AAInstallRefund = "AAInstallRefund";
        public const string ContractRefund = "ContractRefund";
        public const string Other = "Other";
    }

    /// <summary>
    /// Static accessor over the world-owned <c>CityBudgetHost</c> via
    /// <c>CityBudgetFacade</c>. World-bound state (EntityQuery, tracking dicts,
    /// pending deductions counter) lives in the host. The <c>World</c> parameter
    /// is retained for API compatibility but is informational — the actual entity
    /// manipulation goes through the host's own <c>EntityManager</c>.
    /// </summary>
    public static class CityBudgetService
    {
        public readonly struct BudgetSnapshot : IEquatable<BudgetSnapshot>
        {
            public BudgetSnapshot(Dictionary<string, long> expenses, Dictionary<string, long> income, long totalExpenses, long totalIncome)
            {
                Expenses = expenses;
                Income = income;
                TotalExpenses = totalExpenses;
                TotalIncome = totalIncome;
            }

            public Dictionary<string, long> Expenses { get; }
            public Dictionary<string, long> Income { get; }
            public long TotalExpenses { get; }
            public long TotalIncome { get; }

            public bool Equals(BudgetSnapshot other)
                => TotalExpenses == other.TotalExpenses
                    && TotalIncome == other.TotalIncome
                    && DictionaryEquals(Expenses, other.Expenses)
                    && DictionaryEquals(Income, other.Income);

            public override bool Equals(object? obj)
                => obj is BudgetSnapshot other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    TotalExpenses,
                    TotalIncome,
                    Expenses is null ? 0 : Expenses.Count,
                    Income is null ? 0 : Income.Count);

            public static bool operator ==(BudgetSnapshot left, BudgetSnapshot right) => left.Equals(right);
            public static bool operator !=(BudgetSnapshot left, BudgetSnapshot right) => !left.Equals(right);

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

        private static readonly LogContext Log = new("CityBudgetService");
        private static volatile bool s_WarnedNoFacade;

        /// <summary>
        /// Resolve the world-owned host iff it belongs to <paramref name="world"/>.
        /// Returns null when:
        /// (a) façade missing from ServiceRegistry — logs Error once (config bug);
        /// (b) façade present but CurrentHost null — world not attached (silent);
        /// (c) host belongs to a different World than the caller's — cross-world
        ///     guard, prevents mutating world#2's PlayerMoney with world#1's request
        ///     during a transition.
        /// </summary>
        private static CityBudgetFacade? ResolveFacade()
        {
            // TryGet is atomic under s_Lock — survives the teardown race that
            // IsInitialized + Instance.Get cannot (window between the two checks).
            var facade = ServiceRegistry.TryGet<CityBudgetFacade>();
            if (facade == null && !s_WarnedNoFacade)
            {
                s_WarnedNoFacade = true;
                Log.Error("CityBudgetFacade missing from ServiceRegistry — check Mod.OnLoad. All budget ops will return SystemUnavailable.");
            }
            return facade;
        }

        private static CityBudgetHost? GetHostForWorld(World? world)
        {
            if (world == null || !world.IsCreated) return null;
            var host = ResolveFacade()?.CurrentHost;
            if (host == null) return null;
            return ReferenceEquals(host.World, world) ? host : null;
        }

        /// <summary>Get host without world check — for methods that don't accept world.</summary>
        private static CityBudgetHost? GetCurrentHost() => ResolveFacade()?.CurrentHost;

        public static BudgetResult TryDeduct(World world, long amount, string category = BudgetCategory.Other)
        {
            var host = GetHostForWorld(world);
            return host == null ? BudgetResult.SystemUnavailable : host.TryDeduct(amount, category);
        }

        public static long TryDeductUpTo(World world, long amount, string category = BudgetCategory.Other)
        {
            if (amount <= 0) return 0;
            if (TryDeduct(world, amount, category) == BudgetResult.Ok) return amount;
            if (!TryGetBalance(world, out long available) || available <= 0) return 0;
            long partial = Math.Min(available, amount);
            return TryDeduct(world, partial, category) == BudgetResult.Ok ? partial : 0;
        }

        public static bool TryGetBalance(World world, out long balance)
        {
            balance = 0;
            var host = GetHostForWorld(world);
            return host != null && host.TryGetBalance(out balance);
        }

        public static long GetBalance(World world)
            => TryGetBalance(world, out long balance) ? balance : 0;

        /// <summary>
        /// Sync-free balance read for telemetry snapshots. Reads vanilla
        /// CitySystem.moneyAmount (a managed int refreshed each sim-tick) instead of
        /// EntityManager.GetComponentData&lt;PlayerMoney&gt;, so it does NOT force a
        /// PlayerMoney CompleteDependency — the sync was already paid by CitySystem.
        /// Use this for read-only snapshots (telemetry, session_end); budget mutations
        /// must still go through <see cref="TryGetBalance"/> / TryDeduct / AddFunds.
        /// Returns false on the boot window before the host or CitySystem exists.
        /// </summary>
        public static bool TryGetCachedBalance(World world, out long balance)
        {
            balance = 0;
            var host = GetHostForWorld(world);
            return host != null && host.TryGetCachedBalance(out balance);
        }

        public static bool CanAfford(World world, long amount)
            => TryGetBalance(world, out long balance) && balance >= amount;

        public static bool CanAffordWithPending(World world, long amount)
        {
            var host = GetHostForWorld(world);
            return host != null && host.CanAffordWithPending(amount);
        }

        // Methods below don't accept a World param — they target whatever the current
        // host is. Callers (ECS systems) are themselves world-bound, so "current host"
        // is always their own world during normal operation. World-cross contamination
        // is only possible if a non-ECS sidecar calls these mid-transition.
        public static void RegisterPendingDeduction(long amount)
            => GetCurrentHost()?.RegisterPendingDeduction(amount);

        internal static void RollbackPendingDeduction(long amount)
            => GetCurrentHost()?.RollbackPendingDeduction(amount);

        public static long PendingDeductions
        {
            get
            {
                var host = GetCurrentHost();
                return host == null ? 0 : host.PendingDeductions;
            }
        }

        internal static void ResetPendingDeductions()
            => GetCurrentHost()?.ResetPendingDeductions();

        public static BudgetResult AddFunds(World world, long amount, string source = BudgetSource.Other)
            => AddFunds(world, amount, source, ClassifyIncomeSource(source));

        public static BudgetResult AddFunds(World world, long amount, string source, BudgetIncomeKind incomeKind)
        {
            var host = GetHostForWorld(world);
            return host == null ? BudgetResult.SystemUnavailable : host.AddFunds(amount, source, incomeKind);
        }

        public static Dictionary<string, long> GetExpensesByCategory()
        {
            var host = GetCurrentHost();
            return host == null ? new Dictionary<string, long>() : host.GetExpensesByCategory();
        }

        public static Dictionary<string, long> GetIncomeBySource()
        {
            var host = GetCurrentHost();
            return host == null ? new Dictionary<string, long>() : host.GetIncomeBySource();
        }

        public static Dictionary<BudgetIncomeKind, long> GetIncomeByKind()
        {
            var host = GetCurrentHost();
            return host == null ? new Dictionary<BudgetIncomeKind, long>() : host.GetIncomeByKind();
        }

        public static long GetTotalExpenses()
        {
            var host = GetCurrentHost();
            return host == null ? 0 : host.GetTotalExpenses();
        }

        public static long GetTotalIncome()
        {
            var host = GetCurrentHost();
            return host == null ? 0 : host.GetTotalIncome();
        }

        public static BudgetSnapshot GetSnapshot()
        {
            var host = GetCurrentHost();
            return host == null
                ? new BudgetSnapshot(new Dictionary<string, long>(), new Dictionary<string, long>(), 0, 0)
                : host.GetSnapshot();
        }

        public static long GetRecurringIncome()
        {
            var host = GetCurrentHost();
            return host == null ? 0 : host.GetRecurringIncome();
        }

        public static void ResetTracking() => GetCurrentHost()?.ResetTracking();

        // Restore paths used by serialization Deserialize(). Silent no-op on host==null
        // would silently lose tracking-dict restore (W2-class bug: load races host attach).
        // Log Error so the failure is visible in the log.
        internal static void SetExpensesByCategory(Dictionary<string, long> expenses)
        {
            var host = GetCurrentHost();
            if (host == null)
            {
                Mod.Log.Error("CityBudgetService.SetExpensesByCategory: host not attached — restore lost. Check load/host-attach ordering.");
                return;
            }
            host.SetExpensesByCategory(expenses);
        }

        internal static void SetIncomeBySource(Dictionary<string, long> income)
        {
            var host = GetCurrentHost();
            if (host == null)
            {
                Mod.Log.Error("CityBudgetService.SetIncomeBySource: host not attached — restore lost. Check load/host-attach ordering.");
                return;
            }
            host.SetIncomeBySource(income);
        }

        internal static void SetIncomeByKind(Dictionary<BudgetIncomeKind, long> incomeByKind)
        {
            var host = GetCurrentHost();
            if (host == null)
            {
                Mod.Log.Error("CityBudgetService.SetIncomeByKind: host not attached — restore lost. Check load/host-attach ordering.");
                return;
            }
            host.SetIncomeByKind(incomeByKind);
        }

        public static BudgetIncomeKind ClassifyIncomeSource(string? source)
        {
            if (source == BudgetSource.DonorAid || source == BudgetSource.EmergencyFunding)
                return BudgetIncomeKind.DonorOrEmergencyCredit;
            if (source == BudgetSource.ResupplyRefund
                || source == BudgetSource.RepairRefund
                || source == BudgetSource.CivRepairRefund
                || source == BudgetSource.AAInstallRefund
                || source == BudgetSource.ContractRefund)
                return BudgetIncomeKind.Refund;
            return BudgetIncomeKind.RecurringRevenue;
        }
    }
}
