/**
 * GRID domain section styles
 * Combined styles for InfoSection, MarketSection, BackupReservesSection
 * (GridOpsSection migrated to inline styles in 422527944)
 */

import type React from "react";
import { type Theme } from "@themes";

export const createStyles = (theme: Theme) => ({
    // ============================================================================
    // LAYOUT
    // ============================================================================

    container: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        width: "580rem",
        maxHeight: "600rem",
        backgroundColor: theme.colors.background,
        border: `3rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
        overflow: "hidden",
    } as React.CSSProperties,

    header: {
        display: "flex",
        alignItems: "center",
        padding: `${theme.spacing.sm} ${theme.spacing.md}`,
        backgroundColor: theme.colors.paper,
        borderBottom: `2rem solid ${theme.colors.border}`,
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textPrimary,
        letterSpacing: "1rem",
    } as React.CSSProperties,

    content: {
        display: "flex",
        flexDirection: "row" as const,
        flex: 1,
        overflow: "hidden",
    } as React.CSSProperties,

    leftColumn: {
        width: "330rem",
        minWidth: "330rem",
        borderRight: `2rem solid ${theme.colors.border}`,
        padding: theme.spacing.md,
        overflowY: "auto" as const,
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
    } as React.CSSProperties,

    leftColumnChild: {
        marginBottom: theme.spacing.md,
    } as React.CSSProperties,

    rightColumn: {
        flex: 1,
        padding: theme.spacing.md,
        overflowY: "auto" as const,
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
    } as React.CSSProperties,

    rightColumnChild: {
        marginBottom: theme.spacing.md,
    } as React.CSSProperties,

    // ============================================================================
    // INFO PANEL - Grid Integrity
    // ============================================================================

    gridIntegrity: {
        container: {
            display: "flex",
            flexDirection: "column" as const,
            width: "100%",
            minHeight: "120rem",  // Prevent Coherent UI collapse
        } as React.CSSProperties,

        scaleLabels: {
            display: "flex",
            justifyContent: "space-between",
            fontSize: theme.typography.sizeSM,
            color: theme.colors.textPrimary,
            paddingLeft: "4rem",
            paddingRight: "4rem",
        } as React.CSSProperties,

        scaleLabel: {
            fontFamily: theme.typography.fontFamilyMono,
            fontWeight: 700,
        } as React.CSSProperties,

        barContainer: {
            width: "100%",
            minHeight: "36rem",
            padding: "2rem",
            position: "relative" as const,
            display: "flex",
            alignItems: "center",
        } as React.CSSProperties,

        barBackground: {
            width: "100%",
            height: "26rem",
            background: theme.colors.paper,
            borderRadius: "2rem",
            border: `3rem solid ${theme.colors.border}`,
            overflow: "visible",
            position: "relative" as const,
        } as React.CSSProperties,

        barFill: (widthPercent: number, bgColor: string): React.CSSProperties => ({
            height: "100%",
            borderRadius: "2rem",
            transition: "width 0.3s ease-out, background 0.3s ease-out",
            position: "relative" as const,
            zIndex: 1,
            width: `${widthPercent}%`,
            background: bgColor,
        }),

        zoneMarker: (leftPercent: number): React.CSSProperties => ({
            position: "absolute" as const,
            top: "-2rem",
            bottom: "-2rem",
            width: "2rem",
            background: theme.colors.textMuted,
            opacity: 0.8,
            // allow-pointer-events-none: decoration only
            pointerEvents: "none" as const,
            zIndex: 2,
            left: `${leftPercent}%`,
        }),

        zoneLabels: {
            display: "flex",
            justifyContent: "space-between",
            fontSize: theme.typography.sizeXS,  // Smaller font
            fontWeight: 600,
            paddingLeft: "4rem",
            paddingRight: "4rem",
            marginTop: "2rem",
        } as React.CSSProperties,

        zoneLabel: (color: string): React.CSSProperties => ({
            color,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            opacity: 0.7,  // More subtle
        }),

        statusRow: {
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginTop: theme.spacing.xs,
        } as React.CSSProperties,

        statusText: (zone: string): React.CSSProperties => {
            const color = zone === "collapsed" ? theme.colors.zoneCollapsed
                : zone === "red" ? theme.colors.zoneRed
                : zone === "yellow" ? theme.colors.zoneYellow
                : theme.colors.success;
            return {
                color,
                fontSize: theme.typography.sizeSM,
                fontWeight: 600,
                display: "flex",
                alignItems: "center",
            };
        },

        frequencyValue: (color: string): React.CSSProperties => ({
            color,
            fontSize: theme.typography.sizeMD,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            // Status text on the left can get long (collapse countdown);
            // the Hz readout must never shrink or get pushed off-panel.
            flexShrink: 0,
            whiteSpace: "nowrap" as const,
        }),
    },

    zoneColors: {
        normal: theme.colors.zoneGreen,
        warning: theme.colors.zoneYellow,
        critical: theme.colors.zoneRed,
        collapsed: theme.colors.zoneCollapsed,
    },

    // ============================================================================
    // INFO PANEL - Balance Stats
    // ============================================================================

    balance: {
        container: {
            // display: flex handled by Column component
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.borderLight,
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${theme.colors.border}`,
            minHeight: "80rem",  // Prevent Coherent UI collapse
        } as React.CSSProperties,

        row: {
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            // marginBottom removed - using Column gap instead
        } as React.CSSProperties,

        label: {
            color: theme.colors.textMuted,
            fontSize: theme.typography.sizeSM,
            textTransform: "uppercase" as const,
        } as React.CSSProperties,

        value: (color: string): React.CSSProperties => ({
            color,
            fontSize: theme.typography.sizeMD,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            textAlign: "right" as const,
            minWidth: "90rem",  // Consistent width for number alignment
        }),

        divider: {
            height: "1rem",
            backgroundColor: theme.colors.border,
            margin: `${theme.spacing.xs} 0`,
        } as React.CSSProperties,
    },

    // ============================================================================
    // INFO PANEL - Backup
    // ============================================================================

    backup: {
        container: {
            // display: flex handled by Column component
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.borderLight,
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${theme.colors.border}`,
            minHeight: "80rem",  // Prevent Coherent UI collapse
        } as React.CSSProperties,

        title: {
            fontSize: theme.typography.sizeSM,
            fontWeight: 700,
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            marginBottom: theme.spacing.xs,
        } as React.CSSProperties,

        row: {
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            // marginBottom removed - using Column gap instead
        } as React.CSSProperties,

        label: {
            color: theme.colors.textMuted,
            fontSize: theme.typography.sizeSM,
            marginRight: theme.spacing.md,  // Prevent sticking to value
        } as React.CSSProperties,

        value: (color: string): React.CSSProperties => ({
            color,
            fontSize: theme.typography.sizeMD,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            textAlign: "right" as const,
        }),

        batteryContainer: {
            display: "flex",
            alignItems: "center",
            flex: 1,
            marginLeft: theme.spacing.sm,
        } as React.CSSProperties,

        batteryBar: {
            flex: 1,
            height: "8rem",
            backgroundColor: theme.colors.border,
            borderRadius: "4rem",
            overflow: "hidden",
            marginRight: theme.spacing.sm,  // Replaces gap
        } as React.CSSProperties,

        batteryFill: (percent: number, color: string): React.CSSProperties => ({
            width: `${Math.min(100, Math.max(0, percent))}%`,
            height: "100%",
            backgroundColor: color,
            transition: `width ${theme.effects.transitionFast}`,
        }),
    },

    // ============================================================================
    // MARKET SECTION
    // ============================================================================

    market: {
        container: {
            display: "flex",
            flexDirection: "column" as const,
            padding: theme.spacing.sm,
            backgroundColor: theme.colors.borderLight,
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${theme.colors.border}`,
            position: "relative" as const,
            zIndex: 1,
            minHeight: "100rem",  // Prevent Coherent UI collapse
        } as React.CSSProperties,

        title: {
            fontSize: theme.typography.sizeSM,
            fontWeight: 700,
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
            letterSpacing: "0.5rem",
            marginBottom: theme.spacing.xs,
        } as React.CSSProperties,

        row: {
            display: "flex",
            justifyContent: "space-between",
            // Top-align: left (label/value) and right (risk/yield + presets)
            // columns differ in height; centering made rows float out of line.
            alignItems: "flex-start",
            // Single consistent separator between import and export blocks
            // (export row must NOT add its own marginTop on top of this).
            marginBottom: theme.spacing.sm,
        } as React.CSSProperties,

        info: {
            display: "flex",
            flexDirection: "column" as const,
            minHeight: 0,
        } as React.CSSProperties,

        label: {
            fontSize: theme.typography.sizeSM,
            color: theme.colors.textMuted,
        } as React.CSSProperties,

        price: {
            fontSize: theme.typography.sizeMD,
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            color: theme.colors.textPrimary,
        } as React.CSSProperties,

        button: (variant: "buy" | "sell"): React.CSSProperties => ({
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            fontSize: theme.typography.sizeSM,
            fontWeight: 700,
            border: "none",
            borderRadius: theme.layout.borderRadius,
            cursor: "pointer",
            backgroundColor: variant === "buy" ? theme.colors.zoneGreen : theme.colors.zoneYellow,
            color: theme.colors.white,
        }),
    },

    // ============================================================================
    // SECTION TITLES
    // ============================================================================

    sectionTitle: {
        fontSize: theme.typography.sizeSM,
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        marginBottom: theme.spacing.xs,
    } as React.CSSProperties,
});
