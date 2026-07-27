/**
 * Dashboard styles - "Power Block" concept
 * Wide semi-transparent panel with all controls inside
 */

import { type Theme } from "../../themes";
import { Z_INDEX } from "../../themes";
import { type DomainId } from "./DomainTabs/DomainTabs";

// Power Block dimensions by domain
const DOMAIN_WIDTHS: Record<DomainId, number> = {
    grid: 820,      // PowerDashboard: 280 left + 520 right
    news: 820,      // Chipper (280 left) + The Resistor (540 right)
    war: 820,       // Threat List (250 left) + Radar (570 right)
    warroom: 820,   // War Room summary: axis chips + launch control + intel
    shadow: 820,    // Schemes: split layout already implemented
    arena: 820,     // Ladders + the Global Ops teaser subview
};
// Two heights, not three. Every tab is 750; only the radar, which draws a map, gets more.
// A third height for Defense/Intel/Counterattack meant the panel changed size while the player
// merely walked along the sub-tabs — 50rem of animated jump between neighbours.
const BLOCK_HEIGHT = 750;        // Every view
const BLOCK_HEIGHT_RADAR = 820;  // Radar view — the map wants the extra band
const BLOCK_MIN_WIDTH = 200;     // Minimized widget width
const TOP_OFFSET = "60rem";      // Below vanilla top bar
const RIGHT_OFFSET = "10rem";    // Margin from right edge

/** Which of the two panel heights a view asks for. */
export type ViewHeight = "radar" | "default";

export const createDashboardStyles = (theme: Theme) => ({
    // Root container - dynamic size based on domain and view
    // viewType: "radar" | "default"
    root: (domain: DomainId = "grid", viewType: ViewHeight = "default") => {
        const width = DOMAIN_WIDTHS[domain];
        const height = viewType === "radar" ? BLOCK_HEIGHT_RADAR : BLOCK_HEIGHT;
        return {
            display: "flex",
            flexDirection: "column" as const,
            minHeight: 0,
            position: "fixed" as const,
            top: TOP_OFFSET,
            right: RIGHT_OFFSET,
            width: `${width}rem`,
            height: `${height}rem`,
            background: theme.effects.glassBackground,
            borderRadius: theme.layout.borderRadius,
            border: `1rem solid ${theme.colors.border}`,
            boxShadow: theme.effects.shadowLg,
            pointerEvents: "auto" as const,
            zIndex: Z_INDEX.raised,
            overflow: "hidden" as const,
            transition: "width 0.2s ease, height 0.2s ease",
        } as React.CSSProperties;
    },

    // Minimized state - small widget
    rootMinimized: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        position: "fixed" as const,
        top: TOP_OFFSET,
        right: RIGHT_OFFSET,
        width: `${BLOCK_MIN_WIDTH}rem`,
        height: "80rem",
        background: theme.effects.glassBackground,
        borderRadius: theme.layout.borderRadius,
        border: `1rem solid ${theme.colors.border}`,
        boxShadow: theme.effects.shadowLg,
        pointerEvents: "auto" as const,
        zIndex: Z_INDEX.raised,
        cursor: "pointer",
    } as React.CSSProperties,

    // Minimize button
    minimizeButton: {
        position: "absolute" as const,
        top: "8rem",
        right: "8rem",
        width: "24rem",
        height: "24rem",
        background: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: "4rem",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        cursor: "pointer",
        fontSize: "14rem",
        color: theme.colors.textMuted,
        zIndex: 10,
    } as React.CSSProperties,

    // Header zone (GlobalStatus + DomainTabs) - inside Power Block
    header: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        borderBottom: `2rem solid ${theme.colors.border}`,
    } as React.CSSProperties,

    // Main content area
    main: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        flex: 1,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    // Content wrapper
    sidebarWrapper: {
        height: "100%",
        width: "100%",
        overflow: "hidden" as const,
    } as React.CSSProperties,

});
