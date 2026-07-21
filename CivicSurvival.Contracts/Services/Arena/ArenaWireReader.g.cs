// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/arena.contract.yaml
// SourceHash:       sha256:48f3a3a6f9c4e5463af413c93da94056cd2506967fd7e3589720987013a220bf
// Generator:        scripts/generators/arena.py
// GeneratorVersion: 1.0.0
// ContractVersion:  2.2.0
// GeneratedAt:      2026-05-14T00:00:00Z

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Services.Arena
{
    public sealed class ArenaLeaderboardResponse
    {
        public List<LeaderboardEntry> Leaderboard { get; set; } = new();
        public int? YourPosition { get; set; } = null;
        public ArenaStats? YourStats { get; set; } = null;
    }

    public sealed class WeeklyLeaderboardResponse
    {
        public List<WeeklyLeaderboardEntry> Leaderboard { get; set; } = new();
        public int? YourPosition { get; set; } = null;
    }

    public sealed class ShadowLeaderboardResponse
    {
        public List<ShadowLeaderboardEntry> Leaderboard { get; set; } = new();
        public int? YourPosition { get; set; } = null;
        public ShadowStats? YourStats { get; set; } = null;
    }

    public sealed class WeeklyShadowLeaderboardResponse
    {
        public List<WeeklyShadowLeaderboardEntry> Leaderboard { get; set; } = new();
        public int? YourPosition { get; set; } = null;
    }

    public sealed class ProsperityLeaderboardResponse
    {
        public List<ProsperityLeaderboardEntry> Leaderboard { get; set; } = new();
        public int? YourPosition { get; set; } = null;
        public ProsperityStats? YourStats { get; set; } = null;
    }

    public sealed class WeeklyProsperityLeaderboardResponse
    {
        public List<WeeklyProsperityLeaderboardEntry> Leaderboard { get; set; } = new();
        public int? YourPosition { get; set; } = null;
    }

    public static class ArenaWireReader
    {
        public static PendingArenaData ParsePendingArenaData(string json)
        {
            var dto = Deserialize<PendingArenaData>(json, nameof(PendingArenaData));
            ValidatePendingArenaData(dto);
            return dto;
        }

        public static ArenaReportRequest ParseArenaReportRequest(string json)
        {
            var dto = Deserialize<ArenaReportRequest>(json, nameof(ArenaReportRequest));
            ValidateArenaReportRequest(dto);
            return dto;
        }

        public static ArenaReportResponse ParseArenaReportResponse(string json)
        {
            var dto = Deserialize<ArenaReportResponse>(json, nameof(ArenaReportResponse));
            ValidateArenaReportResponse(dto);
            return dto;
        }

        public static LeaderboardEntry ParseLeaderboardEntry(string json)
        {
            var dto = Deserialize<LeaderboardEntry>(json, nameof(LeaderboardEntry));
            ValidateLeaderboardEntry(dto);
            return dto;
        }

        public static WeeklyLeaderboardEntry ParseWeeklyLeaderboardEntry(string json)
        {
            var dto = Deserialize<WeeklyLeaderboardEntry>(json, nameof(WeeklyLeaderboardEntry));
            ValidateWeeklyLeaderboardEntry(dto);
            return dto;
        }

        public static ArenaStats ParseArenaStats(string json)
        {
            var dto = Deserialize<ArenaStats>(json, nameof(ArenaStats));
            ValidateArenaStats(dto);
            return dto;
        }

        public static ShadowLeaderboardEntry ParseShadowLeaderboardEntry(string json)
        {
            var dto = Deserialize<ShadowLeaderboardEntry>(json, nameof(ShadowLeaderboardEntry));
            ValidateShadowLeaderboardEntry(dto);
            return dto;
        }

        public static WeeklyShadowLeaderboardEntry ParseWeeklyShadowLeaderboardEntry(string json)
        {
            var dto = Deserialize<WeeklyShadowLeaderboardEntry>(json, nameof(WeeklyShadowLeaderboardEntry));
            ValidateWeeklyShadowLeaderboardEntry(dto);
            return dto;
        }

        public static ShadowStats ParseShadowStats(string json)
        {
            var dto = Deserialize<ShadowStats>(json, nameof(ShadowStats));
            ValidateShadowStats(dto);
            return dto;
        }

        public static ProsperityLeaderboardEntry ParseProsperityLeaderboardEntry(string json)
        {
            var dto = Deserialize<ProsperityLeaderboardEntry>(json, nameof(ProsperityLeaderboardEntry));
            ValidateProsperityLeaderboardEntry(dto);
            return dto;
        }

        public static WeeklyProsperityLeaderboardEntry ParseWeeklyProsperityLeaderboardEntry(string json)
        {
            var dto = Deserialize<WeeklyProsperityLeaderboardEntry>(json, nameof(WeeklyProsperityLeaderboardEntry));
            ValidateWeeklyProsperityLeaderboardEntry(dto);
            return dto;
        }

        public static ProsperityStats ParseProsperityStats(string json)
        {
            var dto = Deserialize<ProsperityStats>(json, nameof(ProsperityStats));
            ValidateProsperityStats(dto);
            return dto;
        }

        public static ArenaLeaderboardResponse ParseLeaderboardResponse(string json)
            => Deserialize<ArenaLeaderboardResponse>(json, nameof(ArenaLeaderboardResponse));

        public static WeeklyLeaderboardResponse ParseWeeklyLeaderboardResponse(string json)
            => Deserialize<WeeklyLeaderboardResponse>(json, nameof(WeeklyLeaderboardResponse));

        public static ShadowLeaderboardResponse ParseShadowLeaderboardResponse(string json)
            => Deserialize<ShadowLeaderboardResponse>(json, nameof(ShadowLeaderboardResponse));

        public static WeeklyShadowLeaderboardResponse ParseWeeklyShadowLeaderboardResponse(string json)
            => Deserialize<WeeklyShadowLeaderboardResponse>(json, nameof(WeeklyShadowLeaderboardResponse));

        public static ProsperityLeaderboardResponse ParseProsperityLeaderboardResponse(string json)
            => Deserialize<ProsperityLeaderboardResponse>(json, nameof(ProsperityLeaderboardResponse));

        public static WeeklyProsperityLeaderboardResponse ParseWeeklyProsperityLeaderboardResponse(string json)
            => Deserialize<WeeklyProsperityLeaderboardResponse>(json, nameof(WeeklyProsperityLeaderboardResponse));

        private static void ValidatePendingArenaData(PendingArenaData dto)
        {
            RequireMin(dto.DamageDealt, 0L, "PendingArenaData.DamageDealt");
            RequireMin(dto.ShadowSpent, 0L, "PendingArenaData.ShadowSpent");
            RequireMin(dto.VulnerableHits, 0L, "PendingArenaData.VulnerableHits");
        }

        private static void ValidateArenaReportRequest(ArenaReportRequest dto)
        {
            RequireLength(dto.PlayerId, 36, 36, "ArenaReportRequest.PlayerId");
            RequirePattern(dto.PlayerId, "^[a-fA-F0-9-]{36}$", "ArenaReportRequest.PlayerId");
            RequireLength(dto.AuthToken, 0, 128, "ArenaReportRequest.AuthToken");
            RequireMin(dto.DamageDealt, 0L, "ArenaReportRequest.DamageDealt");
            RequireMin(dto.ShadowSpent, 0L, "ArenaReportRequest.ShadowSpent");
            RequireMin(dto.VulnerableHits, 0L, "ArenaReportRequest.VulnerableHits");
        }

        private static void ValidateArenaReportResponse(ArenaReportResponse dto)
        {
        }

        private static void ValidateLeaderboardEntry(LeaderboardEntry dto)
        {
        }

        private static void ValidateWeeklyLeaderboardEntry(WeeklyLeaderboardEntry dto)
        {
        }

        private static void ValidateArenaStats(ArenaStats dto)
        {
        }

        private static void ValidateShadowLeaderboardEntry(ShadowLeaderboardEntry dto)
        {
        }

        private static void ValidateWeeklyShadowLeaderboardEntry(WeeklyShadowLeaderboardEntry dto)
        {
        }

        private static void ValidateShadowStats(ShadowStats dto)
        {
        }

        private static void ValidateProsperityLeaderboardEntry(ProsperityLeaderboardEntry dto)
        {
        }

        private static void ValidateWeeklyProsperityLeaderboardEntry(WeeklyProsperityLeaderboardEntry dto)
        {
        }

        private static void ValidateProsperityStats(ProsperityStats dto)
        {
        }

        private static T Deserialize<T>(string json, string path) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json)
                    ?? throw new ContractValidationException($"{path} is empty or null");
            }
            catch (ContractValidationException) { throw; }
            catch (Exception ex)
            {
                throw new ContractValidationException($"{path} is not valid JSON", ex);
            }
        }

        private static void RequireMin(long value, long min, string path)
        {
            if (value < min) throw new ContractValidationException($"{path} must be >= {min}");
        }

        private static void RequireMax(long value, long max, string path)
        {
            if (value > max) throw new ContractValidationException($"{path} must be <= {max}");
        }

        private static void RequireLength(string value, int? min, int? max, string path)
        {
            if (min.HasValue && value.Length < min.Value) throw new ContractValidationException($"{path} length must be >= {min.Value}");
            if (max.HasValue && value.Length > max.Value) throw new ContractValidationException($"{path} length must be <= {max.Value}");
        }

        private static void RequirePattern(string value, string pattern, string path)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
                throw new ContractValidationException($"{path} does not match required pattern");
        }
    }
}
