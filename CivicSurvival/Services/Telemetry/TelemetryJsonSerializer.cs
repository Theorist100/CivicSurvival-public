using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CivicSurvival.Services.Telemetry;

namespace CivicSurvival.Services.Telemetry
{
    /// <summary>
    /// JSON serializer for telemetry events using generated contract field names.
    /// Contract field names are emitted as-is; no wire-name translation is performed.
    /// </summary>
    internal sealed class TelemetryJsonSerializer
    {
        public static TelemetryJsonSerializer Instance { get; } = new();

        public string SerializeEvent(TelemetryEvent evt)
        {
            // Data must be either RawJson (pre-serialized) or a TelemetryEventData with generated ToJson().
            // No reflection fallback — preserves "zero Colossal.Json" guarantee.
            var dataJson = evt.Data switch
            {
                RawJson raw => raw.Json,
                TelemetryEventData typed => typed.ToJson(),
                null => "{}",
                _ => throw new InvalidOperationException(
                    $"TelemetryEvent.Data must be RawJson or TelemetryEventData; got {evt.Data.GetType().Name}"),
            };
            return TelemetryPayloadWriter.BuildEventJson(
                EnsureEventId(evt),
                evt.SessionId,
                evt.Type,
                evt.Timestamp.ToUniversalTime(),
                dataJson);
        }

        public string SerializePayload(TelemetryPayload payload)
        {
            var events = new List<string>(payload.Events.Count);
            foreach (var evt in payload.Events)
                events.Add(SerializeEvent(evt));

            return TelemetryPayloadWriter.BuildPayloadJson(
                payload.SessionId,
                payload.ModVersion,
                payload.GameVersion,
                payload.Timestamp.ToUniversalTime(),
                events,
                payload.PlayerId);
        }

        public TelemetryEvent DeserializeEvent(string json) => TelemetryPayloadReader.ParseEvent(json);

        private static string EnsureEventId(TelemetryEvent evt)
        {
            if (string.IsNullOrWhiteSpace(evt.EventId))
                evt.EventId = Guid.NewGuid().ToString("D");
            return evt.EventId;
        }
    }

    /// <summary>
    /// Converts enum-style PascalCase values to snake_case telemetry values.
    /// Field names do not use this helper.
    /// </summary>
    internal static class TelemetryStringExtensions
    {
#pragma warning disable CIVIC148 // Pure function cache: PascalCase→snake_case is deterministic, never stale
        private static readonly ConcurrentDictionary<string, string> s_Cache = new();
#pragma warning restore CIVIC148

        public static string ToSnakeCase(this string str)
        {
            if (string.IsNullOrEmpty(str)) return str;

            if (s_Cache.TryGetValue(str, out var cached)) return cached;

            var chars = new List<char>(str.Length + 8);
            for (var i = 0; i < str.Length; i++)
            {
                var c = str[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) chars.Add('_');
                    chars.Add(char.ToLowerInvariant(c));
                }
                else
                {
                    chars.Add(c);
                }
            }
            var result = new string(chars.ToArray());
            s_Cache.TryAdd(str, result);
            return result;
        }
    }
}
