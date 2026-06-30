/**
 * CityPassport - City-level control panel
 * Mass operations for all districts
 */

import React, { useMemo, useCallback } from "react";
import { useTheme } from "../../../themes";
import { type Theme } from "../../../themes/types";
import { type DistrictViewModel } from "../../../types/models/PowerTypes";
import { isCityScheduleId, type CityScheduleId, type SchedulePresetId } from "../../../types/semantic";
import { useLocale } from "../../../locales";
import { usePowerGrid } from "@hooks/domain";
import { PassportHeader } from "./PassportHeader";
import { CitySummaryRow } from "./PassportSummaryRows";
import {
    CityMassControlsSection,
    CityModeSection,
    CityStatsSection,
    type CityPassportStats,
} from "./CityPassportSections";

// ============================================================================
// Types
// ============================================================================

interface CityPassportProps {
    districts: DistrictViewModel[];
    citySchedule: SchedulePresetId;
    autoDispatchEnabled: boolean;
    autoDispatchSheddedCount: number;
    autoDispatchBlockedByVip: boolean;
    citySchedulePending?: boolean;
    internetLocked?: boolean;
    internetPending?: boolean;
    internetLockedText?: string;
    onBack: () => void;
    onSetCitySchedule: (scheduleId: SchedulePresetId) => void;
    onToggleAllVIP: (enable: boolean) => void;
    onToggleAllWealthy: (enable: boolean) => void;
    onToggleAllInternet: (enable: boolean) => void;
    onSetAllMode: (mode: "on" | "off") => void;
    onToggleAutoDispatch: () => void;
}

// ============================================================================
// Styles
// ============================================================================

