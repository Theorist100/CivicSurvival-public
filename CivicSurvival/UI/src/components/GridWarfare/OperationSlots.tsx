/**
 * OperationSlots - Container for 3 Operation Cards
 * Shows all available operations with their current states
 */

import React, { memo, useMemo, useCallback, useEffect, useRef, useState } from "react";
import { Row } from "../coherent";
import { useTheme, useAccents, hexToRgba } from "../../themes";
import { createGridWarfareStyles } from "./GridWarfare.styles";
import { OperationCard } from "./OperationCard";
import { OperationRow } from "./OperationRow";
import { type GridWarfareDto } from "../../types/domainDtos";
import { type GridOperationType, type GridWarfareActions, GRID_OPERATION_TYPES, formatShadowAmount } from "./GridWarfare.types";
import { useLocale } from "../../locales";
import { StatRow } from "../shared/ui";
import { useRequestAction } from "@hooks/actions";
import { operationSlotTarget, requestResultForTarget } from "@hooks/useRequest.generated";

const PREPARE_FIELDS: Record<GridOperationType, {
    canPrepare: keyof Pick<GridWarfareDto, "CanPrepareDrone" | "CanPrepareBlackout" | "CanPrepareDisinfo">;
    lockedReasonId: keyof Pick<GridWarfareDto, "PrepareDroneLockedReasonId" | "PrepareBlackoutLockedReasonId" | "PrepareDisinfoLockedReasonId">;
}> = {
    drone: { canPrepare: "CanPrepareDrone", lockedReasonId: "PrepareDroneLockedReasonId" },
    blackout: { canPrepare: "CanPrepareBlackout", lockedReasonId: "PrepareBlackoutLockedReasonId" },
    disinfo: { canPrepare: "CanPrepareDisinfo", lockedReasonId: "PrepareDisinfoLockedReasonId" },
};

type OperationActionName = "Prepare" | "Execute" | "Cancel";

function operationSlotKey(type: GridOperationType, action: OperationActionName): string {
    return `${type}:${action}`;
}

interface OperationSlotsProps {
    state: GridWarfareDto;
    actions: GridWarfareActions;
    /**
     * "cards" (default) — the vertical OperationCard stack for narrow columns (overlay
     * LAUNCH CONTROL). "rows" — compact horizontal OperationRow strips for wide surfaces
     * (dashboard COMMAND tab): locked total moves into the header meta, the ×1.5 hint is
     * stated once above the rows, and the rows flex-grow to fill the stable tab height.
     */
    layout?: "cards" | "rows";
}

