/**
 * ReliefTab - Buckwheat Protocol (Social Stabilization)
 * Zone 3, Tab 2 of Cognitive Warfare Sandwich
 */

import React, { memo, useCallback, useMemo, useRef } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, formatMoney, hexToRgba } from "../../../../../themes";
import { useLocale } from "../../../../../locales";
import { bindingDataOrDefault, useBuckwheat, useExport } from "@hooks/domain";
import { DEFAULT_BUCKWHEAT_DTO } from "../../../../../types/domainDtos";
import { IconCheck } from "../../../../shared/common/Icons";
import { HoverTipTarget } from "../../../../shared/common/HoverTip";
import { StatRow } from "../../../../shared/ui";
import { useRequestAction } from "@hooks/actions";
import { optionalBindingData, useOptionalBinding } from "@hooks/useOptionalBinding";
import { type ProcurementLevel } from "../../../../../types/semantic";
import { type useCognitiveActions } from "@hooks/actions";

const PROCUREMENT_LEVELS: readonly ProcurementLevel[] = [0, 25, 50, 75, 100];

type ProcurementVerdict = {
    canSet: boolean;
    reasonId: string;
};

interface ReliefTabProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

export const ReliefTab = memo(({ actions }: ReliefTabProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const exportBinding = useOptionalBinding(useExport());

    const buckwheatState = useBuckwheat();
    const buckwheat = bindingDataOrDefault(buckwheatState, DEFAULT_BUCKWHEAT_DTO);
    const buckwheatTons = buckwheat?.BuckwheatTons ?? 0;
    const procurementLevel = buckwheat?.ProcurementLevel ?? 0;
    const dailyCost = buckwheat?.DailyCost ?? 0;
    const canDistribute = buckwheat?.CanDistribute ?? false;
    const procurementVerdicts: Record<ProcurementLevel, ProcurementVerdict> = {
        0: { canSet: true, reasonId: "" },
        25: {
            canSet: buckwheat?.CanSetProcurement25 ?? false,
            reasonId: buckwheat?.Procurement25LockedReasonId ?? "",
        },
        50: {
            canSet: buckwheat?.CanSetProcurement50 ?? false,
            reasonId: buckwheat?.Procurement50LockedReasonId ?? "",
        },
        75: {
            canSet: buckwheat?.CanSetProcurement75 ?? false,
            reasonId: buckwheat?.Procurement75LockedReasonId ?? "",
        },
        100: {
            canSet: buckwheat?.CanSetProcurement100 ?? false,
            reasonId: buckwheat?.Procurement100LockedReasonId ?? "",
        },
    };

    const distributeAction = useRequestAction(() => {
        actions.distributeAid(0);
        return true;
    }, buckwheat?.LastDistributeResult);
    const procurementLevelRef = useRef<ProcurementLevel>(0);
    const procurementAction = useRequestAction(() => {
        actions.setProcurementLevel(procurementLevelRef.current);
        return true;
    }, buckwheat?.ProcurementLevelRequest);
    const distributePending = distributeAction.isPending || buckwheat?.LastDistributeResult.Status === "pending";
    const procurementPending = procurementAction.isPending || buckwheat?.ProcurementLevelRequest.Status === "pending";
    const distributeErrorKey =
        buckwheat?.LastDistributeResult.Status === "failed" && buckwheat.LastDistributeResult.ReasonId
            ? buckwheat.LastDistributeResult.ReasonId
            : "";
    const procurementErrorKey =
        buckwheat?.ProcurementLevelRequest.Status === "failed" && buckwheat.ProcurementLevelRequest.ReasonId
            ? buckwheat.ProcurementLevelRequest.ReasonId
            : "";
    const distributeLockedReasonKey = !canDistribute ? buckwheat?.DistributeLockedReasonId ?? "" : "";
    const procurementLockedReasonKey =
        procurementLevel > 0 && buckwheat && !buckwheat.CanAffordProcurement
            ? buckwheat.AffordProcurementLockedReasonId
            : "";

    const handleSetProcurement = useCallback((level: ProcurementLevel) => {
        procurementLevelRef.current = level;
        procurementAction.execute();
    }, [procurementAction]);

    const handleDistribute = useCallback(() => {
        distributeAction.execute();
    }, [distributeAction]);

    const s = useMemo(() => ({
        container: {
            padding: theme.spacing.sm,
            height: "100%",
        } as React.CSSProperties,

        reserveRow: {
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,

        reserveValue: (isReady: boolean) => ({
            fontSize: "24rem",
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            color: isReady ? accents.resilience.accent : theme.colors.textMuted,
        }) as React.CSSProperties,

        reserveUnit: {
            fontSize: "14rem",
            fontWeight: 600,
            color: theme.colors.textSecondary,
            marginLeft: "4rem",
        } as React.CSSProperties,

        reserveStatus: (isReady: boolean) => ({
            marginLeft: theme.spacing.sm,
            fontSize: "11rem",
            fontWeight: 600,
            color: isReady ? theme.colors.success : theme.colors.textMuted,
            display: "flex",
            alignItems: "center",
        }) as React.CSSProperties,

        statusIcon: {
            marginRight: "4rem",
        } as React.CSSProperties,

        procurementRow: {
            marginBottom: theme.spacing.xs,
        } as React.CSSProperties,

        procurementValue: (isActive: boolean) => ({
            fontSize: "14rem",
            fontWeight: 700,
            color: isActive ? accents.resilience.accent : theme.colors.textMuted,
        }) as React.CSSProperties,

        procurementButton: (isActive: boolean, isDisabled: boolean) => ({
            flex: 1,
            padding: "6rem",
            fontSize: "11rem",
            fontWeight: 600,
            border: isActive ? `3rem solid ${accents.resilience.accent}` : `2rem solid ${theme.colors.border}`,
            borderRadius: "4rem",
            backgroundColor: isActive ? hexToRgba(accents.resilience.accent, 0.12) : "transparent",
            color: isDisabled ? theme.colors.textMuted : isActive ? accents.resilience.accent : theme.colors.textSecondary,
            cursor: isDisabled ? "not-allowed" : "pointer",
            opacity: isDisabled ? 0.5 : 1,
        }) as React.CSSProperties,

        costHint: {
            fontSize: "10rem",
            color: theme.colors.textMuted,
            marginTop: "4rem",
        } as React.CSSProperties,

        distributeButton: (canDo: boolean, isPending: boolean) => ({
            width: "100%",
            padding: "10rem",
            marginTop: theme.spacing.sm,
            fontSize: "12rem",
            fontWeight: 700,
            textTransform: "uppercase" as const,
            border: "none",
            borderRadius: "4rem",
            backgroundColor: canDo ? accents.resilience.accent : theme.colors.surface,
            color: canDo ? theme.colors.white : theme.colors.textMuted,
            cursor: canDo && !isPending ? "pointer" : "not-allowed",
            opacity: isPending ? 0.65 : 1,
        }) as React.CSSProperties,

        effectHint: {
            fontSize: "10rem",
            color: theme.colors.textMuted,
            textAlign: "center" as const,
            marginTop: "4rem",
        } as React.CSSProperties,

        offshoreSection: {
            marginTop: theme.spacing.sm,
            padding: theme.spacing.xs,
            backgroundColor: hexToRgba(accents.schemes.accent, 0.06),
            borderRadius: "4rem",
            borderLeft: `4rem solid ${accents.schemes.accent}`,
        } as React.CSSProperties,

    }), [theme, accents]);

    const isReady = buckwheatTons >= 1;

    const exportData = optionalBindingData(exportBinding);

    return (
        <Column style={s.container}>
            {/* Reserve Display */}
            <Row align="flex-end" style={s.reserveRow}>
                <span style={s.reserveValue(isReady)}>{buckwheatTons.toFixed(1)}</span>
                <span style={s.reserveUnit}>{l.t("UI_RELIEF_TONS")}</span>
                <span style={s.reserveStatus(isReady)}>
                    {isReady ? <><span style={s.statusIcon}><IconCheck /></span> {l.t("UI_RELIEF_READY")}</> : l.t("UI_RELIEF_NEED_MORE", (1 - buckwheatTons).toFixed(1))}
                </span>
            </Row>

            {/* Procurement Rate */}
            <StatRow
                label={l.t("UI_RELIEF_PROCUREMENT")}
                value={`${procurementLevel}%`}
                color={procurementLevel > 0 ? accents.resilience.accent : theme.colors.textMuted}
                emphasis="title"
                style={s.procurementRow}
                labelStyle={{ fontSize: "11rem", fontWeight: 600, textTransform: "uppercase" }}
                valueStyle={s.procurementValue(procurementLevel > 0)}
            />

            <Row gap={theme.spacing.xs}>
                {PROCUREMENT_LEVELS.map((level) => {
                    const verdict = procurementVerdicts[level];
                    const isDisabled = !verdict.canSet || procurementPending;
                    return (
                        <HoverTipTarget key={level} text={!verdict.canSet && verdict.reasonId ? l.tDynamic(verdict.reasonId) : null}>
                            <button
                                style={s.procurementButton(procurementLevel === level, isDisabled)}
                                disabled={isDisabled}
                                onClick={() => !isDisabled && handleSetProcurement(level)}
                            >
                                {level}%
                            </button>
                        </HoverTipTarget>
                    );
                })}
            </Row>

            {procurementLevel > 0 && (
                <div style={s.costHint}>
                    {procurementPending
                        ? l.t("UI_PROCESSING")
                    : procurementErrorKey
                        ? l.tDynamic(procurementErrorKey)
                    : procurementLockedReasonKey
                        ? l.tDynamic(procurementLockedReasonKey)
                        : l.t("UI_RELIEF_DAILY_COST", formatMoney(dailyCost))}
                </div>
            )}

            {/* Distribute Button */}
            <button
                style={s.distributeButton(canDistribute, distributePending)}
                disabled={!canDistribute || distributePending}
                onClick={canDistribute && !distributePending ? handleDistribute : undefined}
            >
                {distributePending ? l.t("UI_PROCESSING") : l.t("UI_RELIEF_DISTRIBUTE")}
            </button>
            <div style={s.effectHint}>
                {distributeErrorKey
                    ? l.tDynamic(distributeErrorKey)
                    : distributeLockedReasonKey
                        ? l.tDynamic(distributeLockedReasonKey)
                    : l.t("UI_RELIEF_DISTRIBUTE_EFFECT")}
            </div>

            {/* Offshore Account */}
            {exportData && (
                <div style={s.offshoreSection}>
                    <StatRow
                        compact
                        label={l.t("UI_RELIEF_OFFSHORE")}
                        value={formatMoney(exportData.OffshoreBalance ?? 0)}
                        color={accents.schemes.accent}
                        labelStyle={{ fontWeight: 700, color: accents.schemes.accent }}
                        valueStyle={{ fontSize: "14rem", fontWeight: 700 }}
                    />
                </div>
            )}
        </Column>
    );
});

ReliefTab.displayName = "ReliefTab";
