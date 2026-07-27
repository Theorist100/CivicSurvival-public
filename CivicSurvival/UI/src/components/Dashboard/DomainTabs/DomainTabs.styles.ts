/**
 * DomainTabs styles
 * Main domain navigation: GRID, NEWS, GLOBAL OPS, HYBRID OPS, SHADOW
 */

import { type Theme, type Accents, hexToRgba } from "../../../themes";
import { type FeatureId } from "../../../types/semantic";
import { type TranslationKey } from "../../../locales";

export type DomainId = "grid" | "news" | "war" | "warroom" | "shadow" | "arena";

export interface DomainConfig {
    id: DomainId;
    /** Localization key — the label is resolved at render time, never stored as text. */
    labelKey: TranslationKey;
    icon: string;
    accent: keyof Accents;
    /**
     * Feature ids backing this tab. The tab is rendered as locked when ANY
     * of these features is closed in the active manifest (Phase 6 — replaces
     * the old static `isLocked` field). Empty / missing means the tab is
     * always open.
     */
    featureIds?: readonly FeatureId[];
}

export const DOMAINS: DomainConfig[] = [
    { id: "grid", labelKey: "UI_TAB_GRID", icon: "power", accent: "operations" },
    { id: "war", labelKey: "UI_TAB_HYBRID_OPS", icon: "shield", accent: "crisis" },
    {
        // Native home of the (formerly announcement-only) War Room: a compact
        // command summary view with the jump into the fullscreen overlay.
        id: "warroom",
        labelKey: "UI_TAB_WAR_ROOM",
        icon: "swords",
        accent: "crisis",
    },
    {
        id: "shadow",
        labelKey: "UI_TAB_SHADOW",
        icon: "money",
        accent: "schemes",
        // Inner-gated: sections independently depend on ShadowEconomy,
        // Corruption, Diplomacy and Countermeasures.
    },
    {
        id: "news",
        labelKey: "UI_TAB_NEWS",
        icon: "news",
        accent: "operations",
        // Inner-gated: Herald/Chipper sections depend on Network/Narrative.
    },
    {
        // Also hosts the GLOBAL OPS teaser as a subview (seasonal opt-in
        // events, ComingSoonOverlay) until that feature ships and earns its
        // own top-level tab again. The former GLOBAL OPS domain (ROADMAP +
        // OPERATIONS) was retired: 8 tabs overflowed the row, and roadmap
        // announcements live in Discord.
        id: "arena",
        labelKey: "UI_TAB_ARENA",
        icon: "trophy",
        accent: "resilience",
    },
];

