/**
 * Shared styles for the cognitive INFO/diagnostic sections (penalties, exodus,
 * household stats, IPSO). Consumed by the diagnostics overlay sections.
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
};
