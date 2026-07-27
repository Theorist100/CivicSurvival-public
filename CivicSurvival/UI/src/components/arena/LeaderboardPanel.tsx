/**
 * LeaderboardPanel — the arena's three ladders in one panel.
 *
 * DEFENSE: the city you held — points for intercepted threats, deeper waves
 * worth more. SHADOW: the money you kept — scheme income minus what
 * investigations confiscated. PROSPERITY: the city that lived — happiness ×
 * housed share, one index per survived wave. All are server-computed from
 * telemetry; the board switch decides which one the tabs, progress strip and
 * footer describe.
 */

import React, { memo, useState, useMemo, useEffect } from "react";
import { Column } from "@coherent";
import { useTheme, useAccents, hexToRgba, formatMoneyCompact } from "@themes";
import { type ArenaLeaderboardState } from "@hooks/state/useArenaLeaderboard";
import { bindingDataOrDefault } from "@hooks/domain";
import {
    DEFAULT_ARENA_PROGRESS,
    DEFAULT_PROSPERITY_LEADERBOARD_STATE,
    DEFAULT_SHADOW_LEADERBOARD_STATE,
    useArenaProgress,
    useProsperityLeaderboard,
    useShadowLeaderboard,
    type ArenaProgress,
    type ProsperityLeaderboardState,
    type ShadowLeaderboardState,
} from "@hooks/state";
import { useLocale } from "../../locales";
import { ArenaProgressHeader } from "./leaderboard/ArenaProgressHeader";
import { BoardSwitch } from "./leaderboard/BoardSwitch";
import { LadderContent, type LadderStyles } from "./leaderboard/LadderContent";
import {
    DEFENSE_BOARD,
    PROSPERITY_BOARD,
    SHADOW_BOARD,
    type LadderBoard,
    type LadderBoardChrome,
    type LadderCellTone,
} from "./leaderboard/ladderView";
import {
    LeaderboardOptInOverlay,
    LeaderboardPositionFooter,
    LeaderboardTabs,
    type LeaderboardTabType,
} from "./leaderboard/LeaderboardChrome";

// ============ Per-board pieces ============
// Each board reads its own slice of state; keeping the pick in these small
// units (instead of ternary chains inside the panel) keeps the panel legible.

interface BoardStates {
    board: LadderBoard;
    defense: ArenaLeaderboardState;
    progress: ArenaProgress;
    shadow: ShadowLeaderboardState;
    prosperity: ProsperityLeaderboardState;
}

/** The progress strip above the tabs, fed by whichever ladder is on screen. */
const BoardProgressStrip: React.FC<Omit<BoardStates, "defense">> = ({ board, progress, shadow, prosperity }) => {
    const l = useLocale();

    if (board === "shadow") {
        return (
            <ArenaProgressHeader
                board="shadow"
                tierName={shadow.tier.name}
                tierIcon={shadow.tier.icon}
                pendingLabel={shadow.pendingNet > 0 ? l.t("UI_ARENA_MONEY_VALUE", formatMoneyCompact(shadow.pendingNet)) : null}
                totalLabel={l.t("UI_ARENA_NET", formatMoneyCompact(shadow.displayedNet))}
                progressToNext={shadow.progressToNext}
                nextLabel={shadow.nextTier
                    ? l.t("UI_ARENA_PROGRESS_TO_NET", formatMoneyCompact(shadow.displayedNet), formatMoneyCompact(shadow.nextTier.minScore), shadow.nextTier.name)
                    : l.t("UI_ARENA_TOP_RANK")}
            />
        );
    }
    if (board === "prosperity") {
        return (
            <ArenaProgressHeader
                board="prosperity"
                tierName={prosperity.tier.name}
                tierIcon={prosperity.tier.icon}
                pendingLabel={prosperity.pendingScore > 0 ? l.t("UI_ARENA_PTS_VALUE", prosperity.pendingScore) : null}
                totalLabel={l.t("UI_ARENA_SCORE", prosperity.displayedScore)}
                progressToNext={prosperity.progressToNext}
                nextLabel={prosperity.nextTier
                    ? l.t("UI_ARENA_PROGRESS_TO", prosperity.displayedScore, prosperity.nextTier.minScore, prosperity.nextTier.name)
                    : l.t("UI_ARENA_TOP_RANK")}
            />
        );
    }
    return (
        <ArenaProgressHeader
            board="defense"
            tierName={progress.tier.name}
            tierIcon={progress.tier.icon}
            pendingLabel={progress.pendingScore > 0 ? l.t("UI_ARENA_PTS_VALUE", progress.pendingScore) : null}
            totalLabel={l.t("UI_ARENA_SCORE", progress.displayedScore)}
            progressToNext={progress.progressToNext}
            nextLabel={progress.nextTier
                ? l.t("UI_ARENA_PROGRESS_TO", progress.displayedScore, progress.nextTier.minScore, progress.nextTier.name)
                : l.t("UI_ARENA_TOP_RANK")}
        />
    );
};

