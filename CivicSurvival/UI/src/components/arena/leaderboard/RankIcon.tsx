import React from "react";
import { GLYPHS } from "@shared/common/iconGlyphs";
import { type RankIconId } from "@hooks/state";
import { type LadderBoard } from "./ladderView";

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
    board?: LadderBoard;
}

// Defense ladder: a military insignia progression (one chevron -> crown).
const DEFENSE_ICONS: Record<RankIconId, React.ReactNode> = {
    rank5: GLYPHS["rank-5-lord"],
    rank4: GLYPHS["rank-4-broker"],
    rank3: GLYPHS["rank-3-tycoon"],
    rank2: GLYPHS["rank-2-operator"],
    rank1: GLYPHS["rank-1-survivor"],
};

// Shadow ladder: each tier draws its own scheme (skimmed aid crate, siphoned
// fuel, the fixer's web, a tender briefcase, war as a business plan).
const SHADOW_ICONS: Record<RankIconId, React.ReactNode> = {
    rank5: GLYPHS["shadow-rank-5-capitalist"],
    rank4: GLYPHS["shadow-rank-4-tender"],
    rank3: GLYPHS["shadow-rank-3-fixer"],
    rank2: GLYPHS["shadow-rank-2-siphoner"],
    rank1: GLYPHS["shadow-rank-1-skimmer"],
};

// Prosperity ladder: a mayor's career from a tent among rubble to a sun over
// an intact skyline (tent, shelter, lit blocks, heart over the crowd, sunrise).
const PROSPERITY_ICONS: Record<RankIconId, React.ReactNode> = {
    rank5: GLYPHS["prosperity-rank-5-unbroken"],
    rank4: GLYPHS["prosperity-rank-4-mayor"],
    rank3: GLYPHS["prosperity-rank-3-manager"],
    rank2: GLYPHS["prosperity-rank-2-shelter"],
    rank1: GLYPHS["prosperity-rank-1-warden"],
};

/** Artwork per ladder — a new board brings its own set here. */
const BOARD_ICONS: Record<LadderBoard, Record<RankIconId, React.ReactNode>> = {
    defense: DEFENSE_ICONS,
    shadow: SHADOW_ICONS,
    prosperity: PROSPERITY_ICONS,
};

export const RankIcon: React.FC<RankIconProps> = ({ icon, isVacant, board = "defense" }) => {
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

    // --iconColor drives the glyph paint (see iconGlyphs.tsx). Under the old
    // masked-div Icon the tier color never reached the artwork (the mask used
    // the :root white --iconColor; the container's `color` was dead styling) —
    // with inline glyphs the documented tier colors finally apply.
    const svgStyle = {
        width: "100%",
        height: "100%",
        display: "block",
        // Paint must be a CSS declaration (var() is invalid in an SVG
        // presentation attribute); glyphs inherit this fill.
        fill: "var(--iconColor)",
        "--iconColor": color,
    } as React.CSSProperties;

    return (
        <div style={containerStyle}>
            <svg viewBox="0 0 24 24" style={svgStyle}>
                {BOARD_ICONS[board][icon]}
            </svg>
        </div>
    );
};
