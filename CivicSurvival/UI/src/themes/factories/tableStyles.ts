import type React from "react";
import { type Theme } from "../types";

function createTableStyles(theme: Theme) {
    const cellPad = `${theme.spacing.sm} ${theme.spacing.sm}`;
    const headerCellPad = `${theme.spacing.xs} ${theme.spacing.sm}`;

    return {
        wrap: {
            display: "flex",
            flexDirection: "column",
            minHeight: "1rem",
            border: `2rem solid ${theme.colors.border}`,
            borderRadius: theme.layout.borderRadius,
            overflow: "hidden",
        } as React.CSSProperties,
        header: {
            display: "flex",
            backgroundColor: theme.colors.paper,
            borderBottom: `3rem solid ${theme.colors.textMuted}`,
        } as React.CSSProperties,
        row: {
            display: "flex",
            alignItems: "center",
        } as React.CSSProperties,
        headerCell: (width: string, align: "left" | "center" | "right" = "center", compact = false): React.CSSProperties => ({
            width,
            ...(width === "auto" && { flex: 1 }),
            padding: compact ? `${theme.spacing.xs} ${theme.spacing.xs}` : headerCellPad,
            fontSize: "10rem",
            fontWeight: 700,
            color: theme.colors.textMuted,
            textTransform: "uppercase",
            textAlign: align,
        }),
        cell: (width: string, align: "left" | "center" | "right" = "left", compact = false): React.CSSProperties => ({
            width,
            ...(width === "auto" && { flex: 1 }),
            padding: compact ? `${theme.spacing.xs} ${theme.spacing.xs}` : cellPad,
            fontSize: "12rem",
            display: "flex",
            alignItems: "center",
            justifyContent: align === "right" ? "flex-end" : align === "center" ? "center" : "flex-start",
        }),
        empty: {
            textAlign: "center",
            padding: theme.spacing.lg,
            color: theme.colors.textMuted,
            fontSize: theme.typography.sizeXS,
        } as React.CSSProperties,
    };
}

const tableStylesCache = new WeakMap<Theme, ReturnType<typeof createTableStyles>>();

export function getTableStyles(theme: Theme): ReturnType<typeof createTableStyles> {
    const cached = tableStylesCache.get(theme);
    if (cached) return cached;

    const styles = createTableStyles(theme);
    tableStylesCache.set(theme, styles);
    return styles;
}
