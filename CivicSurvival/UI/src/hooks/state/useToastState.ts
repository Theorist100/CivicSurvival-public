/**
 * Hook for toast/notification state.
 * Part of ISP refactor — focused on toasts and trust level.
 *
 * Uses useSafe* wrappers to handle Coherent UI race condition
 * where useValue() returns {} before C# bindings are ready.
 */

import { useMemo } from "react";
import {
    useNumberBinding,
    useValidatedJsonArray,
} from "../useSafeBinding";
import {
    toastsJson$,
    toastCount$,
    isToastData,
    type ToastData,
} from "../bindings/toastBindings";
import { useReputation } from "../domain";
import { combineBindingStates } from "../domain/combineBindingStates";
import { type BindingState } from "../domain/useDtoBinding";
import { useOptionalBinding } from "../useOptionalBinding";

// ============ Types ============

export interface ToastState {
    toasts: ToastData[];
    toastCount: number;
    trustReady: boolean;
    trustLevel: number;
    trustTier: string;
    isFrozen: boolean;
}

// ============ Hook ============

export function useToastState(): BindingState<ToastState> {
    const toastsState = useValidatedJsonArray(toastsJson$, isToastData, { debugName: "toastsJson" });
    const toastCountState = useNumberBinding(toastCount$, "toastCount");
    const repState = useReputation();
    const optionalReputation = useOptionalBinding(repState);

    return useMemo(() => {
        const repReady = optionalReputation.status === "available";
        const rep = repReady ? optionalReputation.data : null;

        return combineBindingStates({
        toasts: toastsState,
        toastCount: toastCountState,
        }, ({ toasts, toastCount }) => ({
            toasts,
            toastCount,
            trustReady: repReady,
            trustLevel: rep?.TrustLevel ?? 0,
            trustTier: rep?.TrustTier ?? "",
            isFrozen: rep?.IsFrozenOut ?? false,
        }));
    }, [toastsState, toastCountState, optionalReputation]);
}
