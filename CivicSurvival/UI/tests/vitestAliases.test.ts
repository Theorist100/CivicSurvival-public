import { describe, expect, it } from "vitest";
import { isRecord } from "utils/typeGuards";

describe("vitest aliases", () => {
    it("resolves non-prefixed aliases from tsconfig paths", () => {
        expect(isRecord({ ok: true })).toBe(true);
    });
});
