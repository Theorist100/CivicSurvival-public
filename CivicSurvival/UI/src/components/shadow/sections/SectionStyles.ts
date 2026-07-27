/**
 * Shared styles for SHADOW domain sections
 * Used by LedgerSection, CounterSection, SchemesSection, ProcurementSection
 */

import { type Theme, type Accents, createDomainStyles, formatMoney as commonFormatMoney, hexToRgba } from "@themes";

export const createSectionStyles = (theme: Theme, accents: Accents) => {
    const base = createDomainStyles({
        theme,
        accent: accents.schemes,
        density: "compact",
        titleLetterSpacing: "0.5rem",
        titleFontSize: "12rem",
    });

    return {
        section: base.section,

        sectionTitle: base.sectionTitle,

        valueLarge: (color: string) => ({
            color,
            fontWeight: 700,
            fontSize: "16rem",
            textAlign: "center" as const,
            padding: "4rem 0",
        } as React.CSSProperties),

        presetButtonGroup: {
            display: "flex",
            flexWrap: "wrap" as const,
        } as React.CSSProperties,

        // Compact scheme row: title + preset buttons in ONE wrap row (guide:
        // single wrap-row pattern — no space-between with a nested wrap block).
        schemeRow: {
            display: "flex",
            flexWrap: "wrap" as const,
            alignItems: "center" as const,
        } as React.CSSProperties,

        schemeRowTitle: {
            ...base.sectionTitle,
            marginRight: "8rem",
            marginBottom: "4rem",
        } as React.CSSProperties,

        schemeRowHelp: {
            marginRight: "8rem",
            marginBottom: "4rem",
        } as React.CSSProperties,

        presetButtonGroupChild: {
            marginRight: "4rem",
            marginBottom: "4rem",
        } as React.CSSProperties,

        presetButton: (active: boolean, accent: string, border: string) => ({
            ...base.presetButton(active, {
                accentColor: accent,
                borderColor: border,
                padding: "4rem 8rem",
                minWidth: "34rem",
            }),
            fontSize: "12rem",
        } as React.CSSProperties),

        divider: base.divider("4rem 0"),

        schemeInfo: {
            fontSize: "11rem",
            color: theme.colors.textMuted,
            marginTop: "4rem",
        } as React.CSSProperties,

        noData: base.noData({ fontSize: "11rem" }),

        choicePanel: {
            marginTop: "8rem",
            paddingTop: "8rem",
            borderTop: `1rem solid ${theme.colors.border}`,
        } as React.CSSProperties,

        choiceTitle: {
            color: theme.colors.textPrimary,
            fontSize: "11rem",
            fontWeight: 700,
            marginBottom: "6rem",
        } as React.CSSProperties,

        choiceMeta: {
            color: theme.colors.textMuted,
            fontSize: "10rem",
            marginTop: "6rem",
            lineHeight: 1.35,
        } as React.CSSProperties,

        choiceButton: (disabled: boolean, accent: string) => ({
            width: "100%",
            minHeight: "28rem",
            padding: "5rem 6rem",
            border: `1rem solid ${disabled ? theme.colors.border : accent}`,
            borderRadius: theme.layout.borderRadius,
            background: disabled ? theme.colors.paperHover : hexToRgba(accent, 0.12),
            color: disabled ? theme.colors.textMuted : accent,
            cursor: disabled ? "default" : "pointer",
            fontSize: "10rem",
            fontWeight: 700,
            textAlign: "center" as const,
        } as React.CSSProperties),
    };
};

// Color helpers
export const getPhaseColor = (phase: string, accents: Accents) => {
    if (phase === "Clear" || phase === "Idle" || phase === "UI_COUNTER_PHASE_IDLE") return accents.schemes.accent;
    if (phase === "Investigation" || phase === "UI_COUNTER_PHASE_INVESTIGATION") return accents.resilience.accent;
    return accents.crisis.accent;
};

export const getProgressColor = (progress: number, accents: Accents) => {
    if (progress > 70) return accents.crisis.accent;
    if (progress > 40) return accents.resilience.accent;
    return accents.schemes.accent;
};

export const getRiskColor = (score: number, accents: Accents, theme: Theme) => {
    if (score > 75) return theme.colors.errorBright ?? accents.crisis.accent;
    if (score > 50) return accents.crisis.accent;
    if (score > 25) return accents.resilience.accent;
    return accents.schemes.accent;
};

export const getHeatColor = (level: string, accents: Accents, theme: Theme) => {
    switch (level) {
        case "UI_COUNTER_HEAT_LEVEL_CRITICAL":
        case "Critical": return theme.colors.errorBright ?? accents.crisis.accent;
        case "UI_COUNTER_HEAT_LEVEL_DANGER":
        case "Danger": return accents.crisis.accent;
        case "UI_COUNTER_HEAT_LEVEL_WARNING":
        case "Warning": return accents.resilience.accent;
        default: return accents.schemes.accent;
    }
};

// Re-export from common styles
export { commonFormatMoney as formatMoney };