/** One frame, three boards: picks the state, the view describes the columns. */
const BoardLadder: React.FC<Omit<BoardStates, "progress"> & { activeTab: LeaderboardTabType; styles: LadderStyles }> = (
    { board, activeTab, defense, shadow, prosperity, styles },
) => {
    if (board === "shadow") {
        return (
            <LadderContent
                activeTab={activeTab}
                view={SHADOW_BOARD}
                rankTiers={shadow.rankTiers}
                leaderboard={shadow.leaderboard}
                weeklyLeaderboard={shadow.weeklyLeaderboard}
                styles={styles}
            />
        );
    }
    if (board === "prosperity") {
        return (
            <LadderContent
                activeTab={activeTab}
                view={PROSPERITY_BOARD}
                rankTiers={prosperity.rankTiers}
                leaderboard={prosperity.leaderboard}
                weeklyLeaderboard={prosperity.weeklyLeaderboard}
                styles={styles}
            />
        );
    }
    return (
        <LadderContent
            activeTab={activeTab}
            view={DEFENSE_BOARD}
            rankTiers={defense.rankTiers}
            leaderboard={defense.leaderboard}
            weeklyLeaderboard={defense.weeklyLeaderboard}
            styles={styles}
        />
    );
};

interface FooterView {
    yourPosition: number | null;
    yourWeeklyPosition: number | null;
    allTimeDelta: number;
    weeklyDelta: number;
}

/** Session deltas only exist for defense — the other boards have no baseline yet. */
function pickFooterView({ board, defense, progress, shadow, prosperity }: BoardStates): FooterView {
    if (board === "shadow") {
        return { yourPosition: shadow.yourPosition, yourWeeklyPosition: shadow.yourWeeklyPosition, allTimeDelta: 0, weeklyDelta: 0 };
    }
    if (board === "prosperity") {
        return { yourPosition: prosperity.yourPosition, yourWeeklyPosition: prosperity.yourWeeklyPosition, allTimeDelta: 0, weeklyDelta: 0 };
    }
    return {
        yourPosition: defense.yourPosition,
        yourWeeklyPosition: defense.yourWeeklyPosition,
        allTimeDelta: progress.allTimeDelta,
        weeklyDelta: progress.weeklyDelta,
    };
}

function pickProgressKnown({ board, progress, shadow, prosperity }: Omit<BoardStates, "defense">): boolean {
    if (board === "shadow") return shadow.known;
    if (board === "prosperity") return prosperity.known;
    return progress.known;
}

// ============ Component ============

interface LeaderboardPanelProps {
    leaderboard: ArenaLeaderboardState;
    onRefreshLeaderboard: () => void;
    onlineEnabled: boolean;
    onlineConsentRecorded: boolean;
}

