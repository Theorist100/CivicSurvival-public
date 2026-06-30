/**
 * ContentPanel styles
 * Main panel with ViewMenu, Content, and Footer zones
 */

import { type Theme, type Accents, hexToRgba } from "../../../themes";

export const createContentPanelStyles = (theme: Theme, _accents: Accents) => ({
    // Main container - always full width
    container: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        width: "100%",
        height: "100%",
        background: theme.colors.surface,
    } as React.CSSProperties,

    // View menu (sub-tabs) - full width tabs
    viewMenu: {
        display: "flex",
        alignItems: "center",
        height: "40rem",
        padding: `0 ${theme.spacing.sm}`,
        background: theme.colors.paper,
        borderBottom: `2rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    viewButton: (isActive: boolean, accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: `${theme.spacing.xs} ${theme.spacing.md}`,
        background: isActive ? hexToRgba(accentColor, 0.19) : "transparent",
        borderBottom: isActive ? `4rem solid ${accentColor}` : "4rem solid transparent",
        cursor: "pointer",
        transition: `background ${theme.effects.transitionFast}, border-color ${theme.effects.transitionFast}`,
        marginRight: theme.spacing.xs,
    } as React.CSSProperties),

    viewButtonChild: {
        marginRight: theme.spacing.xs,
    } as React.CSSProperties,

    viewButtonIcon: (isActive: boolean, accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        flexShrink: 0,
        fontSize: theme.typography.sizeXS,
        color: isActive ? accentColor : theme.colors.textSecondary,
        marginRight: theme.spacing.sm,
    } as React.CSSProperties),

    viewButtonLabel: (isActive: boolean, accentColor: string) => ({
        fontSize: theme.typography.sizeXS,
        fontWeight: isActive ? theme.typography.weightBold : theme.typography.weightNormal,
        color: isActive ? accentColor : theme.colors.textSecondary,
        textTransform: "uppercase" as const,
    } as React.CSSProperties),

    // Help button [?] - flex item pushed to right with marginLeft: auto
    helpButton: {
        marginLeft: "auto",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "28rem",
        height: "28rem",
        background: theme.colors.surface,
        border: `3rem solid ${theme.colors.border}`,
        borderRadius: "50%",
        cursor: "pointer",
        fontSize: theme.typography.sizeSM,
        fontWeight: theme.typography.weightBold,
        color: theme.colors.textSecondary,
    } as React.CSSProperties,

    // Content zone (scrollable, stretch children)
    contentZone: {
        flex: 1,
        overflow: "auto" as const,
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        alignItems: "stretch" as const,
    } as React.CSSProperties,

    // Footer (fixed at bottom)
    footer: {
        height: "80rem",
        padding: theme.spacing.md,
        background: theme.colors.paper,
        borderTop: `2rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    footerContent: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
    } as React.CSSProperties,

    footerContentChild: {
        marginBottom: theme.spacing.sm,
    } as React.CSSProperties,

    footerLabel: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        textTransform: "uppercase" as const,
        marginBottom: theme.spacing.sm,
    } as React.CSSProperties,

    footerBar: {
        display: "flex",
        alignItems: "center",
    } as React.CSSProperties,

    footerBarChild: {
        marginRight: theme.spacing.md,
    } as React.CSSProperties,

    footerBarTrack: {
        flex: 1,
        height: "20rem",
        background: theme.colors.border,
        borderRadius: "10rem",
        overflow: "hidden" as const,
    } as React.CSSProperties,

    footerBarFill: (percent: number, color: string) => ({
        height: "100%",
        width: `${percent}%`,
        background: color,
        transition: "width 0.3s ease",
    } as React.CSSProperties),

    footerBarValue: (color: string) => ({
        fontSize: theme.typography.sizeMD,
        fontWeight: theme.typography.weightBold,
        color,
        fontFamily: theme.typography.fontFamilyMono,
        minWidth: "40rem",
        textAlign: "right" as const,
    } as React.CSSProperties),

    footerButton: (accentColor: string) => ({
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: `${theme.spacing.sm} ${theme.spacing.lg}`,
        background: accentColor,
        border: "none",
        borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
        fontSize: theme.typography.sizeXS,
        fontWeight: theme.typography.weightBold,
        color: theme.colors.background,
        textTransform: "uppercase" as const,
    } as React.CSSProperties),
});

// ============================================================================
// View configurations per domain
// ============================================================================

export type GridViewId = "main" | "infra";
export type WarViewId = "radar" | "warroom" | "defense" | "psyops" | "intel" | "allies";
export type ShadowViewId = "overview";
export type NewsViewId = "herald";
export type OpsViewId = "arena" | "operations" | "roadmap" | "counterattack";
export type ViewId = GridViewId | WarViewId | ShadowViewId | NewsViewId | OpsViewId;

export interface ViewConfig<TId extends ViewId = ViewId> {
    id: TId;
    label: string;
    icon: string;
}

export const GRID_VIEWS: ReadonlyArray<ViewConfig<GridViewId>> = [
    { id: "main", label: "POWER", icon: "power" },
    { id: "infra", label: "INFRA", icon: "wrench" },
];

export const WAR_VIEWS: ReadonlyArray<ViewConfig<WarViewId>> = [
    { id: "radar", label: "RADAR", icon: "radar" },
    { id: "defense", label: "DEFENSE", icon: "rocket" },
    { id: "psyops", label: "PSYOPS", icon: "brain" },
    { id: "intel", label: "INTEL", icon: "search" },
    { id: "allies", label: "ALLIES", icon: "handshake" },
];

// War Room is gated behind the GridWarfare beta wave (wave 3). Unlike GlassCase,
// a closed gate means the tab is ABSENT — appended to WAR_VIEWS only once the
// feature is open (see ContentPanel.tsx). Kept separate so the closed state never
// leaks a visible button into the menu.
export const WAR_ROOM_VIEW: ViewConfig<WarViewId> = { id: "warroom", label: "WAR ROOM", icon: "globe" };

export const SHADOW_VIEWS: ReadonlyArray<ViewConfig<ShadowViewId>> = [
    { id: "overview", label: "SHADOW OPS", icon: "schemes" },
];

export const NEWS_VIEWS: ReadonlyArray<ViewConfig<NewsViewId>> = [
    { id: "herald", label: "THE RESISTOR", icon: "news" },
];

export const OPS_VIEWS: ReadonlyArray<ViewConfig<OpsViewId>> = [
    { id: "roadmap", label: "ROADMAP", icon: "rocket" },
    { id: "arena", label: "ARENA", icon: "trophy" },
    { id: "operations", label: "OPERATIONS", icon: "globe" },
    { id: "counterattack", label: "STRIKE", icon: "target" },
];
