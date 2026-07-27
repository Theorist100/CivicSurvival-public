/**
 * DistrictListView - Master view with simplified district table
 * 5 columns: District | MW | Mode | Quick | Status
 * Click on row → opens DistrictPassport (Detail View)
 */

import React, { useMemo, useState, useCallback } from "react";
import { useTheme, useAccents } from "../../../themes";
import { type Theme } from "../../../themes/types";
import { type DistrictViewModel, type BlackoutSource } from "../../../types/models/PowerTypes";
import {
    IconHand,
    IconRobot,
    IconClock,
} from "../../shared/common/Icons";
import { modeToSchedule, type DistrictMode } from "../../shared/common/ModeDropdown";
import { useLocale } from "../../../locales";
import { CityRow, DistrictRow } from "./district-list/DistrictListRows";
import { type CityScheduleId, type EntityIndex } from "../../../types/semantic";

// ============================================================================
// Types
// ============================================================================

interface DistrictListViewProps {
    districts: DistrictViewModel[];
    onSelectDistrict: (entity: EntityIndex) => void;
    onToggleBlackout: (entity: EntityIndex) => void;
    onSetMode: (entity: EntityIndex, mode: DistrictMode) => void;
    // City row props
    cityTotalMW: number;
    cityDeliveredMW: number;
    citySchedule: CityScheduleId;
    cityScheduleLocked?: boolean;
    districtsOverrideCity?: boolean;
    onCityScheduleChange: (schedule: CityScheduleId) => void;
    onCityClick: () => void;
}

type BlackoutSourceInfo = {
    icon: React.FC<{ className?: string }> | null;
    color: string;
    title: string;
};

// ============================================================================
// Styles
// ============================================================================

const createStyles = (theme: Theme) => ({
    container: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        overflow: "hidden",
    } as React.CSSProperties,

    header: {
        display: "flex",
        backgroundColor: theme.colors.paper,
        borderBottom: `3rem solid ${theme.colors.textMuted}`,
        padding: `${theme.spacing.sm} 0`,
    } as React.CSSProperties,

    headerCell: (width: string): React.CSSProperties => ({
        width,
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        textAlign: "center" as const,
    }),

    row: (isOff: boolean, isHovered: boolean): React.CSSProperties => ({
        display: "flex",
        alignItems: "center",
        borderBottom: `2rem solid ${theme.colors.border}`,
        backgroundColor: isHovered
            ? "rgba(255, 255, 255, 0.08)"
            : isOff
                ? "rgba(255, 68, 68, 0.05)"
                : "transparent",
        cursor: "pointer",
        transition: `background-color ${theme.effects.transitionFast}`,
    }),

    cell: (width: string): React.CSSProperties => ({
        width,
        padding: `${theme.spacing.sm} ${theme.spacing.sm}`,
        fontSize: theme.typography.sizeSM,
        display: "flex",
        alignItems: "center",
        justifyContent: "center" as const,
    }),

    districtName: (isOff: boolean): React.CSSProperties => ({
        justifyContent: "flex-start" as const,
        fontWeight: 600,
        color: isOff ? theme.colors.zoneRed : theme.colors.textPrimary,
    }),

    mwValue: (color: string): React.CSSProperties => ({
        fontWeight: 600,
        fontFamily: theme.typography.fontFamilyMono,
        color,
    }),

    quickButton: (isOff: boolean): React.CSSProperties => ({
        width: "28rem",
        height: "28rem",
        display: "flex",
        alignItems: "center",
        justifyContent: "center" as const,
        borderRadius: "4rem",
        border: `2rem solid ${isOff ? theme.colors.zoneRed : theme.colors.border}`,
        cursor: "pointer",
        backgroundColor: isOff ? theme.colors.zoneRed : "transparent",
        color: isOff ? theme.colors.white : theme.colors.textSecondary,
        fontSize: "16rem",
    }),

    // Flags column - active badges only
    flagsContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-end" as const,
    } as React.CSSProperties,

    flagBadge: (color: string): React.CSSProperties => ({
        fontSize: "18rem",
        color,
        marginLeft: "4rem",
    }),

    // Edit button (gear icon)
    editButton: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center" as const,
        width: "28rem",
        height: "28rem",
        borderRadius: "4rem",
        border: `2rem solid ${theme.colors.border}`,
        backgroundColor: "transparent",
        color: theme.colors.textMuted,
        cursor: "pointer",
        transition: `background-color ${theme.effects.transitionFast}, border-color ${theme.effects.transitionFast}`,
    } as React.CSSProperties,

    editButtonHover: {
        backgroundColor: theme.colors.paper,
        borderColor: theme.colors.textSecondary,
        color: theme.colors.textPrimary,
    } as React.CSSProperties,
});

// ============================================================================
// Flag Colors (theme-derived, created inside component via useMemo)
// ============================================================================

export type FlagColors = {
    vip: string;
    wealthy: string;
    internet: string;
    noInternet: string;
    partial: string;
    auto: string;
    manual: string;
    schedule: string;
};

// ============================================================================
// Pure helpers (no component state dependency)
// ============================================================================

