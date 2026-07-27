/**
 * Opinion Board — shared chrome tokens + allegiance/trend/raid color resolvers.
 * Keeps the board's surfaces (regions, cards, header strips, chips) consistent
 * with the project theme instead of the spec's literal hex palette.
 */

import { type CSSProperties } from "react";
import { hexToRgba } from "../../../../../themes";
import { type Accents, type Theme } from "../../../../../themes/types";
import { type AllegianceKind, type RaidAccentState, type TrendKind } from "./opinionData";

/** Loyal / undecided / enemy — the only three "populace" colors. */
export const allegianceColor = (kind: AllegianceKind, theme: Theme): string =>
    kind === "you" ? theme.colors.success
        : kind === "enemy" ? theme.colors.error
            : theme.colors.textMuted;

export const trendColor = (trend: TrendKind, theme: Theme, accents: Accents): string =>
    trend === "holding" ? theme.colors.success
        : trend === "enemy" ? theme.colors.error
            : accents.resilience.accent;

/**
 * Threat box / raid accent — the ONE colour contract for every raid surface:
 * green ⇔ contained (landed BLOCKED — a success), red ⇔ struck clean (landed
 * un-countered — damage, never green), amber ⇔ live in-flight ("act now").
 * Resolve the state via raidAccentState(raid) — never pass an ad-hoc boolean.
 */
export const raidAccent = (state: RaidAccentState, theme: Theme, accents: Accents): string =>
    state === "contained" ? theme.colors.success
        : state === "struck" ? theme.colors.error
            : accents.resilience.accent;

export const createBoardStyles = (theme: Theme, _accents: Accents) => ({
    /** Region fill (a board zone). */
    region: {
        backgroundColor: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadiusLg,
    } as CSSProperties,

    /** Nested card fill. */
    card: {
        backgroundColor: theme.colors.surface,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadiusLg,
        padding: theme.spacing.sm,
    } as CSSProperties,

    /** Header strip inside a region/card. */
    headerStrip: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: `${theme.spacing.xs} ${theme.spacing.sm}`,
        backgroundColor: theme.colors.borderLight,
        borderBottom: `2rem solid ${theme.colors.border}`,
    } as CSSProperties,

    /** Mono section eyebrow. */
    eyebrow: {
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        letterSpacing: "0.8rem",
    } as CSSProperties,

    /** Small mono chip. */
    chip: (color: string): CSSProperties => ({
        padding: "2rem 6rem",
        fontSize: theme.typography.sizeXS,
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        color,
        backgroundColor: hexToRgba(color, 0.12),
        borderRadius: theme.layout.borderRadius,
        textTransform: "uppercase" as const,
        letterSpacing: "0.4rem",
    }),

    /** Faint "awaiting backend" annotation. */
    sampleNote: {
        fontSize: theme.typography.sizeXS,
        color: theme.colors.textMuted,
    } as CSSProperties,
});
