import { afterEach, describe, expect, it } from "vitest";
import { ensureUrlSearchParamsCompat } from "../src/services/urlSearchParamsCompat";

describe("ensureUrlSearchParamsCompat", () => {
    const nativeURLSearchParams = globalThis.URLSearchParams;

    afterEach(() => {
        globalThis.URLSearchParams = nativeURLSearchParams;
    });

    it("installs a constructor when the host does not provide URLSearchParams", () => {
        delete (globalThis as Partial<typeof globalThis>).URLSearchParams;

        ensureUrlSearchParamsCompat();

        const params = new URLSearchParams("?a=1&b=hello+world&a=2");
        expect(params instanceof URLSearchParams).toBe(true);
        expect(params.get("a")).toBe("1");
        expect(params.getAll("a")).toEqual(["1", "2"]);
        expect(params.get("b")).toBe("hello world");
        expect(params.toString()).toBe("a=1&b=hello+world&a=2");
    });

    it("supports mutation and iterable construction", () => {
        delete (globalThis as Partial<typeof globalThis>).URLSearchParams;

        ensureUrlSearchParamsCompat();

        const params = new URLSearchParams([["z", "9"], ["a", "1"]]);
        params.append("a", "2");
        params.set("z", "10");

        expect([...params.entries()]).toEqual([["a", "1"], ["a", "2"], ["z", "10"]]);
    });
});
