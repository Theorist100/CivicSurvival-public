import { useMemo } from "react";
import * as settingsActions from "../../actions/settingsActions";

export function useSettingsActions() {
    return useMemo(() => settingsActions, []);
}
