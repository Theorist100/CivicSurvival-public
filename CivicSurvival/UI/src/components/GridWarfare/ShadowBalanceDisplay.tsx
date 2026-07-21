/**
 * ShadowBalanceDisplay - Shadow Wallet Balance
 * Shows available + locked funds
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents } from "../../themes";
import { createGridWarfareStyles } from "./GridWarfare.styles";
import { formatShadowAmount } from "./GridWarfare.types";
import { useLocale } from "../../locales";
import { StatRow } from "../shared/ui";

interface ShadowBalanceDisplayProps {
    balance: number;
    locked: number;
    total: number;
}

export const ShadowBalanceDisplay: React.FC<ShadowBalanceDisplayProps> = memo(({
    balance,
    locked,
    total,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();

    return (
        <div style={s.balanceContainer}>
            {/* Main balance row */}
            <div style={s.balanceRow}>
                <span style={s.balanceLabel}>{l.t("UI_GW_SHADOW_CASH")}</span>
                <span style={s.balanceAmount(accents.schemes.accent)}>
                    {formatShadowAmount(balance)}
                </span>
            </div>

            {/* Locked funds (if any) */}
            {locked > 0 && (
                <StatRow
                    compact
                    label={l.t("UI_GW_BALANCE_LOCKED")}
                    value={formatShadowAmount(locked)}
                    color={accents.resilience.accent}
                    style={{
                        marginTop: "4rem",
                        paddingTop: "4rem",
                        borderTop: `2rem solid ${theme.colors.border}`,
                    }}
                    valueStyle={s.balanceLocked}
                />
            )}

            {/* Total (if different from balance) */}
            {locked > 0 && (
                <StatRow
                    compact
                    label={l.t("UI_GW_TOTAL_ASSETS")}
                    value={formatShadowAmount(total)}
                    color={theme.colors.textSecondary}
                    style={{ marginTop: "4rem" }}
                    valueStyle={{
                        fontSize: "11rem",
                        fontWeight: 400,
                    }}
                />
            )}
        </div>
    );
});

ShadowBalanceDisplay.displayName = "ShadowBalanceDisplay";
