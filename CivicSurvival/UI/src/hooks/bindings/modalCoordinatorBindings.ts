import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

export const EMPTY_MODAL_SNAPSHOT_JSON = "{\"ActiveId\":\"\",\"ActivePriority\":0,\"ActiveData\":null,\"Queue\":[],\"Version\":0}";

export const activeModalState$ = bindCivicValue(B.ActiveModalState, EMPTY_MODAL_SNAPSHOT_JSON);

export function dismissArrested(): void {
    triggerCivic(B.DismissArrested);
}

export function dismissModLoadFailure(): void {
    triggerCivic(B.DismissModLoadFailure);
}
