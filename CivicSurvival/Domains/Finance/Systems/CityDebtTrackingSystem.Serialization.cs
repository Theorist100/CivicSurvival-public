using Colossal.Serialization.Entities;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Utils;
using System.Collections.Generic;

namespace CivicSurvival.Domains.Finance.Systems
{
    /// <summary>
    /// CityDebtTrackingSystem - Save/Load serialization.
    /// Persists debt by category dictionary from CityDebtService.
    /// </summary>
    public partial class CityDebtTrackingSystem : IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void ResetState()
        {
            m_DayDedup.Reset();
            m_PendingBillingDay = 0;
            m_PendingBillingDecisionDebt = 0;
            m_PendingBillingPeriodIncome = 0;
            CityDebtService.ResetTracking();
            CityBudgetService.ResetTracking();
            Log.Debug("ResetState - debt and budget tracking reset");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                var debt = CityDebtService.GetDebtBreakdown();
                var expenses = CityBudgetService.GetExpensesByCategory();
                var income = CityBudgetService.GetIncomeBySource();
                var incomeByKind = CityBudgetService.GetIncomeByKind();
                int lastAppliedBillingDay = System.Math.Max(
                    m_DayDedup.LastProcessedDay,
                    CityDebtService.GetLastAppliedBillingDay());
                var state = new CityDebtPersistState(
                    ToEntries(debt),
                    ToEntries(expenses),
                    ToEntries(income),
                    ToIncomeKindEntries(incomeByKind),
                    lastAppliedBillingDay,
                    CityDebtService.GetIncomeAtPeriodStart(),
                    CityDebtService.GetIncomePeriodInitialized(),
                    CityDebtService.GetRestructureActive(),
                    CityDebtService.GetWarningPublishedThisCycle(),
                    m_PendingBillingDay,
                    m_PendingBillingDecisionDebt,
                    m_PendingBillingPeriodIncome);
                CityDebtCodec.Write(state, writer);

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized(nameof(CityDebtTrackingSystem), SaveVersions.GLOBAL);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(CityDebtTrackingSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                CityDebtCodec.Read(reader, out var state);
                var debt = ToDictionary(state.Debt);
                var expenses = ToDictionary(state.Expenses);
                var income = ToDictionary(state.Income);
                var incomeByKind = ToIncomeKindDictionary(state.IncomeByKind);

                CityDebtService.SetDebtByCategory(debt);
                CityBudgetService.SetExpensesByCategory(expenses);
                CityBudgetService.SetIncomeBySource(income);
                CityBudgetService.SetIncomeByKind(incomeByKind);
                m_DayDedup = DayChangedDedup.FromSave(state.LastProcessedDay);
                CityDebtService.RestoreLastAppliedBillingDayFromLoad(state.LastProcessedDay);
                CityDebtService.SetIncomeAtPeriodStart(state.IncomeAtPeriodStart);
                CityDebtService.SetIncomePeriodInitialized(state.IncomePeriodInitialized);
                CityDebtService.SetRestructureActive(state.RestructureActive);
                CityDebtService.SetWarningPublishedThisCycle(state.WarningPublishedThisCycle);
                m_PendingBillingDay = state.PendingBillingDay;
                m_PendingBillingDecisionDebt = state.PendingDecisionDebt;
                m_PendingBillingPeriodIncome = state.PendingPeriodIncome;
                if (m_PendingBillingDay <= state.LastProcessedDay)
                {
                    m_PendingBillingDay = 0;
                    m_PendingBillingDecisionDebt = 0;
                    m_PendingBillingPeriodIncome = 0;
                }

                Log.Info($"Deserialized: {debt.Count} debt, {expenses.Count} expenses, {income.Count} income categories. Total debt: ${CityDebtService.GetTotalDebt():N0}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private static StringLongPersistEntry[] ToEntries(Dictionary<string, long> source)
        {
            var entries = new StringLongPersistEntry[source.Count];
            int index = 0;
            foreach (var kvp in source)
                entries[index++] = new StringLongPersistEntry(kvp.Key, kvp.Value);
            return entries;
        }

        private static Dictionary<string, long> ToDictionary(IReadOnlyList<StringLongPersistEntry> entries)
        {
            var result = new Dictionary<string, long>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
                result[entries[i].Key] = entries[i].Value;
            return result;
        }

        private static IncomeKindLongPersistEntry[] ToIncomeKindEntries(Dictionary<BudgetIncomeKind, long> source)
        {
            var entries = new IncomeKindLongPersistEntry[source.Count];
            int index = 0;
            foreach (var kvp in source)
                entries[index++] = new IncomeKindLongPersistEntry((int)kvp.Key, kvp.Value);
            return entries;
        }

        private static Dictionary<BudgetIncomeKind, long> ToIncomeKindDictionary(IReadOnlyList<IncomeKindLongPersistEntry> entries)
        {
            var result = new Dictionary<BudgetIncomeKind, long>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var kind = entries[i].Kind switch
                {
                    1 => BudgetIncomeKind.Refund,
                    2 => BudgetIncomeKind.DebtMovement,
                    3 => BudgetIncomeKind.DonorOrEmergencyCredit,
                    4 => BudgetIncomeKind.Kickback,
                    5 => BudgetIncomeKind.OneOffCredit,
                    _ => BudgetIncomeKind.RecurringRevenue
                };
                result[kind] = entries[i].Value;
            }
            return result;
        }
    }
}
