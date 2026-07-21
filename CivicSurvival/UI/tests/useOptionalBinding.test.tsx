import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { combineBindingStates } from "../src/hooks/domain/combineBindingStates";
import { type BindingState } from "../src/hooks/domain/useDtoBinding";
import { optionalBindingData, useOptionalBinding } from "../src/hooks/useOptionalBinding";

interface SampleDto {
    value: number;
}

describe("useOptionalBinding", () => {
    it("keeps loading with carried data unavailable to availability-gated consumers", () => {
        const carried: SampleDto = { value: 7 };
        const state: BindingState<SampleDto> = { status: "loading", data: carried };

        const { result } = renderHook(() => useOptionalBinding(state));

        expect(result.current).toEqual({ status: "unavailable", reason: "loading", data: carried });
        expect(optionalBindingData(result.current)).toBeNull();
    });

    it("keeps invalid with carried data unavailable to availability-gated consumers", () => {
        const carried: SampleDto = { value: 9 };
        const state: BindingState<SampleDto> = {
            status: "invalid",
            preview: "{bad}",
            error: "bad dto",
            data: carried,
        };

        const { result } = renderHook(() => useOptionalBinding(state));

        expect(result.current).toEqual({
            status: "unavailable",
            reason: "invalid",
            error: "bad dto",
            data: carried,
        });
        expect(optionalBindingData(result.current)).toBeNull();
    });

    it("does not promote combined non-ready inputs to ready", () => {
        const combined = combineBindingStates({
            exportState: { status: "invalid", preview: "x", error: "bad", data: { value: 1 } } satisfies BindingState<SampleDto>,
            intelState: { status: "ready", data: { value: 2 } } satisfies BindingState<SampleDto>,
        }, ({ exportState, intelState }) => exportState.value + intelState.value);

        expect(combined).toEqual({ status: "invalid", preview: "x", error: "bad" });
    });
});
