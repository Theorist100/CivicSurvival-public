/**
 * Hook for endgame modals (Victory + WarFatigue + Defeat) (ISP compliant).
 * Only subscribes to endgame-related bindings.
 */

import { useMemo } from "react";
import { useSafeNumber } from "../useSafeBinding";
import {
    defeatCause$,
    daysSurvived$,
    wavesDefended$,
    missilesIntercepted$,
    blackoutRecoveries$,
    buildingsDamaged$,
    totalRefugees$,
    populationPercent$,
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

    return useMemo(() => ({
        defeatCause,
        daysSurvived,
        wavesDefended,
        missilesIntercepted,
        blackoutRecoveries,
        buildingsDamaged,
        totalRefugees,
        populationPercent,
    }), [defeatCause, daysSurvived, wavesDefended, missilesIntercepted, blackoutRecoveries, buildingsDamaged, totalRefugees, populationPercent]);
}
