using Unity.Entities;
using Game.City;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Finance.UI
{
    /// <summary>
    /// UI system for War Economy monitoring.
    /// Shows city treasury, offshore funds, war expenses breakdown, and aid received.
    /// READ-only panel (monitoring), no triggers.
    /// ECS-Pure: Reads offshore balance from ShadowWalletSingleton directly.
    ///
    /// Migrated from FinanceUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// Note: FinanceUIPanel had its own internal ThrottleHelper (1Hz).
    /// Now uses CivicUIPanelSystem stagger (~2s) which is close enough.
    /// </summary>
    [ActIndependent]
    public partial class FinanceUISystem : CivicUIPanelSystem
    {
        private EntityQuery m_MoneyQuery;
        private EntityQuery m_WalletQuery;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_MoneyQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerMoney>());
            m_WalletQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShadowWalletSingleton>());

            // Need at least PlayerMoney to be useful
            RequireForUpdate(m_MoneyQuery);

            m_DependencyWire = new CivicDependencyWire(nameof(FinanceUISystem));

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // Axiom 15: resolve service in OnStartRunning, not OnCreate (registration order).
            // EnsureWired retries until ShadowWalletService is registered; until then
            // NullShadowWalletService stays in place so the UI keeps showing zeros.
            m_DependencyWire.EnsureWired(() =>
            {
                m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
                return m_WalletService is not NullShadowWalletService;
            });
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(FinanceState, "{}");
        }

        protected override void OnPanelUpdate()
        {
            long cityTreasury = GetCityTreasury();
            var wallet = m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var sw)
                ? sw : ShadowWalletSingleton.Default;
            long pending = m_WalletService.PendingDeductions;
            long shadowAvailable = SaturatingSubtract(wallet.Balance, pending);
            long shadowLocked = SaturatingAdd(wallet.LockedBalance, pending);
            long shadowTotalAssets = SaturatingAdd(shadowAvailable, shadowLocked);
            long shadowIncome = wallet.TotalIncome;
            long shadowExpenses = wallet.TotalExpenses;
            float sanctionsMarkup = wallet.SanctionsMarkup;

            var budgetSnapshot = CityBudgetService.GetSnapshot();
            var debtSnapshot = CityDebtService.GetSnapshot();
            var debtConfig = BalanceConfig.Current.Debt;

            var dto = new FinanceDto
            {
                CityTreasury = cityTreasury,
                TotalLiquidity = SaturatingAdd(cityTreasury, shadowTotalAssets),
                OfficialTreasury = new OfficialTreasuryDto
                {
                    Balance = cityTreasury,
                    TotalIncome = budgetSnapshot.TotalIncome,
                    TotalExpenses = budgetSnapshot.TotalExpenses,
                },
                ShadowWallet = new ShadowWalletDto
                {
                    Available = shadowAvailable,
                    LockedBalance = shadowLocked,
                    TotalAssets = shadowTotalAssets,
                    ShadowIncome = shadowIncome,
                    ShadowExpenses = shadowExpenses,
                },
                Expenses = budgetSnapshot.Expenses,
                Income = budgetSnapshot.Income,
                TotalExpenses = budgetSnapshot.TotalExpenses,
                TotalIncome = budgetSnapshot.TotalIncome,
                TotalDebt = debtSnapshot.TotalDebt,
                DebtBreakdown = debtSnapshot.Breakdown,
                DebtWarning = CityDebtService.ShouldShowDebtWarning(debtSnapshot, debtConfig.WarningRatio),
                DebtRestructured = debtSnapshot.DebtRestructured,
                SanctionsMarkup = sanctionsMarkup
            };

            PublishWhenComplete(FinanceState, NoSourceChecks, () => dto);
        }

        private long GetCityTreasury()
        {
            if (!m_MoneyQuery.TryGetSingleton<PlayerMoney>(out var money)) return 0;
            return money.money;
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (right > 0 && left > long.MaxValue - right)
                return long.MaxValue;
            if (right < 0 && left < long.MinValue - right)
                return long.MinValue;
            return left + right;
        }

        private static long SaturatingSubtract(long left, long right)
        {
            if (right > 0 && left < long.MinValue + right)
                return long.MinValue;
            if (right < 0 && left > long.MaxValue + right)
                return long.MaxValue;
            return left - right;
        }
    }
}
