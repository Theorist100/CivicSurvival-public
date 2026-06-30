/**
 * CognitiveInfoSection styles - Extracted for maintainability
 */

import { type useTheme } from "@themes";

type Theme = ReturnType<typeof useTheme>;

export const styles = {
    statusBox: (theme: Theme) => ({
        padding: theme.spacing.sm,
        backgroundColor: theme.colors.borderLight,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${theme.colors.border}`,
    }),

    // Domain-local label style for dense cognitive icon lists and chart captions.
    miniLabel: (theme: Theme) => ({
        color: theme.colors.textMuted,
        fontSize: "10rem",
        textTransform: "uppercase" as const,
        minWidth: "70rem",
    }),

    divider: (theme: Theme) => ({
        height: "1rem",
        backgroundColor: theme.colors.border,
        margin: `${theme.spacing.sm} 0`,
    }),

    stressIcon: (color: string) => ({
        fontSize: "12rem",
        marginRight: "6rem",
        color,
    }),

    countValue: (theme: Theme, color: string) => ({
        color,
        fontSize: "11rem",
        fontWeight: 600 as const,
        fontFamily: theme.typography.fontFamilyMono,
    }),

    emptyState: (theme: Theme) => ({
        padding: theme.spacing.md,
        textAlign: "center" as const,
        color: theme.colors.textMuted,
        fontSize: theme.typography.sizeSM,
    }),

    // ============ THE VOICE (Gerda) Styles ============

    voiceBox: (theme: Theme, isAgitated: boolean) => ({
        padding: theme.spacing.md,
        backgroundColor: isAgitated ? "rgba(255, 68, 68, 0.08)" : "rgba(255, 255, 255, 0.02)",
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${isAgitated ? "rgba(255, 68, 68, 0.4)" : theme.colors.border}`,
        transition: "background-color 0.3s ease, border-color 0.3s ease",
    }),

    portrait: (theme: Theme) => ({
        width: "48rem",
        height: "48rem",
        borderRadius: "24rem",
        backgroundColor: theme.colors.paper,
        border: `3rem solid ${theme.colors.border}`,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: "24rem",
        color: theme.colors.textSecondary,
        marginRight: theme.spacing.sm,
        transition: "filter 0.3s ease, opacity 0.3s ease",
    }),

    heroName: (theme: Theme) => ({
        fontSize: theme.typography.sizeMD,
        fontWeight: 700 as const,
        color: theme.colors.textPrimary,
        letterSpacing: "1rem",
    }),

    heroStatus: (color: string) => ({
        fontSize: "11rem",
        fontWeight: 700 as const,
        color,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
    }),

    heroLocation: (theme: Theme) => ({
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
    }),

    narrativeBox: (theme: Theme) => ({
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        backgroundColor: theme.colors.surface,
        borderRadius: "4rem",
        borderLeft: `4rem solid ${theme.colors.textMuted}`,
    }),

    narrativeText: (theme: Theme, risk: number) => ({
        fontSize: theme.typography.sizeSM,
        fontStyle: "italic" as const,
        color: risk >= 2 ? theme.colors.textPrimary : theme.colors.textSecondary,
        lineHeight: 1.4,
    }),

    riskLabel: (theme: Theme) => ({
        fontSize: theme.typography.sizeSM,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
    }),

    riskValue: (color: string) => ({
        fontSize: "13rem",
        fontWeight: 700 as const,
        color,
        textTransform: "uppercase" as const,
    }),
};
