import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

export const INACTIVE_ANNOUNCE_JSON = "{\"Active\":false,\"PublishAtUnix\":0}";

/**
 * Pre-publish update announce state from UpdateAnnounceSystem (server /config/announce):
 * {"Active": boolean, "PublishAtUnix": number}. Active while a mod update is scheduled to
 * publish within the announce window; feeds the countdown badge and the ModUpdatePending modal.
 */
export const updateAnnounceState$ = bindCivicValue(B.UpdateAnnounceState, INACTIVE_ANNOUNCE_JSON);

export function dismissModUpdatePending(): void {
    triggerCivic(B.DismissModUpdatePending);
}
