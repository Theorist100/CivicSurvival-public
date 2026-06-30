import { useMemo } from "react";
import * as cognitiveActions from "../../actions/cognitiveActions";
import {
    deployHero,
    recallHero,
    setHeroMode,
    setInternetMode,
    setNarrativeMode,
    setTelemarathonActive,
} from "../../actions/defenseActions";

export function useCognitiveActions() {
    return useMemo(() => ({
        ...cognitiveActions,
        deployHero,
        recallHero,
        setHeroMode,
        setInternetMode,
        setNarrativeMode,
        setTelemarathonActive,
    }), []);
}
