/**
 * Tech Noir Theme
 * Systems Critical - Default Theme
 *
 * "Situation Room" - приборная панель в бункере теневого правительства
 */

import { type Theme, ACCENT_PRESETS } from "./types";

export const techNoirTheme: Theme = {
    name: "Tech Noir",

    colors: {
        // Base surfaces - матовый тёмный
        surface: "#121212",
        paper: "#1E1E1E",
        paperHover: "#2A2A2A",
        background: "#000000",

        // Text - не чисто белый, чтобы не выжигал глаза
        textPrimary: "#E0E0E0",
        textSecondary: "#A0A0A0",
        textMuted: "#999999",

        // Borders
        border: "rgba(255, 255, 255, 0.12)",
        borderLight: "rgba(255, 255, 255, 0.06)",
        borderAccent: ACCENT_PRESETS.operations.accent, // Default

        // Accent (will be overridden by tab)
        accent: ACCENT_PRESETS.operations.accent,
        accentDim: ACCENT_PRESETS.operations.accentDim,
        accentBright: ACCENT_PRESETS.operations.accentBright,

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
        borderRadius: "2rem",    // Sharp edges - брутализм
        borderRadiusLg: "4rem",
    },

    effects: {
        // Glassmorphism
        glassBackground: "rgba(18, 18, 18, 0.85)",
        glassBorder: "rgba(255, 255, 255, 0.1)",

        // Shadows
        shadowSm: "0 2rem 4rem rgba(0, 0, 0, 0.3)",
        shadowMd: "0 4rem 8rem rgba(0, 0, 0, 0.4)",
        shadowLg: "0 8rem 16rem rgba(0, 0, 0, 0.5)",

        // Transitions
        transitionFast: "0.15s ease",
        transitionNormal: "0.3s ease",
    },
};
