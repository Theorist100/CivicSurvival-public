import { useMemo } from "react";
import {
    evacuation,
    sbuVisit,
    toggleCounterOSINT,
} from "../../actions/defenseActions";

export function useSpotterActions() {
    return useMemo(() => ({
        evacuation,
        sbuVisit,
        toggleCounterOSINT,
    }), []);
}
