import React, { useCallback } from "react";
import { Row } from "@coherent";
import { formatCostArg } from "@themes";
import { type RankTier, type LeaderboardEntry, type WeeklyLeaderboardEntry } from "@hooks/state/useArenaLeaderboard";
import { useLocale } from "../../../locales";
import { RANK_COLORS, RankIcon } from "./RankIcon";

type TabType = "ranks" | "alltime" | "weekly";

interface LeaderboardContentStyles {
    section: React.CSSProperties;
    tierRows: { even: React.CSSProperties; odd: React.CSSProperties };
    requirement: React.CSSProperties;
    holderVacant: React.CSSProperties;
    holderActive: React.CSSProperties;
    entryRows: { even: React.CSSProperties; odd: React.CSSProperties };
    positionTop: React.CSSProperties;
    positionRest: React.CSSProperties;
    nickname: React.CSSProperties;
    stats: React.CSSProperties;
    rankBadge: React.CSSProperties;
    empty: React.CSSProperties;
}

interface LeaderboardContentProps {
    activeTab: TabType;
    rankTiers: RankTier[];
    leaderboard: LeaderboardEntry[];
    weeklyLeaderboard: WeeklyLeaderboardEntry[];
    styles: LeaderboardContentStyles;
}

export const LeaderboardContent: React.FC<LeaderboardContentProps> = ({
    activeTab,
    rankTiers,
    leaderboard,
    weeklyLeaderboard,
    styles: s,
}) => {
    const l = useLocale();

    const renderRankTier = useCallback((tier: RankTier, index: number) => {
        const isVacant = tier.holder === null;
        const rankColor = RANK_COLORS[tier.icon] || RANK_COLORS.rank1;

        const tierNameStyle: React.CSSProperties = {
            fontSize: "14rem",
            fontWeight: 700,
            color: rankColor,
            opacity: isVacant ? 0.7 : 1,
            minWidth: "140rem",
        };

        return (
            <Row key={tier.name} align="center" style={index % 2 === 0 ? s.tierRows.even : s.tierRows.odd}>
                <RankIcon icon={tier.icon} isVacant={isVacant} />
                <span style={tierNameStyle}>{tier.name}</span>
                <span style={isVacant ? s.holderVacant : s.holderActive}>
                    {isVacant ? l.t("UI_ARENA_VACANT") : tier.holder?.nickname}
                </span>
                {!isVacant && (
                    <span style={s.requirement}>
                        {l.t("UI_ARENA_HITS", tier.holder?.floor_hits ?? 0)}
                    </span>
                )}
                {isVacant && (
                    <span style={s.requirement}>
                        {l.t("UI_ARENA_REQ", tier.minFloorHits)}
                    </span>
                )}
            </Row>
        );
    }, [s, l]);

    const renderLeaderboardEntry = useCallback((entry: LeaderboardEntry, index: number) => (
        <Row key={entry.id} align="center" style={index % 2 === 0 ? s.entryRows.even : s.entryRows.odd}>
            <span style={entry.position <= 3 ? s.positionTop : s.positionRest}>#{entry.position}</span>
            <span style={s.nickname}>{entry.nickname}</span>
            <span style={s.stats}>{l.t("UI_ARENA_HITS", entry.floor_hits)}</span>
            <span style={s.stats}>{l.t("UI_ARENA_DAMAGE", formatCostArg(entry.total_damage))}</span>
            <span style={s.rankBadge}>{entry.rank_tier}</span>
        </Row>
    ), [s, l]);

    const renderWeeklyEntry = useCallback((entry: WeeklyLeaderboardEntry, index: number) => (
        <Row key={entry.id} align="center" style={index % 2 === 0 ? s.entryRows.even : s.entryRows.odd}>
            <span style={entry.position <= 3 ? s.positionTop : s.positionRest}>#{entry.position}</span>
            <span style={s.nickname}>{entry.nickname}</span>
            <span style={s.stats}>{l.t("UI_ARENA_HITS", entry.floor_hits)}</span>
            <span style={s.stats}>{l.t("UI_ARENA_DAMAGE", formatCostArg(entry.damage_dealt))}</span>
        </Row>
    ), [s, l]);

    switch (activeTab) {
        case "ranks":
            return <div style={s.section}>{rankTiers.map(renderRankTier)}</div>;
        case "alltime":
            return (
                <div style={s.section}>
                    {leaderboard.length > 0
                        ? leaderboard.map(renderLeaderboardEntry)
                        : <div style={s.empty}>{l.t("UI_ARENA_NO_COMMANDERS")}</div>}
                </div>
            );
        case "weekly":
            return (
                <div style={s.section}>
                    {weeklyLeaderboard.length > 0
                        ? weeklyLeaderboard.map(renderWeeklyEntry)
                        : <div style={s.empty}>{l.t("UI_ARENA_WEEKLY_RESET")}</div>}
                </div>
            );
        default:
            return null;
    }
};
