import { describe, expect, it } from "vitest";
import { isWaveManifest } from "../src/hooks/useBetaWaveManifest";

describe("wave manifest guard", () => {
    it("accepts a wave-only manifest", () => {
        const manifest = {
            current: 2,
            waves: { ArenaUI: 1, FuturePanel: 99 },
        };

        expect(isWaveManifest(manifest)).toBe(true);
    });

    it("rejects non-numeric wave values", () => {
        expect(isWaveManifest({
            current: 2,
            waves: { ArenaUI: "1" },
        })).toBe(false);
    });
});
