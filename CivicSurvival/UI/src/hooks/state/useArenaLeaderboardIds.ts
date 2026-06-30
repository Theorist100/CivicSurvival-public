export const leaderboardEntryId = (entry: {
    Position: number;
    Nickname: string;
    FloorHits: number;
    BestStreak: number;
}): string => `all:${entry.Position}:${entry.Nickname}:${entry.FloorHits}:${entry.BestStreak}`;

export const weeklyLeaderboardEntryId = (entry: {
    Position: number;
    Nickname: string;
    FloorHits: number;
    DamageDealt: number;
}): string => `week:${entry.Position}:${entry.Nickname}:${entry.FloorHits}:${entry.DamageDealt}`;