export const COLUMN_WIDTHS = {
    district: "100rem",
    del: "50rem",
    max: "50rem",
    mode: "65rem",
    quick: "65rem",
    flags: "85rem",
    edit: "45rem",
} as const;  // Total: 460rem (delivered/demand split into two 50rem cells; sum unchanged)

export const hasPartialCategories = (d: DistrictViewModel): boolean => {
    const someCategoriesOff = d.categories.some((c) => c.isOff);
    const allCategoriesOff = d.categories.every((c) => c.isOff);
    return someCategoriesOff && !allCategoriesOff;
};

// ============================================================================
// Main Component
// ============================================================================

export const DistrictListView: React.FC<DistrictListViewProps> = ({
    districts,
    onSelectDistrict,
    onToggleBlackout,
    onSetMode,
    cityTotalMW,
    cityDeliveredMW,
    citySchedule,
    cityScheduleLocked = false,
    districtsOverrideCity = false,
    onCityScheduleChange,
    onCityClick,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);
    const [hoveredEntity, setHoveredEntity] = useState<EntityIndex | null>(null);
    const [cityHovered, setCityHovered] = useState(false);

    // Flag colors from theme accents (L05 fix)
    const flagColors: FlagColors = useMemo(() => ({
        vip: accents.vip.accent,
        wealthy: accents.schemes.accent,
        internet: theme.colors.success,
        noInternet: accents.crisis.accent,
        partial: accents.resilience.accent,
        auto: accents.resilience.accent,
        manual: theme.colors.textMuted,
        schedule: accents.operations.accent,
    }), [accents, theme]);

    const blackoutSourceInfoBySource = useMemo<Record<BlackoutSource, BlackoutSourceInfo>>(() => {
        const none: BlackoutSourceInfo = { icon: null, color: "", title: "" };
        return {
            none,
            auto: { icon: IconRobot, color: flagColors.auto, title: l.t("UI_DL_BLACKOUT_AUTO") },
            manual: { icon: IconHand, color: flagColors.manual, title: l.t("UI_DL_BLACKOUT_MANUAL") },
            schedule: { icon: IconClock, color: flagColors.schedule, title: l.t("UI_DL_BLACKOUT_SCHEDULE") },
        };
    }, [flagColors, l]);

    const handleModeChange = useCallback((d: DistrictViewModel, mode: DistrictMode) => {
        onSetMode(d.entity, mode);
    }, [onSetMode]);

    // Stable per-district mode change handlers (avoids inline closures in map)
    const modeHandlers = useMemo(() => {
        const handlers: Record<number, (m: DistrictMode) => void> = {};
        districts.forEach((d) => {
            handlers[d.entity] = (m: DistrictMode) => handleModeChange(d, m);
        });
        return handlers;
    }, [districts, handleModeChange]);

    // Stable city mode change handler
    const handleCityModeChange = useCallback((m: DistrictMode) => {
        onCityScheduleChange(modeToSchedule(m));
    }, [onCityScheduleChange]);

    return (
        <div style={s.container}>
            <CityRow
                cityTotalMW={cityTotalMW}
                cityDeliveredMW={cityDeliveredMW}
                citySchedule={citySchedule}
                cityScheduleLocked={cityScheduleLocked}
                districtsOverrideCity={districtsOverrideCity}
                isHovered={cityHovered}
                styles={s}
                onCityClick={onCityClick}
                onCityModeChange={handleCityModeChange}
                onHoverChange={setCityHovered}
            />

            {/* Header */}
            <div style={s.header}>
                <div style={{ ...s.headerCell(COLUMN_WIDTHS.district), textAlign: "left" as const }}>
                    {l.t("UI_DL_HEADER_DISTRICT")}
                </div>
                <div style={s.headerCell(COLUMN_WIDTHS.del)}>{l.t("UI_DL_HEADER_DEL")}</div>
                <div style={s.headerCell(COLUMN_WIDTHS.max)}>{l.t("UI_DL_HEADER_MAX")}</div>
                <div style={s.headerCell(COLUMN_WIDTHS.mode)}>{l.t("UI_DL_HEADER_MODE")}</div>
                <div style={s.headerCell(COLUMN_WIDTHS.quick)}>{l.t("UI_DL_HEADER_QUICK")}</div>
                <div style={{ ...s.headerCell(COLUMN_WIDTHS.flags), textAlign: "right" as const }}>
                    {l.t("UI_DL_HEADER_FLAGS")}
                </div>
                <div style={s.headerCell(COLUMN_WIDTHS.edit)}></div>
            </div>

            {/* Body */}
            <div>
                {districts.map((district) => {
                    const modeHandler = modeHandlers[district.entity] ?? ((mode: DistrictMode) => handleModeChange(district, mode));
                    return (
                        <DistrictRow
                            key={district.entity}
                            district={district}
                            isHovered={hoveredEntity === district.entity}
                            flagColors={flagColors}
                            styles={s}
                            modeHandler={modeHandler}
                            blackoutSourceInfo={blackoutSourceInfoBySource[district.blackoutSource]}
                            onSelectDistrict={onSelectDistrict}
                            onToggleBlackout={onToggleBlackout}
                            onHoverEntity={setHoveredEntity}
                        />
                    );
                })}
            </div>
        </div>
    );
};
