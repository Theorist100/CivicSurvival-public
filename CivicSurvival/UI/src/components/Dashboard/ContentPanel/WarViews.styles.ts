/**
 * WAR Domain Views styles
 * DefenseContent + IntelContent
 */

import { type Theme, type Accents, createDomainStyles, hexToRgba } from "@themes";
import { type TensionStatus } from "../../../types/domainDtos";

export const createWarViewsStyles = (theme: Theme, accents: Accents) => {
    const base = createDomainStyles({
        theme,
        accent: accents.crisis,
        fillContainer: true,
        titleFontSize: theme.typography.sizeSM,
        titleMarginBottom: "10rem",
        titleDisplay: "flex",
        titleAlignItems: "center",
    });

    return {
        // Shared
        container: {
            display: "flex",
            flexDirection: "column" as const,
            minHeight: 0,
            alignItems: "stretch" as const,
            width: "100%",
            padding: "8rem",
            boxSizing: "border-box" as const,
            flex: 1,
        } as React.CSSProperties,

        containerChild: {
            marginBottom: "8rem",
        } as React.CSSProperties,

        section: base.section,

        sectionTitle: base.sectionTitle,

        sectionTitleIcon: {
            marginRight: "8rem",
        } as React.CSSProperties,

    sectionTitleColored: (color: string) => ({
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        textTransform: "uppercase" as const,
        color,
        marginBottom: "10rem",
    } as React.CSSProperties),

    // Buttons
    button: (color: string) => ({
        padding: "8rem 14rem",
        background: "transparent",
        border: `2rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        color: color,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        flex: 1,
    } as React.CSSProperties),

    buttonFull: (color: string) => base.buttonFull(color),

    buttonWithOpacity: (color: string, opacity: number) => ({
        padding: "8rem 14rem",
        background: opacity === 1 ? color : "transparent",
        border: `2rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        color: opacity === 1 ? theme.colors.paper : color,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        flex: 1,
        opacity: opacity === 1 ? 1 : opacity,
    } as React.CSSProperties),

    buttonGroup: {
        display: "flex",
        marginTop: "8rem",
    } as React.CSSProperties,

    buttonGroupItem: {
        marginRight: "8rem",
    } as React.CSSProperties,

    // Badges
    badge: (color: string) => ({
        fontSize: theme.typography.sizeXS,
        color: color,
        fontWeight: 700,
        marginLeft: "8rem",
    } as React.CSSProperties),

    // Text styles
    note: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        fontStyle: "italic" as const,
        marginTop: "8rem",
    } as React.CSSProperties,

    clear: {
        color: accents.schemes.accent,
        fontSize: theme.typography.sizeXS,
        marginTop: "8rem",
    } as React.CSSProperties,

    stats: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        textAlign: "center" as const,
        marginTop: "6rem",
    } as React.CSSProperties,

    loading: {
        color: theme.colors.textMuted,
        textAlign: "center" as const,
        padding: "20rem",
    } as React.CSSProperties,

    noData: base.noData({
        textAlign: "center",
        whiteSpace: "normal",
        wordWrap: "break-word",
        padding: "10rem 0",
    }),

    // Intel-specific
    assessment: (color: string) => base.assessment(color),

    targetRow: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        padding: "8rem 0",
        borderBottom: `2rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    targetName: {
        color: theme.colors.textPrimary,
        fontSize: theme.typography.sizeXS,
        width: "90rem",
        flexShrink: 0,
    } as React.CSSProperties,

    targetBar: {
        flex: 1,
        height: "6rem",
        background: theme.colors.border,
        borderRadius: "3rem",
        overflow: "hidden" as const,
        margin: "0 8rem",
    } as React.CSSProperties,

    targetFill: (percent: number) => ({
        width: `${percent}%`,
        height: "100%",
        background: percent > 60 ? accents.crisis.accent
            : percent > 30 ? accents.resilience.accent
            : accents.schemes.accent,
    } as React.CSSProperties),

    insiderActive: {
        display: "flex",
        alignItems: "center",
        marginTop: "12rem",
        padding: "14rem",
        background: hexToRgba(accents.schemes.accent, 0.12),
        border: `2rem solid ${accents.schemes.accent}`,
        borderRadius: theme.layout.borderRadius,
        color: accents.schemes.accent,
        fontSize: theme.typography.sizeXS,
    } as React.CSSProperties,

    insiderActiveChild: {
        marginRight: "10rem",
    } as React.CSSProperties,

    buyButton: (isAffordable: boolean) => ({
        display: "flex",
        alignItems: "center",
        marginTop: "12rem",
        padding: "14rem",
        background: hexToRgba(accents.schemes.accent, 0.08),
        border: `2rem solid ${accents.schemes.accent}`,
        borderRadius: theme.layout.borderRadius,
        color: accents.schemes.accent,
        fontSize: theme.typography.sizeXS,
        cursor: isAffordable ? "pointer" : "not-allowed",
        opacity: isAffordable ? 1 : 0.5,
        width: "100%",
        boxSizing: "border-box" as const,
    } as React.CSSProperties),

    buyButtonChild: {
        marginRight: "10rem",
    } as React.CSSProperties,

    shadowBalance: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        marginTop: "6rem",
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties,

    priceImpact: {
        display: "flex",
        alignItems: "center",
        marginTop: "10rem",
        padding: "10rem",
        background: hexToRgba(accents.resilience.accent, 0.08),
        border: `2rem solid ${hexToRgba(accents.resilience.accent, 0.25)}`,
        borderRadius: theme.layout.borderRadius,
        fontSize: theme.typography.sizeXS,
        color: accents.resilience.accent,
    } as React.CSSProperties,

    priceImpactIcon: {
        flexShrink: 0,
        marginRight: "8rem",
        fontWeight: 700,
    } as React.CSSProperties,

    // Large panel (Variant A - combined sections)
    largePanel: {
        flex: 1,
        minWidth: 0,
        // display: flex handled by Column component
        padding: "14rem",
        background: `${theme.colors.paper}`,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${theme.colors.border}`,
        boxSizing: "border-box" as const,
    } as React.CSSProperties,

    panelHeader: (color: string) => ({
        fontSize: theme.typography.sizeMD,
        fontWeight: 700,
        textTransform: "uppercase" as const,
        color: color,
        marginBottom: "12rem",
        letterSpacing: "0.5rem",
    } as React.CSSProperties),

    divider: base.divider("14rem 0"),

    subsectionTitle: (color?: string) => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        textTransform: "uppercase" as const,
        color: color ?? theme.colors.textSecondary,
        marginBottom: "10rem",
        letterSpacing: "0.5rem",
    } as React.CSSProperties),

    statusBadge: (color: string, isActive: boolean) => ({
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
        padding: "2rem 8rem",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        textTransform: "uppercase" as const,
        color: isActive ? theme.colors.paper : color,
        background: isActive ? color : "transparent",
        border: `2rem solid ${color}`,
        borderRadius: "3rem",
        marginLeft: "10rem",
    } as React.CSSProperties),

    inlineStats: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        padding: "8rem 0",
    } as React.CSSProperties,

    inlineStatsChild: {
        marginBottom: "4rem",
    } as React.CSSProperties,

    statItem: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        width: "100%",
    } as React.CSSProperties,

    statItemLabel: {
        marginRight: "12rem",  // Minimum gap between label and value to prevent sticking
    } as React.CSSProperties,

    // Building Card styles (CS2 building menu style)
    buildingCard: {
        display: "flex",
        alignItems: "center",
        background: `${theme.colors.surface}`,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${theme.colors.border}`,
        overflow: "hidden",
        marginTop: "6rem",
        padding: "5rem 10rem",
        minHeight: "64rem",
        cursor: "pointer",
        transition: "border-color 0.15s ease",
    } as React.CSSProperties,

    buildingCardHover: {
        borderColor: accents.operations.accent,
    } as React.CSSProperties,

    buildingThumbnail: {
        width: "64rem",
        height: "64rem",
        minWidth: "64rem",
        objectFit: "contain" as const,
        background: theme.colors.border,
        border: `1rem solid ${theme.colors.border}`,
        marginRight: "12rem",
    } as React.CSSProperties,

    buildingInfo: {
        flex: 1,
        padding: "0 12rem 0 0",
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        justifyContent: "center" as const,
        minWidth: 0,
    } as React.CSSProperties,

    buildingName: {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        marginBottom: "4rem",
    } as React.CSSProperties,

    buildingStats: {
        display: "flex",
        flexDirection: "row" as const,
        minHeight: 0,
        flexWrap: "wrap" as const,
    } as React.CSSProperties,

    buildingStat: {
        display: "flex",
        alignItems: "center",
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        marginBottom: 0,
        marginRight: "12rem",
    } as React.CSSProperties,

    buildingStatIcon: {
        marginRight: "6rem",
        width: "12rem",
        height: "12rem",
    } as React.CSSProperties,

    buildingStatValue: (color: string) => ({
        color: color,
        fontWeight: 600,
        marginLeft: "4rem",
    } as React.CSSProperties),

    buildingPlaceButton: (color: string) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        alignSelf: "center",
        height: "36rem",
        padding: "0 14rem",
        background: hexToRgba(color, 0.18),
        border: `2rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        color,
        fontWeight: 700,
        fontSize: theme.typography.sizeXS,
        textTransform: "uppercase" as const,
        cursor: "pointer",
        minWidth: "76rem",
        letterSpacing: "0.5rem",
    } as React.CSSProperties),

    buildingList: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
    } as React.CSSProperties,
    };
};

