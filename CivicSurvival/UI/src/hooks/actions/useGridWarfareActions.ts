import { useMemo } from "react";
import * as gridWarfareActions from "../../actions/gridWarfareActions";

export function useGridWarfareActions() {
    return useMemo(() => gridWarfareActions, []);
}
