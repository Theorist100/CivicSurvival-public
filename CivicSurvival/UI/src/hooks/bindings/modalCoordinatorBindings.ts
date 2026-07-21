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

export function dismissModUpdatedRestart(): void {
    triggerCivic(B.DismissModUpdatedRestart);
}

export type ModLoadFailureAction = "shown" | "continue" | "send";

/**
 * Telemetry ack for the ModLoadFailure modal: "shown" on render (proof the notice
 * reached the player's eyes), "continue"/"send" on the buttons. C# whitelists the
 * values; cause comes verbatim from the modal payload.
 */
export function reportModLoadFailureAction(action: ModLoadFailureAction, cause: string): void {
    triggerCivic(B.ModLoadFailureAction, action, cause);
}
