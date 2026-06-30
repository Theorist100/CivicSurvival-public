import { describe, expect, it } from "vitest";
import { leaderboardEntryId, weeklyLeaderboardEntryId } from "../src/hooks/state/useArenaLeaderboardIds";

describe("arena leaderboard ids", () => {
    it("include position for all-time duplicate names and scores", () => {
        const first = leaderboardEntryId({
            Position: 1,
            Nickname: "Commander",
            FloorHits: 42,
            BestStreak: 7,
        });
        const second = leaderboardEntryId({
            Position: 2,
            Nickname: "Commander",
            FloorHits: 42,
            BestStreak: 7,
        });

        expect(second).not.toBe(first);
    });

    it("include position for weekly duplicate names and scores", () => {
        const first = weeklyLeaderboardEntryId({
            Position: 1,
            Nickname: "Commander",
            FloorHits: 42,
            DamageDealt: 9000,
        });
        const second = weeklyLeaderboardEntryId({
            Position: 2,
            Nickname: "Commander",
            FloorHits: 42,
            DamageDealt: 9000,
        });

        expect(second).not.toBe(first);
    });
});
