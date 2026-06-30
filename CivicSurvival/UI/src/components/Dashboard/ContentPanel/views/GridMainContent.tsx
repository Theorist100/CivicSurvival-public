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
                padding: theme.spacing.md,
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
