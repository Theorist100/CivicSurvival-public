using System;
using System.Collections.Generic;
using System.Threading;
using Game.City;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned host for <see cref="CityBudgetService"/>. Owns the per-World
    /// PlayerMoney EntityQuery, budget tracking dictionaries, and pending-deductions
    /// counter. OnDestroy disposes the query and clears tracking.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class CityBudgetHost : SystemBase
    {
        private static readonly LogContext Log = new("CityBudgetHost");

        private EntityQuery m_MoneyQuery;
        private bool m_QueryCreated;
        private long m_PendingDeductions;
        [System.NonSerialized] private CityBudgetFacade? m_Facade;
        [System.NonSerialized] private CitySystem? m_CitySystem;

        private readonly object m_TrackingLock = new();
        private Dictionary<string, long> m_ExpensesByCategory = new();
        private Dictionary<string, long> m_IncomeBySource = new();
        private Dictionary<BudgetIncomeKind, long> m_IncomeByKind = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_MoneyQuery = EntityManager.CreateEntityQuery(ComponentType.ReadWrite<PlayerMoney>());
            m_QueryCreated = true;

            // Vanilla CitySystem caches PlayerMoney.money into a managed int every sim-tick
            // (CitySystem.OnUpdate → moneyAmount). Reading that field is sync-free — the
            // PlayerMoney CompleteDependency was already paid by CitySystem. Used by
            // TryGetCachedBalance for telemetry snapshots that must not force a second sync.
            // GetOrCreate is the sanctioned resolve for mandatory vanilla hosts (CIVIC400/468
            // carve out Game.* types) and guarantees non-null.
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            if (ServiceRegistry.IsInitialized)
            {
                m_Facade = ServiceRegistry.Instance.Get<CityBudgetFacade>();
                if (m_Facade != null) m_Facade.CurrentHost = this;
                else Log.Warn("CityBudgetFacade not in ServiceRegistry — budget ops will fail with SystemUnavailable");
            }
        }

        protected override void OnDestroy()
        {
            if (m_Facade != null && ReferenceEquals(m_Facade.CurrentHost, this))
                m_Facade.CurrentHost = null;
            m_Facade = null;
            m_CitySystem = null;

            if (m_QueryCreated)
            {
                try { m_MoneyQuery.Dispose(); }
                catch (ObjectDisposedException) { /* expected on world cleanup */ }
                catch (Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"Query disposal failed (expected on world cleanup): {ex.Message}");
                }
                m_QueryCreated = false;
            }
            Interlocked.Exchange(ref m_PendingDeductions, 0);
            lock (m_TrackingLock)
            {
                m_ExpensesByCategory.Clear();
                m_IncomeBySource.Clear();
                m_IncomeByKind.Clear();
            }
            base.OnDestroy();
        }

        protected override void OnUpdate() { /* host is event-driven via facade callbacks */ }

        internal BudgetResult TryDeduct(long amount, string category)
        {
            if (amount <= 0)
            {
                Mod.Log.Error($"[CityBudgetHost] TryDeduct called with invalid amount: {amount} (must be > 0)");
                return BudgetResult.InvalidAmount;
            }

            var entity = GetMoneyEntity();
            if (entity == Entity.Null) return BudgetResult.BudgetEntityMissing;

            var money = EntityManager.GetComponentData<PlayerMoney>(entity);
            if (money.money < amount) return BudgetResult.InsufficientFunds;

            ApplyPlayerMoneyDeltaSafe(ref money, -amount, out long appliedDelta);
            if (appliedDelta != -amount)
            {
                Log.Error($"[CityBudgetHost] TryDeduct safe-delta invariant failed: requested=-${amount:N0}, applied={appliedDelta:N0}");
                return BudgetResult.SystemUnavailable;
            }
            EntityManager.SetComponentData(entity, money);

            bool expenseOverflow;
            lock (m_TrackingLock)
            {
                if (!m_ExpensesByCategory.TryGetValue(category, out long current))
                    current = 0;
                if (amount > long.MaxValue - current)
                {
                    expenseOverflow = true;
                    m_ExpensesByCategory[category] = long.MaxValue;
                }
                else
                {
                    expenseOverflow = false;
                    m_ExpensesByCategory[category] = current + amount;
                }
            }

            if (expenseOverflow) Log.Warn($" Expense tracking overflow prevented for {category}");
            if (Log.IsDebugEnabled) Log.Debug($" Deducted ${amount:N0} [{category}]");
            return BudgetResult.Ok;
        }

        internal bool TryGetBalance(out long balance)
        {
            balance = 0;
            var entity = GetMoneyEntity();
            if (entity == Entity.Null) return false;
            balance = EntityManager.GetComponentData<PlayerMoney>(entity).money;
            return true;
        }

        /// <summary>
        /// Sync-free read of the current city balance via vanilla
        /// <see cref="CitySystem.moneyAmount"/> — a managed int that CitySystem refreshes
        /// from PlayerMoney every sim-tick. Unlike <see cref="TryGetBalance"/> this does NOT
        /// call EntityManager.GetComponentData, so it does not force a PlayerMoney
        /// CompleteDependency (the sync was already paid by CitySystem). For telemetry
        /// snapshots only — budget mutations (TryDeduct/AddFunds) must keep the authoritative
        /// GetComponentData path. Returns false on the boot window before CitySystem exists.
        /// </summary>
        internal bool TryGetCachedBalance(out long balance)
        {
            balance = 0;
            if (m_CitySystem == null)
            {
                // Resolved unconditionally in OnCreate via GetOrCreateSystemManaged; null here
                // means a call before OnCreate or after OnDestroy — an initialization-order bug,
                // not the normal path. Callers (telemetry snapshot / wave-start) run well after init.
                Log.Warn(" TryGetCachedBalance: CitySystem unresolved — returning no balance");
                return false;
            }
            balance = m_CitySystem.moneyAmount;
            return true;
        }

        internal long GetBalance()
            => TryGetBalance(out long balance) ? balance : 0;

        internal bool CanAfford(long amount)
            => TryGetBalance(out long balance) && balance >= amount;

        internal bool CanAffordWithPending(long amount)
            => TryGetBalance(out long balance)
               && balance - Interlocked.Read(ref m_PendingDeductions) >= amount;

        internal void RegisterPendingDeduction(long amount)
        {
            if (amount <= 0) return;
            Interlocked.Add(ref m_PendingDeductions, amount);
        }

        internal void RollbackPendingDeduction(long amount)
        {
            if (amount <= 0) return;
            long current;
            long next;
            do
            {
                current = Interlocked.Read(ref m_PendingDeductions);
                next = Math.Max(0, current - amount);
            }
            while (Interlocked.CompareExchange(ref m_PendingDeductions, next, current) != current);
        }

        internal long PendingDeductions => Interlocked.Read(ref m_PendingDeductions);

        internal void ResetPendingDeductions()
            => Interlocked.Exchange(ref m_PendingDeductions, 0);

        internal BudgetResult AddFunds(long amount, string source, BudgetIncomeKind incomeKind)
        {
            if (amount <= 0)
            {
                Mod.Log.Error($"[CityBudgetHost] AddFunds called with invalid amount: {amount} (must be > 0)");
                return BudgetResult.InvalidAmount;
            }

            var entity = GetMoneyEntity();
            if (entity == Entity.Null) return BudgetResult.BudgetEntityMissing;

            var money = EntityManager.GetComponentData<PlayerMoney>(entity);
            ApplyPlayerMoneyDeltaSafe(ref money, amount, out long appliedDelta);
            EntityManager.SetComponentData(entity, money);

            long trackedAmount = Math.Max(0, appliedDelta);
            bool incomeOverflow;
            lock (m_TrackingLock)
            {
                if (!m_IncomeBySource.TryGetValue(source, out long current)) current = 0;
                if (trackedAmount > long.MaxValue - current)
                {
                    incomeOverflow = true;
                    m_IncomeBySource[source] = long.MaxValue;
                }
                else
                {
                    incomeOverflow = false;
                    m_IncomeBySource[source] = current + trackedAmount;
                }
                m_IncomeByKind[incomeKind] = SaturatingAdd(
                    m_IncomeByKind.TryGetValue(incomeKind, out long currentKind) ? currentKind : 0,
                    trackedAmount);
            }

            if (incomeOverflow) Log.Warn($" Income tracking overflow prevented for {source}");
            if (appliedDelta != amount)
            {
                Log.Warn($" AddFunds capped ${amount:N0} [{source}], applied ${trackedAmount:N0}");
                return BudgetResult.Capped;
            }

            Log.Info($" Added ${amount:N0} [{source}]");
            return BudgetResult.Ok;
        }

        internal Dictionary<string, long> GetExpensesByCategory()
        {
            lock (m_TrackingLock) { return new(m_ExpensesByCategory); }
        }

        internal Dictionary<string, long> GetIncomeBySource()
        {
            lock (m_TrackingLock) { return new(m_IncomeBySource); }
        }

        internal Dictionary<BudgetIncomeKind, long> GetIncomeByKind()
        {
            lock (m_TrackingLock) { return new(m_IncomeByKind); }
        }

        internal long GetTotalExpenses()
        {
            long total = 0;
            lock (m_TrackingLock)
            {
                foreach (var kvp in m_ExpensesByCategory)
                    total = SaturatingAdd(total, kvp.Value);
            }
            return total;
        }

        internal long GetTotalIncome()
        {
            long total = 0;
            lock (m_TrackingLock)
            {
                foreach (var kvp in m_IncomeBySource)
                    total = SaturatingAdd(total, kvp.Value);
            }
            return total;
        }

        internal CityBudgetService.BudgetSnapshot GetSnapshot()
        {
            lock (m_TrackingLock)
            {
                long totalExpenses = 0;
                foreach (var kvp in m_ExpensesByCategory)
                    totalExpenses = SaturatingAdd(totalExpenses, kvp.Value);
                long totalIncome = 0;
                foreach (var kvp in m_IncomeBySource)
                    totalIncome = SaturatingAdd(totalIncome, kvp.Value);
                return new CityBudgetService.BudgetSnapshot(
                    new Dictionary<string, long>(m_ExpensesByCategory),
                    new Dictionary<string, long>(m_IncomeBySource),
                    totalExpenses,
                    totalIncome);
            }
        }

        internal long GetRecurringIncome()
        {
            long total = 0;
            lock (m_TrackingLock)
                m_IncomeByKind.TryGetValue(BudgetIncomeKind.RecurringRevenue, out total);
            return total;
        }

        internal void ResetTracking()
        {
            Interlocked.Exchange(ref m_PendingDeductions, 0);
            lock (m_TrackingLock)
            {
                m_ExpensesByCategory.Clear();
                m_IncomeBySource.Clear();
                m_IncomeByKind.Clear();
            }
            Log.Debug(" Tracking reset");
        }

        internal void SetExpensesByCategory(Dictionary<string, long> expenses)
        {
            if (expenses == null)
            {
                Mod.Log.Warn("[CityBudgetHost] SetExpensesByCategory: null input, resetting");
                ResetTracking();
                return;
            }

            const int MAX_CATEGORIES = 20;
            if (expenses.Count > MAX_CATEGORIES)
                Log.Warn($" SetExpensesByCategory: too many categories ({expenses.Count}), capping to {MAX_CATEGORIES}");

            lock (m_TrackingLock)
            {
                m_ExpensesByCategory.Clear();
                int count = 0;
                foreach (var kvp in expenses)
                {
                    if (count >= MAX_CATEGORIES) break;
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    if (kvp.Value < 0) continue;
                    m_ExpensesByCategory[kvp.Key] = kvp.Value;
                    count++;
                }
            }
        }

        internal void SetIncomeBySource(Dictionary<string, long> income)
        {
            if (income == null)
            {
                Mod.Log.Warn("[CityBudgetHost] SetIncomeBySource: null input, resetting");
                ResetTracking();
                return;
            }

            const int MAX_SOURCES = 20;
            if (income.Count > MAX_SOURCES)
                Log.Warn($" SetIncomeBySource: too many sources ({income.Count}), capping to {MAX_SOURCES}");

            lock (m_TrackingLock)
            {
                m_IncomeBySource.Clear();
                m_IncomeByKind.Clear();
                int count = 0;
                foreach (var kvp in income)
                {
                    if (count >= MAX_SOURCES) break;
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    if (kvp.Value < 0) continue;
                    m_IncomeBySource[kvp.Key] = kvp.Value;
                    var kind = CityBudgetService.ClassifyIncomeSource(kvp.Key);
                    m_IncomeByKind[kind] = SaturatingAdd(
                        m_IncomeByKind.TryGetValue(kind, out long currentKind) ? currentKind : 0,
                        kvp.Value);
                    count++;
                }
            }
        }

        internal void SetIncomeByKind(Dictionary<BudgetIncomeKind, long> incomeByKind)
        {
            if (incomeByKind == null) return;
            lock (m_TrackingLock)
            {
                m_IncomeByKind.Clear();
                foreach (var kvp in incomeByKind)
                {
                    if (kvp.Value >= 0)
                        m_IncomeByKind[kvp.Key] = kvp.Value;
                }
            }
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (right > 0 && left > long.MaxValue - right) return long.MaxValue;
            if (right < 0 && left < long.MinValue - right) return long.MinValue;
            return left + right;
        }

        internal static void ApplyPlayerMoneyDeltaSafe(ref PlayerMoney money, long requestedDelta, out long appliedDelta)
        {
            const long minMoney = -2_000_000_000L;
            const long maxMoney = 2_000_000_000L;

            long current = money.money;
            long target;
            if (requestedDelta >= 0)
            {
                target = requestedDelta > maxMoney - current
                    ? maxMoney
                    : current + requestedDelta;
            }
            else
            {
                target = requestedDelta < minMoney - current
                    ? minMoney
                    : current + requestedDelta;
            }
            appliedDelta = target - current;
            long remaining = appliedDelta;

            while (remaining > 0)
            {
                current = money.money;
                long headroom = maxMoney - current;
                if (headroom <= 0)
                    break;

                int chunk = (int)Math.Min(Math.Min(remaining, headroom), int.MaxValue);
                if (chunk <= 0)
                    break;

                money.Add(chunk);
                remaining -= chunk;
            }

            while (remaining < 0)
            {
                current = money.money;
                long tailroom = current - minMoney;
                if (tailroom <= 0)
                    break;

                int chunk = (int)Math.Min(Math.Min(-remaining, tailroom), int.MaxValue);
                if (chunk <= 0)
                    break;

                money.Add(-chunk);
                remaining += chunk;
            }
        }

        private Entity GetMoneyEntity()
        {
            if (!m_QueryCreated) return Entity.Null;

            string queryDebugMsg = null!;
            Entity entityResult;

            if (m_MoneyQuery.IsEmpty)
            {
                queryDebugMsg = " Money query is empty";
                entityResult = Entity.Null;
            }
            else
            {
                var entities = m_MoneyQuery.ToEntityArray(Allocator.Temp);
                entityResult = entities.Length > 0 ? entities[0] : Entity.Null;
                if (entities.IsCreated) entities.Dispose();

                if (entityResult == Entity.Null)
                    queryDebugMsg = " Money entity not found";
                else if (!EntityManager.Exists(entityResult))
                {
                    queryDebugMsg = " Money entity destroyed between query and access";
                    entityResult = Entity.Null;
                }
            }

            if (queryDebugMsg != null && Log.IsDebugEnabled)
                Log.Debug(queryDebugMsg);

            return entityResult;
        }
    }
}
