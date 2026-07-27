/**
 * Hook for endgame modals (Victory + WarFatigue + Defeat) (ISP compliant).
 * Only subscribes to endgame-related bindings.
 */

import { useMemo } from "react";
import { useSafeBoolean, useSafeNumber } from "../useSafeBinding";
import {
    defeatCause$,
    daysSurvived$,
    wavesDefended$,
    missilesIntercepted$,
    blackoutRecoveries$,
    buildingsDamaged$,
    totalRefugees$,
    populationPercent$,
    populationMeasured$,
} from "../bindings/scenarioDirectorBindings";

export interface EndgameScenarioState {
    // Defeat data
    defeatCause: number;
    daysSurvived: number;

    // Stats (shared by Victory + WarFatigue + Defeat)
    wavesDefended: number;
    missilesIntercepted: number;
    blackoutRecoveries: number;
    buildingsDamaged: number;
    totalRefugees: number;
    populationPercent: number;
    /** False = populationPercent is a stale last-good value; render a dash, not a number. */
    populationMeasured: boolean;
}

export function useEndgameScenario(): EndgameScenarioState {
    const defeatCause = useSafeNumber(defeatCause$, 0);
    const daysSurvived = useSafeNumber(daysSurvived$, 0);

    const wavesDefended = useSafeNumber(wavesDefended$, 0);
    const missilesIntercepted = useSafeNumber(missilesIntercepted$, 0);
    const blackoutRecoveries = useSafeNumber(blackoutRecoveries$, 0);
    const buildingsDamaged = useSafeNumber(buildingsDamaged$, 0);
    const totalRefugees = useSafeNumber(totalRefugees$, 0);
    const populationPercent = useSafeNumber(populationPercent$, 1);
    const populationMeasured = useSafeBoolean(populationMeasured$, false);

    return useMemo(() => ({
        defeatCause,
        daysSurvived,
        wavesDefended,
        missilesIntercepted,
        blackoutRecoveries,
        buildingsDamaged,
        totalRefugees,
        populationPercent,
        populationMeasured,
    }), [defeatCause, daysSurvived, wavesDefended, missilesIntercepted, blackoutRecoveries, buildingsDamaged, totalRefugees, populationPercent, populationMeasured]);
}
