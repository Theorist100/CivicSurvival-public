/**
 * Board descriptions for the arena ladders.
 *
 * Every ladder draws the same frame — tiers with a holder or VACANT, ranked rows
 * with a position and a nickname — and differs only in what it counts and what it
 * calls it. That difference is data, not a component: a board is a list of columns
 * plus the strings that name them, and {@link LadderContent} renders any of them.
 * A new ladder is a new const in this file.
 */

import { formatMoneyCompact, type AccentPresetName } from "@themes";
import {
    type LadderEntryBase,
    type LeaderboardEntry,
    type ProsperityEntry,
    type ShadowEntry,
    type WeeklyLeaderboardEntry,
    type WeeklyProsperityEntry,
    type WeeklyShadowEntry,
} from "@hooks/state";
import { type TranslationKey, type useLocale } from "../../../locales";

type Locale = ReturnType<typeof useLocale>;

/** Which ladder — picks the rank artwork and the switch button. */
export type LadderBoard = "defense" | "shadow" | "prosperity";

/**
 * What a value means, not what colour it is: `loss` is the one red number on a
 * board (money taken back), `badge` is the rank label at the end of a row.
 */
export type LadderCellTone = "stat" | "loss" | "badge";

export interface LadderColumn<TEntry> {
    key: string;
    /** null omits the cell entirely — a zero confiscation earns no column. */
    label: (entry: TEntry, l: Locale) => string | null;
    /** Defaults to "stat". */
    tone?: LadderCellTone;
}

export interface LadderTabView<TEntry> {
    columns: LadderColumn<TEntry>[];
    /** Shown instead of rows when the board has none. */
    emptyKey: TranslationKey;
}

/**
 * The half of a board that does not depend on its entry shape — everything the
 * panel chrome and the guide need without knowing what a row holds.
 */
export interface LadderBoardChrome {
    id: LadderBoard;
    accent: AccentPresetName;
    headerKey: TranslationKey;
    /** How to get onto this board, shown when the player is unranked. */
    unrankedHintKey: TranslationKey;
    /** What a tier costs — the VACANT line and the guide's rank list. */
    tierRequirement: (minScore: number, l: Locale) => string;
    guide: { scoreKey: TranslationKey; entryKey: TranslationKey };
}

export interface LadderBoardView<TEntry extends LadderEntryBase, TWeekly extends LadderEntryBase>
    extends LadderBoardChrome {
    allTime: LadderTabView<TEntry>;
    weekly: LadderTabView<TWeekly>;
    /** What the tier's holder scored to take it. */
    tierHolder: (holder: TEntry, l: Locale) => string;
}

// ============ Boards ============

/** The city you held: points for intercepted threats, deeper waves worth more. */
export const DEFENSE_BOARD: LadderBoardView<LeaderboardEntry, WeeklyLeaderboardEntry> = {
    id: "defense",
    accent: "operations",
    headerKey: "UI_ARENA_GLOBAL_COMMANDERS",
    unrankedHintKey: "UI_ARENA_UNRANKED_DEFENSE",
    tierRequirement: (minScore, l) => l.t("UI_ARENA_REQ", minScore),
    tierHolder: (holder, l) => l.t("UI_ARENA_SCORE", holder.score),
    guide: { scoreKey: "UI_ARENA_GUIDE_SCORE", entryKey: "UI_ARENA_GUIDE_ENTRY" },
    allTime: {
        emptyKey: "UI_ARENA_NO_COMMANDERS",
        columns: [
            { key: "waves", label: (e, l) => l.t("UI_ARENA_WAVES", e.waves_survived) },
            { key: "score", label: (e, l) => l.t("UI_ARENA_SCORE", e.score) },
            { key: "rank", label: (e) => e.rank_tier, tone: "badge" },
        ],
    },
    weekly: {
        emptyKey: "UI_ARENA_WEEKLY_RESET",
        columns: [
            { key: "waves", label: (e, l) => l.t("UI_ARENA_WAVES", e.waves_survived) },
            { key: "score", label: (e, l) => l.t("UI_ARENA_SCORE", e.score) },
        ],
    },
};

/** The city that lived: happiness × housed share, one index per survived wave. */
export const PROSPERITY_BOARD: LadderBoardView<ProsperityEntry, WeeklyProsperityEntry> = {
    id: "prosperity",
    accent: "resilience",
    headerKey: "UI_ARENA_PROSPERITY_COMMANDERS",
    unrankedHintKey: "UI_ARENA_UNRANKED_PROSPERITY",
    tierRequirement: (minScore, l) => l.t("UI_ARENA_REQ", minScore),
    tierHolder: (holder, l) => l.t("UI_ARENA_SCORE", holder.score),
    guide: { scoreKey: "UI_ARENA_GUIDE_PROSPERITY_SCORE", entryKey: "UI_ARENA_GUIDE_PROSPERITY_ENTRY" },
    allTime: {
        emptyKey: "UI_ARENA_NO_MAYORS",
        columns: [
            { key: "waves", label: (e, l) => l.t("UI_ARENA_WAVES", e.waves) },
            { key: "index", label: (e, l) => l.t("UI_ARENA_INDEX", e.avgIndex) },
            { key: "score", label: (e, l) => l.t("UI_ARENA_SCORE", e.score) },
            { key: "rank", label: (e) => e.rank_tier, tone: "badge" },
        ],
    },
    weekly: {
        emptyKey: "UI_ARENA_WEEKLY_RESET",
        columns: [
            { key: "score", label: (e, l) => l.t("UI_ARENA_SCORE", e.score) },
        ],
    },
};

/** The money you kept: scheme income minus what investigations confiscated. */
export const SHADOW_BOARD: LadderBoardView<ShadowEntry, WeeklyShadowEntry> = {
    id: "shadow",
    accent: "schemes",
    headerKey: "UI_ARENA_SHADOW_COMMANDERS",
    unrankedHintKey: "UI_ARENA_UNRANKED_SHADOW",
    tierRequirement: (minScore, l) => l.t("UI_ARENA_REQ_NET", formatMoneyCompact(minScore)),
    tierHolder: (holder, l) => l.t("UI_ARENA_NET", formatMoneyCompact(holder.net)),
    guide: { scoreKey: "UI_ARENA_GUIDE_SHADOW_SCORE", entryKey: "UI_ARENA_GUIDE_SHADOW_ENTRY" },
    allTime: {
        emptyKey: "UI_ARENA_NO_SCHEMERS",
        columns: [
            { key: "earned", label: (e, l) => l.t("UI_ARENA_EARNED", formatMoneyCompact(e.earned)) },
            {
                key: "confiscated",
                tone: "loss",
                label: (e, l) => (e.confiscated > 0
                    ? l.t("UI_ARENA_CONFISCATED", formatMoneyCompact(e.confiscated))
                    : null),
            },
            { key: "net", label: (e, l) => l.t("UI_ARENA_NET", formatMoneyCompact(e.net)) },
            { key: "rank", label: (e) => e.rank_tier, tone: "badge" },
        ],
    },
    weekly: {
        emptyKey: "UI_ARENA_WEEKLY_RESET",
        columns: [
            { key: "net", label: (e, l) => l.t("UI_ARENA_NET", formatMoneyCompact(e.net)) },
        ],
    },
};
