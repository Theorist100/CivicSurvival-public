import { useMemo } from "react";
import { setTelemetryEnabled } from "../../actions/settingsActions";
import { setPlayerNickname, toggleGlobalConnection } from "../bindings/networkBindings";

export function useNetworkActions() {
    return useMemo(() => ({
        setPlayerNickname,
        setTelemetryEnabled,
        toggleGlobalConnection,
    }), []);
}
