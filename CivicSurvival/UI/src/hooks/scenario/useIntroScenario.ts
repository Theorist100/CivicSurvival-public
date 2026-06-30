/**
 * Hook for intro modal visibility (ISP compliant).
 * Only subscribes to intro-related bindings.
 */

import { useMemo } from "react";
import { useSafeNumber, useSafeBoolean } from "../useSafeBinding";
import {
    IntroPhase,
    type IntroPhaseValue,
    introHudVisible$,
    introPhase$,
} from "../bindings/introBindings";

export interface IntroScenarioState {
    introHudVisible: boolean;
    introPhase: IntroPhaseValue;
}

export function useIntroScenario(): IntroScenarioState {
    const introHudVisible = useSafeBoolean(introHudVisible$, true);
    const introPhase = useSafeNumber(introPhase$, IntroPhase.None) as IntroPhaseValue;

    return useMemo(() => ({
        introHudVisible,
        introPhase,
    }), [introHudVisible, introPhase]);
}
