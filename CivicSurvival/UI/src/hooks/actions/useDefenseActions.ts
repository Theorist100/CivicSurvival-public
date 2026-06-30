import { useMemo } from "react";
import * as defenseActions from "../../actions/defenseActions";

export function useDefenseActions() {
    return useMemo(() => defenseActions, []);
}
