/**
 * Toast domain actions — accept, reject, dismiss toast notifications.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import { type ToastId } from "../types/semantic";

export const acceptToast = (toastId: ToastId): void =>
    triggerCivic(B.AcceptToast, toastId);

export const rejectToast = (toastId: ToastId): void =>
    triggerCivic(B.RejectToast, toastId);

export const dismissToast = (toastId: ToastId): void =>
    triggerCivic(B.DismissToast, toastId);