const createStyles = (theme: Theme) => ({
    container: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        width: "100%",
        backgroundColor: theme.colors.background,
    } as React.CSSProperties,

    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: `${theme.spacing.sm} ${theme.spacing.md}`,
        borderBottom: `2rem solid ${theme.colors.border}`,
        backgroundColor: theme.colors.paper,
    } as React.CSSProperties,

    backButton: {
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        fontSize: theme.typography.sizeSM,
        fontWeight: 600,
        color: theme.colors.textSecondary,
        backgroundColor: "transparent",
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
    } as React.CSSProperties,

    title: {
        fontSize: theme.typography.sizeLG,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        textTransform: "uppercase" as const,
        letterSpacing: "1rem",
    } as React.CSSProperties,

    modeGrid: {
        display: "flex",
        flexWrap: "wrap" as const,
    } as React.CSSProperties,

    modeButton: (isSelected: boolean, color: string): React.CSSProperties => ({
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        alignItems: "center",
        padding: "8rem 16rem",
        marginRight: theme.spacing.sm,
        marginBottom: theme.spacing.sm,
        minWidth: "70rem",
        borderRadius: "4rem",
        border: isSelected ? `3rem solid ${color}` : `2rem solid ${theme.colors.border}`,
        backgroundColor: isSelected ? color : "transparent",
        color: isSelected ? theme.colors.white : theme.colors.textSecondary,
        cursor: "pointer",
        transition: `background-color ${theme.effects.transitionFast}, border-color ${theme.effects.transitionFast}, color ${theme.effects.transitionFast}`,
    }),

    modeButtonDisabled: {
        opacity: 0.45,
        cursor: "not-allowed",
    } as React.CSSProperties,

    modeLabel: {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
    } as React.CSSProperties,

    modeDesc: {
        fontSize: theme.typography.sizeXS,
        marginTop: "2rem",
        opacity: 0.8,
    } as React.CSSProperties,

    toggleRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: `${theme.spacing.sm} 0`,
        borderBottom: `2rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    toggleLabel: {
        fontSize: theme.typography.sizeSM,
        color: theme.colors.textPrimary,
    } as React.CSSProperties,

    toggleButtons: {
        display: "flex",
    } as React.CSSProperties,

    toggleButton: (isActive: boolean, color: string): React.CSSProperties => ({
        padding: "4rem 12rem",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        borderRadius: "2rem",
        border: "none",
        cursor: "pointer",
        backgroundColor: isActive ? color : theme.colors.paper,
        color: isActive ? theme.colors.white : theme.colors.textMuted,
        marginLeft: "4rem",
    }),

});

// ============================================================================
// Main Component
// ============================================================================

export const CityPassport: React.FC<CityPassportProps> = ({
    districts,
    citySchedule,
    autoDispatchEnabled,
    autoDispatchSheddedCount,
    autoDispatchBlockedByVip,
    citySchedulePending = false,
    internetLocked = false,
    internetPending = false,
    internetLockedText = "",
    onBack,
    onSetCitySchedule,
    onToggleAllVIP,
    onToggleAllWealthy,
    onToggleAllInternet,
    onSetAllMode,
    onToggleAutoDispatch,
}) => {
    const theme = useTheme();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);
    const gridState = usePowerGrid();
    const grid = gridState.status === "ready" ? gridState.data : null;
    const cityScheduleLocked = grid?.CityScheduleAvailability.CanRun === false;
    const cityScheduleDisabled = cityScheduleLocked || citySchedulePending;
    const cityScheduleLockedReasonId = grid?.CityScheduleAvailability.LockedReasonId ?? "";

    // Calculate city stats
    const stats = useMemo<CityPassportStats>(() => {
        const totalMW = districts.reduce((sum, d) => sum + d.totalMW, 0);
        const blackoutCount = districts.filter((d) => d.isFullBlackout).length;
        const blackoutPercent = districts.length > 0
            ? Math.round((blackoutCount / districts.length) * 100)
            : 0;
        const vipCount = districts.filter((d) => d.isVIP).length;
        const allVIP = vipCount === districts.length && districts.length > 0;
        const someVIP = vipCount > 0;
        const wealthyCount = districts.filter((d) => d.isVIPBypass).length;
        const allWealthy = wealthyCount === districts.length && districts.length > 0;
        const someWealthy = wealthyCount > 0;
        const internetOffCount = districts.filter((d) => d.internetDisabled).length;
        const allInternetOff = internetOffCount === districts.length && districts.length > 0;
        const someInternetOff = internetOffCount > 0;

        return {
            totalMW,
            districtCount: districts.length,
            blackoutCount,
            blackoutPercent,
            allVIP,
            someVIP,
            allWealthy,
            someWealthy,
            allInternetOff,
            someInternetOff,
            vipCount,
            wealthyCount,
            internetOffCount,
        };
    }, [districts]);

    const dtoEffectiveMode = grid?.EffectiveCityMode;
    const effectiveMode: CityScheduleId = typeof dtoEffectiveMode === "number" && isCityScheduleId(dtoEffectiveMode)
        ? dtoEffectiveMode
        : citySchedule;

    // Stable handler using data attribute to avoid inline closures in map
    const handleScheduleClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const sched = Number(e.currentTarget.dataset.sched);
        if (!isCityScheduleId(sched)) return;
        if (sched === -1) {
            onSetAllMode("off");
        } else {
            if (cityScheduleDisabled) return;
            onSetCitySchedule(sched);
            onSetAllMode("on");
        }
    }, [cityScheduleDisabled, onSetAllMode, onSetCitySchedule]);

    const isCityModeScheduleDisabled = useCallback((schedule: CityScheduleId) =>
        schedule !== -1 && cityScheduleDisabled,
    [cityScheduleDisabled]);

    return (
        <div style={s.container}>
            {/* Header */}
            <PassportHeader
                headerStyle={s.header}
                backButtonStyle={s.backButton}
                titleStyle={s.title}
                backLabel={l.t("UI_CP_BACK")}
                title={l.t("UI_CP_CITY_CONTROL")}
                onBack={onBack}
            />

            <CitySummaryRow stats={stats} />

            <CityModeSection
                effectiveMode={effectiveMode}
                isScheduleDisabled={isCityModeScheduleDisabled}
                lockedReasonId={cityScheduleLockedReasonId}
                styles={s}
                onScheduleClick={handleScheduleClick}
            />

            <CityMassControlsSection
                stats={stats}
                autoDispatchEnabled={autoDispatchEnabled}
                autoDispatchSheddedCount={autoDispatchSheddedCount}
                autoDispatchBlockedByVip={autoDispatchBlockedByVip}
                internetLocked={internetLocked}
                internetPending={internetPending}
                internetLockedText={internetLockedText}
                styles={s}
                onToggleAllVIP={onToggleAllVIP}
                onToggleAllWealthy={onToggleAllWealthy}
                onToggleAllInternet={onToggleAllInternet}
                onToggleAutoDispatch={onToggleAutoDispatch}
            />

            <CityStatsSection stats={stats} fleetSaturation={grid?.FleetSaturationFactor ?? 1} />
        </div>
    );
};
