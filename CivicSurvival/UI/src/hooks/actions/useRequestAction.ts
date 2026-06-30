import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { RequestResult } from "../../types/dtoSubTypes";

type RequestActionResult = {
    isPending: boolean;
    execute: () => boolean;
    reset: () => void;
    lastResult: RequestResult;
};

const IDLE_RESULT: RequestResult = {
    RequestId: 0,
    Status: "idle",
    ReasonId: "",
    CanonicalEcho: "",
    DiscriminatorKind: "none",
    DiscriminatorValue: "",
};

export function useRequestAction(
    action: () => boolean,
    result: RequestResult | undefined
): RequestActionResult {
    const currentResult = result ?? IDLE_RESULT;
    const requestId = currentResult.RequestId;
    const status = currentResult.Status;
    const [isPending, setIsPending] = useState(false);
    const pendingRef = useRef(false);
    const baselineRequestIdRef = useRef(requestId);
    const actionRef = useRef(action);
    actionRef.current = action;
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
            reset();
            return;
        }

        if (!pendingRef.current) {
            baselineRequestIdRef.current = requestId;
            return;
        }

        if (
            requestId > baselineRequestIdRef.current
            && status !== "pending"
        ) {
            reset();
        }
    }, [requestId, status, reset]);

    const execute = useCallback(() => {
        if (pendingRef.current || status === "pending") return false;
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
    }, [requestId, status]);

    const pending = isPending || status === "pending";
    return useMemo(
        () => ({ isPending: pending, execute, reset, lastResult: stableResult }),
        [pending, execute, reset, stableResult]
    );
}
