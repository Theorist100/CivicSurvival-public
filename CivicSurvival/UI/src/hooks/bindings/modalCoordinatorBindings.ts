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

/**
 * Closes whichever quest-outcome modal holds the slot (QuestCompleted or QuestFailed).
 * One trigger for both: they are mutually exclusive, and C# dismisses by id.
 */
export function dismissQuestResult(): void {
    triggerCivic(B.DismissQuestResult);
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

export type UpdateModalKind = "pending" | "restart";
export type UpdateModalAction = "shown" | "dismissed";

/**
 * Telemetry ack for the update-safety modals (ModUpdatePending / ModUpdatedRestart):
 * "shown" on render — the only proof the notice reached the player (a C#-side show
 * request alone can't tell; the React layer may be dead) — "dismissed" on the button.
 * C# whitelists the values.
 */
export function reportUpdateModalAction(modal: UpdateModalKind, action: UpdateModalAction): void {
    triggerCivic(B.ModUpdateModalAction, modal, action);
}
