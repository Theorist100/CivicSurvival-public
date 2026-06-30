/**
 * ArenaPreviewContent - Coming Soon War Room (Polis Wars)
 * ARENA domain → PREVIEW view
 */

import React, { memo } from "react";
import { Column } from "@coherent";
import { ComingSoonOverlay } from "../../../shared/ui";
import { useAccents } from "@themes";
import {
    ArenaArsenalCard,
    ArenaBattleStatusCard,
    ArenaEnterCard,
    ArenaStabilityComparison,
} from "./arena-preview-content/ArenaPreviewSections";

const FEATURE_LABELS = [
    "30-40 min PvP battles",
    "Your save stays safe",
    "Matchmaking by city power",
];

export const ArenaPreviewContent = memo(() => {
    const accents = useAccents();

    // Dimmed content style - minimal dimming, just not interactive
    const dimmedStyle: React.CSSProperties = {
        opacity: 0.95,
    };

    const containerStyle: React.CSSProperties = {
        padding: "16rem",
        position: "relative" as const,
        minHeight: "500rem",
    };

    return (
        <Column style={containerStyle}>
            <ComingSoonOverlay
                badge="COMING SOON"
                title="GRID WARFARE"
                subtitle={"\"Build Locally. Fight Globally.\""}
                features={FEATURE_LABELS}
                accent={accents.resilience.accent}
            />

            {/* Dimmed War Room UI */}
            <div style={dimmedStyle}>
                <ArenaBattleStatusCard />
                <ArenaStabilityComparison />
                <ArenaArsenalCard />
                <ArenaEnterCard />
            </div>
        </Column>
    );
});
ArenaPreviewContent.displayName = "ArenaPreviewContent";
