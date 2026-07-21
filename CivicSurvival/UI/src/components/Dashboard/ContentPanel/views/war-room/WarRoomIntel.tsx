/**
 * WarRoomIntel — the right-column INTEL block: next-wave forecast (type / est. swarm / window),
 * a tension bar with its status band, and the two intel spend buttons (buy insider tip, upgrade
 * intel level).
 *
 * Both buttons fire the existing intel triggers verbatim — purchaseInsider / upgradeIntel through
 * useIntelActions, correlated against the DTO's InsiderRequest / IntelUpgradeRequest. Disabled state
 * follows the backend Can* flags and shows the honest locked reason (never "available" alongside a
 * funds error): CanBuyInsider / CanUpgradeIntel fold in affordability + already-active/maxed state.
 * Purchases are deliberately NOT wave-gated — the War Room stays operable mid-wave (strikes are too);
 * a mid-wave insider self-balances by expiring at wave end.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba, formatCostArg } from "@themes";
import { useLocale } from "@locales";
import { useIntel, bindingDataOrDefault } from "@hooks/domain";
import { useIntelActions, useRequestAction } from "@hooks/actions";
import { DEFAULT_INTEL_DTO } from "../../../../../types/domainDtos";
import { getThreatColorByStatus } from "../../WarViews.styles";

export const WarRoomIntel: React.FC = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const intel = bindingDataOrDefault(useIntel(), DEFAULT_INTEL_DTO);
    const actions = useIntelActions();

    const insiderAction = useRequestAction(() => { actions.purchaseInsider(); return true; }, intel.InsiderRequest);
    const upgradeAction = useRequestAction(() => { actions.upgradeIntel(); return true; }, intel.IntelUpgradeRequest);

    const tensionColor = getThreatColorByStatus(intel.TensionStatus ?? "LOW", accents, theme);
    const tensionPct = Math.max(0, Math.min(100, intel.TensionLevel ?? 0));

    const hasInsider = intel.HasInsider;
    const isMaxIntel = (intel.IntelUpgradeLevel ?? 0) >= 2;

    // Forecast window text — mirrors AttackForecastSection's status handling so the readouts agree.
    const eta = useMemo(() => {
        const te = intel.TimeEstimate;
        if (!te) return "?";
        if (te.Status === "unknown") return l.t("UI_INTEL_ETA_UNKNOWN");
        if (te.Status === "in-attack") return l.t("UI_INTEL_ETA_DURING_ATTACK");
        if (te.Status === "in-recovery") return l.t("UI_INTEL_ETA_RECOVERY");
        if (te.Status === "awaiting-window") return l.t("UI_INTEL_ETA_AWAITING_WINDOW");
        if (hasInsider) return `${te.MinHours?.toFixed(1) ?? "?"}h`;
        return `${te.MinHours?.toFixed(0) ?? "?"}-${te.MaxHours?.toFixed(0) ?? "?"}h`;
    }, [intel.TimeEstimate, hasInsider, l]);

    const shaheds = intel.EstimatedShaheds ?? 0;
    const ballistics = intel.EstimatedBallistics ?? 0;
    const swarmKnown = shaheds > 0 || ballistics > 0;
    const composition = swarmKnown
        ? [shaheds > 0 ? `${shaheds} ${l.t("INTEL_SHAHEDS")}` : "", ballistics > 0 ? `${ballistics} ${l.t("INTEL_BALLISTICS")}` : ""]
            .filter(Boolean).join(" / ")
        : l.t("INTEL_SWARM_UNKNOWN");
    const waveType = intel.WaveTypePrediction ? l.tDynamic(intel.WaveTypePrediction) : l.t("INTEL_UNKNOWN");

    const insiderDisabled = !intel.CanBuyInsider || insiderAction.isPending;
    const insiderText = insiderAction.isPending
        ? l.t("UI_PROCESSING")
        : intel.CanBuyInsider
            ? l.t("UI_WARROOM_BUY_INSIDER", `$${formatCostArg(intel.InsiderCost)}k`)
            : intel.InsiderLockedReasonId
                ? l.tDynamic(intel.InsiderLockedReasonId)
                : l.t("UI_WARROOM_BUY_INSIDER", `$${formatCostArg(intel.InsiderCost)}k`);

    const upgradeDisabled = !intel.CanUpgradeIntel || upgradeAction.isPending;
    const nextLevel = (intel.IntelUpgradeLevel ?? 0) + 1;
    const upgradeText = upgradeAction.isPending
        ? l.t("UI_PROCESSING")
        : intel.CanUpgradeIntel
            ? l.t("UI_WARROOM_UPGRADE_INTEL", nextLevel, `$${formatCostArg(intel.IntelUpgradeCost)}k`)
            : intel.IntelUpgradeLockedReasonId
                ? l.tDynamic(intel.IntelUpgradeLockedReasonId)
                : l.t("UI_WARROOM_UPGRADE_INTEL", nextLevel, `$${formatCostArg(intel.IntelUpgradeCost)}k`);

    const s = useMemo(() => ({
        block: {
            display: "flex",
            flexDirection: "column" as const,
            minHeight: "40rem",
            // minWidth 0: a long unshrinkable row value (EST. SWARM) otherwise
            // widens the whole block past the side panel's right edge.
            minWidth: 0,
            flexShrink: 0,
            marginTop: theme.spacing.sm,
            paddingTop: theme.spacing.xs,
            borderTop: `1rem solid ${theme.colors.border}`,
        } as React.CSSProperties,
        title: {
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            color: accents.operations.accent,
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,
        subTitle: {
            fontSize: "10rem",
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: theme.colors.textMuted,
            marginBottom: "4rem",
            fontFamily: mono,
        } as React.CSSProperties,
        row: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: "3rem",
        } as React.CSSProperties,
        rowLabel: {
            fontSize: "10rem",
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: theme.colors.textMuted,
            fontFamily: mono,
        } as React.CSSProperties,
        rowValue: (color: string): React.CSSProperties => ({
            fontSize: "11rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            color,
            fontFamily: mono,
            // Long values ("24 drones / 6 ballistic") must shrink and wrap
            // inside the row instead of running past the panel edge.
            minWidth: 0,
            textAlign: "right" as const,
        }),
        track: {
            height: "8rem",
            marginTop: "4rem",
            marginBottom: "8rem",
            borderRadius: "4rem",
            background: hexToRgba(theme.colors.textMuted, 0.18),
            overflow: "hidden" as const,
        } as React.CSSProperties,
        fill: (color: string, pct: number): React.CSSProperties => ({
            width: `${pct}%`,
            height: "100%",
            background: color,
            boxShadow: `0 0 6rem ${hexToRgba(color, 0.5)}`,
        }),
        button: (disabled: boolean, color: string): React.CSSProperties => ({
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            marginTop: "4rem",
            padding: "5rem 6rem",
            borderRadius: theme.layout.borderRadiusLg,
            border: `1rem solid ${hexToRgba(color, disabled ? 0.25 : 0.7)}`,
            background: disabled ? "transparent" : hexToRgba(color, 0.1),
            cursor: disabled ? "not-allowed" : "pointer",
            opacity: disabled ? 0.6 : 1,
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            textAlign: "center" as const,
            color: disabled ? theme.colors.textMuted : color,
            fontFamily: mono,
        }),
        maxed: {
            marginTop: "6rem",
            padding: "8rem 0",
            textAlign: "center" as const,
            fontSize: "10rem",
            fontWeight: theme.typography.weightBold,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            color: accents.schemes.accent,
            fontFamily: mono,
        } as React.CSSProperties,
    }), [theme, accents, mono]);

    return (
        <div style={s.block}>
            <div style={s.title}>{l.t("UI_WARROOM_INTEL")}</div>

            <div style={s.subTitle}>{l.t("UI_WARROOM_INTEL_FORECAST")}</div>
            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_INTEL_WAVE_TYPE")}</span>
                <span style={s.rowValue(theme.colors.textSecondary)}>{waveType}</span>
            </div>
            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_INTEL_COMPOSITION")}</span>
                <span style={s.rowValue(swarmKnown ? accents.crisis.accent : theme.colors.textMuted)}>{composition}</span>
            </div>
            <div style={s.row}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_INTEL_WINDOW")}</span>
                <span style={s.rowValue(theme.colors.textPrimary)}>{eta}</span>
            </div>

            <div style={{ ...s.row, marginTop: "8rem" }}>
                <span style={s.rowLabel}>{l.t("UI_WARROOM_INTEL_TENSION")}</span>
                <span style={s.rowValue(tensionColor)}>{intel.TensionStatus}</span>
            </div>
            <div style={s.track}>
                <div style={s.fill(tensionColor, tensionPct)} />
            </div>

            {hasInsider ? (
                <div style={s.maxed}>{l.t("UI_WARROOM_INSIDER_ACTIVE")}</div>
            ) : (
                <div
                    style={s.button(insiderDisabled, accents.operations.accent)}
                    role="button"
                    tabIndex={0}
                    onClick={() => { if (!insiderDisabled) insiderAction.execute(); }}
                    onKeyDown={(e) => {
                        if ((e.key === "Enter" || e.key === " ") && !insiderDisabled) {
                            e.preventDefault();
                            insiderAction.execute();
                        }
                    }}
                >
                    {insiderText}
                </div>
            )}

            {isMaxIntel ? (
                <div style={s.maxed}>{l.t("UI_WARROOM_INTEL_MAX")}</div>
            ) : (
                <div
                    style={s.button(upgradeDisabled, accents.schemes.accent)}
                    role="button"
                    tabIndex={0}
                    onClick={() => { if (!upgradeDisabled) upgradeAction.execute(); }}
                    onKeyDown={(e) => {
                        if ((e.key === "Enter" || e.key === " ") && !upgradeDisabled) {
                            e.preventDefault();
                            upgradeAction.execute();
                        }
                    }}
                >
                    {upgradeText}
                </div>
            )}
        </div>
    );
});

WarRoomIntel.displayName = "WarRoomIntel";
