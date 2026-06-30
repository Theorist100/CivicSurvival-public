/**
 * Hook for refugee/collapse modals (ISP compliant).
 * Only subscribes to refugee-related bindings.
 */

import { useMemo } from "react";
import { useSafeNumber } from "../useSafeBinding";
import {
    refugeesReceived$,
} from "../bindings/refugeeBindings";

export interface RefugeeScenarioState {
    refugeesReceived: number;
}

export function useRefugeeScenario(): RefugeeScenarioState {
    const refugeesReceived = useSafeNumber(refugeesReceived$, 0);

    return useMemo(() => ({
        refugeesReceived,
    }), [refugeesReceived]);
}
