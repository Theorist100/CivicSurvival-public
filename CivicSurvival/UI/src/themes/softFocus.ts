/**
 * Soft Focus Theme
 * Systems Critical - Easy on the eyes
 *
 * Low contrast, muted colors for comfortable viewing.
 * Designed for users sensitive to bright/high-contrast UIs.
 */

import { type Theme } from "./types";

export const softFocusTheme: Theme = {
    name: "Soft Focus",

    colors: {
        // Base surfaces - warm dark gray (not black)
        surface: "#1a1d21",
        paper: "#252930",
        paperHover: "#2f343b",
        background: "#14161a",

        // Text - reduced contrast, warm gray
        textPrimary: "#c8ccd0",
        textSecondary: "#8a9099",
        textMuted: "#6b7280",

        // Borders - subtle
        border: "rgba(200, 204, 208, 0.15)",
        borderLight: "rgba(200, 204, 208, 0.08)",
        borderAccent: "#6b8cae",

        // Accent - muted blue-gray
        accent: "#6b8cae",
        accentDim: "#3d5066",
        accentBright: "#8aacc8",

        // Semantic colors - desaturated
        error: "#c75050",
        errorBright: "#e06060",
        success: "#5a9a6a",
        warning: "#b89040",
        neutral: "#707580",
        white: "#e0e4e8",

        // Power grid zones - muted
        zoneGreen: "#5aa060",
        zoneYellow: "#b89040",
        zoneRed: "#c75050",
        zoneCollapsed: "#a04040",

        // Corruption schemes - desaturated
        schemePurple: "#7a5a8a",
        schemePurpleBright: "#9a7aaa",
        schemeOrange: "#b08040",
        schemeOrangeBright: "#c89850",
        schemeActiveGreen: "#5a8a5a",

        // Trust levels - muted
        trustFrozen: "#c75050",
        trustLow: "#b89040",
        trustMedium: "#6a9a5a",
        trustInnerCircle: "#5a8aaa",
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
        weightBold: 600, // Slightly lighter bold
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
        borderRadius: "6rem",    // Softer rounded corners
        borderRadiusLg: "10rem",
    },

    effects: {
        // Soft glass
        glassBackground: "rgba(26, 29, 33, 0.9)",
        glassBorder: "rgba(200, 204, 208, 0.1)",

        // Subtle shadows
        shadowSm: "0 2rem 4rem rgba(0, 0, 0, 0.15)",
        shadowMd: "0 4rem 8rem rgba(0, 0, 0, 0.2)",
        shadowLg: "0 8rem 16rem rgba(0, 0, 0, 0.25)",

        // Transitions
        transitionFast: "0.15s ease",
        transitionNormal: "0.3s ease",
    },
};

// Soft accent presets - all desaturated
export const SOFT_FOCUS_ACCENTS = {
    operations: {
        name: "Operations",
        accent: "#6b8cae",      // Muted blue
        accentDim: "#3d5066",
        accentBright: "#8aacc8",
    },
    schemes: {
        name: "Schemes",
        accent: "#5a9a6a",      // Muted green
        accentDim: "#3a6a4a",
        accentBright: "#7aba8a",
    },
    resilience: {
        name: "Resilience",
        accent: "#b08050",      // Muted orange
        accentDim: "#705030",
        accentBright: "#c89868",
    },
    vip: {
        name: "VIP",
        accent: "#b8a050",      // Muted gold
        accentDim: "#786830",
        accentBright: "#d0b868",
    },
    crisis: {
        name: "Crisis",
        accent: "#b06060",      // Muted red
        accentDim: "#704040",
        accentBright: "#c88080",
    },
} as const;
