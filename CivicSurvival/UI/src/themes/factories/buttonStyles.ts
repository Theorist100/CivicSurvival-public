import type React from "react";
import { getReadableForeground, hexToRgba } from "../colorUtils";
import { type Accents, type Theme } from "../types";

function createButtonStyles(theme: Theme, accents: Accents) {
    const base: React.CSSProperties = {
        borderRadius: theme.layout.borderRadius,
        cursor: "pointer",
        pointerEvents: "auto",
    };

    return {
        action: (color = accents.operations.accent): React.CSSProperties => ({
            ...base,
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            fontSize: theme.typography.sizeXS,
            fontWeight: 700,
            background: color,
            border: "none",
            color: getReadableForeground(color),
        }),
        outline: (color = theme.colors.border, active = false): React.CSSProperties => ({
            ...base,
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            fontSize: theme.typography.sizeXS,
            fontWeight: active ? 700 : 400,
            background: active ? color : theme.colors.surface,
            border: `2rem solid ${active ? color : theme.colors.border}`,
            color: active ? theme.colors.background : theme.colors.textPrimary,
            textAlign: "center",
        }),
        ghost: (color = theme.colors.textSecondary): React.CSSProperties => ({
            ...base,
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            fontSize: theme.typography.sizeXS,
            fontWeight: 600,
            background: "transparent",
            border: `2rem solid ${color}`,
            color,
        }),
        tab: (active: boolean, color = theme.colors.accent): React.CSSProperties => ({
            ...base,
            flex: 1,
            marginBottom: theme.spacing.xs,
            padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
            background: active ? color : theme.colors.surface,
            border: `2rem solid ${active ? color : theme.colors.border}`,
            color: active ? theme.colors.background : theme.colors.textPrimary,
            fontSize: theme.typography.sizeXS,
            fontWeight: active ? 700 : 400,
            textAlign: "center",
            minWidth: "36rem",
        }),
        menuItem: (active = false, color = accents.operations.accent): React.CSSProperties => ({
            ...base,
            width: "100%",
            display: "flex",
            flexDirection: "column",
            minHeight: "1rem",
            alignItems: "flex-start",
            padding: `${theme.spacing.sm} ${theme.spacing.md}`,
            background: active ? hexToRgba(color, 0.15) : "transparent",
            border: "none",
            borderBottom: `1rem solid ${theme.colors.border}`,
            color: active ? color : theme.colors.textPrimary,
            textAlign: "left",
        }),
    };
}

const buttonStylesCache = new WeakMap<Theme, WeakMap<Accents, ReturnType<typeof createButtonStyles>>>();

export function getButtonStyles(theme: Theme, accents: Accents): ReturnType<typeof createButtonStyles> {
    let perTheme = buttonStylesCache.get(theme);
    if (!perTheme) {
        perTheme = new WeakMap();
        buttonStylesCache.set(theme, perTheme);
    }

    const cached = perTheme.get(accents);
    if (cached) return cached;

    const styles = createButtonStyles(theme, accents);
    perTheme.set(accents, styles);
    return styles;
}
