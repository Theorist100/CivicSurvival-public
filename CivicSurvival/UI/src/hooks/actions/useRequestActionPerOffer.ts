import { useCallback, useEffect, useMemo, useState } from "react";
import type { RequestResult } from "../../types/dtoSubTypes";
import { offerKeyTarget, requestResultForTarget } from "../useRequest.generated";
import { useRequestAction } from "./useRequestAction";

export function useRequestActionPerOffer(
    action: () => boolean,
    result: RequestResult | undefined,
    offerKey: string
) {
    const scopedResult = requestResultForTarget(
        "MaintenanceContractRequest",
        result,
        offerKeyTarget(offerKey)
    );
    const base = useRequestAction(action, scopedResult);
    const baseExecute = base.execute;
    const [pendingOfferKey, setPendingOfferKey] = useState<string | null>(null);
    const resultRequestId = scopedResult?.RequestId;
    const resultStatus = scopedResult?.Status;

    useEffect(() => {
        if (pendingOfferKey !== null && pendingOfferKey !== offerKey) {
            setPendingOfferKey(null);
        }
    }, [offerKey, pendingOfferKey]);

    useEffect(() => {
        if (resultStatus == null || pendingOfferKey === null) return;
        if (resultStatus === "success" || resultStatus === "failed") {
            setPendingOfferKey(null);
        }
    }, [pendingOfferKey, resultRequestId, resultStatus]);

    const execute = useCallback(() => {
        if (!offerKey || pendingOfferKey !== null) return false;
        const emitted = baseExecute();
        if (emitted) {
            setPendingOfferKey(offerKey);
        }
        return emitted;
    }, [baseExecute, offerKey, pendingOfferKey]);

    const isPending = base.isPending || pendingOfferKey === offerKey || scopedResult?.Status === "pending";
    return useMemo(
        () => ({
            ...base,
            execute,
            isPending,
        }),
        [base, execute, isPending]
    );
}
