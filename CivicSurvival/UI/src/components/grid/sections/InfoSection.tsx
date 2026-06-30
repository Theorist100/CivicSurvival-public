/**
 * InfoSection - Left column of PowerDashboard
 * Shows: Grid Integrity (Hz), Balance (Prod/Cons)
 */

import React, { useEffect, useMemo, useState } from "react";
import { t, Column, Row } from "../../coherent";
import { useTheme, useAccents } from "../../../themes";
import { bindingDataOrDefault, usePowerGrid } from "@hooks/domain";
import { type StressZone } from "../../../types";
import { DEFAULT_POWER_GRID_DTO } from "../../../types/domainDtos";
import { createStyles } from "./SectionStyles";
import { IconCheck, IconAlert } from "../../shared/common/Icons";
import { HoverTip } from "../../shared/common/HoverTip";
import { HelpSection } from "../../shared/common/HelpSection";
import { SectionHeader } from "../../shared/ui";
import { useLocale } from "../../../locales";

const HISTORY_SIZE = 10;
const STABILITY_PCT = 0.02;
const STABILITY_FLOOR_BUILDINGS = 3;
const STABILITY_FLOOR_MW = 5;

interface StabilityState {
    buildingsCutDisplay: number;
    buildingsCutHistory: number[];
    autoOffDisplay: number;
}

const appendHistorySample = (history: number[], value: number): number[] => {
    const next = [...history, value];
    return next.length > HISTORY_SIZE ? next.slice(next.length - HISTORY_SIZE) : next;
};

// Balance-row label + tooltip. gridFailure (production 0 with demand) is framed
// as a grid failure, not a dispatchable deficit; otherwise the legacy deficit /
// surplus wording with the shared headroom tooltip.
const selectBalanceText = (
    l: ReturnType<typeof useLocale>,
    gridFailure: boolean,
    isDeficit: boolean,
): { label: string; tip: string } => {
    if (gridFailure) {
        return { label: l.t("UI_INFO_GRID_FAILURE"), tip: l.t("TIP_GRID_FAILURE") };
    }
    return { label: isDeficit ? l.t("UI_INFO_DEFICIT") : l.t("UI_INFO_SURPLUS"), tip: l.t("TIP_SURPLUS") };
};

interface ZoneColors {
    collapsed: string;
    critical: string;
    warning: string;
    normal: string;
}

const selectZoneColor = (zone: StressZone, zoneColors: ZoneColors): string => {
    switch (zone) {
        case "collapsed": return zoneColors.collapsed;
        case "red": return zoneColors.critical;
        case "yellow": return zoneColors.warning;
        default: return zoneColors.normal;
    }
};

// Delivered colour: green if matches demand, yellow brownout when 50-90%, red when <50%
const selectDeliveredColor = (
    deliveryRatio: number,
    colors: { success: string; warning: string; error: string },
): string => {
    if (deliveryRatio >= 0.99) return colors.success;
    return deliveryRatio >= 0.5 ? colors.warning : colors.error;
};

// Headroom row colour: crisis on deficit or grid failure, warning while the
// reserve is thinner than the largest single plant, green otherwise.
const selectBalanceColor = (
    gridFailure: boolean,
    isDeficit: boolean,
    headroom: number,
    headroomWarn: number,
    colors: { crisis: string; warning: string; success: string },
): string => {
    if (isDeficit || gridFailure) return colors.crisis;
    return headroom < headroomWarn ? colors.warning : colors.success;
};

