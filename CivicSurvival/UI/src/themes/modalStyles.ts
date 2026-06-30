/**
 * Theme-Aware Modal Styles
 *
 * Shared styles for all scenario modals, aware of the active theme.
 * Each theme provides its own ModalPalette via getModalPalette().
 *
 * Usage (React components):
 *   import { useModalPalette, createBaseModalStyles } from "themes";
 *   const m = useModalPalette();
 *   const base = React.useMemo(() => createBaseModalStyles(m, m.accents.crisis), [m]);
 *
 * Usage (non-component code):
 *   import { getCurrentTheme, getModalPalette } from "themes";
 *   const m = getModalPalette(getCurrentTheme());
 */

import { type Theme } from "./types";
import { hexToRgba } from "./colorUtils";
import { Z_INDEX } from "./zIndex";

// ============================================================================
// Types
// ============================================================================

export interface ModalStylesConfig {
    /** Primary accent color (border, title, buttons) */
    accentColor: string;
    /** Background darkness 0.85-0.95 */
    overlayOpacity?: number;
    /** Modal width in rem */
    width?: string;
    /** Z-index for stacking */
    zIndex?: number;
}

interface ModalSemanticVariant {
    bg: string;
    border: string;
    text: string;
}

interface ModalGridTokens {
    green: string;
    greenDark: string;
    greenBorder: string;
    greenBright: string;
    greenText: string;
    greenSubtle: string;
    greenBullet: string;
    knobOff: string;
    itemText: string;
}

export interface ModalPalette {
    // Backgrounds
    bg: string;
    bgDeep: string;
    bgDark: string;
    bgWarm: string;
    bgBlue: string;
    bgDarkBlue: string;
    bgDarkGreen: string;
    bgGray: string;

    // Text
    textPrimary: string;
    textBody: string;
    textContent: string;
    textSecondary: string;
    textMuted: string;
    textLabel: string;
    textQuote: string;
    textBlue: string;
    textAccent: string;
    textHerald: string;

    // Borders
    border: string;
    borderSubtle: string;
    borderGray: string;
    borderDark: string;
    borderBlue: string;
    borderGreen: string;

    // Semantic variants
    danger: ModalSemanticVariant;
    warning: ModalSemanticVariant;
    info: ModalSemanticVariant;
    success: ModalSemanticVariant;
    neutral: ModalSemanticVariant;

    // Progress
    progressGood: string;
    progressWarn: string;
    progressBad: string;

    // Overlays
    overlay98: string;
    overlay50: string;

    // Misc
    buttonHover: string;
    buttonHoverGlow: string;
    introGlow: string;
    gray555: string;
    gray444: string;
    gray888: string;
    grayBorder: string;
    heraldDark: string;
    heraldCardDark: string;
    heraldCardDanger: string;
    heraldSource: string;
    newsCardDark: string;

    // Global grid section (IntroModal)
    grid: ModalGridTokens;

    // Crisis border
    crisisBorder: string;

    // Modal semantic accents
    accents: {
        crisis: string;
        warning: string;
        info: string;
        success: string;
    };
}

// ============================================================================
// Tech Noir — dark blue cinematic (default)
// ============================================================================

