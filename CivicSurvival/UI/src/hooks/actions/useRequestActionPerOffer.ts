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
    const baseReset = base.reset;
    const [pendingOfferKey, setPendingOfferKey] = useState<string | null>(null);
    const resultRequestId = scopedResult?.RequestId;
    const resultStatus = scopedResult?.Status;

    useEffect(() => {
        if (pendingOfferKey !== null && pendingOfferKey !== offerKey) {
            setPendingOfferKey(null);
            // Synchronous consumers can consume the offer and deliver the terminal
            // result in the SAME DTO snapshot. The offerKey-scoped result then never
            // matches again (the target already changed), so base's optimistic
            // pending latch would stick forever — and the component is not
            // remounted between offers. The in-flight target is gone: drop the
            // optimistic pending with it. A late terminal for the old offer keeps
            // its old discriminator and can never match a future target.
            baseReset();
        }
    }, [offerKey, pendingOfferKey, baseReset]);

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

    const isPending = base.isPending || pendingOfferKey === offerKey;
    return useMemo(
        () => ({
            ...base,
            execute,
            isPending,
        }),
        [base, execute, isPending]
    );
}
