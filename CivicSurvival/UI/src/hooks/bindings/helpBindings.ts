/**
 * Help state bindings - tracks whether user has seen help modals.
 * Used for highlighting "?" button when help is available.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

// ===== Value Bindings (read from C#) =====

/** Whether user has seen Grid domain help */
export const gridHelpSeen$ = bindCivicValue(B.GridHelpSeen, false);

/** Whether user has seen Shadow domain help */
export const shadowHelpSeen$ = bindCivicValue(B.ShadowHelpSeen, false);

// ===== Triggers (call to C#) =====

/** Mark Grid help as seen */
export function markGridHelpSeen(): void {
    triggerCivic(B.MarkGridHelpSeen);
}

/** Mark Shadow help as seen */
export function markShadowHelpSeen(): void {
    triggerCivic(B.MarkShadowHelpSeen);
}
