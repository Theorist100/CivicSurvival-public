/**
 * Refugee Crisis UI Bindings
 *
 * Connects UI to RefugeeInfluxSystem C# backend.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

// ===== State Bindings =====

/** Total refugees received */
export const refugeesReceived$ = bindCivicValue(B.RefugeesReceived, 0);

/** Hours remaining in refugee wave */
export const refugeeHoursRemaining$ = bindCivicValue(B.RefugeeHoursRemaining, 0);

/** Number of refugee households in city (for budget display) - CDI-3 */
export const refugeeHouseholdCount$ = bindCivicValue(B.RefugeeHouseholdCount, 0);

// ===== Modal Dismiss Triggers =====

export function dismissRefugeeModal(): void {
    triggerCivic(B.DismissRefugeeModal);
}

export function dismissCollapseModal(): void {
    triggerCivic(B.DismissCollapseModal);
}
