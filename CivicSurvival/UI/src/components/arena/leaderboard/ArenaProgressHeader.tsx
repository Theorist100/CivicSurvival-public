import React, { useMemo } from "react";
import { Row } from "@coherent";
import { useTheme, hexToRgba } from "@themes";
import { type RankIconId } from "@hooks/state";
import { RANK_COLORS, RankIcon } from "./RankIcon";
import { type LadderBoard } from "./ladderView";

interface ArenaProgressHeaderProps {
    /** Which ladder's artwork/accent to draw. */
    board: LadderBoard;
    /** Current tier name and icon. */
    tierName: string;
    tierIcon: RankIconId;
    /** Locally pending chip ("+340 pts" / "+$12,400"); null hides it. */
    pendingLabel: string | null;
    /** Confirmed total, right-aligned. */
    totalLabel: string;
    /** 0..1 fill toward the next tier. */
    progressToNext: number;
    /** Line under the bar ("X / Y pts to <tier>" or the top-rank note). */
    nextLabel: string;
}

/**
 * Your rank progress strip under the panel header: current tier, fill toward
 * the next threshold, live chip for locally pending gains. Presentational —
 * the panel feeds it from whichever ladder is on screen.
 */
export const ArenaProgressHeader: React.FC<ArenaProgressHeaderProps> = ({
    board,
    tierName,
    tierIcon,
    pendingLabel,
    totalLabel,
    progressToNext,
    nextLabel,
}) => {
    const theme = useTheme();
    const rankColor = RANK_COLORS[tierIcon] || RANK_COLORS.rank1;

    const containerStyle = useMemo((): React.CSSProperties => ({
        padding: "10rem 12rem",
        marginBottom: "12rem",
        background: theme.colors.paper,
        border: `2rem solid ${theme.colors.border}`,
        borderRadius: theme.layout.borderRadius,
    }), [theme.colors.paper, theme.colors.border, theme.layout.borderRadius]);

    const tierNameStyle = useMemo((): React.CSSProperties => ({
        fontSize: "13rem",
        fontWeight: 700,
        color: rankColor,
        textTransform: "uppercase" as const,
        marginRight: "8rem",
    }), [rankColor]);

    const pendingChipStyle = useMemo((): React.CSSProperties => ({
        fontSize: "11rem",
        fontWeight: 700,
        fontFamily: theme.typography.fontFamilyMono,
        color: theme.colors.success,
    }), [theme.colors.success, theme.typography.fontFamilyMono]);

    // No marginLeft:"auto" here — cohtml ignores auto margins in flex rows
    // (chip and total collided); the row uses justify="space-between" instead.
    const totalStyle = useMemo((): React.CSSProperties => ({
        fontSize: "11rem",
        color: theme.colors.textSecondary,
        fontFamily: theme.typography.fontFamilyMono,
    }), [theme.colors.textSecondary, theme.typography.fontFamilyMono]);

    const barTrackStyle = useMemo((): React.CSSProperties => ({
        height: "8rem",
        marginTop: "8rem",
        background: hexToRgba(theme.colors.border, 0.5),
        borderRadius: "4rem",
        overflow: "hidden" as const,
    }), [theme.colors.border]);

    const barFillStyle = useMemo((): React.CSSProperties => ({
        height: "100%",
        width: `${Math.round(progressToNext * 100)}%`,
        background: rankColor,
        borderRadius: "4rem",
        transition: "width 0.6s ease-out",
    }), [progressToNext, rankColor]);

    const nextLineStyle = useMemo((): React.CSSProperties => ({
        fontSize: "10rem",
        color: theme.colors.textMuted,
        marginTop: "4rem",
    }), [theme.colors.textMuted]);

    return (
        <div style={containerStyle}>
            <Row align="center" justify="space-between">
                <Row align="center">
                    <RankIcon icon={tierIcon} isVacant={false} board={board} />
                    <span style={tierNameStyle}>{tierName}</span>
                    {pendingLabel !== null && <span style={pendingChipStyle}>{pendingLabel}</span>}
                </Row>
                <span style={totalStyle}>{totalLabel}</span>
            </Row>
            <div style={barTrackStyle}>
                <div style={barFillStyle} />
            </div>
            <div style={nextLineStyle}>{nextLabel}</div>
        </div>
    );
};
