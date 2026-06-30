/**
 * CognitiveWarfareContent - Cognitive Warfare Info + Operations
 * WAR domain → PSYOPS view
 *
 * Layout: Left column (info) + Right column (operations)
 */

import React, { memo } from "react";
import { Column } from "@coherent";
import { useTheme } from "@themes";
import { CognitiveInfoSection, CognitiveOpsSection } from "../../../war/sections/cognitive";
import { useCognitiveActions } from "../../../../hooks/actions";
import { GlassCase } from "../../../shared/ui";

export const CognitiveWarfareContent = memo(() => {
    const theme = useTheme();
    const actions = useCognitiveActions();

    return (
        <GlassCase
            feature="Cognitive"
            name="Cognitive Warfare"
            description="Telemarathon broadcasts, narrative tone (Soothing/Alarmist/Realistic), media trust and audience fatigue. Shape how citizens react to the war and stretch civic morale further."
        >
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
                <CognitiveInfoSection />
            </Column>

            {/* Right Column - OPERATIONS */}
            <Column style={{
                flex: 1,
                padding: theme.spacing.md,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                minWidth: 0,
            }}>
                <CognitiveOpsSection actions={actions} />
            </Column>
        </div>
        </GlassCase>
    );
});
CognitiveWarfareContent.displayName = "CognitiveWarfareContent";
