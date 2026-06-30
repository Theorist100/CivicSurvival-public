/**
 * GlobalOperationsContent - Global Operations Panel (Coming Soon)
 * OPS domain → CURRENT view
 *
 * Shows 3 global operations that players can opt-in to:
 * - Economic (Arctic Snap, Oil Embargo, etc.)
 * - Military (Swarm Defense, Missile Alert, etc.)
 * - Infrastructure (Outbreak Response, Heat Wave, etc.)
 */

import React, { memo } from "react";
import { Column } from "@coherent";
import { useTheme, useAccents } from "@themes";
import { ComingSoonOverlay } from "../../../shared/ui";
import { MOCK_OPERATIONS, type OperationType } from "./global-operations-content/operations";
import {
    OperationCard,
    OperationsHeader,
} from "./global-operations-content/GlobalOperationsSections";
import { assertNever } from "../../../../utils/exhaustive";

const FEATURE_LABELS = [
    "Opt-in seasonal challenges",
    "Compete with mayors worldwide",
    "Earn unique rewards",
];

export const GlobalOperationsContent = memo(() => {
    const theme = useTheme();
    const accents = useAccents();


    // Get accent color for operation type
    const getTypeColor = (type: OperationType): string => {
        switch (type) {
            case "economic": return accents.operations.accent;
            case "military": return accents.crisis.accent;
            case "infrastructure": return accents.resilience.accent;
            default: return assertNever(type, "getTypeColor.type");
        }
    };

    // Dimmed content style - minimal dimming, just not interactive
    const dimmedStyle: React.CSSProperties = {
        opacity: 0.95,
    };

    const containerStyle: React.CSSProperties = {
        padding: "16rem",
        position: "relative" as const,
        minHeight: "500rem",
    };

    // Header style
    const headerStyle: React.CSSProperties = {
        fontSize: "12rem",
        fontWeight: 700,
        color: theme.colors.textMuted,
        textTransform: "uppercase" as const,
        letterSpacing: "0.5rem",
        marginBottom: "12rem",
        paddingBottom: "8rem",
        borderBottom: `2rem solid ${theme.colors.border}`,
    };

    return (
        <Column style={containerStyle}>
            <ComingSoonOverlay
                badge="COMING SOON"
                title="GLOBAL OPERATIONS"
                subtitle={"\"Unite. Survive. Prevail.\""}
                features={FEATURE_LABELS}
                accent={getTypeColor("infrastructure")}
            />

            {/* Dimmed Content */}
            <div style={dimmedStyle}>
                <OperationsHeader headerStyle={headerStyle} />

                {/* Operation Cards */}
                {MOCK_OPERATIONS.map((operation) => (
                    <OperationCard
                        key={operation.id}
                        operation={operation}
                        getTypeColor={getTypeColor}
                    />
                ))}
            </div>
        </Column>
    );
});
GlobalOperationsContent.displayName = "GlobalOperationsContent";
