import React, { useCallback } from "react";
import { Row } from "@coherent";
import { type LadderEntryBase, type LadderTier } from "@hooks/state";
import { useLocale } from "../../../locales";
import { RANK_COLORS, RankIcon } from "./RankIcon";
import { LeaderboardGuide } from "./LeaderboardGuide";
import { type LadderBoardView, type LadderCellTone, type LadderColumn } from "./ladderView";

export type LadderTabType = "ranks" | "alltime" | "weekly" | "guide";

/**
 * Rows drawn per board. The server ranks the top 100, but the panel is a fixed
 * 750rem block: header, board switch, progress strip, tabs and footer take ~330rem,
 * leaving room for about ten ~40rem rows. Showing more would either clip the tail
 * (the panel does not scroll) or push the footer — which is where the player reads
 * their own position — out of sight. Anyone outside the top ten reads their standing
 * there, not by hunting through a list.
 */
const VISIBLE_ROWS = 10;

export interface LadderStyles {
    section: React.CSSProperties;
    tierRows: { even: React.CSSProperties; odd: React.CSSProperties };
    requirement: React.CSSProperties;
    holderVacant: React.CSSProperties;
    holderActive: React.CSSProperties;
    entryRows: { even: React.CSSProperties; odd: React.CSSProperties };
    positionTop: React.CSSProperties;
    positionRest: React.CSSProperties;
    nickname: React.CSSProperties;
    /** One style per tone — a column picks its cell by meaning. */
    cells: Record<LadderCellTone, React.CSSProperties>;
    empty: React.CSSProperties;
    /** Footnote when the board holds more players than the panel can draw. */
    truncated: React.CSSProperties;
}

interface LadderContentProps<TEntry extends LadderEntryBase, TWeekly extends LadderEntryBase> {
    activeTab: LadderTabType;
    view: LadderBoardView<TEntry, TWeekly>;
    rankTiers: LadderTier<TEntry>[];
    leaderboard: TEntry[];
    weeklyLeaderboard: TWeekly[];
    styles: LadderStyles;
}

/**
 * The ladder frame, drawn for whichever board {@link LadderBoardView} describes.
 * Positions, nicknames and tier holders are the same on every board; the counted
 * values come from the view's columns.
 */
export function LadderContent<TEntry extends LadderEntryBase, TWeekly extends LadderEntryBase>({
    activeTab,
    view,
    rankTiers,
    leaderboard,
    weeklyLeaderboard,
    styles: s,
}: LadderContentProps<TEntry, TWeekly>): React.ReactElement | null {
    const l = useLocale();

    const renderTier = useCallback((tier: LadderTier<TEntry>, index: number) => {
        const isVacant = tier.holder === null;
        const rankColor = RANK_COLORS[tier.icon] || RANK_COLORS.rank1;

        const tierNameStyle: React.CSSProperties = {
            fontSize: "14rem",
            fontWeight: 700,
            color: rankColor,
            opacity: isVacant ? 0.7 : 1,
            minWidth: "140rem",
            // minWidth only pads SHORT names; a long tier name (prosperity's
            // "Mayor of the Unbroken City") grows past it and would sit flush
            // against the holder text without an explicit gap.
            marginRight: "12rem",
        };

        return (
            <Row key={tier.name} align="center" style={index % 2 === 0 ? s.tierRows.even : s.tierRows.odd}>
                <RankIcon icon={tier.icon} isVacant={isVacant} board={view.id} />
                <span style={tierNameStyle}>{tier.name}</span>
                <span style={isVacant ? s.holderVacant : s.holderActive}>
                    {tier.holder === null ? l.t("UI_ARENA_VACANT") : tier.holder.nickname}
                </span>
                <span style={s.requirement}>
                    {tier.holder === null
                        ? view.tierRequirement(tier.minScore, l)
                        : view.tierHolder(tier.holder, l)}
                </span>
            </Row>
        );
    }, [s, l, view]);

    const renderRow = useCallback(<T extends LadderEntryBase>(
        entry: T,
        index: number,
        columns: LadderColumn<T>[],
    ) => (
        <Row key={entry.id} align="center" style={index % 2 === 0 ? s.entryRows.even : s.entryRows.odd}>
            <span style={entry.position <= 3 ? s.positionTop : s.positionRest}>#{entry.position}</span>
            <span style={s.nickname}>{entry.nickname}</span>
            {columns.map((column) => {
                const label = column.label(entry, l);
                if (label === null) return null;
                return <span key={column.key} style={s.cells[column.tone ?? "stat"]}>{label}</span>;
            })}
        </Row>
    ), [s, l]);

    switch (activeTab) {
        case "ranks":
            return <div style={s.section}>{rankTiers.map(renderTier)}</div>;
        case "alltime":
            return (
                <div style={s.section}>
                    {leaderboard.length > 0
                        ? <>
                            {leaderboard.slice(0, VISIBLE_ROWS).map((entry, index) => renderRow(entry, index, view.allTime.columns))}
                            {leaderboard.length > VISIBLE_ROWS && (
                                <div style={s.truncated}>{l.t("UI_ARENA_SHOWING_TOP", VISIBLE_ROWS)}</div>
                            )}
                        </>
                        : <div style={s.empty}>{l.t(view.allTime.emptyKey)}</div>}
                </div>
            );
        case "weekly":
            return (
                <div style={s.section}>
                    {weeklyLeaderboard.length > 0
                        ? <>
                            {weeklyLeaderboard.slice(0, VISIBLE_ROWS).map((entry, index) => renderRow(entry, index, view.weekly.columns))}
                            {weeklyLeaderboard.length > VISIBLE_ROWS && (
                                <div style={s.truncated}>{l.t("UI_ARENA_SHOWING_TOP", VISIBLE_ROWS)}</div>
                            )}
                        </>
                        : <div style={s.empty}>{l.t(view.weekly.emptyKey)}</div>}
                </div>
            );
        case "guide":
            return (
                <LeaderboardGuide
                    rankTiers={rankTiers}
                    styles={{ section: s.section, tierRows: s.tierRows, requirement: s.requirement }}
                    view={view}
                />
            );
        default:
            return null;
    }
}
