/**
 * IntelPreview - Intel level + upgrade.
 * Higher intel sharpens the player's read on the enemy's three axes; offers upgrade.
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents } from "../../themes";
import { createGridWarfareStyles } from "./GridWarfare.styles";
import { IconSatellite } from "../shared/common/Icons";
import { formatShadowAmount } from "./GridWarfare.types";
import { useLocale } from "../../locales";
import { type RequestResult } from "../../types/dtoSubTypes";
import { useRequestAction } from "@hooks/actions";

interface IntelPreviewProps {
    intelLevel: number;         // 0, 1, or 2
    upgradeCost: number;
    canUpgrade: boolean;
    lockedReasonId: string;
    intelUpgradeRequest: RequestResult;
    onUpgrade: () => void;
}

export const IntelPreview: React.FC<IntelPreviewProps> = memo(({
    intelLevel,
    upgradeCost,
    canUpgrade,
    lockedReasonId,
    intelUpgradeRequest,
    onUpgrade,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();
    const upgradeAction = useRequestAction(() => {
        onUpgrade();
        return true;
    }, intelUpgradeRequest);
    const isPending = upgradeAction.isPending || intelUpgradeRequest.Status === "pending";
    const errorText = intelUpgradeRequest.Status === "failed" && intelUpgradeRequest.ReasonId
        ? l.tDynamic(intelUpgradeRequest.ReasonId)
        : "";

    const getNextLevelDesc = (): string => {
        if (intelLevel === 0) return l.t("UI_GW_INTEL_DESC_LV1");
        if (intelLevel === 1) return l.t("UI_GW_INTEL_DESC_LV2");
        return l.t("UI_GW_INTEL_DESC_MAX");
    };

    const lockedText = lockedReasonId ? l.tDynamic(lockedReasonId) : l.t("UI_GW_INTEL_NEED", formatShadowAmount(upgradeCost));
    const isMaxLevel = intelLevel >= 2;

    return (
        <div style={s.intelContainer}>
            {/* Header */}
            <div style={s.intelHeader}>
                <span style={s.intelIcon}><IconSatellite /></span>
                <span style={s.intelTitle}>{l.t("UI_GW_INTEL_LEVEL", intelLevel)}</span>
            </div>

            {/* Content */}
            <div style={s.intelContent}>
                {isMaxLevel ? (
                    <div style={{
                        fontSize: "10rem",
                        color: accents.schemes.accent,
                        textAlign: "center" as const,
                    }}>
                        {l.t("UI_GW_FULL_AWARENESS")}
                    </div>
                ) : (
                    <span style={{ color: theme.colors.textMuted }}>{l.t("UI_GW_NO_INTEL")}</span>
                )}
            </div>

            {!isMaxLevel && (
                <>
                    <button
                        style={{
                            ...s.intelUpgrade,
                            opacity: canUpgrade && !isPending ? 1 : 0.5,
                            cursor: canUpgrade && !isPending ? "pointer" : "not-allowed",
                        }}
                        onClick={canUpgrade && !isPending ? upgradeAction.execute : undefined}
                        disabled={!canUpgrade || isPending}
                    >
                        {isPending
                            ? l.t("UI_PROCESSING")
                            : canUpgrade
                            ? l.t("UI_GW_INTEL_UPGRADE", formatShadowAmount(upgradeCost), getNextLevelDesc())
                            : lockedText
                        }
                    </button>
                    {errorText && <div style={{ marginTop: "4rem", color: accents.crisis.accent, fontSize: "10rem", fontWeight: 700, textAlign: "center" }}>{errorText}</div>}
                </>
            )}
        </div>
    );
});

IntelPreview.displayName = "IntelPreview";
