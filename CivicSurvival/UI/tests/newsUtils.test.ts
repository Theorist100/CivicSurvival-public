import { describe, expect, it } from "vitest";
import { formatTimeAgo } from "../src/components/news/sections/newsTime";

const t = (key: string, ...args: (string | number)[]): string =>
    args.length > 0 ? `${key}:${args.join(",")}` : key;

describe("formatTimeAgo", () => {
    it.each([
        [59, "UI_NEWS_TIME_MIN_AGO:1"],
        [60, "UI_NEWS_TIME_MIN_AGO:1"],
        [119, "UI_NEWS_TIME_MIN_AGO:2"],
        [120, "UI_NEWS_TIME_MIN_AGO:2"],
    ])("uses minute buckets for a post timestamp %s seconds old", (ageSeconds, expected) => {
        const nowMinute = 10;
        const timestamp = nowMinute * 60 - ageSeconds;

        expect(formatTimeAgo(timestamp, nowMinute, t)).toBe(expected);
    });

    it("clamps future timestamps to just now", () => {
        expect(formatTimeAgo(601, 10, t)).toBe("UI_NEWS_TIME_JUST_NOW");
    });
});
