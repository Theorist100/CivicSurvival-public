import { useMemo } from "react";
import * as donorActions from "../../actions/donorActions";

export function useDonorActions() {
    return useMemo(() => donorActions, []);
}
