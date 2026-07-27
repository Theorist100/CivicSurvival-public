import { useMemo } from "react";
import * as intelActions from "../../actions/intelActions";

export function useIntelActions() {
    return useMemo(() => intelActions, []);
}
