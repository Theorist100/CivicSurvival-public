/**
 * NewsMainContent - Chipper Feed + The Resistor
 * NEWS domain → MAIN view
 * Layout: Left (Chipper narrow) + Right (Resistor wide)
 *
 * Uses reusable section components from components/news/sections
 */

import React, { memo } from "react";
import { Column } from "@coherent";
import { useTheme } from "@themes";
import { ChipperSection, HeraldSection } from "../../../news/sections";

export const NewsMainContent = memo(() => {
    const theme = useTheme();

    return (
        <div style={{
            display: "flex",
            height: "100%",
            overflow: "hidden" as const,
            position: "relative" as const,
        }}>
            {/* Left Column - CHIPPER */}
            <Column style={{
                width: "280rem",
                minWidth: "280rem",
                maxWidth: "280rem",
                borderRight: `2rem solid ${theme.colors.border}`,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                flexShrink: 0,
            }}>
                <ChipperSection />
            </Column>

            {/* Right Column - THE RESISTOR */}
            <Column style={{
                flex: 1,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                minWidth: 0,
            }}>
                <HeraldSection />
            </Column>
        </div>
    );
});
NewsMainContent.displayName = "NewsMainContent";
