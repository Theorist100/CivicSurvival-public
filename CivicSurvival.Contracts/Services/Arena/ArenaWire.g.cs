// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/arena.contract.yaml
// SourceHash:       sha256:48f3a3a6f9c4e5463af413c93da94056cd2506967fd7e3589720987013a220bf
// Generator:        scripts/generators/arena.py
// GeneratorVersion: 1.0.0
// ContractVersion:  2.2.0
// GeneratedAt:      2026-05-14T00:00:00Z

namespace CivicSurvival.Services.Arena
{
    public sealed class PendingArenaData
    {
        public int SchemaVersion { get; set; } = 1;
        public int DamageDealt { get; set; } = 0;
        public int ShadowSpent { get; set; } = 0;
        public int VulnerableHits { get; set; } = 0;
        public bool FloorHit { get; set; } = false;
        public bool StreakBroken { get; set; } = false;
        public long Timestamp { get; set; } = 0L;
    }

    public sealed class ArenaReportRequest
    {
        public string PlayerId { get; set; } = "";
        public string AuthToken { get; set; } = "";
        public int DamageDealt { get; set; } = 0;
        public int ShadowSpent { get; set; } = 0;
        public bool FloorHit { get; set; } = false;
        public int VulnerableHits { get; set; } = 0;
        public bool StreakBroken { get; set; } = false;
    }

    public sealed class ArenaReportResponse
    {
        public bool Success { get; set; } = false;
        public int NewFloorHits { get; set; } = 0;
        public string NewRank { get; set; } = "";
        public int? Position { get; set; } = null;
        public int? WeeklyPosition { get; set; } = null;
    }

    public sealed class LeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public int WavesSurvived { get; set; } = 0;
        public long Intercepted { get; set; } = 0L;
        public long Score { get; set; } = 0L;
        public float SuccessRate { get; set; } = 0.0f;
        public string RankTier { get; set; } = "";
    }

    public sealed class WeeklyLeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public int WavesSurvived { get; set; } = 0;
        public long Intercepted { get; set; } = 0L;
        public long Score { get; set; } = 0L;
        public string WeekStart { get; set; } = "";
    }

    public sealed class ArenaStats
    {
        public int WavesSurvived { get; set; } = 0;
        public long Intercepted { get; set; } = 0L;
        public long Score { get; set; } = 0L;
        public float SuccessRate { get; set; } = 0.0f;
        public string RankTier { get; set; } = "";
    }

    public sealed class ShadowLeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public long Earned { get; set; } = 0L;
        public long Confiscated { get; set; } = 0L;
        public long Net { get; set; } = 0L;
        public string RankTier { get; set; } = "";
    }

    public sealed class WeeklyShadowLeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public long Net { get; set; } = 0L;
        public string WeekStart { get; set; } = "";
    }

    public sealed class ShadowStats
    {
        public long Earned { get; set; } = 0L;
        public long Confiscated { get; set; } = 0L;
        public long Net { get; set; } = 0L;
        public string RankTier { get; set; } = "";
    }

    public sealed class ProsperityLeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public int Waves { get; set; } = 0;
        public int AvgIndex { get; set; } = 0;
        public long Score { get; set; } = 0L;
        public string RankTier { get; set; } = "";
    }

    public sealed class WeeklyProsperityLeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public long Score { get; set; } = 0L;
        public string WeekStart { get; set; } = "";
    }

    public sealed class ProsperityStats
    {
        public int Waves { get; set; } = 0;
        public int AvgIndex { get; set; } = 0;
        public long Score { get; set; } = 0L;
        public string RankTier { get; set; } = "";
    }

}
