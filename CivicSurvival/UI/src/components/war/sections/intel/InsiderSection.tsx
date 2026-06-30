/**
 * InsiderSection - Insider info status, purchase button, shadow balance
 * Intel domain → Right column
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents, formatMoney, formatCostArg } from "@themes";
import { useLocale } from "@locales";
import { createWarViewsStyles } from "../../../Dashboard/ContentPanel/WarViews.styles";
import { useRequestAction } from "@hooks/actions";
import type { RequestResult } from "../../../../types/dtoSubTypes";
import { type useIntelActions } from "@hooks/actions";

export interface InsiderSectionProps {
    actions: ReturnType<typeof useIntelActions>;
    hasInsiderInfo: boolean;
    insiderCost: number;
    canBuyInsider: boolean;
    insiderLockedReasonId: string;
    offshoreBalance: number;
    priceModifier: number;
    isFrozen: boolean;
    insiderRequest: RequestResult;
}

export const InsiderSection: React.FC<InsiderSectionProps> = memo(({
    actions,
    hasInsiderInfo,
    insiderCost,
    canBuyInsider,
    insiderLockedReasonId,
    offshoreBalance,
    priceModifier,
    isFrozen,
    insiderRequest,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);

    const purchaseAction = useRequestAction(() => {
        actions.purchaseInsider();
        return true;
    }, insiderRequest);
    const isPending = purchaseAction.isPending || insiderRequest.Status === "pending";
    const errorKey = insiderRequest.Status === "failed" ? insiderRequest.ReasonId : "";
    const lockedText = insiderLockedReasonId ? l.tDynamic(insiderLockedReasonId) : l.t("INTEL_BUY_INSIDER_SHADOW", formatCostArg(insiderCost));

    return (
        <div style={s.section}>
            {/* Insider Status / Purchase */}
            {hasInsiderInfo ? (
                <div style={{ ...s.insiderActive, marginBottom: "8rem" }}>
                    <span style={{ ...s.insiderActiveChild, fontWeight: 700, color: theme.colors.success }}>{l.t("UI_INSIDER_OK")}</span>
                    <span>{l.t("INTEL_INSIDER_ACTIVE")}</span>
                </div>
            ) : (
                <>
                    <button
                        style={{ ...s.buyButton(canBuyInsider && !isPending), marginBottom: "8rem" }}
                        onClick={purchaseAction.execute}
                        disabled={!canBuyInsider || isPending}
                    >
                        <span style={{ ...s.buyButtonChild, fontWeight: 700 }}>[!]</span>
                        <span>
                            {isPending ? l.t("UI_PROCESSING") : canBuyInsider ? l.t("INTEL_BUY_INSIDER_SHADOW", formatCostArg(insiderCost)) : lockedText}
                        </span>
                    </button>
                    <div style={{ ...s.shadowBalance, marginBottom: "8rem" }}>
                        {`[$] ${l.t("INTEL_SHADOW_BALANCE")}: ${formatMoney(offshoreBalance)}`}
                    </div>
                    {isFrozen && (
                        <div style={{ fontSize: theme.typography.sizeSM, color: accents.crisis.accent, marginBottom: "8rem" }}>
                            {l.t("UI_WALLET_FROZEN")}
                        </div>
                    )}
                    {errorKey && (
                        <div style={{ fontSize: theme.typography.sizeSM, color: theme.colors.error, marginBottom: "8rem" }}>
                            {l.tDynamic(errorKey)}
                        </div>
                    )}
                </>
            )}

            {/* Price Impact Warning */}
            {priceModifier > 0 && (
                <div style={s.priceImpact}>
                    <span style={s.priceImpactIcon}>[!]</span>
                    <span>{l.t("INTEL_PRICE_IMPACT", priceModifier)}</span>
                </div>
            )}
        </div>
    );
});

InsiderSection.displayName = "InsiderSection";
