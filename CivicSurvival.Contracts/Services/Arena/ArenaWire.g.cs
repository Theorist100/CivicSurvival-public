// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/arena.contract.yaml
// SourceHash:       sha256:2c9e071ceacebca8403ae3d0de81d204312dcbf4b7296d05b208a576cf8e08f3
// Generator:        scripts/generators/arena.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
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
        public int FloorHits { get; set; } = 0;
        public long TotalDamage { get; set; } = 0L;
        public int BestStreak { get; set; } = 0;
        public string RankTier { get; set; } = "";
    }

    public sealed class WeeklyLeaderboardEntry
    {
        public int Position { get; set; } = 0;
        public string Nickname { get; set; } = "";
        public int FloorHits { get; set; } = 0;
        public long DamageDealt { get; set; } = 0L;
        public string WeekStart { get; set; } = "";
    }

    public sealed class ArenaStats
    {
        public int FloorHits { get; set; } = 0;
        public long TotalDamageDealt { get; set; } = 0L;
        public int CurrentStreak { get; set; } = 0;
        public int BestStreak { get; set; } = 0;
        public string RankTier { get; set; } = "";
    }

}
