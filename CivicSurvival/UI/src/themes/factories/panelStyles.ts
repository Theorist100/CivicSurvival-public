import type React from "react";
import { type Theme } from "../types";

function createPanelStyles(theme: Theme) {
    return {
        section: (noBorder = false): React.CSSProperties => ({
            padding: theme.spacing.md,
            borderBottom: noBorder ? "none" : `2rem solid ${theme.colors.border}`,
        }),
        sectionTitle: (color?: string): React.CSSProperties => ({
            fontSize: theme.typography.sizeSM,
            fontWeight: 700,
            color: color ?? theme.colors.textMuted,
            textTransform: "uppercase",
            letterSpacing: "0.5rem",
            marginBottom: theme.spacing.sm,
        }),
        panel: {
            background: theme.colors.paper,
            border: `1rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadius,
            padding: theme.spacing.md,
            boxShadow: theme.effects.shadowMd,
        } as React.CSSProperties,
        card: {
            background: theme.colors.paper,
            borderRadius: theme.layout.borderRadius,
            padding: theme.spacing.md,
            border: `2rem solid ${theme.colors.border}`,
        } as React.CSSProperties,
    };
}

const panelStylesCache = new WeakMap<Theme, ReturnType<typeof createPanelStyles>>();

export function getPanelStyles(theme: Theme): ReturnType<typeof createPanelStyles> {
    const cached = panelStylesCache.get(theme);
    if (cached) return cached;

    const styles = createPanelStyles(theme);
    panelStylesCache.set(theme, styles);
    return styles;
}
