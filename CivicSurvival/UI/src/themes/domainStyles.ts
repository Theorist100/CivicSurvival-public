import type React from "react";
import { hexToRgba } from "./colorUtils";
import { type AccentPreset, type Theme } from "./types";

type DomainDensity = "compact" | "default" | "wide";

interface DomainStylesConfig {
    theme: Theme;
    accent: AccentPreset;
    density?: DomainDensity;
    fillContainer?: boolean;
    titleFontSize?: string;
    titleMarginBottom?: string;
    titleDisplay?: React.CSSProperties["display"];
    titleAlignItems?: React.CSSProperties["alignItems"];
    titleLetterSpacing?: string;
}

interface DomainPresetButtonOptions {
    accentColor?: string;
    borderColor?: string;
    borderWidth?: string;
    padding?: string;
    minWidth?: string;
}

interface DomainTextBlockOptions {
    padding?: string;
    margin?: string;
    fontSize?: string;
    textAlign?: React.CSSProperties["textAlign"];
    whiteSpace?: React.CSSProperties["whiteSpace"];
    wordWrap?: React.CSSProperties["wordWrap"];
}

const paddingByDensity: Record<DomainDensity, string> = {
    compact: "10rem",
    default: "12rem",
    wide: "14rem",
};

const compact = (density: DomainDensity) => density === "compact";

export function createDomainStyles({
    theme,
    accent,
    density = "default",
    fillContainer = false,
    titleFontSize,
    titleMarginBottom,
    titleDisplay,
    titleAlignItems,
    titleLetterSpacing,
}: DomainStylesConfig) {
    const sectionBase: React.CSSProperties = {
        padding: paddingByDensity[density],
        background: hexToRgba(accent.accent, 0.03),
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${hexToRgba(accent.accent, 0.12)}`,
    };

    return {
        section: {
            ...sectionBase,
            ...(fillContainer && { width: "100%", boxSizing: "border-box" as const }),
        } as React.CSSProperties,

        sectionTitle: {
            fontSize: titleFontSize ?? (compact(density) ? "10rem" : "11rem"),
            fontWeight: 700,
            textTransform: "uppercase" as const,
            color: accent.accent,
            marginBottom: titleMarginBottom ?? (compact(density) ? "6rem" : "8rem"),
            ...(titleDisplay && { display: titleDisplay }),
            ...(titleAlignItems && { alignItems: titleAlignItems }),
            ...(titleLetterSpacing && { letterSpacing: titleLetterSpacing }),
        } as React.CSSProperties,

        presetButton: (active: boolean, options: DomainPresetButtonOptions = {}) => {
            const accentColor = options.accentColor ?? accent.accent;
            const borderColor = options.borderColor ?? theme.colors.border;
            return {
                padding: options.padding ?? (compact(density) ? "3rem 6rem" : "4rem 8rem"),
                background: active ? hexToRgba(accentColor, 0.19) : "transparent",
                border: `${options.borderWidth ?? (compact(density) ? "1rem" : "2rem")} solid ${active ? accentColor : borderColor}`,
                borderRadius: theme.layout.borderRadius,
                color: active ? accentColor : theme.colors.textSecondary,
                cursor: "pointer",
                fontSize: "10rem",
                fontWeight: 600,
                minWidth: options.minWidth ?? (compact(density) ? "28rem" : "36rem"),
            } as React.CSSProperties;
        },

        presetButtonDisabled: (active: boolean, disabled: boolean, options: DomainPresetButtonOptions = {}) => {
            const accentColor = options.accentColor ?? accent.accent;
            const borderColor = options.borderColor ?? theme.colors.border;
            return {
                padding: options.padding ?? (compact(density) ? "3rem 6rem" : "4rem 8rem"),
                background: active ? hexToRgba(accentColor, 0.19) : "transparent",
                border: `${options.borderWidth ?? (compact(density) ? "1rem" : "2rem")} solid ${active ? accentColor : borderColor}`,
                borderRadius: theme.layout.borderRadius,
                color: active ? accentColor : theme.colors.textSecondary,
                cursor: disabled ? "not-allowed" : "pointer",
                fontSize: "10rem",
                fontWeight: 600,
                minWidth: options.minWidth ?? (compact(density) ? "28rem" : "36rem"),
                opacity: disabled ? 0.5 : 1,
            } as React.CSSProperties;
        },

        buttonFull: (color: string) => ({
            padding: "10rem 14rem",
            background: "transparent",
            border: `2rem solid ${color}`,
            borderRadius: theme.layout.borderRadius,
            color,
            cursor: "pointer",
            fontSize: theme.typography.sizeXS,
            fontWeight: 600,
            width: "100%",
            marginTop: "10rem",
        } as React.CSSProperties),

        assessment: (color: string) => ({
            marginTop: "10rem",
            padding: "10rem",
            background: hexToRgba(color, 0.06),
            border: `2rem solid ${hexToRgba(color, 0.19)}`,
            borderRadius: theme.layout.borderRadius,
            fontSize: theme.typography.sizeXS,
            color: theme.colors.textSecondary,
            fontStyle: "italic" as const,
        } as React.CSSProperties),

        divider: (margin = compact(density) ? "4rem 0" : "14rem 0") => ({
            height: "1rem",
            background: theme.colors.border,
            margin,
            ...(!compact(density) && { width: "100%" }),
        } as React.CSSProperties),

        noData: (options: DomainTextBlockOptions = {}) => ({
            color: theme.colors.textMuted,
            fontStyle: "italic" as const,
            ...(options.fontSize && { fontSize: options.fontSize }),
            ...(options.textAlign && { textAlign: options.textAlign }),
            ...(options.whiteSpace && { whiteSpace: options.whiteSpace }),
            ...(options.wordWrap && { wordWrap: options.wordWrap }),
            ...(options.padding && { padding: options.padding }),
            ...(options.margin && { margin: options.margin }),
        } as React.CSSProperties),
    };
}
