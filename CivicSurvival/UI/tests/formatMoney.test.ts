import { describe, expect, it } from "vitest";
import { formatMoney } from "../src/themes/commonStyles";

describe("formatMoney", () => {
    it("places the negative sign before the currency symbol", () => {
        expect(formatMoney(-50_000)).toBe("-$50k");
        expect(formatMoney(-1_250_000)).toBe("-$1.25M");
        expect(formatMoney(-50)).toBe("-$50");
    });

    it("keeps existing positive formatting", () => {
        expect(formatMoney(50_000)).toBe("$50k");
        expect(formatMoney(1_250_000)).toBe("$1.25M");
        expect(formatMoney(50)).toBe("$50");
    });
});