const TECH_NOIR_MODAL: ModalPalette = {
    bg: "#0d1520",
    bgDeep: "#0a0a10",
    bgDark: "#0a0a0a",
    bgWarm: "#1a1510",
    bgBlue: "#1a2a4a",
    bgDarkBlue: "#101520",
    bgDarkGreen: "rgba(40, 60, 40, 0.3)",
    bgGray: "#333",

    textPrimary: "#ffffff",
    textBody: "#aabbcc",
    textContent: "#cccccc",
    textSecondary: "#999999",
    textMuted: "#8899aa",
    textLabel: "#6688aa",
    textQuote: "#99aacc",
    textBlue: "#6699cc",
    textAccent: "#ffaa44",
    textHerald: "#b0a090",

    border: "#2a3a4a",
    borderSubtle: "#223344",
    borderGray: "#555",
    borderDark: "#333333",
    borderBlue: "#335588",
    borderGreen: "#3a5a3a",

    danger:  { bg: "rgba(244, 67, 54, 0.2)",  border: "#c41e1e", text: "#e57373" },
    warning: { bg: "rgba(255, 152, 0, 0.2)",  border: "#ff9800", text: "#ffb74d" },
    info:    { bg: "rgba(68, 136, 204, 0.2)",  border: "#4488cc", text: "#6699cc" },
    success: { bg: "rgba(76, 175, 80, 0.2)",   border: "#4caf50", text: "#81c784" },
    neutral: { bg: "rgba(128, 128, 128, 0.2)", border: "#666666", text: "#999999" },

    progressGood: "#4488cc",
    progressWarn: "#ff9944",
    progressBad: "#cc4444",

    overlay98: "rgba(0, 0, 0, 0.98)",
    overlay50: "rgba(0, 0, 0, 0.5)",

    buttonHover: "#e62222",
    buttonHoverGlow: "0 0 48rem rgba(230, 34, 34, 0.7)",
    introGlow: "0 0 32rem rgba(196, 30, 30, 0.5)",
    gray555: "#555",
    gray444: "#444",
    gray888: "#888",
    grayBorder: "#222",
    heraldDark: "#0e0e0c",
    heraldCardDark: "#0c0c0c",
    heraldCardDanger: "#1a0505",
    heraldSource: "#555",
    newsCardDark: "#200505",

    grid: {
        green: "#4a7c4a",
        greenDark: "#3a6a3a",
        greenBorder: "#4a8a4a",
        greenBright: "#6aba6a",
        greenText: "#aaccaa",
        greenSubtle: "#6a8a6a",
        greenBullet: "#5a8a5a",
        knobOff: "#777",
        itemText: "#aaa",
    },

    crisisBorder: "#ff4444",

    accents: {
        crisis: "#c41e1e",
        warning: "#ff6600",
        info: "#4488cc",
        success: "#44aa44",
    },
};

// ============================================================================
// Classic Gold — warm brown/amber tones
// ============================================================================

const CLASSIC_GOLD_MODAL: ModalPalette = {
    bg: "#120e08",
    bgDeep: "#0a0806",
    bgDark: "#0a0a08",
    bgWarm: "#1a1408",
    bgBlue: "#2a2010",
    bgDarkBlue: "#181208",
    bgDarkGreen: "rgba(50, 50, 20, 0.3)",
    bgGray: "#2a2418",

    textPrimary: "#f0e8d0",
    textBody: "#c0a888",
    textContent: "#ccbb99",
    textSecondary: "#908060",
    textMuted: "#887050",
    textLabel: "#a09060",
    textQuote: "#b8a878",
    textBlue: "#c0a040",
    textAccent: "#ffd700",
    textHerald: "#b0a080",

    border: "#3a2a18",
    borderSubtle: "#2a1e10",
    borderGray: "#4a3a28",
    borderDark: "#2a2218",
    borderBlue: "#5a4820",
    borderGreen: "#3a4a18",

    danger:  { bg: "rgba(200, 60, 40, 0.2)",  border: "#c43020", text: "#e08060" },
    warning: { bg: "rgba(200, 140, 0, 0.2)",  border: "#cc8800", text: "#e0b040" },
    info:    { bg: "rgba(180, 150, 40, 0.2)",  border: "#c0a030", text: "#d4b850" },
    success: { bg: "rgba(76, 140, 40, 0.2)",   border: "#4a8828", text: "#80b850" },
    neutral: { bg: "rgba(120, 100, 60, 0.2)", border: "#685830", text: "#908060" },

    progressGood: "#c0a030",
    progressWarn: "#cc8800",
    progressBad: "#c44030",

    overlay98: "rgba(8, 6, 2, 0.98)",
    overlay50: "rgba(8, 6, 2, 0.52)",

    buttonHover: "#cc3020",
    buttonHoverGlow: "0 0 48rem rgba(200, 48, 32, 0.6)",
    introGlow: "0 0 32rem rgba(196, 30, 30, 0.4)",
    gray555: "#4a3a28",
    gray444: "#3a2a18",
    gray888: "#807060",
    grayBorder: "#1e1810",
    heraldDark: "#100e08",
    heraldCardDark: "#0e0c08",
    heraldCardDanger: "#1a0c05",
    heraldSource: "#685838",
    newsCardDark: "#200c05",

    grid: {
        green: "#5a7a30",
        greenDark: "#4a6828",
        greenBorder: "#5a8a30",
        greenBright: "#80aa40",
        greenText: "#b0c880",
        greenSubtle: "#6a8838",
        greenBullet: "#5a7a30",
        knobOff: "#685838",
        itemText: "#a09070",
    },

    crisisBorder: "#ee4030",

    accents: {
        crisis: "#c41e1e",
        warning: "#cc8800",
        info: "#c0a030",
        success: "#44aa44",
    },
};

