/**
 * OpinionBoard — the cognitive war-room's PSYOPS screen, mounted under the WAR
 * domain's PSYOPS view. The domain view-menu (RADAR / DEFENSE / PSYOPS / IPSO /
 * INTEL / ALLIES, ContentPanel) owns the war navigation, so the board draws NO
 * tab row of its own — the enemy-intelligence dossier lives on the sibling IPSO
 * tab (IpsoContent). The board carries only the operate screen. The DIAGNOSTICS
 * trigger has been removed from the header; its INFO subpanel (DiagnosticsOverlay)
 * is kept in source, pending a decision on where to re-home it.
 *
 * The board is available in peacetime: seeing the city and preparing is never
 * war-gated. Only the enemy's attacks are gated, and each action's own trigger
 * warns on click when it is not yet available (e.g. "Available from Crisis").
 */

import React, { memo } from "react";
import { Column } from "../../../../coherent";
import { useTheme } from "../../../../../themes";
import { type useCognitiveActions } from "@hooks/actions";
import { PsyopsScreen } from "./PsyopsScreen";

interface OpinionBoardProps {
    actions: ReturnType<typeof useCognitiveActions>;
}

export const OpinionBoard = memo(({ actions }: OpinionBoardProps) => {
    const theme = useTheme();

    return (
        <Column style={{
            position: "relative",
            height: "100%",
            overflow: "hidden",
            padding: theme.spacing.md,
            minHeight: "100rem",
        }}>
            {/* Operate screen — fills the remaining panel height, scrolls internally. */}
            <Column align="stretch" style={{ flex: 1, minHeight: 0, overflowY: "auto", overflowX: "hidden" }}>
                <PsyopsScreen actions={actions} disabled={false} />
            </Column>
        </Column>
    );
});

OpinionBoard.displayName = "OpinionBoard";
