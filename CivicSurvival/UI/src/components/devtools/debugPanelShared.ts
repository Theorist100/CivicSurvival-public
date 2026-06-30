/**
 * Shared types, styles, and helpers for the debug panel tabs.
 */

import type React from "react";
import type { Theme, Accents } from "../../themes";
import { Z_INDEX } from "../../themes";
import type { useDebugData } from "../../hooks/state/useDebugData";

export type AccentPresets = Accents;

export interface TabProps {
    debug: ReturnType<typeof useDebugData>;
    styles: ReturnType<typeof createStyles>;
    theme: Theme;
    accents: AccentPresets;
}

// ============================================================================
// STYLES
// ============================================================================

export const createStyles = (t: Theme, accents: AccentPresets) => ({
    container: {
        position: "fixed" as const,
        top: "10rem",
        left: "10rem",
        width: "520rem",
        maxHeight: "800rem",
        minHeight: "100rem",
        overflowY: "auto" as const,
        backgroundColor: t.effects.glassBackground,
        border: `1rem solid ${t.colors.borderLight}`,
        borderRadius: "6rem",
        padding: "12rem",
        fontFamily: t.typography.fontFamilyMono,
        fontSize: "12rem",
        color: t.colors.white,
        zIndex: Z_INDEX.critical,
    } as React.CSSProperties,
    header: {
        fontSize: "14rem",
        fontWeight: "bold" as const,
        color: accents.resilience.accent,
        marginBottom: "8rem",
        textTransform: "uppercase" as const,
        letterSpacing: "1rem",
    } as React.CSSProperties,
    section: {
        marginBottom: "10rem",
        minHeight: "20rem",
        paddingBottom: "8rem",
        borderBottom: `1rem solid ${t.colors.borderLight}`,
    } as React.CSSProperties,
    sparklineLabel: {
        color: t.colors.textSecondary,
    } as React.CSSProperties,
    sparklineBg: {
        backgroundColor: t.colors.surface,
    } as React.CSSProperties,
    waitingText: {
        fill: t.colors.textMuted,
    } as React.CSSProperties,
    sparklineContainer: {
        marginBottom: "10rem",
        minHeight: "40rem",
    } as React.CSSProperties,
    sparklineHeader: {
        display: "flex",
        justifyContent: "space-between",
        marginBottom: "4rem",
        fontSize: "11rem",
    } as React.CSSProperties,
    sparklineSecondaryText: {
        color: t.colors.textSecondary,
    } as React.CSSProperties,
    sparklineBoldValue: (color: string): React.CSSProperties => ({
        color,
        fontWeight: "bold" as const,
    }),
    svgContainer: {
        backgroundColor: t.colors.surface,
        borderRadius: "3rem",
    } as React.CSSProperties,
    sparklineFooter: {
        display: "flex",
        justifyContent: "space-between",
        fontSize: "10rem",
        color: t.colors.textMuted,
        marginTop: "2rem",
    } as React.CSSProperties,
    chartLegend: {
        display: "flex",
        justifyContent: "space-between",
        fontSize: "10rem",
        marginTop: "4rem",
    } as React.CSSProperties,
    successText: {
        color: t.colors.success,
    } as React.CSSProperties,
    mutedText: {
        color: t.colors.textMuted,
    } as React.CSSProperties,
    crisisText: {
        color: accents.crisis.accent,
    } as React.CSSProperties,
    sectionNoBorder: {
        marginBottom: "8rem",
        minHeight: "20rem",
        paddingBottom: "6rem",
        borderBottom: "none",
    } as React.CSSProperties,
    debugBtn: (color: string): React.CSSProperties => ({
        padding: "6rem 10rem",
        fontSize: "10rem",
        backgroundColor: "transparent",
        color,
        border: `1rem solid ${color}`,
        borderRadius: "3rem",
        cursor: "pointer",
        pointerEvents: "auto" as const,
        marginRight: "4rem",
        marginBottom: "4rem",
    }),
});
