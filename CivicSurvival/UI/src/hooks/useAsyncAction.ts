/**
 * useAsyncAction - Hook for wrapping trigger actions with pending state
 * Provides double-click protection for fire-and-forget UI commands.
 * Request/result workflows should use useRequestAction.
 */

import { useState, useCallback, useRef, useEffect } from "react";

interface AsyncActionOptions {
    /** Safety timeout in ms before auto-reset (default: 5000) */
    timeout?: number;
}

interface AsyncActionResult {
    /** Whether action is currently pending */
    isPending: boolean;
    /** Wrapped action that sets pending state */
    execute: () => void;
    /** Manually reset pending state */
    reset: () => void;
}

/**
 * Wraps a trigger action with pending state management.
 *
 * @example
 * ```tsx
 * const { isPending, execute } = useAsyncAction(
 *     () => trigger("CivicSurvival", "buyAA", position),
 *     { timeout: 3000 }
 * );
 *
 * <ActionButton onClick={execute} isPending={isPending}>
 *     Buy AA
 * </ActionButton>
 * ```
 */
export function useAsyncAction(
    action: () => void,
    options: AsyncActionOptions = {}
): AsyncActionResult {
    const { timeout = 5000 } = options;
    const [isPending, setIsPending] = useState(false);
    const pendingRef = useRef(false);
    const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const actionRef = useRef(action);
    actionRef.current = action;

    // Cleanup on unmount
    useEffect(() => {
        return () => {
            if (timeoutRef.current) {
                clearTimeout(timeoutRef.current);
            }
        };
    }, []);

    const reset = useCallback(() => {
        pendingRef.current = false;
        setIsPending(false);
        if (timeoutRef.current) {
            clearTimeout(timeoutRef.current);
            timeoutRef.current = null;
        }
    }, []);

    const execute = useCallback(() => {
        if (pendingRef.current) return;

        pendingRef.current = true;
        setIsPending(true);

        actionRef.current();

        timeoutRef.current = setTimeout(() => {
            pendingRef.current = false;
            setIsPending(false);
            timeoutRef.current = null;
        }, timeout);
    }, [timeout]);

    return { isPending, execute, reset };
}
