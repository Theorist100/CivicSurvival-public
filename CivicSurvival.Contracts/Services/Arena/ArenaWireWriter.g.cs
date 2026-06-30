// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/arena.contract.yaml
// SourceHash:       sha256:2c9e071ceacebca8403ae3d0de81d204312dcbf4b7296d05b208a576cf8e08f3
// Generator:        scripts/generators/arena.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

using System.Globalization;
using System.Text;

namespace CivicSurvival.Services.Arena
{
    public static class ArenaWireWriter
    {
        private const int INITIAL_CAPACITY = 256;

        public static string BuildPendingArenaDataJson(PendingArenaData dto)
        {
            var sb = new StringBuilder(INITIAL_CAPACITY);
            sb.Append('{');
            WriteName(sb, "SchemaVersion");
            sb.Append(dto.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "DamageDealt");
            sb.Append(dto.DamageDealt.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "ShadowSpent");
            sb.Append(dto.ShadowSpent.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "VulnerableHits");
            sb.Append(dto.VulnerableHits.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "FloorHit");
            sb.Append(dto.FloorHit ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "StreakBroken");
            sb.Append(dto.StreakBroken ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "Timestamp");
            sb.Append(dto.Timestamp.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildArenaReportRequestJson(ArenaReportRequest dto)
        {
            var sb = new StringBuilder(INITIAL_CAPACITY);
            sb.Append('{');
            WriteName(sb, "PlayerId");
            WriteString(sb, dto.PlayerId);
            sb.Append(',');
            WriteName(sb, "AuthToken");
            WriteString(sb, dto.AuthToken);
            sb.Append(',');
            WriteName(sb, "DamageDealt");
            sb.Append(dto.DamageDealt.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "ShadowSpent");
            sb.Append(dto.ShadowSpent.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "FloorHit");
            sb.Append(dto.FloorHit ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "VulnerableHits");
            sb.Append(dto.VulnerableHits.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "StreakBroken");
            sb.Append(dto.StreakBroken ? "true" : "false");
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildArenaReportResponseJson(ArenaReportResponse dto)
        {
            var sb = new StringBuilder(INITIAL_CAPACITY);
            sb.Append('{');
            WriteName(sb, "Success");
            sb.Append(dto.Success ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "NewFloorHits");
            sb.Append(dto.NewFloorHits.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "NewRank");
            WriteString(sb, dto.NewRank);
            sb.Append(',');
            WriteName(sb, "Position");
            if (dto.Position == null) sb.Append("null"); else sb.Append(dto.Position.Value.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "WeeklyPosition");
            if (dto.WeeklyPosition == null) sb.Append("null"); else sb.Append(dto.WeeklyPosition.Value.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildLeaderboardEntryJson(LeaderboardEntry dto)
        {
            var sb = new StringBuilder(INITIAL_CAPACITY);
            sb.Append('{');
            WriteName(sb, "Position");
            sb.Append(dto.Position.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "Nickname");
            WriteString(sb, dto.Nickname);
            sb.Append(',');
            WriteName(sb, "FloorHits");
            sb.Append(dto.FloorHits.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "TotalDamage");
            sb.Append(dto.TotalDamage.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "BestStreak");
            sb.Append(dto.BestStreak.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "RankTier");
            WriteString(sb, dto.RankTier);
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildWeeklyLeaderboardEntryJson(WeeklyLeaderboardEntry dto)
        {
            var sb = new StringBuilder(INITIAL_CAPACITY);
            sb.Append('{');
            WriteName(sb, "Position");
            sb.Append(dto.Position.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "Nickname");
            WriteString(sb, dto.Nickname);
            sb.Append(',');
            WriteName(sb, "FloorHits");
            sb.Append(dto.FloorHits.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "DamageDealt");
            sb.Append(dto.DamageDealt.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "WeekStart");
            WriteString(sb, dto.WeekStart);
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildArenaStatsJson(ArenaStats dto)
        {
            var sb = new StringBuilder(INITIAL_CAPACITY);
            sb.Append('{');
            WriteName(sb, "FloorHits");
            sb.Append(dto.FloorHits.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "TotalDamageDealt");
            sb.Append(dto.TotalDamageDealt.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "CurrentStreak");
            sb.Append(dto.CurrentStreak.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "BestStreak");
            sb.Append(dto.BestStreak.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "RankTier");
            WriteString(sb, dto.RankTier);
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteName(StringBuilder sb, string name)
        {
            sb.Append('"').Append(name).Append("\":");
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '\"': sb.Append("\\\""); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
