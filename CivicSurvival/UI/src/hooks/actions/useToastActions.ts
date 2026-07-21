import { useMemo } from "react";
import * as toastActions from "../../actions/toastActions";

export function useToastActions() {
    return useMemo(() => toastActions, []);
}
