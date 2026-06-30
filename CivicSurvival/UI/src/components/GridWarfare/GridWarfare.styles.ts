/**
 * GridWarfare Domain Styles
 * Shadow Operations UI - War Room styling
 */

import { type Theme, type Accents, hexToRgba } from "../../themes";
import { type GridOperationType } from "../../types/domainDtos";

export const createGridWarfareStyles = (theme: Theme, accents: Accents) => ({
    // ========== Sections ==========
    section: {
        padding: "12rem",
        background: hexToRgba(accents.crisis.accent, 0.03),
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${hexToRgba(accents.crisis.accent, 0.12)}`,
        width: "100%",
        minHeight: "20rem",
        boxSizing: "border-box" as const,
    } as React.CSSProperties,

    // ========== Progress Bars ==========
    progressBar: {
        height: "6rem",
        background: theme.colors.border,
        borderRadius: "3rem",
        overflow: "hidden" as const,
        marginTop: "4rem",
    } as React.CSSProperties,

    progressFill: (percent: number, color: string) => ({
        width: `${percent}%`,
        height: "100%",
        background: color,
        transition: "width 0.3s ease",
    } as React.CSSProperties),

    // ========== Operation Cards ==========
    slotsContainer: {
        display: "flex",
        flexDirection: "column" as const,
        width: "100%",
        minHeight: "20rem",
    } as React.CSSProperties,

    operationCard: (borderColor: string, isActive: boolean) => ({
        display: "flex",
        flexDirection: "column" as const,
        padding: "10rem",
        marginBottom: "4rem",
        background: isActive ? hexToRgba(borderColor, 0.08) : `${theme.colors.paper}`,
        borderRadius: theme.layout.borderRadius,
        border: `3rem solid ${isActive ? borderColor : theme.colors.border}`,
        transition: "background-color 0.2s ease, border-color 0.2s ease",
        minHeight: "20rem",
    } as React.CSSProperties),

    cardHeader: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    } as React.CSSProperties,

    cardTitle: (color: string) => ({
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color,
        textTransform: "uppercase" as const,
    } as React.CSSProperties),

    cardCost: {
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    cardState: (color: string) => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        color,
        textTransform: "uppercase" as const,
        padding: "2rem 6rem",
        borderRadius: "2rem",
        background: hexToRgba(color, 0.12),
    } as React.CSSProperties),

    cardProgress: {
        height: "4rem",
        background: theme.colors.border,
        borderRadius: "2rem",
        overflow: "hidden" as const,
        marginTop: "8rem",
    } as React.CSSProperties,

    cardActions: {
        display: "flex",
        marginTop: "8rem",
    } as React.CSSProperties,

    // ========== Buttons ==========
    button: (color: string, enabled: boolean = true) => ({
        padding: "8rem 14rem",
        marginRight: "4rem",
        background: "transparent",
        border: `2rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        color,
        cursor: enabled ? "pointer" : "not-allowed",
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        flex: 1,
        opacity: enabled ? 1 : 0.4,
    } as React.CSSProperties),

    buttonPrimary: (color: string, enabled: boolean = true) => ({
        padding: "8rem 14rem",
        background: enabled ? color : "transparent",
        border: `2rem solid ${color}`,
        borderRadius: theme.layout.borderRadius,
        color: enabled ? theme.colors.background : color,
        cursor: enabled ? "pointer" : "not-allowed",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        flex: 1,
        opacity: enabled ? 1 : 0.4,
    } as React.CSSProperties),

    // ========== Balance Display ==========
    balanceContainer: {
        display: "flex",
        flexDirection: "column" as const,
        padding: "10rem 12rem",
        background: hexToRgba(accents.schemes.accent, 0.06),
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${hexToRgba(accents.schemes.accent, 0.19)}`,
        minHeight: "20rem",
    } as React.CSSProperties,

    balanceRow: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "flex-start",
    } as React.CSSProperties,

    balanceLabel: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
    } as React.CSSProperties,

    balanceAmount: (color: string) => ({
        fontSize: theme.typography.sizeLG,
        fontWeight: 700,
        color,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    balanceLocked: {
        fontSize: theme.typography.sizeXS,
        color: accents.resilience.accent,
        fontFamily: theme.typography.fontFamilyMono,
        marginTop: "4rem",
    } as React.CSSProperties,

    // ========== Pressure / Stability Bars ==========
    metricBar: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: "20rem",
    } as React.CSSProperties,

    metricHeader: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    } as React.CSSProperties,

    metricLabel: (color: string) => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        color,
        textTransform: "uppercase" as const,
    } as React.CSSProperties),

    metricValue: {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties,

    // ========== Intel Preview ==========
    intelContainer: {
        display: "flex",
        flexDirection: "column" as const,
        padding: "10rem 12rem",
        background: hexToRgba(accents.operations.accent, 0.06),
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${hexToRgba(accents.operations.accent, 0.25)}`,
        minHeight: "20rem",
    } as React.CSSProperties,

    intelHeader: {
        display: "flex",
        alignItems: "center",
        marginBottom: "6rem",
    } as React.CSSProperties,

    intelIcon: {
        fontSize: theme.typography.sizeSM,
    } as React.CSSProperties,

    intelTitle: {
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        color: accents.operations.accent,
        textTransform: "uppercase" as const,
    } as React.CSSProperties,

    intelContent: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
    } as React.CSSProperties,

    intelUpgrade: {
        marginTop: "8rem",
        padding: "8rem 12rem",
        background: "transparent",
        border: `2rem solid ${accents.operations.accent}`,
        borderRadius: theme.layout.borderRadius,
        color: accents.operations.accent,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: 600,
        width: "100%",
    } as React.CSSProperties,
});

// ========== Color Helpers ==========

export const getOperationColor = (type: GridOperationType, accents: Accents): string => {
    switch (type) {
        case "drone": return accents.crisis.accent;        // Kinetic - red
        case "blackout": return accents.operations.accent; // Cyber - blue
        case "disinfo": return accents.schemes.accent;     // Psyops - green
        default: return accents.resilience.accent;
    }
};

export const getPressureColor = (pressure: number, accents: Accents, _theme: Theme): string => {
    if (pressure >= 80) return accents.crisis.accent;
    if (pressure >= 50) return accents.resilience.accent;
    return accents.schemes.accent;
};

export const getStabilityColor = (stability: number, accents: Accents, _theme: Theme): string => {
    if (stability >= 70) return accents.schemes.accent;
    if (stability >= 40) return accents.resilience.accent;
    return accents.crisis.accent;
};
