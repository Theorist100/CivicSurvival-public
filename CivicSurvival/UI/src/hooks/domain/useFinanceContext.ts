/**
 * Cross-cutting finance context.
 *
 * Use this when a view needs finance decisions or display helpers, not just
 * the raw FinanceDto. Raw useFinance remains the low-level binding hook.
 */

import { useMemo } from "react";
import { useAccents, useTheme } from "../../themes";
import { formatMoney } from "../../themes/commonStyles";
import { useFinance } from "./useFinance";
import { bindingDataOrDefault } from "./useDtoBinding";
import { DEFAULT_FINANCE_DTO } from "../../types/domainDtos";

export function useFinanceContext() {
    const financeState = useFinance();
    const theme = useTheme();
    const accents = useAccents();

    const finance = bindingDataOrDefault(financeState, DEFAULT_FINANCE_DTO);

    return useMemo(() => {
        const expenses = finance.Expenses;
        const income = finance.Income;
        const debt = finance.DebtBreakdown;
        const netPosition = finance.TotalIncome - finance.TotalExpenses;
        const isInDeficit = netPosition < 0;

        return {
            raw: finance,
            cityTreasury: finance.CityTreasury,
            shadowWallet: finance.ShadowWallet,
            offshoreBalance: finance.ShadowWallet.Available,
            lockedBalance: finance.ShadowWallet.LockedBalance,
            totalLiquidity: finance.ShadowWallet.TotalAssets + finance.OfficialTreasury.Balance,
            expenses,
            income,
            totalExpenses: finance.TotalExpenses,
            totalIncome: finance.TotalIncome,
            totalDebt: finance.TotalDebt,
            debt,
            debtWarning: finance.DebtWarning,
            debtRestructured: finance.DebtRestructured,
            sanctionsMarkup: finance.SanctionsMarkup,
            hasExpenses: Object.keys(expenses).length > 0,
            hasIncome: Object.keys(income).length > 0,
            hasDebt: finance.TotalDebt > 0,
            hasSanctionsMarkup: finance.SanctionsMarkup > 0,
            isInDeficit,
            netPosition,
            netColor: isInDeficit ? accents.crisis.accent : accents.schemes.accent,
            treasuryColor: finance.CityTreasury < 0 ? accents.crisis.accent : theme.colors.textPrimary,
            treasuryFormatted: formatMoney(finance.CityTreasury),
            sanctionsMarkupDisplay: `${Math.round(finance.SanctionsMarkup * 100)}%`,
        };
    }, [finance, accents, theme]);
}
