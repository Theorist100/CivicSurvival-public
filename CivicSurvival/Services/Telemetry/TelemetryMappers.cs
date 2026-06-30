using System;
using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// Pure mapping helpers shared by telemetry sub-listeners.
    /// </summary>
    [OutboundTelemetry]
    internal static class TelemetryMappers
    {
        private const int MaxNarrativeContextEntries = 16;
        private const int MaxNarrativeContextChars = 4096;

        public static string MapThreatNarrativeSubtype(ThreatNarrativeEventType type)
        {
            if (type == ThreatNarrativeEventType.AAInstallationLost)
                return "aa_installation_lost";

            return type.ToString().ToSnakeCase();
        }

        public static string MapCorruptionNarrativeSubtype(CorruptionNarrativeEventType type)
        {
            if (type == CorruptionNarrativeEventType.VIPProtected)
                return "vip_protected";
            if (type == CorruptionNarrativeEventType.VIPBypass)
                return "vip_bypass";
            if (type == CorruptionNarrativeEventType.VIPOverridden)
                return "vip_overridden";

            return type.ToString().ToSnakeCase();
        }

        public static string? SerializeNarrativeContext(IReadOnlyDictionary<string, string> context)
        {
            if (context == null || context.Count == 0) return null;

            var limited = new Dictionary<string, string>(Math.Min(context.Count, MaxNarrativeContextEntries), StringComparer.Ordinal);
            var count = 0;
            foreach (var kvp in context)
            {
                if (count >= MaxNarrativeContextEntries) break;
                limited[kvp.Key ?? ""] = kvp.Value ?? "";
                count++;
            }

            var builder = JsonBuilder.Object();
            foreach (var kvp in limited)
            {
                builder.Add(kvp.Key, kvp.Value);
            }
            var json = builder.Build();
            if (json.Length <= MaxNarrativeContextChars && count == context.Count) return json;

            return JsonBuilder.Object()
                .Add("_truncated", true)
                .Add("original_entries", context.Count)
                .Add("included_entries", count)
                .Add("original_chars", json.Length)
                .Build();
        }
    }
}
