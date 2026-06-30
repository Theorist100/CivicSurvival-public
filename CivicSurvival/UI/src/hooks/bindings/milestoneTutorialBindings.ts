/**
 * Milestone Tutorial UI Bindings
 *
 * Connects UI to MilestoneTutorialSystem C# backend.
 * 8 modals that fire once per save at key game moments.
 */

import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";

// ===== Dismiss Triggers =====

export function dismissWarBegins(): void {
    triggerCivic(B.DismissWarBegins);
}

export function dismissFirstDonorAid(): void {
    triggerCivic(B.DismissFirstDonorAid);
}

export function dismissFirstSuccessfulDefense(): void {
    triggerCivic(B.DismissFirstSuccessfulDefense);
}

export function dismissGeneratorEra(): void {
    triggerCivic(B.DismissGeneratorEra);
}

export function dismissSpotterAlert(): void {
    triggerCivic(B.DismissSpotterAlert);
}

export function dismissCorruptionOffer(): void {
    triggerCivic(B.DismissCorruptionOffer);
}

export function dismissGhostTown(): void {
    triggerCivic(B.DismissGhostTown);
}

export function dismissWhoStaysBehind(): void {
    triggerCivic(B.DismissWhoStaysBehind);
}