export const OperationSlots: React.FC<OperationSlotsProps> = memo(({ state, actions, layout = "cards" }) => {
    const theme = useTheme();
    const accents = useAccents();
    const s = useMemo(() => createGridWarfareStyles(theme, accents), [theme, accents]);
    const l = useLocale();
    const { prepareOperation, executeOperation, cancelOperation } = actions;
    const operationActionRef = useRef<() => boolean>(() => false);
    const pendingCancelTypeRef = useRef<GridOperationType | null>(null);
    const pendingCancelRefundRef = useRef(0);
    const handledOperationRequestIdRef = useRef(0);
    const flashTimersRef = useRef<Partial<Record<GridOperationType, ReturnType<typeof setTimeout>>>>({});
    const [flashByType, setFlashByType] = useState<Partial<Record<GridOperationType, number>>>({});
    const [pendingOperationSlot, setPendingOperationSlot] = useState<string | null>(null);
    const operationRequest = state.OperationRequest;
    const scopedOperationRequest = useMemo(
        () => pendingOperationSlot
            ? requestResultForTarget("OperationRequest", operationRequest, operationSlotTarget(pendingOperationSlot))
            : undefined,
        [operationRequest, pendingOperationSlot],
    );
    const operationRequestId = scopedOperationRequest?.RequestId ?? 0;
    const operationRequestStatus = scopedOperationRequest?.Status ?? "idle";
    const operationAction = useRequestAction(() => operationActionRef.current(), scopedOperationRequest);
    const operationPending = operationAction.isPending;
    const operationErrorKey = operationRequestStatus === "failed" ? scopedOperationRequest?.ReasonId ?? "" : "";

    const slotMap = useMemo(() => {
        const map = new Map<GridOperationType, GridWarfareDto["OperationSlots"][number]>();
        for (const slot of state.OperationSlots) {
            map.set(slot.AttackType as GridOperationType, slot);
        }
        return map;
    }, [state.OperationSlots]);

    const lockedTotal = useMemo(() =>
        state.OperationSlots.reduce((sum, slot) => sum + (slot.Cost ?? 0), 0),
        [state.OperationSlots],
    );

    const showRefundFlash = useCallback((type: GridOperationType, amount: number) => {
        if (amount <= 0) return;
        const existing = flashTimersRef.current[type];
        if (existing) clearTimeout(existing);
        setFlashByType((prev) => ({ ...prev, [type]: amount }));
        flashTimersRef.current[type] = setTimeout(() => {
            setFlashByType((prev) => {
                const next = { ...prev };
                delete next[type];
                return next;
            });
            delete flashTimersRef.current[type];
        }, 2500);
    }, []);

    useEffect(() => () => {
        for (const timer of Object.values(flashTimersRef.current)) {
            if (timer) clearTimeout(timer);
        }
    }, []);

    useEffect(() => {
        if (operationRequestId === 0 || operationRequestStatus === "pending") return;
        if (operationRequestId === handledOperationRequestIdRef.current) return;
        handledOperationRequestIdRef.current = operationRequestId;

        if (operationRequestStatus === "success" && pendingCancelTypeRef.current !== null) {
            showRefundFlash(pendingCancelTypeRef.current, pendingCancelRefundRef.current);
        }

        pendingCancelTypeRef.current = null;
        pendingCancelRefundRef.current = 0;
        setPendingOperationSlot(null);
    }, [operationRequestId, operationRequestStatus, showRefundFlash]);

    const runOperationAction = useCallback((operationSlot: string, action: () => void) => {
        if (operationPending) return false;
        setPendingOperationSlot(operationSlot);
        operationActionRef.current = () => {
            action();
            return true;
        };
        const emitted = operationAction.execute();
        if (!emitted) setPendingOperationSlot(null);
        return emitted;
    }, [operationAction, operationPending]);

    const handlePrepare = useCallback((type: GridOperationType) => {
        runOperationAction(operationSlotKey(type, "Prepare"), () => prepareOperation(type));
    }, [prepareOperation, runOperationAction]);
    const handleExecute = useCallback((type: GridOperationType) => {
        runOperationAction(operationSlotKey(type, "Execute"), () => executeOperation(type));
    }, [executeOperation, runOperationAction]);
    const handleCancel = useCallback((type: GridOperationType) => {
        const slot = slotMap.get(type);
        pendingCancelTypeRef.current = type;
        pendingCancelRefundRef.current = slot?.Cost ?? 0;
        if (!runOperationAction(operationSlotKey(type, "Cancel"), () => cancelOperation(type))) {
            pendingCancelTypeRef.current = null;
            pendingCancelRefundRef.current = 0;
        }
    }, [cancelOperation, runOperationAction, slotMap]);

    const rows = layout === "rows";

    return (
        <div style={rows ? { ...s.slotsContainer, flex: 1, minHeight: 0 } : s.slotsContainer}>
            {/* Header — in rows mode the locked total rides here instead of a bottom bar */}
            <Row justify="space-between" align="center" style={{ marginBottom: rows ? "6rem" : "4rem" }}>
                <span style={{
                    fontSize: "11rem",
                    fontWeight: 700,
                    color: accents.crisis.accent,
                    textTransform: "uppercase" as const,
                }}>
                    {l.t("UI_GW_SHADOW_OPS")}
                </span>
                <span style={{
                    fontSize: "10rem",
                    color: theme.colors.textMuted,
                    fontFamily: theme.typography.fontFamilyMono,
                    textTransform: rows ? ("uppercase" as const) : ("none" as const),
                }}>
                    {l.t("UI_GW_SLOTS_COUNT", state.OperationSlots.length)}
                    {rows && lockedTotal > 0 && (
                        <span style={{ color: accents.resilience.accent, marginLeft: "8rem" }}>
                            {l.t("UI_GW_LOCKED_SHORT", formatShadowAmount(lockedTotal))}
                        </span>
                    )}
                </span>
            </Row>

            {/* Operation Cards / Rows */}
            {GRID_OPERATION_TYPES.map((type) => {
                const slot = slotMap.get(type) ?? null;
                const cost = state.AttackCosts[type] ?? 0;
                const fields = PREPARE_FIELDS[type];
                const isPendingForType = operationPending && pendingOperationSlot?.startsWith(`${type}:`) === true;
                // Drone strikes need a built launcher to fly from — gate EXECUTE (not Prepare/Cancel,
                // not arsenal purchase) when none exist. Other operations are never launcher-gated.
                const executeLockedReasonId = type === "drone" && (state.DroneLauncherCount ?? 0) === 0
                    ? "UI_GW_EXECUTE_NEED_LAUNCHER"
                    : "";
                const common = {
                    type,
                    slot,
                    cost,
                    canPrepare: Boolean(state[fields.canPrepare]),
                    prepareLockedReasonId: String(state[fields.lockedReasonId] ?? ""),
                    executeLockedReasonId,
                    isPending: isPendingForType,
                    onPrepare: handlePrepare,
                    onExecute: handleExecute,
                    onCancel: handleCancel,
                    refundFlash: flashByType[type] ?? null,
                };

                return rows
                    ? <OperationRow key={type} {...common} />
                    : <OperationCard key={type} {...common} />;
            })}

            {/* Locked funds summary (cards mode only — rows carry it in the header) */}
            {!rows && lockedTotal > 0 && (
                <StatRow
                    compact
                    label={l.t("UI_GW_FUNDS_LOCKED")}
                    value={formatShadowAmount(lockedTotal)}
                    color={accents.resilience.accent}
                    style={{
                        marginTop: "6rem",
                        padding: "4rem 10rem",
                        background: hexToRgba(accents.resilience.accent, 0.06),
                        borderRadius: theme.layout.borderRadius,
                    }}
                    valueStyle={{ fontWeight: 700 }}
                />
            )}
            {operationErrorKey && (
                <div style={{
                    marginTop: "8rem",
                    fontSize: "11rem",
                    color: theme.colors.error,
                    textAlign: "center" as const,
                }}>
                    {l.tDynamic(operationErrorKey)}
                </div>
            )}
        </div>
    );
});

OperationSlots.displayName = "OperationSlots";
