/**
 * DistrictPassport - Detail view for a single district
 * List-based design for categories, buttons for protection toggles
 */

import React, { useMemo, useCallback } from "react";
import { useTheme, hexToRgba } from "../../../themes";
import { type Theme } from "../../../themes/types";
import { type DistrictViewModel } from "../../../types/models/PowerTypes";
import { scheduleToMode, applyModeChange, type DistrictMode } from "../../shared/common/ModeDropdown";
import { useLocale } from "../../../locales";
import { asBuildingCategoryId, asScheduleId, type BuildingCategoryId, type ScheduleId } from "../../../types/semantic";
import { PassportHeader } from "./PassportHeader";
import { DistrictSummaryRow } from "./PassportSummaryRows";
import {
    DistrictCategoriesSection,
    DistrictPenaltiesSection,
    DistrictProtectionSection,
} from "./DistrictPassportSections";

// ============================================================================
// Types
// ============================================================================

interface DistrictPassportProps {
    district: DistrictViewModel;
    onBack: () => void;
    onToggleCategory: (categoryId: BuildingCategoryId) => void;
    onToggleBlackout: () => void;
    onSetSchedule: (scheduleId: ScheduleId) => void;
    onToggleVIP: () => void;
    onToggleVIPBypass: () => void;
    onToggleInternet: () => void;
    internetLocked?: boolean;
    internetPending?: boolean;
    internetLockedText?: string;
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

    districtName: {
        fontSize: theme.typography.sizeLG,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        textTransform: "uppercase" as const,
        letterSpacing: "1rem",
    } as React.CSSProperties,

    // Category list row
    categoryRow: (isOff: boolean): React.CSSProperties => ({
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} 0`,
        borderBottom: `2rem solid ${theme.colors.border}`,
        opacity: isOff ? 0.6 : 1,
    }),

    categoryAbbrev: (color: string): React.CSSProperties => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        color: color,
        textTransform: "uppercase" as const,
        marginRight: "8rem",
    }),

    categoryName: {
        flex: 1,
        fontSize: theme.typography.sizeSM,
        color: theme.colors.textPrimary,
    } as React.CSSProperties,

    categoryMW: {
        width: "80rem",
        fontSize: theme.typography.sizeSM,
        fontWeight: 600,
        fontFamily: theme.typography.fontFamilyMono,
        color: theme.colors.textSecondary,
        textAlign: "right" as const,
        marginRight: theme.spacing.md,
    } as React.CSSProperties,

    categoryToggle: (isOff: boolean, color: string, disabled?: boolean): React.CSSProperties => ({
        padding: "4rem 12rem",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        borderRadius: "4rem",
        border: "none",
        cursor: disabled ? "not-allowed" : "pointer",
        backgroundColor: isOff ? theme.colors.textMuted : color,
        color: theme.colors.white,
        minWidth: "50rem",
        opacity: disabled ? 0.4 : 1,
    }),

    // Protection buttons row
    protectionRow: {
        display: "flex",
        justifyContent: "flex-start",
        flexWrap: "wrap" as const,
    } as React.CSSProperties,

    protectionButton: (isOn: boolean, color: string, disabled?: boolean): React.CSSProperties => ({
        display: "flex",
        alignItems: "center",
        padding: "8rem 16rem",
        marginRight: theme.spacing.sm,
        marginBottom: theme.spacing.sm,
        fontSize: theme.typography.sizeSM,
        fontWeight: 600,
        borderRadius: "4rem",
        border: isOn ? `3rem solid ${color}` : `3rem solid ${theme.colors.border}`,
        backgroundColor: isOn ? hexToRgba(color, 0.12) : "transparent",
        color: isOn ? color : theme.colors.textMuted,
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.45 : 1,
    }),

    protectionLabel: {
        marginRight: "8rem",
    } as React.CSSProperties,

    protectionStatus: (isOn: boolean): React.CSSProperties => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        color: isOn ? "inherit" : theme.colors.textMuted,
    }),

    penaltyHint: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        fontStyle: "italic" as const,
        marginTop: theme.spacing.xs,
    } as React.CSSProperties,
});

// ============================================================================
// Main Component
// ============================================================================

export const DistrictPassport: React.FC<DistrictPassportProps> = ({
    district,
    onBack,
    onToggleCategory,
    onToggleBlackout,
    onSetSchedule,
    onToggleVIP,
    onToggleVIPBypass,
    onToggleInternet,
    internetLocked = false,
    internetPending = false,
    internetLockedText = "",
}) => {
    const theme = useTheme();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);

    const currentMode = scheduleToMode(district.scheduleId, district.isFullBlackout);

    const handleModeChange = useCallback((mode: DistrictMode) => {
        applyModeChange(mode, district.isFullBlackout, onToggleBlackout, (scheduleId) => onSetSchedule(asScheduleId(scheduleId)));
    }, [district.isFullBlackout, onToggleBlackout, onSetSchedule]);

    const displayName = district.name === "No District" ? l.t("UI_DL_NO_DISTRICT") : district.name;

    // Stable handler using data attribute to avoid inline closures in map
    const handleCategoryToggle = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const catId = Number(e.currentTarget.dataset.category);
        if (!Number.isNaN(catId)) {
            onToggleCategory(asBuildingCategoryId(catId));
        }
    }, [onToggleCategory]);

    return (
        <div style={s.container}>
            {/* Header */}
            <PassportHeader
                headerStyle={s.header}
                backButtonStyle={s.backButton}
                titleStyle={s.districtName}
                backLabel={l.t("UI_DP_BACK")}
                title={displayName}
                onBack={onBack}
            />

            <DistrictSummaryRow
                district={district}
                currentMode={currentMode}
                onModeChange={handleModeChange}
            />

            <DistrictCategoriesSection
                district={district}
                styles={s}
                onCategoryToggle={handleCategoryToggle}
            />

            <DistrictProtectionSection
                district={district}
                styles={s}
                onToggleVIP={onToggleVIP}
                onToggleVIPBypass={onToggleVIPBypass}
                onToggleInternet={onToggleInternet}
                internetLocked={internetLocked}
                internetPending={internetPending}
                internetLockedText={internetLockedText}
            />

            <DistrictPenaltiesSection district={district} styles={s} />
        </div>
    );
};
