/**
 * Shock Act UI Bindings
 *
 * Connects UI to ShockActSystem C# backend.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

// ===== State Bindings =====

/** Is Shock Act currently active */
export const shockActActive$ = bindCivicValue(B.ShockActActive, false);

/** Current tax multiplier (0.2 during shock) */
export const taxMultiplier$ = bindCivicValue(B.TaxMultiplier, 1);

/** Current day of crisis (1, 2, 3...) - 0 when not in crisis */
export const crisisDayNumber$ = bindCivicValue(B.CrisisDayNumber, 0);

// exodusRatePercentPerDay is published through AttentionState (single source of truth)

/** Are loans available */
export const loansAvailable$ = bindCivicValue(B.LoansAvailable, true);

// ===== Modal Dismiss Triggers =====

export function dismissFirstStrike(): void {
    triggerCivic(B.DismissFirstStrike);
}

export function dismissExodusWarning(): void {
    triggerCivic(B.DismissExodusWarning);
}

// ===== Tab-Open Analytics Signals =====
// Notify C# of GRID/SHADOW tab opens so CrisisTutorialSystem can record
// "first open in Crisis" telemetry (no UI side-effect — analytics only).

export function notifyOpenGridTab(): void {
    triggerCivic(B.OnOpenGridTab);
}

export function notifyOpenShadowTab(): void {
    triggerCivic(B.OnOpenShadowTab);
}
