import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { RequestResult } from "../../types/dtoSubTypes";
import { useRequestAction } from "./useRequestAction";

type OptimisticChoiceResult<T> = {
    /** optimistic pick if one is in flight, otherwise the authoritative value. */
    shown: T;
    /** false: same value as shown, or a command is already in flight on the key. */
    pick: (value: T) => boolean;
    isPending: boolean;
    /** reject reason of this instance's own last pick (proxied from useRequestAction). */
    failureReasonId: string;
};

/**
 * Optimistic value-picker over useRequestAction: click a choice → highlight it
 * immediately → reconcile against the backend on the next DTO snapshot.
 *
 * The single reconcile rule (drop the optimistic value once the base hook
 * leaves pending) closes both outcomes: a terminal result — success OR failed —
 * ships in the same DTO snapshot as the authoritative value, so clearing the
 * optimistic state lands either on the new value (success) or the old one
 * (reject — the highlight honestly rolls back).
 *
 * Limitation: while base.isPending (≤ ~500ms DTO throttle on the sync backend),
 * a repeat pick returns false — the click is dropped rather than issuing a
 * second ambiguous command on the same key.
 */
export function useOptimisticChoice<T>(
    authoritative: T,
    send: (value: T) => void,
    result: RequestResult | undefined
): OptimisticChoiceResult<T> {
    const pendingValueRef = useRef<T | null>(null);
    const [pendingValue, setPendingValue] = useState<T | null>(null);

    const base = useRequestAction(() => {
        const value = pendingValueRef.current;
        if (value === null) return false;
        send(value);
        return true;
    }, result);

    const shown = pendingValue ?? authoritative;

    const pick = useCallback((value: T): boolean => {
        if (value === shown) return false;
        pendingValueRef.current = value;
        const emitted = base.execute();
        if (emitted) setPendingValue(value);
        return emitted;
    }, [shown, base]);

    useEffect(() => {
        if (pendingValue === null) return;
        // Success caught up early, or the base hook left pending (terminal
        // result of either kind) → drop the optimistic value.
        if (pendingValue === authoritative || !base.isPending) {
            setPendingValue(null);
        }
    }, [pendingValue, authoritative, base.isPending]);

    return useMemo(
        () => ({ shown, pick, isPending: base.isPending, failureReasonId: base.failureReasonId }),
        [shown, pick, base.isPending, base.failureReasonId]
    );
}
