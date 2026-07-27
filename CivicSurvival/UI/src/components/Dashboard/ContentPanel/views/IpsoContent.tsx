/**
 * IpsoContent — WAR domain → IPSO view.
 *
 * The enemy-intelligence dossier of the cognitive layer (threat forecast, active
 * raids + interception windows, enemy delivery channels, dominant narrative). It
 * is the sibling of the PSYOPS board: PSYOPS operates, IPSO watches the enemy.
 * Split into its own top-level war tab so the board no longer carries a second,
 * duplicate sub-tab row. Distinct from the INTEL view (physical/military intel).
 */

import React, { memo } from "react";
import { Column } from "@coherent";
import { useTheme } from "@themes";
import { IntelScreen } from "../../../war/sections/cognitive";
import { GlassCase } from "../../../shared/ui";

export const IpsoContent = memo(() => {
    const theme = useTheme();

    return (
        <GlassCase
            feature="Cognitive"
            name="Enemy Intelligence (IPSO)"
            description="The enemy's cognitive-warfare picture: incoming PSYOPS raids and their interception windows, delivery channels, and the narrative dominating the city's mind."
        >
            <Column
                style={{
                    height: "100%",
                    overflow: "hidden",
                    // sm, not md: the IPSO stack is the tallest war view and must fit
                    // the fixed 750rem panel without the outer column scrolling.
                    padding: theme.spacing.sm,
                    minHeight: "100rem",
                }}
            >
                <Column align="stretch" style={{ flex: 1, minHeight: 0, overflowY: "auto", overflowX: "hidden" }}>
                    <IntelScreen />
                </Column>
            </Column>
        </GlassCase>
    );
});

IpsoContent.displayName = "IpsoContent";