// ============================================================================
// Soft Focus — muted/desaturated neutral
// ============================================================================

const SOFT_FOCUS_MODAL: ModalPalette = {
    bg: "#141418",
    bgDeep: "#101014",
    bgDark: "#101012",
    bgWarm: "#181618",
    bgBlue: "#1a1e28",
    bgDarkBlue: "#121418",
    bgDarkGreen: "rgba(40, 50, 40, 0.25)",
    bgGray: "#282830",

    textPrimary: "#d0d0d8",
    textBody: "#a0a0a8",
    textContent: "#b0b0b8",
    textSecondary: "#787880",
    textMuted: "#686878",
    textLabel: "#6878a0",
    textQuote: "#8888a0",
    textBlue: "#5a7ca0",
    textAccent: "#b89050",
    textHerald: "#8a8880",

    border: "#2e3038",
    borderSubtle: "#242830",
    borderGray: "#404048",
    borderDark: "#282830",
    borderBlue: "#384060",
    borderGreen: "#304030",

    danger:  { bg: "rgba(180, 60, 50, 0.15)", border: "#a04040", text: "#c08080" },
    warning: { bg: "rgba(180, 120, 40, 0.15)", border: "#b07030", text: "#c0a060" },
    info:    { bg: "rgba(80, 110, 150, 0.15)", border: "#5a7ca0", text: "#7898b8" },
    success: { bg: "rgba(60, 130, 60, 0.15)",  border: "#4a8a4a", text: "#70a870" },
    neutral: { bg: "rgba(100, 100, 110, 0.15)", border: "#505058", text: "#787880" },

    progressGood: "#5a7ca0",
    progressWarn: "#b08040",
    progressBad: "#a05050",

    overlay98: "rgba(10, 10, 14, 0.98)",
    overlay50: "rgba(10, 10, 14, 0.52)",

    buttonHover: "#a03838",
    buttonHoverGlow: "0 0 48rem rgba(160, 56, 56, 0.5)",
    introGlow: "0 0 32rem rgba(160, 40, 40, 0.35)",
    gray555: "#404048",
    gray444: "#343438",
    gray888: "#707078",
    grayBorder: "#1e1e22",
    heraldDark: "#101014",
    heraldCardDark: "#0e0e12",
    heraldCardDanger: "#180a0a",
    heraldSource: "#484850",
    newsCardDark: "#1c0a0a",

    grid: {
        green: "#406840",
        greenDark: "#305830",
        greenBorder: "#408040",
        greenBright: "#58a858",
        greenText: "#90b090",
        greenSubtle: "#587858",
        greenBullet: "#487848",
        knobOff: "#606068",
        itemText: "#909098",
    },

    crisisBorder: "#c05050",

    accents: {
        crisis: "#a04040",
        warning: "#b07030",
        info: "#5a7ca0",
        success: "#4a8a4a",
    },
};

// ============================================================================
// Palette Selection
// ============================================================================

/**
 * Get modal palette for the given theme.
 * In React components, prefer useModalPalette() hook from "themes".
 */
export function getModalPalette(theme: Theme): ModalPalette {
    switch (theme.name) {
        case "Classic Gold": return CLASSIC_GOLD_MODAL;
        case "Soft Focus": return SOFT_FOCUS_MODAL;
        default: return TECH_NOIR_MODAL;
    }
}

// ============================================================================
// Base Modal Styles Factory
// ============================================================================

/**
 * Creates base modal styles with consistent look across all scenario modals.
 *
 * @param m - Modal palette from useModalPalette()
 * @param config - Accent color string or full config object
 */