export const LeaderboardPanel = memo(({ leaderboard, onRefreshLeaderboard, onlineEnabled, onlineConsentRecorded }: LeaderboardPanelProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const [activeTab, setActiveTab] = useState<LeaderboardTabType>("ranks");
    const [board, setBoard] = useState<LadderBoard>("defense");
    const progress = bindingDataOrDefault(useArenaProgress(), DEFAULT_ARENA_PROGRESS);
    const shadow = bindingDataOrDefault(useShadowLeaderboard(), DEFAULT_SHADOW_LEADERBOARD_STATE);
    const prosperity = bindingDataOrDefault(useProsperityLeaderboard(), DEFAULT_PROSPERITY_LEADERBOARD_STATE);

    // Refresh leaderboard on mount and Online enable transition. The board is an Online
    // feature (backend gated on OnlineEnabled), so Online — not diagnostics — gates it.
    useEffect(() => {
        if (onlineEnabled) onRefreshLeaderboard();
    }, [onRefreshLeaderboard, onlineEnabled]);

    // ========== Memoized Styles ==========

    const containerStyle = useMemo((): React.CSSProperties => ({
        padding: "16rem",
        minHeight: "400rem",
        position: "relative" as const,
    }), []);

    const dimmedStyle = useMemo((): React.CSSProperties => onlineEnabled ? {} : {
        opacity: 0.4,
    }, [onlineEnabled]);

    // The board describes itself: accent, header, ladder columns, footer hint.
    const isShadow = board === "shadow";
    const isProsperity = board === "prosperity";
    const chrome: LadderBoardChrome = isShadow ? SHADOW_BOARD : isProsperity ? PROSPERITY_BOARD : DEFENSE_BOARD;
    const boardAccent = accents[chrome.accent].accent;

    const headerStyle = useMemo((): React.CSSProperties => ({
        fontSize: "14rem",
        fontWeight: 700,
        color: boardAccent,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        marginBottom: "12rem",
        borderBottom: `2rem solid ${hexToRgba(boardAccent, 0.25)}`,
        paddingBottom: "8rem",
    }), [boardAccent]);

    const sectionStyle = useMemo((): React.CSSProperties => ({
        background: theme.colors.paper,
        borderRadius: theme.layout.borderRadius,
        border: `2rem solid ${theme.colors.border}`,
        overflow: "hidden" as const,
    }), [theme.colors.paper, theme.layout.borderRadius, theme.colors.border]);

    const tierRowStyles = useMemo(() => ({
        even: {
            padding: "12rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: "transparent",
        } as React.CSSProperties,
        odd: {
            padding: "12rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: hexToRgba(theme.colors.border, 0.12),
        } as React.CSSProperties,
    }), [theme.colors.border]);

    const requirementStyle = useMemo((): React.CSSProperties => ({
        fontSize: "10rem",
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
    }), [theme.colors.textMuted, theme.typography.fontFamilyMono]);

    const entryRowStyles = useMemo(() => ({
        even: {
            padding: "10rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: "transparent",
        } as React.CSSProperties,
        odd: {
            padding: "10rem 16rem",
            borderBottom: `2rem solid ${theme.colors.border}`,
            background: hexToRgba(theme.colors.border, 0.12),
        } as React.CSSProperties,
    }), [theme.colors.border]);

    // Pre-memoized position styles (2 variants: top-3 vs rest)
    const positionStyleTop = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 700,
        color: accents.resilience.accent,
        fontFamily: theme.typography.fontFamilyMono,
        minWidth: "40rem",
    }), [accents.resilience.accent, theme.typography.fontFamilyMono]);

    const positionStyleRest = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        fontFamily: theme.typography.fontFamilyMono,
        minWidth: "40rem",
    }), [theme.colors.textMuted, theme.typography.fontFamilyMono]);

    // Pre-memoized holder styles (2 variants: vacant vs active)
    const holderStyleVacant = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        color: theme.colors.textMuted,
        flex: 1,
    }), [theme.colors.textMuted]);

    const holderStyleActive = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        color: accents.schemes.accent,
        flex: 1,
    }), [accents.schemes.accent]);

    const nicknameStyle = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 600,
        color: theme.colors.textPrimary,
        flex: 1,
    }), [theme.colors.textPrimary]);

    const statsStyle = useMemo((): React.CSSProperties => ({
        fontSize: "11rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
        marginLeft: "12rem",
    }), [theme.colors.textSecondary, theme.typography.fontFamilyMono]);

    // A value taken back reads as loss — the one red number on a board.
    const lossStyle = useMemo((): React.CSSProperties => ({
        fontSize: "10rem",
        color: theme.colors.error,
        fontFamily: theme.typography.fontFamilyMono,
        marginLeft: "12rem",
    }), [theme.colors.error, theme.typography.fontFamilyMono]);

    const rankBadgeStyle = useMemo((): React.CSSProperties => ({
        fontSize: "10rem",
        color: accents.schemes.accentDim,
        textTransform: "uppercase" as const,
        marginLeft: "12rem",
    }), [accents.schemes.accentDim]);

    const emptyStyle = useMemo((): React.CSSProperties => ({
        padding: "32rem",
        textAlign: "center" as const,
        color: theme.colors.textMuted,
        fontSize: "12rem",
    }), [theme.colors.textMuted]);

    // A column picks its cell by meaning, so the styles are keyed by tone.
    const cellStyles = useMemo((): Record<LadderCellTone, React.CSSProperties> => ({
        stat: statsStyle,
        loss: lossStyle,
        badge: rankBadgeStyle,
    }), [statsStyle, lossStyle, rankBadgeStyle]);

    // Says the list is cut, in the one place where that is true — the header sits
    // above every tab, including Ranks, where "top 10" would be a lie.
    const truncatedStyle = useMemo((): React.CSSProperties => ({
        padding: "6rem 16rem",
        textAlign: "center" as const,
        color: theme.colors.textMuted,
        fontSize: "10rem",
    }), [theme.colors.textMuted]);

    const contentStyles = useMemo((): LadderStyles => ({
        section: sectionStyle,
        tierRows: tierRowStyles,
        requirement: requirementStyle,
        holderVacant: holderStyleVacant,
        holderActive: holderStyleActive,
        entryRows: entryRowStyles,
        positionTop: positionStyleTop,
        positionRest: positionStyleRest,
        nickname: nicknameStyle,
        cells: cellStyles,
        empty: emptyStyle,
        truncated: truncatedStyle,
    }), [sectionStyle, tierRowStyles, requirementStyle, holderStyleVacant, holderStyleActive,
        entryRowStyles, positionStyleTop, positionStyleRest, nicknameStyle, cellStyles,
        emptyStyle, truncatedStyle]);

    // ========== Main Render ==========

    const progressKnown = pickProgressKnown({ board, progress, shadow, prosperity });
    const footer = pickFooterView({ board, defense: leaderboard, progress, shadow, prosperity });

    return (
        <Column style={containerStyle}>
            {/* Online opt-in overlay */}
            <LeaderboardOptInOverlay
                onlineEnabled={onlineEnabled}
                onlineConsentRecorded={onlineConsentRecorded}
            />

            {/* Dimmed content when Online disabled */}
            <div style={dimmedStyle}>
                {/* Header */}
                <div style={headerStyle}>{l.t(chrome.headerKey)}</div>

                {/* Board switch: the city you held vs. the money you kept */}
                <BoardSwitch board={board} onBoardChange={setBoard} disabled={!onlineEnabled} />

                {/* Your rank progress (hidden until the server confirms your stats) */}
                {onlineEnabled && progressKnown && (
                    <BoardProgressStrip board={board} progress={progress} shadow={shadow} prosperity={prosperity} />
                )}

                {/* Tabs */}
                <LeaderboardTabs activeTab={activeTab} onTabChange={setActiveTab} disabled={!onlineEnabled} />

                {leaderboard.lastRefreshResult.Status === "failed" && leaderboard.lastRefreshResult.ReasonId && (
                    <div style={emptyStyle}>
                        {l.tDynamic(leaderboard.lastRefreshResult.ReasonId)}
                    </div>
                )}

                <BoardLadder
                    board={board}
                    activeTab={activeTab}
                    defense={leaderboard}
                    shadow={shadow}
                    prosperity={prosperity}
                    styles={contentStyles}
                />

                <LeaderboardPositionFooter
                    yourPosition={footer.yourPosition}
                    yourWeeklyPosition={footer.yourWeeklyPosition}
                    allTimeDelta={footer.allTimeDelta}
                    weeklyDelta={footer.weeklyDelta}
                    unrankedHint={l.t(chrome.unrankedHintKey)}
                />
            </div>
        </Column>
    );
});
LeaderboardPanel.displayName = "LeaderboardPanel";
