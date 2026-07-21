// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/telemetry.contract.yaml
// SourceHash:       sha256:d0219cb77f538b4ba4209445d0db6411c24d8f0b369e79db58d9c58429a7d0bf
// Generator:        scripts/generators/telemetry.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.25
// GeneratedAt:      2026-05-14T00:00:00Z

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivicSurvival.Services.Telemetry
{
    public static class TelemetryPayloadWriter
    {
        private const int EVENT_JSON_INITIAL_CAPACITY = 192;

        public static string BuildEventJson(
            string eventId,
            string sessionId,
            string type,
            DateTime timestampUtc,
            string dataJson)
        {
            var sb = new StringBuilder(EVENT_JSON_INITIAL_CAPACITY);
            sb.Append('{');
            WriteString(sb, "EventId", eventId);
            sb.Append(',');
            WriteString(sb, "SessionId", sessionId);
            sb.Append(',');
            WriteString(sb, "Type", type);
            sb.Append(',');
            WriteString(sb, "Timestamp", timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append("\"Data\":");
            sb.Append(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson);
            sb.Append('}');
            return sb.ToString();
        }

        public static string BuildPayloadJson(
            string sessionId,
            string modVersion,
            string gameVersion,
            DateTime timestampUtc,
            IReadOnlyList<string> serializedEvents,
            string playerId = null)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            WriteString(sb, "SessionId", sessionId);
            sb.Append(',');
            WriteString(sb, "ModVersion", modVersion);
            sb.Append(',');
            WriteString(sb, "GameVersion", gameVersion);
            sb.Append(',');
            WriteString(sb, "ContractVersion", TelemetryContract.CurrentVersion);
            sb.Append(',');
            WriteString(sb, "Timestamp", timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            // PlayerId is the durable identity link for Personal Chronicle. Omitted
            // entirely for anon / opt-out clients (empty id), so the server sees no
            // PlayerId field and leaves sessions.player_id NULL for them.
            if (!string.IsNullOrEmpty(playerId))
            {
                sb.Append(',');
                WriteString(sb, "PlayerId", playerId);
            }
            sb.Append(',');
            sb.Append("\"Events\":[");
            for (int i = 0; i < serializedEvents.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(serializedEvents[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        internal static void WriteString(StringBuilder sb, string name, string value)
        {
            sb.Append('\"').Append(name).Append("\":");
            WriteStringValue(sb, value);
        }

        internal static void WriteStringValue(StringBuilder sb, string value)
        {
            if (value == null) value = "";
            sb.Append('\"');
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
                        if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('\"');
        }
    }
}
