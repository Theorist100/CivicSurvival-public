import React from "react";
import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useRequestAction } from "../src/hooks/actions/useRequestAction";
import { useRequestActionPerOffer } from "../src/hooks/actions/useRequestActionPerOffer";
import type { RequestResult } from "../src/types/dtoSubTypes";

function result(overrides: Partial<RequestResult> = {}): RequestResult {
    return {
        RequestId: 7,
        Status: "success",
        ReasonId: "",
        CanonicalEcho: "",
        DiscriminatorKind: "none",
        DiscriminatorValue: "",
        ...overrides,
    };
}

afterEach(() => {
    cleanup();
});

describe("useRequestAction", () => {
    it("keeps the returned object stable when equal result values are recreated", () => {
        const action = vi.fn(() => true);
        const seen: ReturnType<typeof useRequestAction>[] = [];

        function Probe({ requestResult }: { requestResult: RequestResult }) {
            seen.push(useRequestAction(action, requestResult));
            return null;
        }

        const { rerender } = render(<Probe requestResult={result()} />);
        const first = seen.at(-1);

        rerender(<Probe requestResult={result()} />);

        expect(seen.at(-1)).toBe(first);
        expect(seen.at(-1)?.lastResult).toBe(first?.lastResult);
    });

    it("keeps a second consumer stable on unchanged idle input", () => {
        const action = vi.fn(() => true);
        const emptyResult: RequestResult | undefined = undefined;
        const seen: ReturnType<typeof useRequestAction>[] = [];

        function Probe() {
            seen.push(useRequestAction(action, emptyResult));
            return null;
        }

        const { rerender } = render(<Probe />);
        const first = seen.at(-1);

        rerender(<Probe />);

        expect(seen.at(-1)).toBe(first);
    });

    it("updates identity when observable result values change", () => {
        const action = vi.fn(() => true);
        const seen: ReturnType<typeof useRequestAction>[] = [];

        function Probe({ requestResult }: { requestResult: RequestResult }) {
            seen.push(useRequestAction(action, requestResult));
            return null;
        }

        const { rerender } = render(<Probe requestResult={result({ Status: "pending" })} />);
        const first = seen.at(-1);

        rerender(<Probe requestResult={result({ Status: "success" })} />);

        expect(seen.at(-1)).not.toBe(first);
        expect(seen.at(-1)?.isPending).toBe(false);
    });
});

describe("useRequestActionPerOffer", () => {
    it("keeps its wrapper stable when the scoped offer result is unchanged", () => {
        const action = vi.fn(() => true);
        const offerKey = "official:1";
        const seen: ReturnType<typeof useRequestActionPerOffer>[] = [];
        const offerResult = result({
            DiscriminatorKind: "offerKey",
            DiscriminatorValue: offerKey,
        });

        function Probe({ requestResult }: { requestResult: RequestResult }) {
            seen.push(useRequestActionPerOffer(action, requestResult, offerKey));
            return null;
        }

        const { rerender } = render(<Probe requestResult={offerResult} />);
        const first = seen.at(-1);

        rerender(<Probe requestResult={{ ...offerResult }} />);

        expect(seen.at(-1)).toBe(first);
    });
});
