import React from "react";
import { Icon } from "@shared/common/Icons";
import { ICON_HOST } from "@shared/common/iconHost";
import { type RankIconId } from "@hooks/state/useArenaLeaderboard";

// Rank artwork uses fixed tier colors to match the SVG assets.
/* eslint-disable civic/no-hardcoded-rgba */
export const RANK_COLORS: Record<RankIconId, string> = {
    rank5: "#FFD700",
    rank4: "#BA68C8",
    rank3: "#FF9100",
    rank2: "#64B5F6",
    rank1: "#78909C",
};
/* eslint-enable civic/no-hardcoded-rgba */

interface RankIconProps {
    icon: RankIconId;
    isVacant: boolean;
}

export const RankIcon: React.FC<RankIconProps> = ({ icon, isVacant }) => {
    const color = RANK_COLORS[icon];

    const containerStyle: React.CSSProperties = {
        width: "28rem",
        height: "28rem",
        marginRight: "12rem",
        opacity: isVacant ? 0.5 : 1,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        color,
    };

    const iconMap: Record<RankIconId, string> = {
        rank5: `${ICON_HOST}rank-5-lord.svg`,
        rank4: `${ICON_HOST}rank-4-broker.svg`,
        rank3: `${ICON_HOST}rank-3-tycoon.svg`,
        rank2: `${ICON_HOST}rank-2-operator.svg`,
        rank1: `${ICON_HOST}rank-1-survivor.svg`,
    };

    const src = iconMap[icon];

    return (
        <div style={containerStyle}>
            <Icon src={src} tinted />
        </div>
    );
};