// Color helpers
export const getAmmoColor = (percent: number, accents: Accents, theme: Theme) => {
    if (percent < 20) return accents.crisis.accent;
    if (percent < 50) return accents.resilience.accent;
    return theme.colors.textPrimary;
};

export const getAmmoBarColor = (percent: number, accents: Accents) => {
    if (percent < 20) return accents.crisis.accent;
    if (percent < 50) return accents.resilience.accent;
    return accents.schemes.accent;
};

export const getPenaltyColor = (penalty: number, accents: Accents, theme: Theme) => {
    if (penalty > 10) return accents.crisis.accent;
    if (penalty > 0) return accents.resilience.accent;
    return theme.colors.textMuted;
};

export const getThreatColor = (level: number, accents: Accents, theme: Theme) => {
    if (level >= 70) return theme.colors.errorBright;
    if (level >= 50) return accents.crisis.accent;
    if (level >= 30) return accents.resilience.accent;
    return accents.schemes.accent;
};

export const getThreatColorByStatus = (status: TensionStatus, accents: Accents, theme: Theme) => {
    switch (status) {
        case "CRITICAL": return theme.colors.errorBright;
        case "HIGH": return accents.crisis.accent;
        case "ELEVATED": return accents.resilience.accent;
        case "LOW":
        default: return accents.schemes.accent;
    }
};
