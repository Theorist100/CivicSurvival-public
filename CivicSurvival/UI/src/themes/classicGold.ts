/**
 * Classic Gold Theme
 * Systems Critical - Original Power Utility Style
 *
 * Gold-on-dark aesthetic for power grid management.
 * The "official" look before the corruption mechanics.
 */

import { type Theme } from "./types";

export const classicGoldTheme: Theme = {
    name: "Classic Gold",

    colors: {
        // Base surfaces - slightly warmer dark
        surface: "#0a0a14",
        paper: "#1a1a2e",
        paperHover: "#252540",
        background: "#000000",

        // Text - warm white
        textPrimary: "#f0f0e8",
        textSecondary: "#a0a090",
        textMuted: "#908070",

        // Borders - gold tinted
        border: "rgba(255, 215, 0, 0.25)",
        borderLight: "rgba(255, 215, 0, 0.12)",
        borderAccent: "#ffd700",

        // Accent - gold everywhere
        accent: "#ffd700",
        accentDim: "#997a00",
        accentBright: "#ffe54c",

        // Semantic colors
        error: "#ff4444",
        errorBright: "#ff0000",
        success: "#4ade80",
        warning: "#ffaa00",
        neutral: "#888888",
        white: "#ffffff",

        // Power grid zones
        zoneGreen: "#44ff44",
        zoneYellow: "#ffaa00",
        zoneRed: "#ff4444",
        zoneCollapsed: "#ff0000",

        // Corruption schemes
        schemePurple: "#9C27B0",
        schemePurpleBright: "#CE93D8",
        schemeOrange: "#FF9800",
        schemeOrangeBright: "#FFB74D",
        schemeActiveGreen: "#4CAF50",

        // Trust levels
        trustFrozen: "#ff4444",       // < 25 - Frozen trust (red)
        trustLow: "#ffaa00",          // 25-50 - Low trust (orange)
        trustMedium: "#88cc44",       // 50-75 - Medium trust (green)
        trustInnerCircle: "#44ddff",  // 75+ - Inner circle (cyan)
    },

    typography: {
        // Font families
        // Only fonts shipped in Content\Game\UI\Fonts resolve in GameFace; unknown families
        // fall back to an engine font that lacks U+2014 (em dash) and other glyphs
        fontFamily: "'Noto Sans', Overpass, sans-serif",
        // Perfect DOS VGA 437 is the only monospace the game ships; Noto Sans next
        // covers glyphs it lacks (Cyrillic, dashes)
        fontFamilyMono: "'Perfect DOS VGA 437', 'Noto Sans', monospace",

        // Font sizes (CS2 rem = 1px, not CSS rem)
        // T-shirt sizing - larger for game UI readability
        sizeXS: "14rem",      // Minimum readable (badges, annotations)
        sizeSM: "16rem",      // Labels, secondary text
        sizeMD: "18rem",      // Body text, main content
        sizeLG: "22rem",      // Section titles
        sizeXL: "28rem",      // Headers, big numbers

        // Weights
        weightNormal: 400,
        weightMedium: 500,
        weightBold: 700,
    },

    spacing: {
        xs: "4rem",
        sm: "8rem",
        md: "12rem",
        lg: "16rem",
        xl: "24rem",
        xxl: "32rem",
    },

    layout: {
        panelWidth: "500rem",
        panelMinWidth: "420rem",
        panelMaxWidth: "600rem",
        borderRadius: "8rem",    // More rounded than Tech Noir
        borderRadiusLg: "12rem",
    },

    effects: {
        // Semi-transparent overlay (matching techNoir/softFocus pattern)
        glassBackground: "rgba(26, 26, 46, 0.85)",
        glassBorder: "#ffd700",

        // Gold glow shadows
        shadowSm: "0 2rem 4rem rgba(255, 215, 0, 0.1)",
        shadowMd: "0 4rem 8rem rgba(255, 215, 0, 0.15)",
        shadowLg: "0 8rem 16rem rgba(255, 215, 0, 0.2)",

        // Transitions
        transitionFast: "0.15s ease",
        transitionNormal: "0.3s ease",
    },
};

// Gold-specific accent presets (overrides for this theme)
export const CLASSIC_GOLD_ACCENTS = {
    operations: {
        name: "Operations",
        accent: "#ffd700",      // Gold
        accentDim: "#997a00",
        accentBright: "#ffe54c",
    },
    schemes: {
        name: "Schemes",
        accent: "#44ff88",      // Green (money)
        accentDim: "#1a6633",
        accentBright: "#88ffaa",
    },
    resilience: {
        name: "Resilience",
        accent: "#ffaa00",      // Orange
        accentDim: "#995500",
        accentBright: "#ffcc44",
    },
    vip: {
        name: "VIP",
        accent: "#ffd700",      // Gold
        accentDim: "#997a00",
        accentBright: "#ffe54c",
    },
    crisis: {
        name: "Crisis",
        accent: "#ff4444",      // Red
        accentDim: "#992222",
        accentBright: "#ff6666",
    },
} as const;
