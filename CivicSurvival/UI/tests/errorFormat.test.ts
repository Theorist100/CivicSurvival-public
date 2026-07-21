import { describe, expect, it } from "vitest";
import { formatCaughtError } from "../src/utils/errorFormat";

function namedFunction(): void {}

describe("formatCaughtError", () => {
    it("returns a string message for symbols", () => {
        expect(formatCaughtError(Symbol("boom"))).toEqual({
            message: "Symbol(boom)",
            stack: "no stack",
        });
    });

    it("returns a string message when JSON.stringify returns undefined", () => {
        const formatted = formatCaughtError(namedFunction);

        expect(formatted.message).toContain("namedFunction");
        expect(formatted.stack).toBe("no stack");
    });
});
