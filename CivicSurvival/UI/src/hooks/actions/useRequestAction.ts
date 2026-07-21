import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { RequestResult } from "../../types/dtoSubTypes";

type RequestActionResult = {
    isPending: boolean;
    /**
     * Pending state of THIS hook instance's own execute() only — false while a DIFFERENT
     * action sharing the same request key is in flight. Use it for "PROCESSING"-style labels
     * on a per-button basis; keep `isPending` (which also folds the shared key's status) for
     * disabling, so concurrent submits on a shared key stay blocked.
     */
    isOwnPending: boolean;
    execute: () => boolean;
    reset: () => void;
    lastResult: RequestResult;
    /**
     * ReasonId of the reject of the LAST execute() of THIS hook instance.
     * Set when the reconcile effect resolves this instance's own pending into
     * `failed`; cleared by (1) the next execute(), (2) the bridge reset
     * (RequestId 0 / idle), (3) a newer result arriving on the same key
     * (our reject went stale), and (4) a 5s TTL. It is a transient hint about
     * a completed own attempt — NOT a persistent eligibility blocker (those
     * are the DTO's live *LockedReasonId fields).
     *
     * Shared-key caveat: keys such as HeroActionRequest are shared by
     * Deploy/Recall/SetMode/SetArchetype. Two commands from different hook
     * instances inside one throttle window (~500ms) can mis-attribute the
     * reason text (the first terminal result newer than baseline is assigned
     * to its own execute). The execute-gate makes this near-impossible, and
     * the optimistic-highlight reset is always correct regardless (the
     * authoritative value ships in the same DTO snapshot).
     */
    failureReasonId: string;
};

const IDLE_RESULT: RequestResult = {
    RequestId: 0,
    Status: "idle",
    ReasonId: "",
    CanonicalEcho: "",
    DiscriminatorKind: "none",
    DiscriminatorValue: "",
};

const FAILURE_HINT_TTL_MS = 5000;

export function useRequestAction(
    action: () => boolean,
    result: RequestResult | undefined
): RequestActionResult {
    const currentResult = result ?? IDLE_RESULT;
    const requestId = currentResult.RequestId;
    const status = currentResult.Status;
    const reasonId = currentResult.ReasonId;
    const [isPending, setIsPending] = useState(false);
    const pendingRef = useRef(false);
    const baselineRequestIdRef = useRef(requestId);
    const actionRef = useRef(action);
    actionRef.current = action;

    const [failureReasonId, setFailureReasonId] = useState("");
    const latchedRequestIdRef = useRef(0);
    const ttlTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    const clearFailure = useCallback(() => {
        latchedRequestIdRef.current = 0;
        if (ttlTimerRef.current !== null) {
            clearTimeout(ttlTimerRef.current);
            ttlTimerRef.current = null;
        }
        setFailureReasonId("");
    }, []);

    const latchFailure = useCallback((latchedReasonId: string, latchedRequestId: number) => {
        latchedRequestIdRef.current = latchedRequestId;
        if (ttlTimerRef.current !== null) clearTimeout(ttlTimerRef.current);
        ttlTimerRef.current = setTimeout(() => {
            ttlTimerRef.current = null;
            latchedRequestIdRef.current = 0;
            setFailureReasonId("");
        }, FAILURE_HINT_TTL_MS);
        setFailureReasonId(latchedReasonId);
    }, []);

    // Clear the TTL timer if the hook unmounts mid-hint.
    useEffect(() => () => {
        if (ttlTimerRef.current !== null) clearTimeout(ttlTimerRef.current);
    }, []);

    const stableResult = useMemo<RequestResult>(() => ({
        RequestId: requestId,
        Status: status,
        ReasonId: currentResult.ReasonId,
        CanonicalEcho: currentResult.CanonicalEcho,
        DiscriminatorKind: currentResult.DiscriminatorKind,
        DiscriminatorValue: currentResult.DiscriminatorValue,
    }), [
        requestId,
        status,
        currentResult.ReasonId,
        currentResult.CanonicalEcho,
        currentResult.DiscriminatorKind,
        currentResult.DiscriminatorValue,
    ]);

    const reset = useCallback(() => {
        pendingRef.current = false;
        setIsPending(false);
        baselineRequestIdRef.current = requestId;
    }, [requestId]);

    useEffect(() => {
        if (requestId === 0 && status === "idle") {
            clearFailure();
            reset();
            return;
        }

        // A newer result arrived on this key than the one we latched a failure
        // for → the hint is stale, drop it.
        if (latchedRequestIdRef.current !== 0 && requestId > latchedRequestIdRef.current) {
            clearFailure();
        }

        if (!pendingRef.current) {
            baselineRequestIdRef.current = requestId;
            return;
        }

        if (
            requestId > baselineRequestIdRef.current
            && status !== "pending"
        ) {
            if (status === "failed") {
                latchFailure(reasonId, requestId);
            }
            reset();
        }
    }, [requestId, status, reasonId, reset, clearFailure, latchFailure]);

    const execute = useCallback(() => {
        if (pendingRef.current || status === "pending") return false;
        clearFailure();
        baselineRequestIdRef.current = requestId;
        pendingRef.current = true;
        setIsPending(true);
        let emitted = false;
        try {
            emitted = actionRef.current();
        } catch (error) {
            pendingRef.current = false;
            setIsPending(false);
            throw error;
        }
        if (!emitted) {
            pendingRef.current = false;
            setIsPending(false);
        }
        return emitted;
    }, [requestId, status, clearFailure]);

    const pending = isPending || status === "pending";
    return useMemo(
        () => ({ isPending: pending, isOwnPending: isPending, execute, reset, lastResult: stableResult, failureReasonId }),
        [pending, isPending, execute, reset, stableResult, failureReasonId]
    );
}
