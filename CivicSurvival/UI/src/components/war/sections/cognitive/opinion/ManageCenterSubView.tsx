/**
 * ManageCenterSubView — the full-panel Propaganda Center drill-down.
 *
 * Layout (variant 3): a status-tile band on top (tier / reach / telecom /
 * broadcast budget + the tier UPGRADE), the household-coverage read below it,
 * and one card per stratum side by side (AllocationSection). The network rail
 * keeps only a compact summary; every Center control lives here. The view
 * stays open for an OFFLINE Center — upgrading a struck Center is valid —
 * only the allocation section is online-gated, because the coverage ceiling
 * can only be split while broadcasting. The owner (PsyopsScreen) closes the
 * view when the Center stops existing (bulldozed / destroyed).
 */

import React, { memo, useMemo } from "react";
import { Column, Row } from "../../../../coherent";
import { useTheme, useAccents, hexToRgba, formatMoney } from "../../../../../themes";
import { useLocale, type TranslationKey } from "../../../../../locales";
import { bindingDataOrDefault, useCognitive } from "@hooks/domain";
import { DEFAULT_COGNITIVE_DTO } from "../../../../../types/domainDtos";
import { useRequestAction, type useCognitiveActions } from "@hooks/actions";
import { ProgressBar } from "../../../../shared/ui";
import { createBoardStyles } from "./opinionStyles";
import { AllocationSection } from "./AllocationSection";
import { BackButton } from "./BackButton";
import { Sep } from "./Sep";

interface ManageCenterSubViewProps {
    actions: ReturnType<typeof useCognitiveActions>;
    onBack: () => void;
}

const pct01 = (value: number): number => Math.round(Math.max(0, Math.min(1, value)) * 100);

