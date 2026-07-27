/**
 * Intro Scenario UI Bindings
 *
 * Connects UI to IntroScenarioSystem C# backend.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";
import { IntroPhase, type IntroPhaseValue } from "../../types/sharedEnums.generated";

export { IntroPhase, type IntroPhaseValue };

// ===== Value Bindings =====

/** Current intro phase (0 = None, 1 = Modal, etc.) */
export const introPhase$ = bindCivicValue(B.IntroPhase, 0);

/** Whether game HUD should be visible (hidden during intro) */
export const introHudVisible$ = bindCivicValue(B.IntroHudVisible, true);

// ===== Triggers =====

/**
 * Called when player clicks "Accept Reality" button.
 * Starts the intro sequence (explosion -> siren -> attack).
 */
export function acceptReality(): void {
    triggerCivic(B.OnAcceptReality);
}

