export const leaderboardEntryId = (entry: {
    Position: number;
    Nickname: string;
    WavesSurvived: number;
    Score: number;
}): string => `all:${entry.Position}:${entry.Nickname}:${entry.WavesSurvived}:${entry.Score}`;

export const weeklyLeaderboardEntryId = (entry: {
    Position: number;
    Nickname: string;
    WavesSurvived: number;
    Score: number;
}): string => `week:${entry.Position}:${entry.Nickname}:${entry.WavesSurvived}:${entry.Score}`;
