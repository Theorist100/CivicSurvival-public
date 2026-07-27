/**
 * CognitiveWarfareContent — WAR domain → PSYOPS view.
 *
 * Hosts the Opinion Board: a sub-tabbed cognitive war-room (PSYOPS operate +
 * INTEL enemy intelligence) that reads the population as a crowd by stratum and
 * deploys countermeasures. Replaces the former two-column Info + Sandwich
 * layout — every working binding moved into the board's screens.
 */

import React, { memo } from "react";
import { OpinionBoard } from "../../../war/sections/cognitive";
import { useCognitiveActions } from "../../../../hooks/actions";
import { GlassCase } from "../../../shared/ui";

export const CognitiveWarfareContent = memo(() => {
    const actions = useCognitiveActions();

    return (
        <GlassCase
            feature="Cognitive"
            name="Cognitive Warfare"
            description="Read the city as a crowd split by allegiance and deploy countermeasures per stratum — broadcasts, relief, the hero, and the network switch — to keep opinion from being captured by the enemy."
        >
            <OpinionBoard actions={actions} />
        </GlassCase>
    );
});

CognitiveWarfareContent.displayName = "CognitiveWarfareContent";
