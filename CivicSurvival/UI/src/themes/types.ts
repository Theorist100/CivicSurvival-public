/**
 * Theme System Types
 * Systems Critical - Tech Noir UI
 */

// ============================================================================
// Color Tokens
// ============================================================================

export interface ColorTokens {
    // Base surfaces
    surface: string;      // Main background (#121212)
    paper: string;        // Panel background (#1E1E1E)
    paperHover: string;   // Hover state (#2A2A2A)
    background: string;   // Darkest background (for contrast text on light buttons)

    // Text
    textPrimary: string;  // Main text (#E0E0E0)
    textSecondary: string; // Labels (#A0A0A0)
    textMuted: string;    // Disabled (#666666)

    // Borders
    border: string;       // Default border
    borderLight: string;  // Subtle separator
    borderAccent: string; // Colored border (uses accent)

    // Accent (changes per tab)
    accent: string;       // Primary accent color
    accentDim: string;    // Dimmed accent (for backgrounds)
    accentBright: string; // Bright accent (for highlights)

    // Semantic colors
    error: string;        // Error/danger (#ff4444)
    errorBright: string;  // Critical error (#ff0000)
    success: string;      // Success/OK (#4ade80)
    warning: string;      // Warning (#ffaa00)
    neutral: string;      // Neutral/disabled (#888888)
    white: string;        // Pure white (#ffffff)

    // Power grid zones (for PowerStats, GridIntegrityBar)
    zoneGreen: string;     // Normal zone (#44ff44)
    zoneYellow: string;    // Warning zone (#ffaa00)
    zoneRed: string;       // Critical zone (#ff4444)
    zoneCollapsed: string; // Collapsed zone (#ff0000)

    // Corruption schemes (for RegulatoryOverride)
    schemePurple: string;       // Purple scheme (#9C27B0)
    schemePurpleBright: string; // Purple highlight (#CE93D8)
    schemeOrange: string;       // Orange scheme (#FF9800)
    schemeOrangeBright: string; // Orange highlight (#FFB74D)
    schemeActiveGreen: string;  // Active state (#4CAF50)

    // Trust levels (for Toast notifications, Corruption contacts)
    trustFrozen: string;       // < 25 - Frozen trust (#ff4444)
    trustLow: string;          // 25-50 - Low trust (#ffaa00)
    trustMedium: string;       // 50-75 - Medium trust (#88cc44)
    trustInnerCircle: string;  // 75+ - Inner circle (#44ddff)
}

// ============================================================================
// Accent Presets
// ============================================================================

export interface AccentPreset {
    name: string;
    accent: string;
    accentDim: string;
    accentBright: string;
}

export const ACCENT_PRESETS = {
    operations: {
        name: "Operations",
        accent: "#2979FF",      // Tech Blue
        accentDim: "#1a4a99",
        accentBright: "#64B5F6",
    },
    schemes: {
        name: "Schemes",
        accent: "#00E676",      // Toxic Green
        accentDim: "#00994d",
        accentBright: "#69F0AE",
    },
    resilience: {
        name: "Resilience",
        accent: "#FF9100",      // Safety Orange
        accentDim: "#995700",
        accentBright: "#FFAB40",
    },
    vip: {
        name: "VIP",
        accent: "#FFD700",      // Gold
        accentDim: "#997a00",
        accentBright: "#FFE54C",
    },
    crisis: {
        name: "Crisis",
        accent: "#FF1744",      // Alarm Red
        accentDim: "#990d29",
        accentBright: "#FF5252",
    },
} as const;

export type AccentPresetName = keyof typeof ACCENT_PRESETS;

/** Reusable Accents type alias — import this instead of redefining locally */
export type Accents = Record<AccentPresetName, AccentPreset>;

// ============================================================================
// Typography
// ============================================================================

export interface TypographyTokens {
    // Font families
    fontFamily: string;
    fontFamilyMono: string;  // For numbers

    // Font sizes (CS2 rem = 1px, NOT CSS rem where 1rem = 16px)
    // T-shirt sizing for flexibility
    sizeXS: string;      // 14rem - Minimum readable (badges, tiny annotations)
    sizeSM: string;      // 16rem - Labels, secondary text
    sizeMD: string;      // 18rem - Body text, main content
    sizeLG: string;      // 22rem - Section titles
    sizeXL: string;      // 28rem - Headers, big numbers

    // Font weights
    weightNormal: number;
    weightMedium: number;
    weightBold: number;
}

// ============================================================================
// Spacing
// ============================================================================

export interface SpacingTokens {
    xs: string;   // 4rem
    sm: string;   // 8rem
    md: string;   // 12rem
    lg: string;   // 16rem
    xl: string;   // 24rem
    xxl: string;  // 32rem
}

// ============================================================================
// Layout
// ============================================================================

export interface LayoutTokens {
    panelWidth: string;      // 25vw with min-width
    panelMinWidth: string;   // 450rem
    panelMaxWidth: string;   // 600rem
    borderRadius: string;    // Sharp: 2rem
    borderRadiusLg: string;  // 4rem for larger elements
}

// ============================================================================
// Effects
// ============================================================================

export interface EffectTokens {
    // Glassmorphism
    glassBackground: string;
    glassBorder: string;

    // Shadows
    shadowSm: string;
    shadowMd: string;
    shadowLg: string;

    // Transitions
    transitionFast: string;
    transitionNormal: string;
}

// ============================================================================
// Full Theme
// ============================================================================

export interface Theme {
    name: string;
    colors: ColorTokens;
    typography: TypographyTokens;
    spacing: SpacingTokens;
    layout: LayoutTokens;
    effects: EffectTokens;
}

