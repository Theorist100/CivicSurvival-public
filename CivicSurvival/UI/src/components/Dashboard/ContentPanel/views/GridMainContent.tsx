/**
 * GridMainContent - Power Grid Info + Controls
 * GRID domain → MAIN view
 *
 * Uses reusable section components from components/grid/sections
 */

import React, { memo } from "react";
import { Column } from "@coherent";
import { useTheme } from "@themes";
import { InfoSection, GridOpsSection } from "../../../grid/sections";

export const GridMainContent = memo(() => {
    const theme = useTheme();

    return (
        <div style={{
            display: "flex",
            height: "100%",
            overflow: "hidden" as const,
            position: "relative" as const,
        }}>
            {/* Left Column - INFO */}
            <Column style={{
                width: "280rem",
                minWidth: "280rem",
                maxWidth: "280rem",
                borderRight: `2rem solid ${theme.colors.border}`,
                // The column is a hard 280rem: at padding md the balance rows had ~124rem
                // left for the label, and the two-word ones (DEMAND THROTTLE, AVAILABLE
                // OUTPUT) need ~135rem — they wrapped and stretched the panel past its
                // scroll bound.
                padding: theme.spacing.sm,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                flexShrink: 0,
            }}>
                <InfoSection />
            </Column>

            {/* Right Column - CONTROLS */}
            <Column style={{
                flex: 1,
                padding: theme.spacing.md,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                minWidth: 0,
            }}>
                <GridOpsSection />
            </Column>
        </div>
    );
});
GridMainContent.displayName = "GridMainContent";
