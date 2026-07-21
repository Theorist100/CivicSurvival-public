/**
 * Theme System Entry Point
 * Civic Survival
 *
 * Usage:
 *   import { useTheme, getCurrentTheme, setTheme, THEMES } from "themes";
 *
 *   // In React components (preferred - reactive):
 *   const theme = useTheme();
 *
 *   // In non-component code:
 *   const theme = getCurrentTheme();
 *
 *   // Switch theme
 *   setTheme("classicGold");
 */

import React from "react";
import { type Theme, type Accents, type AccentPreset, ACCENT_PRESETS } from "./types";
import { techNoirTheme } from "./techNoir";
import { classicGoldTheme, CLASSIC_GOLD_ACCENTS } from "./classicGold";
import { softFocusTheme, SOFT_FOCUS_ACCENTS } from "./softFocus";
import { type ModalPalette, getModalPalette } from "./modalStyles";
import { scLog } from "../utils/logging";
import { type UIThemeId } from "../types/semantic";

// ============================================================================
// Available Themes
// ============================================================================

export const THEMES = {
    techNoir: {
        theme: techNoirTheme,
        accents: ACCENT_PRESETS,
    },
    classicGold: {
        theme: classicGoldTheme,
        accents: CLASSIC_GOLD_ACCENTS,
    },
    softFocus: {
        theme: softFocusTheme,
        accents: SOFT_FOCUS_ACCENTS,
    },
} as const;

export type ThemeName = keyof typeof THEMES;

// ============================================================================
// Current Theme State
// ============================================================================

let currentThemeName: ThemeName = "techNoir";

/**
 * Get current theme
 */
export function getCurrentTheme(): Theme {
    return THEMES[currentThemeName].theme;
}

/**
 * Get current theme's accent presets
 */
export function getCurrentAccents(): Accents {
    return THEMES[currentThemeName].accents;
}

/**
 * Set active theme by name
 */
export function setTheme(name: ThemeName): void {
    if (THEMES[name]) {
        currentThemeName = name;
        scLog(`Theme changed to: ${name}`);
    }
}

// ============================================================================
// Exports (backward compatible)
// ============================================================================

export * from "./types";
export { techNoirTheme } from "./techNoir";
export { classicGoldTheme } from "./classicGold";
export { softFocusTheme } from "./softFocus";

// FIX BUG-UI-401: Removed static `export const theme = getCurrentTheme()`
// Use useTheme() hook in components or getCurrentTheme() in non-component code

// ============================================================================
// Color Helpers (re-export from colorUtils — safe for circular imports)
// ============================================================================

export { hexToRgba } from "./colorUtils";

export * from "./factories";
export * from "./modalStyles";
export * from "./commonStyles";
export * from "./domainStyles";
export * from "./zIndex";

// ============================================================================
// React Context
// ============================================================================

export const ThemeContext = React.createContext<Theme>(techNoirTheme);
export const AccentContext = React.createContext<AccentPreset>(ACCENT_PRESETS.operations);
export const AccentsContext = React.createContext<Accents>(ACCENT_PRESETS);

export function useTheme(): Theme {
    return React.useContext(ThemeContext);
}

export function useAccents(): Accents {
    return React.useContext(AccentsContext);
}

// ============================================================================
// Modal Palette Hook
// ============================================================================

export function useModalPalette(): ModalPalette {
    const theme = React.useContext(ThemeContext);
    return React.useMemo(() => getModalPalette(theme), [theme]);
}

// ============================================================================
// Theme Provider
// ============================================================================

interface ThemeProviderProps {
    /** 0 = Tech Noir, 1 = Classic Gold, 2 = Soft Focus */
    themeId: UIThemeId;
    children: React.ReactNode;
}

/**
 * ThemeProvider - provides reactive theme context.
 * Listens to themeId changes and updates context accordingly.
 */
const themeNameById = (id: UIThemeId): ThemeName =>
    id === 2 ? "softFocus" : id === 1 ? "classicGold" : "techNoir";

export const ThemeProvider: React.FC<ThemeProviderProps> = ({ themeId, children }) => {
    const themeData = React.useMemo(() => THEMES[themeNameById(themeId)], [themeId]);

    React.useEffect(() => {
        setTheme(themeNameById(themeId));
    }, [themeId]);

    const accents = themeData.accents;

    return (
        <ThemeContext.Provider value={themeData.theme}>
            <AccentsContext.Provider value={accents}>
                <AccentContext.Provider value={accents.operations}>
                    {children}
                </AccentContext.Provider>
            </AccentsContext.Provider>
        </ThemeContext.Provider>
    );
};
