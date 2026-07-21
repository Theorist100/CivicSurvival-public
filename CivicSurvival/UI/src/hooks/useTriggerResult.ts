/**
 * Hook for reading the triggerResult binding (rejection feedback from C# domain systems).
 */

import { bindCivicValue } from "./typedBinding.generated";
import { B } from "./bindingNames.generated";
import { useSafeString } from "./useSafeBinding";

const triggerResult$ = bindCivicValue(B.TriggerResult, "");

export type TriggerResult = {
    requestId: number;
    reasonId: string;
    kind: string;
};

const EMPTY_RESULT: TriggerResult = {
    requestId: 0,
    reasonId: "",
    kind: "",
};

export function useTriggerResult(): TriggerResult {
    const raw = useSafeString(triggerResult$, "", "triggerResult");
    if (raw.length === 0) return EMPTY_RESULT;

    try {
        const parsed = JSON.parse(raw) as Partial<{
            RequestId: number;
            ReasonId: string;
            Kind: string;
        }>;
        return {
            requestId: typeof parsed.RequestId === "number" ? parsed.RequestId : 0,
            reasonId: typeof parsed.ReasonId === "string" ? parsed.ReasonId : "",
            kind: typeof parsed.Kind === "string" ? parsed.Kind : "",
        };
    } catch {
        return {
            requestId: 0,
            reasonId: raw,
            kind: "",
        };
    }
}
