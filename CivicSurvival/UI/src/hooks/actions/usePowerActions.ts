import { useMemo } from "react";
import * as powerActions from "../../actions/powerActions";

export function usePowerActions() {
    return useMemo(() => powerActions, []);
}
