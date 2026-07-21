import { bindCivicValue } from "../typedBinding.generated";
import { B } from "../bindingNames.generated";
import { useDtoBinding } from "../domain/useDtoBinding";
import { DEFAULT_DEBUG_TOGGLE_SNAPSHOT, isDebugToggleSnapshot } from "../../types/domainDtos";

const toggleStates$ = bindCivicValue(B.DebugToggleStates, "{}");

export const useDebugToggleStates = () =>
    useDtoBinding(toggleStates$, isDebugToggleSnapshot, { debugName: "debugToggleStates", defaultValue: DEFAULT_DEBUG_TOGGLE_SNAPSHOT });