export const createDomainTabsStyles = (theme: Theme, accents: Accents) => ({
    container: {
        display: "flex",
        alignItems: "center",
        height: "40rem",
        padding: `0 ${theme.spacing.md}`,
        background: theme.colors.paper,
        borderBottom: `2rem solid ${theme.colors.border}`,
        boxSizing: "border-box" as const,
        minWidth: 0,
        overflow: "hidden" as const,
    } as React.CSSProperties,

    tab: (isActive: boolean, accentName: keyof Accents) => {
        const accent = accents[accentName];
        return {
            display: "flex",
            alignItems: "center",
            flex: "0 1 auto",
            minWidth: 0,
            padding: `${theme.spacing.sm} ${theme.spacing.md}`,
            background: isActive ? accent.accentDim : "transparent",
            border: isActive ? `3rem solid ${accent.accent}` : "3rem solid transparent",
            borderRadius: theme.layout.borderRadius,
            cursor: "pointer",
            transition: `background ${theme.effects.transitionFast}, border-color ${theme.effects.transitionFast}`,
            marginRight: theme.spacing.sm,  // Replaces container gap
            whiteSpace: "nowrap" as const,
        } as React.CSSProperties;
    },

    tabHover: (accentName: keyof Accents) => {
        const accent = accents[accentName];
        return {
            background: hexToRgba(accent.accent, 0.12),
        } as React.CSSProperties;
    },

    tabIcon: (isActive: boolean, accentName: keyof Accents) => {
        const accent = accents[accentName];
        return {
            fontSize: theme.typography.sizeMD,
            color: isActive ? accent.accent : theme.colors.textSecondary,
            marginRight: theme.spacing.sm,  // Replaces gap in tab
        } as React.CSSProperties;
    },

    tabLabel: (isActive: boolean, accentName: keyof Accents) => {
        const accent = accents[accentName];
        return {
            fontSize: theme.typography.sizeXS,
            fontWeight: isActive ? theme.typography.weightBold : theme.typography.weightMedium,
            color: isActive ? accent.accent : theme.colors.textSecondary,
            textTransform: "uppercase" as const,
            letterSpacing: "0.05rem",
            overflow: "hidden" as const,
            textOverflow: "ellipsis",
            whiteSpace: "nowrap" as const,
        } as React.CSSProperties;
    },

    // Locked tab — feature is closed in the active manifest. Visually
    // muted, no hover affordance, "not-allowed" cursor. The "SOON" badge
    // is layered on top by the consumer (DomainTabs.tsx).
    tabLocked: () => {
        return {
            display: "flex",
            alignItems: "center",
            flex: "0 1 auto",
            minWidth: 0,
            padding: `${theme.spacing.sm} ${theme.spacing.md}`,
            background: "transparent",
            border: `3rem solid ${hexToRgba(theme.colors.textMuted, 0.25)}`,
            borderRadius: theme.layout.borderRadius,
            cursor: "not-allowed",
            opacity: 0.45,
            WebkitFilter: "grayscale(40%)",
            filter: "grayscale(40%)",
            transition: "none",
            marginRight: theme.spacing.sm,  // Replaces container gap
            whiteSpace: "nowrap" as const,
        } as React.CSSProperties;
    },

    // Hover on locked tab is a no-op: do NOT brighten or bump opacity.
    // The locked state must not suggest interactivity. Returning an empty
    // object keeps the spread safe in DomainTabs.tsx.
    tabLockedHover: () => {
        return {} as React.CSSProperties;
    },

    tabIconLocked: {
        fontSize: theme.typography.sizeMD,
        color: theme.colors.textMuted,
        marginRight: theme.spacing.sm,
    } as React.CSSProperties,

    tabLabelLocked: {
        fontSize: theme.typography.sizeXS,
        fontWeight: theme.typography.weightMedium,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        letterSpacing: "0.05rem",
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    } as React.CSSProperties,

    tabLockedBadge: {
        fontSize: "10rem",
        fontWeight: theme.typography.weightBold,
        color: theme.colors.textMuted,
        background: hexToRgba(theme.colors.textMuted, 0.19),
        padding: "2rem 6rem",
        borderRadius: "4rem",
        marginLeft: theme.spacing.xs,
        textTransform: "uppercase" as const,
    } as React.CSSProperties,

    betaBadge: {
        flex: "0 0 auto",
        fontSize: "10rem",
        fontWeight: theme.typography.weightBold,
        color: accents.resilience.accent,
        background: hexToRgba(accents.resilience.accent, 0.12),
        border: `1rem solid ${accents.resilience.accent}`,
        padding: "2rem 6rem",
        borderRadius: "4rem",
        textTransform: "uppercase" as const,
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties,

    // Same footprint as betaBadge (it takes the badge's slot while an update announce is
    // active), crisis accent instead of the calm resilience one — it is a countdown.
    updateBadge: {
        flex: "0 0 auto",
        fontSize: "10rem",
        fontWeight: theme.typography.weightBold,
        color: accents.crisis.accent,
        background: hexToRgba(accents.crisis.accent, 0.12),
        border: `1rem solid ${accents.crisis.accent}`,
        padding: "2rem 6rem",
        borderRadius: "4rem",
        textTransform: "uppercase" as const,
        whiteSpace: "nowrap" as const,
    } as React.CSSProperties,

    spacer: {
        flex: "1 1 8rem",
        minWidth: "8rem",
    } as React.CSSProperties,
});
