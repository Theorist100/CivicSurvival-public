/**
 * CrowdBoard — the strata of one axis laid out side by side, width ∝ count.
 */

import React, { memo } from "react";
import { Row } from "../../../../coherent";
import { useTheme } from "../../../../../themes";
import { type Axis, type ArchetypeId } from "./opinionData";
import { StratumCard } from "./StratumCard";

interface CrowdBoardProps {
    axis: Axis;
    selectedId: string;
    householdsPerDot: number;
    dimDoubters: boolean;
    /** The currently deployed hero archetype, or null when none is deployed. */
    currentArchetypeId: ArchetypeId | null;
    /** Live contacts with a fogged seam — they belong to no card, so every card must know. */
    seamUnknownCount: number;
    onSelect: (id: string) => void;
    /** Forwarded onto the root so the Column gap emulation (injected marginBottom
     *  via cloneElement) lands — see CityOpinionLine. */
    style?: React.CSSProperties;
}

export const CrowdBoard = memo(({
    axis, selectedId, householdsPerDot, dimDoubters, currentArchetypeId, seamUnknownCount, onSelect, style,
}: CrowdBoardProps) => {
    const theme = useTheme();
    return (
        <Row gap={theme.spacing.md} wrap="wrap" align="stretch" style={{ width: "100%", ...style }}>
            {axis.strata.map((stratum) => (
                <StratumCard
                    key={stratum.id}
                    axis={axis}
                    stratum={stratum}
                    selected={stratum.id === selectedId}
                    householdsPerDot={householdsPerDot}
                    dimDoubters={dimDoubters}
                    currentArchetypeId={currentArchetypeId}
                    seamUnknownCount={seamUnknownCount}
                    onSelect={onSelect}
                />
            ))}
        </Row>
    );
});

CrowdBoard.displayName = "CrowdBoard";
