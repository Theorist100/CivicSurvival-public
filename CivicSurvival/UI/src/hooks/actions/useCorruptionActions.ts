import { useMemo } from "react";
import * as corruptionActions from "../../actions/corruptionActions";
import {
    acceptOfficialContract,
    acceptShadyContract,
    declineProcurement,
} from "../bindings/procurementBindings";

export function useCorruptionActions() {
    return useMemo(() => ({
        ...corruptionActions,
        acceptOfficialContract,
        acceptShadyContract,
        declineProcurement,
    }), []);
}
