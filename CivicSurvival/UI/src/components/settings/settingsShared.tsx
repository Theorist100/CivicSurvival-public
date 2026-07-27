/**
 * Shared pieces for the SETTINGS domain views.
 *
 * Settings used to be a floating window portaled to the mod root. It was the only surface
 * in the mod whose open/closed state lived OUTSIDE React — in C# (SettingsUISystem
 * .IsSettingsExpanded) — and the only one portaled out of the game UI layer. Those two
 * together made it the only surface that could come back on its own: leaving a city hides
 * the layer, the portal sits in <body> outside that layer, and the C# flag still said
 * "open", so the panel stayed on top of the main menu.
 *
 * It is now four ordinary dashboard views, like every other tab. Living inside ContentPanel
 * means it is hidden with the layer, needs no portal to escape the GlobalStatus bar, and
 * keeps no state outside React.
 */

import React, { memo } from "react";
import { type Accents, type Theme, useAccents, useTheme } from "../../themes";
import { useSettings } from "@hooks/domain";
import { type SettingsDto } from "../../types/domainDtos";
import { ToggleSwitch } from "../shared/ui";

/**
 * The settings DTO once it is live, or null while the binding is still warming up.
 * Every section gates on it, so none of them renders half-populated controls.
 */
export function useSettingsDto(): SettingsDto | null {
    const settings = useSettings();
    return settings.status === "ready" ? settings.data : null;
}

export const createSettingsStyles = (theme: Theme, accents: Accents) => ({
    // The dashboard content box is as wide as the widest domain (820rem), but settings are
    // a column of narrow controls — stretched across the full width the toggles would drift
    // an entire screen away from their labels. Cap the column and let the box hold it.
    column: {
        display: "flex",
        flexDirection: "column" as const,
        minHeight: 0,
        width: "100%",
        maxWidth: "440rem",
        overflowY: "auto" as const,
        overflowX: "hidden" as const,
        padding: "4rem",
    } as React.CSSProperties,

    section: {
        padding: "12rem",
        marginBottom: "10rem",
        background: theme.colors.surface,
        border: `1rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
    } as React.CSSProperties,

    label: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        marginBottom: theme.spacing.xs,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
    } as React.CSSProperties,

    settingRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginBottom: "8rem",
    } as React.CSSProperties,

    rowLabel: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        minWidth: 0,
        paddingRight: "10rem",
    } as React.CSSProperties,

    description: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
        marginTop: theme.spacing.xs,
        opacity: 0.75,
        lineHeight: 1.35,
    } as React.CSSProperties,

    error: {
        fontSize: "10rem",
        color: accents.crisis.accent,
        marginTop: theme.spacing.xs,
    } as React.CSSProperties,

    status: {
        fontSize: theme.typography.sizeXS,
        color: accents.schemes.accent,
        marginTop: theme.spacing.xs,
    } as React.CSSProperties,

    hint: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textSecondary,
        marginTop: theme.spacing.xs,
    } as React.CSSProperties,
});

export type SettingsStyles = ReturnType<typeof createSettingsStyles>;

export function useSettingsStyles(): { theme: Theme; accents: Accents; s: SettingsStyles } {
    const theme = useTheme();
    const accents = useAccents();
    const s = React.useMemo(() => createSettingsStyles(theme, accents), [theme, accents]);
    return { theme, accents, s };
}

interface SettingsToggleRowProps {
    label: string;
    checked: boolean;
    onChange: () => void;
    color?: string;
    disabled?: boolean;
    styles: SettingsStyles;
}

export const SettingsToggleRow: React.FC<SettingsToggleRowProps> = memo(({
    label,
    checked,
    onChange,
    color,
    disabled = false,
    styles: s,
}) => {
    const accents = useAccents();
    return (
        <div style={s.settingRow}>
            <span style={s.rowLabel}>{label}</span>
            <ToggleSwitch
                checked={checked}
                color={color ?? accents.operations.accent}
                onChange={onChange}
                disabled={disabled}
            />
        </div>
    );
});
SettingsToggleRow.displayName = "SettingsToggleRow";
