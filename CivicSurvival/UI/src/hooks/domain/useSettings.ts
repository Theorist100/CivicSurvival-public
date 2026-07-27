/**
 * Settings domain hook.
 */

import { settingsState$ } from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import { isSettingsDto } from "../../types/domainDtos";

export const useSettings = () =>
    useDtoBinding(settingsState$, isSettingsDto, { debugName: "settingsState" });

export const useSettingsState = useSettings;