export const InfoSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);
    const gridState = usePowerGrid();

    // ========================================================================
    // BINDINGS
    // ========================================================================

    const grid = bindingDataOrDefault(gridState, DEFAULT_POWER_GRID_DTO);
    const frequency = grid?.GridFrequency ?? 50;
    const stressZone = (grid?.StressZone ?? "normal") as StressZone;
    const stressPercent = grid?.StressPercent ?? 0;
    const collapseThresholdHours = grid?.CollapseThresholdHours ?? 2;
    const recoveryHours = grid?.RecoveryHours ?? 0;

    // Derived values (use ?? for numeric values that can legitimately be 0)
    const production = grid?.Production ?? 0;
    const demand = grid?.Demand ?? 0;
    const consumption = grid?.Consumption ?? 0;
    const delivered = grid?.DeliveredMW ?? consumption;
    const autoOff = grid?.AutoCutMW ?? 0;
    const manualOff = (grid?.DistrictShedMW ?? 0) + (grid?.AutoDispatchShedMW ?? 0);
    const buildingsCut = grid?.BuildingsCutCount ?? 0;
    const dispatchable = grid?.CityDispatchableMW ?? 0;
    const headroom = grid?.CapacityHeadroomMW ?? 0;
    const gridExport = grid?.GridExportMW ?? 0;
    const headroomWarn = grid?.HeadroomWarningMW ?? 0;
    const isDeficit = headroom < 0;
    // Production = 0 with real demand is a grid failure, not a dispatchable deficit:
    // there is nothing to auto-dispatch. Frame it distinctly so the player reaches for
    // build/repair plants, backup power, or a blackout schedule instead.
    const gridFailure = production <= 0 && demand > 0;
    // dto-coverage-allow: delivery ratio is a UI load-health meter, not powerBalance recomputation.
    const deliveryRatio = demand > 0 ? delivered / demand : 1;
    const deliveredColor = selectDeliveredColor(deliveryRatio, theme.colors);

    // Hysteresis + instability detection for AUTO OFF.
    // Threshold cuts at the 90% boundary oscillate by ±1-3 buildings each tick because
    // vanilla flow distribution rebalances per-frame. Suppress small flicker by holding
    // the displayed value until the change exceeds 2% of itself, and surface a "unstable"
    // hint when the recent range exceeds the noise floor — so the player knows the
    // flicker is a feature (grid teetering on the threshold), not a UI bug.
    const [stability, setStability] = useState<StabilityState>(() => ({
        buildingsCutDisplay: buildingsCut,
        buildingsCutHistory: [],
        autoOffDisplay: autoOff,
    }));

    useEffect(() => {
        setStability((prev) => {
            const buildingsCutDelta = Math.abs(buildingsCut - prev.buildingsCutDisplay);
            const buildingsCutThreshold = Math.max(STABILITY_FLOOR_BUILDINGS, prev.buildingsCutDisplay * STABILITY_PCT);
            const autoOffDelta = Math.abs(autoOff - prev.autoOffDisplay);
            const autoOffThreshold = Math.max(STABILITY_FLOOR_MW, prev.autoOffDisplay * STABILITY_PCT);
            return {
                buildingsCutDisplay: buildingsCutDelta > buildingsCutThreshold
                    ? buildingsCut
                    : prev.buildingsCutDisplay,
                buildingsCutHistory: appendHistorySample(prev.buildingsCutHistory, buildingsCut),
                autoOffDisplay: autoOffDelta > autoOffThreshold
                    ? autoOff
                    : prev.autoOffDisplay,
            };
        });
    }, [autoOff, buildingsCut]);

    const buildingsCutDisplay = stability.buildingsCutDisplay;
    const autoOffDisplay = stability.autoOffDisplay;
    const buildingsRange = stability.buildingsCutHistory.length > 1
        ? Math.max(...stability.buildingsCutHistory) - Math.min(...stability.buildingsCutHistory)
        : 0;
    const isUnstable = autoOff > 0 && buildingsRange > Math.max(STABILITY_FLOOR_BUILDINGS, buildingsCutDisplay * STABILITY_PCT);
    const buildingsRangeHalf = Math.round(buildingsRange / 2);

    // ========================================================================
    // COLORS
    // ========================================================================

    const frequencyColor = selectZoneColor(stressZone, s.zoneColors);
    const balanceColor = selectBalanceColor(gridFailure, isDeficit, headroom, headroomWarn, {
        crisis: accents.crisis.accent,
        warning: theme.colors.warning,
        success: theme.colors.success,
    });
    const balanceText = selectBalanceText(l, gridFailure, isDeficit);

    // ========================================================================
    // GRID INTEGRITY CALCULATIONS
    // ========================================================================

    const minFreq = 48;
    const maxFreq = 50;
    const clampedFreq = Math.max(minFreq, Math.min(maxFreq, frequency));
    const positionPercent = ((clampedFreq - minFreq) / (maxFreq - minFreq)) * 100;

    // Collapse timer (shown in yellow and red zones). Threshold comes from the
    // DTO (CollapseThresholdHours), not a hardcoded 2 that would drift from config.
    const collapseMinutes = Math.max(0, Math.floor((1 - stressPercent) * collapseThresholdHours * 60));
    const collapseTimer = (stressZone === "yellow" || stressZone === "red")
        ? `${Math.floor(collapseMinutes / 60)}:${(collapseMinutes % 60).toString().padStart(2, "0")}`
        : null;

    // Recovery countdown while collapsed: emergency restart takes RecoveryHours
    // game-hours. Shown so the player knows the collapse is temporary and how long
    // to wait (the modal explains this once; this is the persistent indicator).
    const recoveryTimer = recoveryHours > 0
        ? `${Math.floor(recoveryHours)}:${Math.round((recoveryHours % 1) * 60).toString().padStart(2, "0")}`
        : null;

    // Balance bar computations
    const barScale = Math.max(dispatchable, 100);
    const barBalancePercent = gridFailure ? 100 : Math.min(50, Math.abs(headroom) / barScale * 50);
    const barLeftPos = gridFailure ? 0 : headroom >= 0 ? 50 : 50 - barBalancePercent;

    // ========================================================================
    // RENDER
    // ========================================================================

    return (
        <div>
            {/* GRID INTEGRITY */}
            <SectionHeader
                title={l.t("UI_INFO_GRID_INTEGRITY")}
                titleStyle={s.sectionTitle}
                help={<HelpSection id="power" title={l.t("UI_INFO_GRID_INTEGRITY")}>{l.t("HELP_POWER")}</HelpSection>}
            />
            <div style={s.gridIntegrity.container}>
                {/* Scale labels */}
                <div style={s.gridIntegrity.scaleLabels}>
                    <span style={s.gridIntegrity.scaleLabel}>{l.t("UI_UNIT_HZ", "48.0")}</span>
                    <span style={s.gridIntegrity.scaleLabel}>{l.t("UI_UNIT_HZ", "49.0")}</span>
                    <span style={s.gridIntegrity.scaleLabel}>{l.t("UI_UNIT_HZ", "50.0")}</span>
                </div>

                {/* Progress bar with zone markers */}
                <div style={s.gridIntegrity.barContainer}>
                    <div style={s.gridIntegrity.barBackground}>
                        <div style={s.gridIntegrity.barFill(positionPercent, frequencyColor)} />
                        <div style={s.gridIntegrity.zoneMarker(25)} />
                        <div style={s.gridIntegrity.zoneMarker(75)} />
                    </div>
                </div>

                {/* Zone labels */}
                <div style={s.gridIntegrity.zoneLabels}>
                    <span style={s.gridIntegrity.zoneLabel(s.zoneColors.critical)}>{l.t("UI_INFO_CRITICAL")}</span>
                    <span style={s.gridIntegrity.zoneLabel(s.zoneColors.warning)}>{l.t("UI_INFO_WARNING")}</span>
                    <span style={s.gridIntegrity.zoneLabel(s.zoneColors.normal)}>{l.t("UI_INFO_NOMINAL")}</span>
                </div>

                {/* Status row */}
                <div style={s.gridIntegrity.statusRow}>
                    <span style={s.gridIntegrity.statusText(stressZone)}>
                        {stressZone === "collapsed" ? (
                            <><IconAlert /> {recoveryTimer ? t`POWER IN ${recoveryTimer}` : t`COLLAPSED`}</>
                        ) : stressZone === "red" ? (
                            <><IconAlert /> {t`${collapseTimer} TO FAIL`}</>
                        ) : stressZone === "yellow" ? (
                            <><IconAlert /> {t`${collapseTimer} TO FAIL`}</>
                        ) : (
                            <><IconCheck /> {t`STABLE`}</>
                        )}
                    </span>
                    <HoverTip text={l.t("TIP_FREQUENCY")} style={s.gridIntegrity.frequencyValue(frequencyColor)}>
                        {t`${frequency.toFixed(1)} Hz`}
                    </HoverTip>
                </div>
            </div>

            {/* BALANCE */}
            <div style={{ marginTop: theme.spacing.md }}>
                <div style={s.sectionTitle}>{l.t("UI_INFO_POWER_BALANCE")}</div>
                <Column gap={theme.spacing.xs} style={s.balance.container}>
                    <Row justify="space-between" align="center">
                        <span style={s.balance.label}>{l.t("UI_INFO_PRODUCTION")}</span>
                        <HoverTip text={l.t("TIP_PRODUCTION")} style={s.balance.value(theme.colors.success)}>
                            {t`${production} MW`}
                        </HoverTip>
                    </Row>
                    <Row justify="space-between" align="center">
                        <span style={s.balance.label}>{l.t("UI_INFO_TOTAL_DEMAND")}</span>
                        <HoverTip text={l.t("TIP_DEMAND")} style={s.balance.value(theme.colors.textSecondary)}>
                            {t`${demand} MW`}
                        </HoverTip>
                    </Row>
                    <Row justify="space-between" align="center">
                        <span style={s.balance.label}>{l.t("UI_INFO_ACTIVE_LOAD")}</span>
                        <HoverTip text={l.t("TIP_ACTIVE_LOAD")} style={s.balance.value(deliveredColor)}>
                            {t`${delivered} MW`}
                        </HoverTip>
                    </Row>
                    <Row justify="space-between" align="center">
                        <span style={s.balance.label}>{l.t("UI_INFO_EXPORT")}</span>
                        <HoverTip text={l.t("TIP_EXPORT")} style={s.balance.value(gridExport > 0 ? theme.colors.textSecondary : theme.colors.textMuted)}>
                            {t`${gridExport} MW`}
                        </HoverTip>
                    </Row>
                    <Row justify="space-between" align="center">
                        <span style={s.balance.label}>{l.t("UI_INFO_SHEDDING")}</span>
                        <HoverTip text={l.t("TIP_SHEDDING")} style={s.balance.value(autoOff > 0 ? accents.crisis.accent : theme.colors.textMuted)}>
                            {t`${autoOffDisplay} MW`}
                        </HoverTip>
                    </Row>
                    {autoOff > 0 && buildingsCut > 0 && (
                        <Row justify="flex-end" align="center" style={{ marginTop: "-4rem" }}>
                            <span style={{ fontSize: "12rem", color: theme.colors.textMuted }}>
                                {isUnstable
                                    ? t`${buildingsCutDisplay} ±${buildingsRangeHalf} buildings · unstable`
                                    : t`${buildingsCutDisplay} buildings`}
                            </span>
                        </Row>
                    )}
                    {manualOff > 0 && (
                        <Row justify="space-between" align="center">
                            <span style={s.balance.label}>{l.t("UI_INFO_MANUAL_OFF")}</span>
                            <HoverTip text={l.t("TIP_MANUAL_OFF")} style={s.balance.value(theme.colors.warning)}>
                                {t`${manualOff} MW`}
                            </HoverTip>
                        </Row>
                    )}
                    <div style={s.balance.divider} />
                    <Row justify="space-between" align="center">
                        <span style={s.balance.label}>{balanceText.label}</span>
                        <HoverTip text={balanceText.tip} style={s.balance.value(balanceColor)}>
                            {t`${headroom > 0 ? "+" : ""}${headroom} MW`}
                        </HoverTip>
                    </Row>
                    {/* Power Balance Bar (scale = production) */}
                    <div style={{
                        width: "100%",
                        height: "12rem",
                        background: theme.colors.paper,
                        borderRadius: "6rem",
                        border: `2rem solid ${theme.colors.border}`,
                        position: "relative" as const,
                        overflow: "hidden" as const,
                        marginTop: theme.spacing.xs,
                    }}>
                        {/* Center line (0 MW) */}
                        <div style={{
                            position: "absolute" as const,
                            left: "50%",
                            top: 0,
                            bottom: 0,
                            width: "2rem",
                            background: theme.colors.textMuted,
                            zIndex: 2,
                        }} />
                        {/* Balance fill */}
                        <div style={{
                            position: "absolute" as const,
                            top: 0,
                            bottom: 0,
                            left: `${barLeftPos}%`,
                            width: `${barBalancePercent}%`,
                            background: balanceColor,
                            transition: "left 0.3s ease, width 0.3s ease",
                        }} />
                    </div>
                    <Row justify="space-between" style={{ fontSize: "10rem", color: theme.colors.textMuted, marginTop: "2rem" }}>
                        <span>{l.t("UI_INFO_DEFICIT_LABEL")}</span>
                        <span>0</span>
                        <span>{l.t("UI_INFO_SURPLUS_LABEL")}</span>
                    </Row>
                </Column>
            </div>

        </div>
    );
};