export const ManageCenterSubView = memo(({ actions, onBack }: ManageCenterSubViewProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const b = createBoardStyles(theme, accents);

    const cw = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);

    // Upgrade runs through the existing request-result lifecycle so the button
    // reflects pending + the failure reason (e.g. can't afford, max tier).
    const upgradeAction = useRequestAction(() => {
        actions.upgradePropagandaCenter();
        return true;
    }, cw.PropagandaCenterUpgradeRequest);
    const upgradePending = upgradeAction.isPending;
    const upgradeError = upgradeAction.failureReasonId;

    const capacity = cw.BroadcastCapacity;
    const free = cw.BroadcastCapacityFree;
    const usedPct = capacity > 0 ? Math.round(((capacity - free) / capacity) * 100) : 0;
    const atMaxTier = cw.CenterTier >= cw.CenterMaxTier;

    const s = useMemo(() => ({
        tile: {
            flex: 1,
            minWidth: 0,
            backgroundColor: theme.colors.surface,
            border: `2rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadiusLg,
            padding: "12rem 14rem",
        } as React.CSSProperties,
        tileLabel: {
            fontSize: theme.typography.sizeXS,
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
            letterSpacing: "0.6rem",
            marginBottom: "4rem",
        } as React.CSSProperties,
        tileValue: {
            fontSize: "20rem",
            fontWeight: 700,
            color: theme.colors.textPrimary,
        } as React.CSSProperties,
        upgradeBtn: (off: boolean): React.CSSProperties => ({
            width: "100%",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "10rem 14rem",
            fontSize: theme.typography.sizeXS,
            fontWeight: 700,
            textTransform: "uppercase",
            letterSpacing: "0.6rem",
            color: off ? theme.colors.textMuted : accents.resilience.accent,
            backgroundColor: off ? "transparent" : hexToRgba(accents.resilience.accent, 0.08),
            border: `2rem solid ${off ? theme.colors.border : accents.resilience.accent}`,
            borderRadius: theme.layout.borderRadius,
            cursor: off ? "not-allowed" : "pointer",
            opacity: off ? 0.55 : 1,
        }),
        hint: {
            marginTop: "4rem",
            fontSize: theme.typography.sizeXS,
            color: theme.colors.textMuted,
            textAlign: "center" as const,
        } as React.CSSProperties,
    }), [theme, accents]);

    // One span, one size — a big number with a small unit next to it read as a
    // typographic fraction ("4" over "/4"), and cohtml stacked loose spans anyway.
    const tile = (labelKey: TranslationKey, value: string): React.ReactNode => (
        <div style={s.tile}>
            <div style={s.tileLabel}>{l.t(labelKey)}</div>
            <span style={s.tileValue}>{value}</span>
        </div>
    );

    return (
        <Column gap={theme.spacing.sm} style={{ width: "100%", height: "100%", minHeight: 0 }}>
            {/* Back header — title + tier + live status pill (an OFFLINE Center stays manageable). */}
            <Row align="center" gap={theme.spacing.sm} style={{ flexShrink: 0 }}>
                <BackButton onBack={onBack} />
                <span style={{ ...b.eyebrow, color: theme.colors.textSecondary, fontSize: theme.typography.sizeSM, letterSpacing: "0.6rem" }}>
                    {l.t("UI_OB_CENTER")}<Sep />{l.t("UI_OB_CENTER_TIER", cw.CenterTier, cw.CenterMaxTier)}
                </span>
                <span style={{ marginLeft: "auto" }}>
                    <span style={b.chip(cw.CenterOnline ? theme.colors.success : accents.resilience.accent)}>
                        {l.t(cw.CenterOnline ? "UI_OB_CENTER_STATUS_ONLINE" : "UI_OB_CENTER_STATUS_OFFLINE")}
                    </span>
                </span>
            </Row>

            <div style={{ ...b.region, flex: 1, minHeight: 0, overflowY: "auto", overflowX: "hidden", padding: theme.spacing.lg }}>
                <Column gap={theme.spacing.lg} style={{ width: "100%" }}>
                    {/* Status-tile band: the whole Center state at a glance + the upgrade. */}
                    <Row gap={theme.spacing.md} align="stretch" style={{ width: "100%" }}>
                        {tile("UI_OB_STAT_TIER", `${cw.CenterTier}/${cw.CenterMaxTier}`)}
                        {tile("UI_OB_STAT_REACH", `${pct01(cw.CenterReach)}%`)}
                        {tile("UI_OB_STAT_TELECOM", `${pct01(cw.TelecomFactor)}%`)}
                        <div style={s.tile}>
                            <div style={s.tileLabel}>{l.t("UI_OB_STAT_BUDGET")}</div>
                            <div style={{ margin: "6rem 0 4rem" }}>
                                <ProgressBar value={usedPct} color={accents.crisis.accent} height="8rem" />
                            </div>
                            <span style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textSecondary }}>
                                {l.t("UI_OB_CAPACITY_USED", usedPct)}
                            </span>
                        </div>
                        <div style={{ ...s.tile, flex: 1.4, display: "flex", flexDirection: "column", justifyContent: "center" }}>
                            {atMaxTier ? (
                                <div style={{ ...s.hint, marginTop: 0, color: theme.colors.success }}>
                                    {l.t("UI_OB_CENTER_MAXED")}
                                </div>
                            ) : (
                                <>
                                    <button
                                        style={s.upgradeBtn(upgradePending)}
                                        disabled={upgradePending}
                                        onClick={() => { if (!upgradePending) upgradeAction.execute(); }}
                                    >
                                        <span>{l.t("UI_OB_CENTER_UPGRADE")}</span>
                                        <span>{upgradePending ? l.t("UI_PROCESSING") : formatMoney(cw.CenterUpgradeCost)}</span>
                                    </button>
                                    {upgradeError && <div style={s.hint}>{l.tDynamic(upgradeError)}</div>}
                                </>
                            )}
                        </div>
                    </Row>

                    {/* Coverage split — online-only (the ceiling can only be split while
                        broadcasting); an offline Center shows the honest reason. */}
                    {cw.CenterOnline ? (
                        <AllocationSection actions={actions} />
                    ) : (
                        <div style={b.card}>
                            <span style={{ fontSize: theme.typography.sizeXS, color: theme.colors.textMuted }}>
                                {l.t("UI_OB_ALLOC_OFFLINE")}
                            </span>
                        </div>
                    )}
                </Column>
            </div>
        </Column>
    );
});

ManageCenterSubView.displayName = "ManageCenterSubView";
