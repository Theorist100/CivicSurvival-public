/**
 * LedgerSection - Financial overview (Treasury, Offshore, Expenses, Income)
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../shared/common/HoverTip";
import { HelpSection } from "../../shared/common/HelpSection";
import { useTheme, useAccents, hexToRgba } from "../../../themes";
import { useSafeNumber } from "../../../hooks/useSafeBinding";
import { useFinanceContext } from "../../../hooks/domain";
import { refugeeHouseholdCount$ } from "../../../hooks/bindings/refugeeBindings";  // CDI-3 (live ValueBinding)
import { GlassCase, SectionHeader, StatRow } from "../../shared/ui";
import { createSectionStyles, formatMoney } from "./SectionStyles";
import { useLocale, type TranslationKey } from "../../../locales";

const EXPENSE_L10N: Record<string, TranslationKey> = {
    AirDefense: "UI_LEDGER_EXP_AIR_DEFENSE",
    SpotterOps: "UI_LEDGER_EXP_SPOTTER_OPS",
    Repairs: "UI_LEDGER_EXP_REPAIRS",
    CognitiveOps: "UI_LEDGER_EXP_COGNITIVE_OPS",
    Procurement: "UI_LEDGER_EXP_PROCUREMENT",
    Penalties: "UI_LEDGER_EXP_PENALTIES",
    DebtPayment: "UI_LEDGER_EXP_DEBT_PAYMENT",
    RefugeeSupport: "UI_LEDGER_EXP_REFUGEE_SUPPORT",
    Other: "UI_LEDGER_EXP_OTHER",
};

const INCOME_L10N: Record<string, TranslationKey> = {
    DonorAid: "UI_LEDGER_INC_DONOR_AID",
    EmergencyFunding: "UI_LEDGER_INC_EMERGENCY_FUND",
    ResupplyRefund: "UI_LEDGER_INC_RESUPPLY_REFUND",
    Other: "UI_LEDGER_INC_OTHER",
};

const DEBT_L10N: Record<string, TranslationKey> = {
    WarDamage: "UI_LEDGER_DEBT_WAR_DAMAGE",
    Infrastructure: "UI_LEDGER_DEBT_INFRASTRUCTURE",
    IMFLoan: "UI_LEDGER_DEBT_IMF_LOAN",
};

export const LedgerSection = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createSectionStyles(theme, accents), [theme, accents]);

    const finance = useFinanceContext();
    const refugeeHouseholdCount = useSafeNumber(refugeeHouseholdCount$, 0);  // CDI-3

    return (
        <GlassCase
            feature="ShadowEconomy"
            name="Shadow Ledger"
            description="Total city liquidity: official treasury, offshore funds, war expenses, daily income and city debt. Unlocks when shadow economy systems come online."
        >
            {/* Total Liquidity */}
            <div style={s.section}>
                <SectionHeader
                    title={l.t("UI_LEDGER_TOTAL_LIQUIDITY")}
                    titleStyle={s.sectionTitle}
                    help={<HelpSection id="ledger" title={l.t("UI_LEDGER_TOTAL_LIQUIDITY")}>{l.t("HELP_LEDGER")}</HelpSection>}
                />
                <div style={s.valueLarge(accents.schemes.accent)}>
                    {formatMoney(finance.totalLiquidity)}
                </div>
            </div>

            {/* Accounts */}
            <div style={s.section}>
                <div style={s.sectionTitle}>{l.t("UI_LEDGER_ACCOUNTS")}</div>
                <StatRow
                    label={l.t("UI_LEDGER_CITY_TREASURY")}
                    value={<HoverTip text={l.t("TIP_TREASURY")}>{finance.treasuryFormatted}</HoverTip>}
                    color={finance.treasuryColor}
                />
                <StatRow
                    label={l.t("UI_LEDGER_OFFSHORE")}
                    value={<HoverTip text={l.t("TIP_OFFSHORE")}>{formatMoney(finance.offshoreBalance)}</HoverTip>}
                    color={accents.schemes.accent}
                    emphasis="title"
                />
                {finance.lockedBalance > 0 && (
                    <StatRow
                        label={l.t("UI_GW_BALANCE_LOCKED")}
                        value={formatMoney(finance.lockedBalance)}
                        color={theme.colors.textMuted}
                    />
                )}
                {finance.hasSanctionsMarkup && (
                    <div style={{
                        padding: "4rem 6rem",
                        marginTop: "4rem",
                        background: hexToRgba(accents.crisis.accent, 0.13),
                        border: `2rem solid ${accents.crisis.accent}`,
                        borderRadius: "4rem",
                        fontSize: "11rem",
                        color: accents.crisis.accent,
                    }}>
                        {l.t("UI_LEDGER_SANCTIONS_MARKUP", finance.sanctionsMarkupDisplay)}
                    </div>
                )}
            </div>

            {/* Expenses */}
            <div style={s.section}>
                <div style={s.sectionTitle}>{l.t("UI_LEDGER_WAR_EXPENSES")}</div>
                {finance.hasExpenses ? (
                    <>
                        {Object.entries(finance.expenses).map(([key, value]) => {
                            const labelKey = EXPENSE_L10N[key];
                            return (
                                <StatRow
                                    key={key}
                                    label={<>
                                        {labelKey ? l.t(labelKey) : key}
                                        {key === "RefugeeSupport" && refugeeHouseholdCount > 0 && (
                                            <span style={{ opacity: 0.7, marginLeft: "0.3rem" }}>
                                                ({refugeeHouseholdCount})
                                            </span>
                                        )}
                                    </>}
                                    value={`-${formatMoney(Math.abs(value))}`}
                                    color={accents.crisis.accent}
                                />
                            );
                        })}
                        <div style={s.divider} />
                        <StatRow label={l.t("UI_LEDGER_TOTAL")} value={`-${formatMoney(Math.abs(finance.totalExpenses))}`} color={accents.crisis.accent} emphasis="title" />
                    </>
                ) : (
                    <div style={s.noData}>{l.t("UI_LEDGER_NO_EXPENSES")}</div>
                )}
            </div>

            {/* Income */}
            <div style={s.section}>
                <div style={s.sectionTitle}>{l.t("UI_LEDGER_INCOME")}</div>
                {finance.hasIncome ? (
                    <>
                        {Object.entries(finance.income).map(([key, value]) => {
                            const labelKey = INCOME_L10N[key];
                            return (
                                <StatRow
                                    key={key}
                                    label={labelKey ? l.t(labelKey) : key}
                                    value={`+${formatMoney(value)}`}
                                    color={accents.schemes.accent}
                                />
                            );
                        })}
                        <div style={s.divider} />
                        <StatRow label={l.t("UI_LEDGER_TOTAL")} value={`+${formatMoney(finance.totalIncome)}`} color={accents.schemes.accent} emphasis="title" />
                    </>
                ) : (
                    <div style={s.noData}>{l.t("UI_LEDGER_NO_INCOME")}</div>
                )}
            </div>

            {/* Net Position */}
            {(finance.hasExpenses || finance.hasIncome) && (
                <div style={s.section}>
                    <div style={s.sectionTitle}>{l.t("UI_LEDGER_NET_POSITION")}</div>
                    <div style={s.valueLarge(finance.netColor)}>
                        {finance.netPosition >= 0 ? "+" : "-"}{formatMoney(Math.abs(finance.netPosition))}
                    </div>
                </div>
            )}

            {/* City Debt */}
            {finance.hasDebt && (
                <div style={s.section}>
                    <div style={s.sectionTitle}>{l.t("UI_LEDGER_CITY_DEBT")}</div>
                    {finance.debtRestructured && (
                        <div style={{
                            padding: "4rem 6rem",
                            marginBottom: "4rem",
                            background: hexToRgba(accents.crisis.accent, 0.13),
                            border: `2rem solid ${accents.crisis.accent}`,
                            borderRadius: "4rem",
                            fontSize: "11rem",
                            color: accents.crisis.accent,
                        }}>
                            {l.t("UI_LEDGER_DEBT_RESTRUCTURED")}
                        </div>
                    )}
                    {finance.debtWarning && !finance.debtRestructured && (
                        <div style={{
                            padding: "4rem 6rem",
                            marginBottom: "4rem",
                            background: hexToRgba(accents.schemes.accent, 0.13),
                            border: `2rem solid ${accents.schemes.accent}`,
                            borderRadius: "4rem",
                            fontSize: "11rem",
                            color: accents.schemes.accent,
                        }}>
                            {l.t("UI_LEDGER_DEBT_WARNING")}
                        </div>
                    )}
                    {Object.entries(finance.debt).map(([key, value]) => {
                        const labelKey = DEBT_L10N[key];
                        return (
                            <StatRow
                                key={key}
                                label={labelKey ? l.t(labelKey) : key}
                                value={formatMoney(value)}
                                color={accents.crisis.accent}
                            />
                        );
                    })}
                    <div style={s.divider} />
                    <StatRow label={l.t("UI_LEDGER_TOTAL_DEBT")} value={formatMoney(finance.totalDebt)} color={accents.crisis.accent} emphasis="title" />
                </div>
            )}
        </GlassCase>
    );
});
LedgerSection.displayName = "LedgerSection";

