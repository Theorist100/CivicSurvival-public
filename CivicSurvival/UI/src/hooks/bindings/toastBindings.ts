/**
 * Toast Notification Bindings
 * Non-modal notifications — toast queue data + type + parse helper.
 * Trust data → useReputation() from hooks/domain.
 * Triggers → actions/toastActions.ts.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { B } from "../bindingNames.generated";
import { isToastDataDto, type ToastDataDto } from "../../types/domainDtos.generated";
import { type ToastId } from "../../types/semantic";

// ============================================================================
// TOAST DATA
// ============================================================================

// Mirrors C# Core/Types/ToastTypes.cs — every enum member must appear here AND in
// isToastType below, or its toasts are silently dropped by the JSON validator before
// render (live bug: DraftExemptionOffer was missing from this union since its C# type
// shipped, so draft-exemption offers never reached the player).
export type ToastType =
    | "ProcurementOffer"
    | "AuditorWarning"
    | "InsuranceClaim"
    | "SafetyAccident"
    | "DraftExemptionOffer"
    | "QuestOffer";
export type ToastPriority = 0 | 1 | 2 | 3;

/**
 * The Trust header over the toast stack contextualizes CORRUPTION toasts only —
 * a quest call must not drag the shadow-reputation meter onto the screen.
 */
export const isCorruptionToastType = (type: ToastType): boolean => type !== "QuestOffer";

/**
 * Narrow generated wire DTO — Type / Priority retain their canonical
 * literal-union shape and Id carries the semantic brand so consumers (icon
 * map, border color tier, action triggers) stay type-safe. Wire layout
 * lives in ui-dto.contract.yaml.
 */
export interface ToastData extends Omit<ToastDataDto, "Id" | "Type" | "Priority"> {
    Id: ToastId;
    Type: ToastType;
    Priority: ToastPriority;
}

// Toast queue (JSON array from C#)
export const toastsJson$ = bindCivicValue(B.ToastsJson, "[]");
export const toastCount$ = bindCivicValue(B.ToastCount, 0);

const isToastType = (value: string): value is ToastType =>
    value === "ProcurementOffer"
    || value === "AuditorWarning"
    || value === "InsuranceClaim"
    || value === "SafetyAccident"
    || value === "DraftExemptionOffer"
    || value === "QuestOffer";

const isToastPriority = (value: number): value is ToastPriority =>
    value === 0 || value === 1 || value === 2 || value === 3;

export const isToastData = (value: unknown): value is ToastData => {
    if (!isToastDataDto(value)) return false;
    return isToastType(value.Type) && isToastPriority(value.Priority);
};
