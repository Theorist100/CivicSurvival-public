/**
 * OperationRow - Single Operation Slot as a compact horizontal row
 *
 * The wide-surface counterpart of OperationCard (used by OperationSlots layout="rows"
 * on the dashboard COMMAND tab): name + cost on the left, state badge in the middle,
 * a fixed-width action zone on the right so every row's buttons align. Same props,
 * same pause-safe Prepare/Execute/Cancel callbacks — layout only.
 */

import React, { memo, useMemo, useCallback } from "react";
import { Row } from "../coherent";
import { useTheme, useAccents } from "../../themes";
import { createGridWarfareStyles, getOperationColor } from "./GridWarfare.styles";
import { type OperationSlotDto } from "../../types/domainDtos.generated";
import { type GridOperationType, formatShadowAmount } from "./GridWarfare.types";
import { useLocale, type TranslationKey } from "../../locales";
import { ProgressBar } from "@shared/ui";

interface OperationRowProps {
    type: GridOperationType;
    slot: OperationSlotDto | null;
    cost: number;
    canPrepare: boolean;
    prepareLockedReasonId: string;
    // Non-empty = the EXECUTE action is gated (e.g. drone strike with no launcher built): the
    // button shows this localized reason and is disabled. Prepare/Cancel are unaffected.
    executeLockedReasonId?: string;
    isPending: boolean;
    onPrepare: (type: GridOperationType) => void;
    onExecute: (type: GridOperationType) => void;
    onCancel: (type: GridOperationType) => void;
    refundFlash: number | null;
}

// Operation name localization keys — mirrors OperationCard.
const OP_NAME_KEYS: Record<GridOperationType, TranslationKey> = {
    drone: "UI_GW_OP_DRONE",
    blackout: "UI_GW_OP_BLACKOUT",
    disinfo: "UI_GW_OP_DISINFO",
};

export const OperationRow: React.FC<OperationRowProps> = memo(({
    type,
    slot,
    cost,
    canPrepare,
    prepareLockedReasonId,
    executeLockedReasonId,
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
    const executeLocked = Boolean(executeLockedReasonId);

    // S6-04: locked-price indicator when the current price drifted from the locked one.
    const lockedCost = slot?.Cost ?? 0;
    const costDrifted = !isIdle && lockedCost > 0 && lockedCost !== cost;

    const progress = slot?.Progress ? Math.min(1, Math.max(0, slot.Progress)) : 0;

    const stateDisplay = ((): { text: string; color: string } => {
        if (isReady) return { text: l.t("UI_GW_STATE_READY"), color: accents.schemes.accent };
        if (isExecuting) return { text: l.t("UI_GW_STATE_EXECUTING"), color: accents.crisis.accent };
        if (isPreparing) return { text: l.t("UI_GW_STATE_PREPARING"), color: accents.resilience.accent };
        return { text: l.t("UI_GW_STATE_AVAILABLE"), color: theme.colors.textMuted };
    })();

    return (
        <div style={s.operationRow(opColor, !isIdle)}>
            {/* Identity: name + cost (+ locked tag), preparing progress, refund flash */}
            <div style={s.rowId}>
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
                {isPreparing && (
                    <div style={{ marginTop: "5rem" }}>
                        <ProgressBar value={progress * 100} color={accents.resilience.accent} height="5rem" />
                    </div>
                )}
                {refundFlash !== null && (
                    <span style={{
                        marginTop: "3rem",
                        fontSize: "11rem",
                        fontWeight: 700,
                        color: accents.resilience.accent,
                    }}>
                        {l.t("UI_GW_REFUNDED", formatShadowAmount(refundFlash))}
                    </span>
                )}
            </div>

            <div style={s.rowStateBadge}>
                <span style={s.cardState(stateDisplay.color)}>{stateDisplay.text}</span>
            </div>

            {/* Fixed-width action zone: rows stay right-aligned across states */}
            <div style={s.rowActions}>
                {isIdle && (
                    <button
                        style={s.rowButtonPrimary(opColor, canPrepare && !isPending)}
                        onClick={canPrepare && !isPending ? handlePrepare : undefined}
                        disabled={!canPrepare || isPending}
                    >
                        {isPending ? l.t("UI_PROCESSING") : canPrepare ? l.t("UI_GW_BTN_PREPARE") : prepareDisabledText}
                    </button>
                )}

                {isPreparing && (
                    <button
                        style={s.rowButtonGhost(accents.crisis.accent, !isPending)}
                        onClick={isPending ? undefined : handleCancel}
                        disabled={isPending}
                    >
                        {isPending ? l.t("UI_PROCESSING") : l.t("UI_GW_BTN_CANCEL")}
                    </button>
                )}

                {isReady && (
                    <>
                        <button
                            style={s.rowButtonPrimary(opColor, !isPending && !executeLocked)}
                            onClick={!isPending && !executeLocked ? handleExecute : undefined}
                            disabled={isPending || executeLocked}
                        >
                            {isPending
                                ? l.t("UI_PROCESSING")
                                : executeLockedReasonId
                                    ? l.tDynamic(executeLockedReasonId)
                                    : l.t("UI_GW_BTN_EXECUTE")}
                        </button>
                        <button
                            style={s.rowButtonGhost(accents.crisis.accent, !isPending)}
                            onClick={isPending ? undefined : handleCancel}
                            disabled={isPending}
                        >
                            {isPending ? l.t("UI_PROCESSING") : l.t("UI_GW_BTN_CANCEL")}
                        </button>
                    </>
                )}

                {isExecuting && (
                    <button style={s.rowButtonGhost(accents.crisis.accent, false)} disabled={true}>
                        {l.t("UI_PROCESSING")}
                    </button>
                )}
            </div>
        </div>
    );
});

OperationRow.displayName = "OperationRow";