export const createBaseModalStyles = (m: ModalPalette, config: ModalStylesConfig | string) => {
    // Allow simple string for just accent color
    const cfg: ModalStylesConfig = typeof config === "string"
        ? { accentColor: config } // eslint-disable-line civic/no-coherent-unsupported-prop -- config field, not CSS
        : config;

    const {
        accentColor,
        overlayOpacity = 0.9,
        width = "480rem",
        zIndex = Z_INDEX.modal,
    } = cfg;

    return {
        // ===== OVERLAY =====
        overlay: {
            position: "fixed" as const,
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: `rgba(0, 0, 0, ${overlayOpacity})`,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            // Vertical margin for the modal: the modal caps itself at
            // maxHeight:100%, which resolves against this padded content box.
            padding: "20rem 0",
            zIndex,
            pointerEvents: "auto" as const,
        } as React.CSSProperties,

        // ===== MODAL CONTAINER =====
        modal: {
            width,
            maxWidth: "900rem",
            // Cap to the overlay's content box: overlay is fixed top/bottom:0
            // (= viewport) with 20rem vertical padding, so 100% here is
            // viewport minus 40rem and a tall modal never spills off-screen.
            // NOT calc(100% - 40rem): cohtml cannot combine % with other unit
            // types in calc() — the unbuildable expression leaves a null node
            // that the layout thread dereferences, crashing the game
            // (CIVIC-UI-052). body keeps overflowY:auto as a safety net.
            maxHeight: "100%",
            backgroundColor: m.bgDark,
            border: `2rem solid ${accentColor}`,
            borderRadius: "8rem",
            boxShadow: `0 0 40rem ${hexToRgba(accentColor, 0.25)}`,
            overflow: "hidden",
            display: "flex",
            flexDirection: "column" as const,
            minHeight: 0,
            fontFamily: "'Perfect DOS VGA 437', 'Noto Sans', monospace",
            pointerEvents: "auto" as const,
        } as React.CSSProperties,

        // ===== HEADER =====
        header: {
            padding: "24rem",
            textAlign: "center" as const,
            borderBottom: `1rem solid ${accentColor}`,
            background: `linear-gradient(180deg, ${hexToRgba(accentColor, 0.12)} 0%, transparent 100%)`, // eslint-disable-line civic/no-linear-gradient -- intentional header gradient
        } as React.CSSProperties,

        headerIcon: {
            fontSize: "48rem",
            marginBottom: "16rem",
            display: "block",
        } as React.CSSProperties,

        title: {
            color: accentColor,
            fontSize: "24rem",
            fontWeight: "bold" as const,
            margin: 0,
            letterSpacing: "2rem",
            textTransform: "uppercase" as const,
        } as React.CSSProperties,

        subtitle: {
            color: hexToRgba(accentColor, 0.6),
            fontSize: "14rem",
            marginTop: "8rem",
            fontStyle: "italic" as const,
        } as React.CSSProperties,

        // ===== BODY =====
        body: {
            padding: "28rem",
            flex: "1",
            minHeight: 0,
            overflowY: "auto" as const,
        } as React.CSSProperties,

        text: {
            color: m.textContent,
            fontSize: "14rem",
            lineHeight: 1.8,
            marginBottom: "20rem",
        } as React.CSSProperties,

        textSecondary: {
            color: m.textSecondary,
            fontSize: "13rem",
            lineHeight: 1.7,
            fontStyle: "italic" as const,
        } as React.CSSProperties,

        textItalicDimmed: {
            color: m.textContent,
            fontSize: "14rem",
            lineHeight: 1.8,
            marginBottom: "20rem",
            fontStyle: "italic" as const,
            opacity: 0.8,
        } as React.CSSProperties,

        highlight: {
            color: accentColor,
            fontWeight: "bold" as const,
        } as React.CSSProperties,

        divider: {
            height: "1rem",
            backgroundColor: m.borderDark,
            margin: "20rem 0",
        } as React.CSSProperties,

        // ===== BUTTONS =====
        buttonContainer: {
            display: "flex",
            justifyContent: "center",
            paddingTop: "20rem",
            borderTop: `1rem solid ${m.borderDark}`,
        } as React.CSSProperties,

        buttonContainerChild: {
            marginRight: "16rem",
        } as React.CSSProperties,

        primaryButton: {
            padding: "14rem 40rem",
            backgroundColor: accentColor,
            border: "none",
            borderRadius: "6rem",
            color: "#000000",
            fontSize: "14rem",
            fontWeight: "bold" as const,
            fontFamily: "'Perfect DOS VGA 437', 'Noto Sans', monospace",
            textTransform: "uppercase" as const,
            letterSpacing: "1rem",
            cursor: "pointer",
            transition: "background-color 0.2s ease, border-color 0.2s ease, opacity 0.2s ease",
        } as React.CSSProperties,

        secondaryButton: {
            padding: "14rem 32rem",
            backgroundColor: "transparent",
            border: `2rem solid ${accentColor}`,
            borderRadius: "6rem",
            color: accentColor,
            fontSize: "14rem",
            fontWeight: "bold" as const,
            fontFamily: "'Perfect DOS VGA 437', 'Noto Sans', monospace",
            textTransform: "uppercase" as const,
            letterSpacing: "1rem",
            cursor: "pointer",
            transition: "background-color 0.2s ease, border-color 0.2s ease, opacity 0.2s ease",
        } as React.CSSProperties,
    };
};
