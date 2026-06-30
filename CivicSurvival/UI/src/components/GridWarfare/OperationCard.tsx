/**
 * OperationCard - Single Operation Slot
 * Shows operation state: idle, preparing, ready, or executing
 * Handles Prepare/Execute/Cancel actions
 */

import React, { memo, useMemo, useCallback } from "react";
import { Row } from "../coherent";
import { useTheme, useAccents } from "../../themes";
import { createGridWarfareStyles, getOperationColor } from "./GridWarfare.styles";
import { type OperationSlotDto } from "../../types/domainDtos.generated";
import { type GridOperationType, formatShadowAmount } from "./GridWarfare.types";
import { useLocale, type TranslationKey } from "../../locales";
import { ProgressBar } from "@shared/ui";

interface OperationCardProps {
    type: GridOperationType;
    slot: OperationSlotDto | null;
    cost: number;
    canPrepare: boolean;
    prepareLockedReasonId: string;
    isVulnerable: boolean;
    isPending: boolean;
    onPrepare: (type: GridOperationType) => void;
    onExecute: (type: GridOperationType) => void;
    onCancel: (type: GridOperationType) => void;
    refundFlash: number | null;
}

// Operation name localization keys
const OP_NAME_KEYS: Record<GridOperationType, TranslationKey> = {
    drone: "UI_GW_OP_DRONE",
    blackout: "UI_GW_OP_BLACKOUT",
    disinfo: "UI_GW_OP_DISINFO",
};

export const OperationCard: React.FC<OperationCardProps> = memo(({
    type,
    slot,
    cost,
    canPrepare,
    prepareLockedReasonId,
    isVulnerable,
    isPending,
    onPrepare,
    onExecute,
    onCancel,
    refundFlash,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();

    const opColor = getOperationColor(type, accents);
    const opName = l.t(OP_NAME_KEYS[type]);

    const handlePrepare = useCallback(() => onPrepare(type), [onPrepare, type]);
    const handleExecute = useCallback(() => onExecute(type), [onExecute, type]);
    const handleCancel = useCallback(() => onCancel(type), [onCancel, type]);

    const isIdle = !slot;
    const state = slot?.OperationState ?? "idle";
    const isPreparing = !isIdle && state === "preparing";
    const isReady = !isIdle && state === "ready";
    const isExecuting = !isIdle && state === "executing";
    const prepareDisabledText = prepareLockedReasonId
        ? l.tDynamic(prepareLockedReasonId)
        : l.t("UI_GW_BTN_NO_FUNDS");

    // S6-04: Show locked cost indicator when current price differs from locked price
    const lockedCost = slot?.Cost ?? 0;
    const costDrifted = !isIdle && lockedCost > 0 && lockedCost !== cost;

    // Progress for preparing state (0-1)
    const progress = slot?.Progress ? Math.min(1, Math.max(0, slot.Progress)) : 0;

    // State display
    const getStateDisplay = (): { text: string; color: string } => {
        if (isReady) return { text: l.t("UI_GW_STATE_READY"), color: accents.schemes.accent };
        if (isExecuting) return { text: l.t("UI_GW_STATE_EXECUTING"), color: accents.crisis.accent };
        if (isPreparing) return { text: l.t("UI_GW_STATE_PREPARING"), color: accents.resilience.accent };
        return { text: l.t("UI_GW_STATE_AVAILABLE"), color: theme.colors.textMuted };
    };

    const stateDisplay = getStateDisplay();

    return (
        <div style={s.operationCard(opColor, !isIdle)}>
            {/* Header: Name + Cost + State */}
            <div style={s.cardHeader}>
                <Row align="center">
                    <span style={{ ...s.cardTitle(opColor), marginRight: "8rem" }}>{opName}</span>
                    <span style={s.cardCost}>
                        {formatShadowAmount(slot?.Cost ?? cost)}
                        {costDrifted && (
                            <span style={{ fontSize: "10rem", color: accents.resilience.accent, marginLeft: "4rem" }}>
                                {l.t("UI_GW_LOCKED")}
                            </span>
                        )}
                    </span>
                </Row>
                <span style={s.cardState(stateDisplay.color)}>{stateDisplay.text}</span>
            </div>

            {/* Progress bar (only for preparing) */}
            {isPreparing && (
                <ProgressBar value={progress * 100} color={accents.resilience.accent} height="6rem" />
            )}

            {/* Actions */}
            <div style={s.cardActions}>
                {isIdle && (
                    <button
                        style={s.buttonPrimary(opColor, canPrepare && !isPending)}
                        onClick={canPrepare && !isPending ? handlePrepare : undefined}
                        disabled={!canPrepare || isPending}
                    >
                        {isPending ? l.t("UI_PROCESSING") : canPrepare ? l.t("UI_GW_BTN_PREPARE") : prepareDisabledText}
                    </button>
                )}

                {isPreparing && (
                    <button
                        style={s.button(accents.crisis.accent, true)}
                        onClick={isPending ? undefined : handleCancel}
                        disabled={isPending}
                    >
                        {isPending ? l.t("UI_PROCESSING") : l.t("UI_GW_BTN_CANCEL")}
                    </button>
                )}

                {isReady && (
                    <>
                        <button
                            style={s.buttonPrimary(
                                isVulnerable ? accents.schemes.accent : opColor,
                                !isPending
                            )}
                            onClick={!isPending ? handleExecute : undefined}
                            disabled={isPending}
                        >
                            {isPending ? l.t("UI_PROCESSING") : l.t("UI_GW_BTN_EXECUTE")}
                        </button>
                        <button
                            style={s.button(accents.crisis.accent, true)}
                            onClick={isPending ? undefined : handleCancel}
                            disabled={isPending}
                        >
                            {isPending ? l.t("UI_PROCESSING") : l.t("UI_GW_BTN_CANCEL")}
                        </button>
                    </>
                )}

                {isExecuting && (
                    <button
                        style={s.button(accents.crisis.accent, false)}
                        disabled={true}
                    >
                        {l.t("UI_PROCESSING")}
                    </button>
                )}
            </div>

            {/* Damage hint for ready operations */}
            {isReady && (
                <div style={{
                    fontSize: "10rem",
                    color: theme.colors.textMuted,
                    marginTop: "4rem",
                    textAlign: "center" as const,
                }}>
                    {isVulnerable ? (
                        <span style={{ color: accents.schemes.accent }}>{l.t("UI_GW_BONUS_ACTIVE")}</span>
                    ) : (
                        <span>{l.t("UI_GW_BONUS_HINT")}</span>
                    )}
                </div>
            )}

            {/* Refund flash after backend-confirmed cancel */}
            {refundFlash !== null && (
                <div style={{
                    fontSize: "11rem",
                    fontWeight: 700,
                    color: accents.resilience.accent,
                    textAlign: "center" as const,
                    marginTop: "4rem",
                    padding: "4rem 0",
                }}>
                    {l.t("UI_GW_REFUNDED", formatShadowAmount(refundFlash))}
                </div>
            )}
        </div>
    );
});

OperationCard.displayName = "OperationCard";
