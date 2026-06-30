/**
 * GlobalStatus styles
 * Fixed top bar showing critical metrics
 */

import { type Theme, type Accents, getPhaseColors, hexToRgba } from "../../../themes";
import { type WavePhase } from "../../../types/domainDtos";

export const createGlobalStatusStyles = (theme: Theme, accents: Accents) => {
    const phaseColors = getPhaseColors(accents);
    const phaseColor = (phase: WavePhase): string => phaseColors[phase] || theme.colors.textMuted;

    return ({
    container: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        height: "50rem",
        padding: `0 ${theme.spacing.md}`,
        paddingRight: "48rem",  // Space for minimize button
        background: theme.colors.surface,
        borderBottom: `3rem solid ${theme.colors.border}`,
        boxSizing: "border-box" as const,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    metricsGroup: {
        display: "flex",
        alignItems: "center",
        minWidth: 0,
    } as React.CSSProperties,

    metricsGroupPrimary: {
        display: "flex",
        alignItems: "center",
        flex: "1 1 auto",
        minWidth: 0,
        overflow: "hidden" as const,
        // Guaranteed separation from the secondary group. space-between only
        // spaces them while there is free room; once content fills the bar that
        // space collapses to zero and "219 cut" butts against "CRISIS". A margin
        // never collapses, so the two groups stay visually separated. Coherent UI
        // has no flex gap, so the spacing lives on the child as marginRight.
        marginRight: theme.spacing.md,
    } as React.CSSProperties,

    metricsGroupSecondary: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-end",
        flex: "0 1 auto",
        minWidth: 0,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    // Frequency gauge
    frequencyGauge: {
        display: "flex",
        alignItems: "center",
    } as React.CSSProperties,

    frequencyBar: {
        width: "68rem",
        height: "12rem",
        background: theme.colors.paper,
        borderRadius: "6rem",
        overflow: "hidden",
        position: "relative" as const,
        marginRight: theme.spacing.sm,  // Replaces gap
    } as React.CSSProperties,

    frequencyFill: (percent: number, color: string) => ({
        position: "absolute" as const,
        left: 0,
        top: 0,
        height: "100%",
        width: `${percent}%`,
        background: color,
        transition: "width 0.3s ease, background 0.3s ease",
    } as React.CSSProperties),

    frequencyValue: (color: string) => ({
        fontSize: theme.typography.sizeSM,
        fontWeight: theme.typography.weightBold,
        color,
        fontFamily: theme.typography.fontFamilyMono,
        minWidth: "56rem",
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties),

    // Balance badge
    balanceBadge: (isPositive: boolean) => ({
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} 6rem`,
        background: isPositive ? accents.schemes.accentDim : accents.crisis.accentDim,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${isPositive ? accents.schemes.accent : accents.crisis.accent}`,
        flex: "0 0 auto",
    } as React.CSSProperties),

    balanceValue: (isPositive: boolean) => ({
        fontSize: theme.typography.sizeSM,
        fontWeight: theme.typography.weightBold,
        color: isPositive ? accents.schemes.accent : accents.crisis.accent,
        fontFamily: theme.typography.fontFamilyMono,
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties),

    // Battery badge
    batteryContainer: {
        display: "flex",
        alignItems: "center",
    } as React.CSSProperties,

    batteryBar: {
        width: "46rem",
        height: "16rem",
        background: theme.colors.paper,
        borderRadius: "4rem",
        overflow: "hidden",
        border: `2rem solid ${theme.colors.border}`,
        marginRight: theme.spacing.sm,  // Replaces gap
    } as React.CSSProperties,

    batteryFill: (percent: number, color: string) => ({
        height: "100%",
        width: `${percent}%`,
        background: color,
        transition: "width 0.3s ease",
    } as React.CSSProperties),

    batteryValue: (color: string) => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: theme.typography.weightBold,
        color,
        fontFamily: theme.typography.fontFamilyMono,
    } as React.CSSProperties),

    // Threshold operation badge
    thresholdBadge: (isActive: boolean) => ({
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} 6rem`,
        background: isActive ? accents.resilience.accentDim : "transparent",
        borderRadius: theme.layout.borderRadius,
        border: isActive ? `2rem solid ${accents.resilience.accent}` : "none",
        flex: "0 0 auto",
    } as React.CSSProperties),

    thresholdValue: (isActive: boolean) => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: theme.typography.weightMedium,
        color: isActive ? accents.resilience.accent : theme.colors.textMuted,
    } as React.CSSProperties),

    // Threat badge
    threatBadge: (phase: WavePhase) => {
        const color = phaseColor(phase);
        return {
            display: "flex",
            alignItems: "center",
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            background: hexToRgba(color, 0.12),
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${color}`,
        } as React.CSSProperties;
    },

    threatDot: (phase: WavePhase) => {
        const color = phaseColor(phase);
        return {
            width: "8rem",
            height: "8rem",
            borderRadius: "50%",
            background: color,
            animation: phase === "attack" ? "threat-pulse 1s infinite" : "none",
            marginRight: theme.spacing.xs,  // Replaces gap
        } as React.CSSProperties;
    },

    threatText: (phase: WavePhase) => {
        const color = phaseColor(phase);
        return {
            fontSize: theme.typography.sizeXS,
            fontWeight: theme.typography.weightBold,
            color,
            textTransform: "uppercase" as const,
        } as React.CSSProperties;
    },

    // Separator
    separator: {
        width: "1rem",
        height: "30rem",
        background: theme.colors.border,
        margin: "0 6rem",
        flex: "0 0 auto",
    } as React.CSSProperties,

    // Crisis Economy Badge
    crisisEconomyBadge: {
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.xs} 6rem`,
        background: accents.crisis.accentDim,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${accents.crisis.accent}`,
        minWidth: 0,
        overflow: "hidden" as const,
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties,

    crisisEconomyLabel: {
        fontSize: "12rem",
        fontWeight: theme.typography.weightBold,
        color: accents.crisis.accent,
        textTransform: "uppercase" as const,
        marginRight: theme.spacing.sm,
    } as React.CSSProperties,

    crisisEconomyItem: {
        fontSize: "12rem",
        fontWeight: theme.typography.weightMedium,
        color: accents.crisis.accent,
        marginRight: theme.spacing.sm,
    } as React.CSSProperties,

    crisisEconomyItemLast: {
        fontSize: "12rem",
        fontWeight: theme.typography.weightMedium,
        color: accents.crisis.accent,
    } as React.CSSProperties,
});
};
