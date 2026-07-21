import React, { useMemo } from "react";
import { Row } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { type RankIconId, type RankTierDef } from "@hooks/state";
import { useLocale } from "../../../locales";
import { RANK_COLORS, RankIcon } from "./RankIcon";
import { type LadderBoardChrome } from "./ladderView";

interface LeaderboardGuideStyles {
    section: React.CSSProperties;
    tierRows: { even: React.CSSProperties; odd: React.CSSProperties };
    requirement: React.CSSProperties;
}

interface LeaderboardGuideProps {
    rankTiers: RankTierDef[];
    styles: LeaderboardGuideStyles;
    view: LadderBoardChrome;
}

/**
 * "Guide" tab — how the active ladder scores and which ranks exist.
 * Thresholds come from the live tier defs (config-driven), so the page never
 * drifts from what the server actually ranks with.
 */
export const LeaderboardGuide: React.FC<LeaderboardGuideProps> = ({ rankTiers, styles: s, view }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const boardAccent = accents[view.accent].accent;

    const titleStyle = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        fontWeight: 700,
        color: boardAccent,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        padding: "12rem 16rem 4rem",
    }), [boardAccent]);

    const paragraphStyle = useMemo((): React.CSSProperties => ({
        fontSize: "12rem",
        lineHeight: "17rem",
        color: theme.colors.textSecondary,
        padding: "4rem 16rem",
    }), [theme.colors.textSecondary]);

    const tierNameStyles = useMemo((): Record<RankIconId, React.CSSProperties> => {
        const base: React.CSSProperties = {
            fontSize: "14rem",
            fontWeight: 700,
            minWidth: "140rem",
            // Same gap as the Ranks tab: long tier names outgrow minWidth and
            // need an explicit margin before the requirement column.
            marginRight: "12rem",
        };
        return {
            rank1: { ...base, color: RANK_COLORS.rank1 },
            rank2: { ...base, color: RANK_COLORS.rank2 },
            rank3: { ...base, color: RANK_COLORS.rank3 },
            rank4: { ...base, color: RANK_COLORS.rank4 },
            rank5: { ...base, color: RANK_COLORS.rank5 },
        };
    }, []);

    // Highest tier first — same order as the Ranks tab.
    const sortedTiers = useMemo(
        () => [...rankTiers].sort((a, b) => b.minScore - a.minScore),
        [rankTiers],
    );

    return (
        <div style={s.section}>
            <div style={titleStyle}>{l.t("UI_ARENA_GUIDE_HOW_TITLE")}</div>
            <div style={paragraphStyle}>{l.t(view.guide.scoreKey)}</div>
            <div style={paragraphStyle}>{l.t(view.guide.entryKey)}</div>
            <div style={paragraphStyle}>{l.t("UI_ARENA_GUIDE_WEEKLY")}</div>
            <div style={paragraphStyle}>{l.t("UI_ARENA_GUIDE_ONLINE")}</div>

            <div style={titleStyle}>{l.t("UI_ARENA_GUIDE_RANKS_TITLE")}</div>
            {sortedTiers.map((tier, index) => (
                <Row key={tier.name} align="center" style={index % 2 === 0 ? s.tierRows.even : s.tierRows.odd}>
                    <RankIcon icon={tier.icon} isVacant={false} board={view.id} />
                    <span style={tierNameStyles[tier.icon]}>{tier.name}</span>
                    <span style={s.requirement}>{view.tierRequirement(tier.minScore, l)}</span>
                </Row>
            ))}
        </div>
    );
};
