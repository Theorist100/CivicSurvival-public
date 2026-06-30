// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/telemetry.contract.yaml
// SourceHash:       sha256:3515e64e73b49d520cb25fc87d0bd83ce736be3b456788d3eb5009358fc13b3b
// Generator:        scripts/generators/telemetry.py
// GeneratorVersion: 1.0.0
// ContractVersion:  1.15
// GeneratedAt:      2026-05-14T00:00:00Z

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Services.Telemetry
{
    public static class TelemetryPayloadReader
    {
        // Stream-parses a single event JSON object — no AST build, no Colossal.Json calls.
        // Caller-provided JSON must be a UTF-16 string (standard for .NET).
        public static TelemetryEvent ParseEvent(string json)
        {
            var evt = new TelemetryEvent { EventId = string.Empty, SessionId = string.Empty, Type = string.Empty, Data = new RawJson("{}") };
            bool hasEventId = false, hasSessionId = false, hasType = false, hasTimestamp = false;
            int p = 0;
            SkipWhitespace(json, ref p);
            if (p >= json.Length || json[p] != '{')
                throw new ContractValidationException("TelemetryEvent must be a JSON object");
            p++;
            while (p < json.Length)
            {
                SkipWhitespace(json, ref p);
                if (p < json.Length && json[p] == '}') { p++; break; }
                if (p >= json.Length || json[p] != '\"')
                    throw new ContractValidationException("TelemetryEvent: expected key");
                var key = ReadJsonString(json, ref p);
                SkipWhitespace(json, ref p);
                if (p >= json.Length || json[p] != ':')
                    throw new ContractValidationException($"TelemetryEvent.{key}: expected ':'");
                p++;
                SkipWhitespace(json, ref p);
                switch (key)
                {
                    case nameof(TelemetryEvent.EventId):
                        if (p >= json.Length || json[p] != '\"')
                            throw new ContractValidationException("TelemetryEvent.EventId must be a string");
                        evt.EventId = ReadJsonString(json, ref p);
                        hasEventId = true;
                        break;
                    case nameof(TelemetryEvent.SessionId):
                        if (p >= json.Length || json[p] != '\"')
                            throw new ContractValidationException("TelemetryEvent.SessionId must be a string");
                        evt.SessionId = ReadJsonString(json, ref p);
                        hasSessionId = true;
                        break;
                    case nameof(TelemetryEvent.Type):
                        if (p >= json.Length || json[p] != '\"')
                            throw new ContractValidationException("TelemetryEvent.Type must be a string");
                        evt.Type = ReadJsonString(json, ref p);
                        hasType = true;
                        break;
                    case nameof(TelemetryEvent.Timestamp):
                        if (p >= json.Length || json[p] != '\"')
                            throw new ContractValidationException("TelemetryEvent.Timestamp must be a string");
                        var ts = ReadJsonString(json, ref p);
                        if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var stamp))
                            throw new ContractValidationException("TelemetryEvent.Timestamp is not a valid timestamp");
                        evt.Timestamp = stamp;
                        hasTimestamp = true;
                        break;
                    case nameof(TelemetryEvent.Data):
                        int dataStart = p;
                        if (!SkipJsonValue(json, ref p))
                            throw new ContractValidationException("TelemetryEvent.Data is malformed");
                        var dataRaw = json.Substring(dataStart, p - dataStart);
                        evt.Data = new RawJson(string.IsNullOrWhiteSpace(dataRaw) ? "{}" : dataRaw);
                        break;
                    default:
                        // Forward-compatible: unknown fields are skipped silently.
                        if (!SkipJsonValue(json, ref p))
                            throw new ContractValidationException($"TelemetryEvent.{key} is malformed");
                        break;
                }
                SkipWhitespace(json, ref p);
                if (p < json.Length && json[p] == ',') { p++; continue; }
                if (p < json.Length && json[p] == '}') { p++; break; }
            }
            if (!hasEventId) evt.EventId = Guid.NewGuid().ToString("D");
            if (!hasSessionId) throw new ContractValidationException("TelemetryEvent.SessionId is required");
            if (!hasType) throw new ContractValidationException("TelemetryEvent.Type is required");
            if (!hasTimestamp) throw new ContractValidationException("TelemetryEvent.Timestamp is required");
            return evt;
        }

        public static List<TelemetryEvent> ParsePayload(string json)
        {
            // Iterate the Events array as raw substrings — avoids one giant JSON.Load on the whole payload
            // (which would also build AST for every nested Data field just to drop it).
            var result = new List<TelemetryEvent>();
            int p = 0;
            SkipWhitespace(json, ref p);
            if (p >= json.Length || json[p] != '{')
                throw new ContractValidationException("TelemetryPayload must be a JSON object");
            p++;
            bool found = false;
            while (p < json.Length)
            {
                SkipWhitespace(json, ref p);
                if (p < json.Length && json[p] == '}') break;
                if (p >= json.Length || json[p] != '\"')
                    throw new ContractValidationException("TelemetryPayload: expected key");
                p++;
                int keyStart = p;
                while (p < json.Length && json[p] != '\"')
                {
                    if (json[p] == '\\') { p += 2; continue; }
                    p++;
                }
                if (p >= json.Length)
                    throw new ContractValidationException("TelemetryPayload: unterminated key");
                var key = json.Substring(keyStart, p - keyStart);
                p++;
                SkipWhitespace(json, ref p);
                if (p >= json.Length || json[p] != ':')
                    throw new ContractValidationException("TelemetryPayload: expected ':' after key");
                p++;
                SkipWhitespace(json, ref p);
                if (key == nameof(TelemetryPayload.Events))
                {
                    if (p >= json.Length || json[p] != '[')
                        throw new ContractValidationException("TelemetryPayload.Events must be an array");
                    p++;
                    int index = 0;
                    while (p < json.Length)
                    {
                        SkipWhitespace(json, ref p);
                        if (p < json.Length && json[p] == ']') { p++; break; }
                        int itemStart = p;
                        if (!SkipJsonValue(json, ref p))
                            throw new ContractValidationException($"TelemetryPayload.Events[{index}] is malformed");
                        var itemJson = json.Substring(itemStart, p - itemStart);
                        result.Add(ParseEvent(itemJson));
                        SkipWhitespace(json, ref p);
                        if (p < json.Length && json[p] == ',') p++;
                        index++;
                    }
                    found = true;
                    break;
                }
                else
                {
                    if (!SkipJsonValue(json, ref p))
                        throw new ContractValidationException($"TelemetryPayload.{key} is malformed");
                    SkipWhitespace(json, ref p);
                    if (p < json.Length && json[p] == ',') p++;
                }
            }
            if (!found)
                throw new ContractValidationException("TelemetryPayload.Events is required and must be an array");
            return result;
        }

        public static List<TelemetryEvent> ParseRetryRecord(string json) => ParsePayload(json);

        // --- Lightweight JSON scanner (no full AST build) ---

        private static string ReadJsonString(string json, ref int p)
        {
            if (p >= json.Length || json[p] != '\"')
                throw new ContractValidationException("Expected JSON string");
            p++;
            int start = p;
            // Fast path: no escapes — return substring directly.
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '\"')
                {
                    var fast = json.Substring(start, p - start);
                    p++;
                    return fast;
                }
                if (c == '\\') return ReadJsonStringSlow(json, ref p, start);
                p++;
            }
            throw new ContractValidationException("Unterminated JSON string");
        }

        private static string ReadJsonStringSlow(string json, ref int p, int start)
        {
            var sb = new StringBuilder(json.Length - start);
            sb.Append(json, start, p - start);
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '\"') { p++; return sb.ToString(); }
                if (c == '\\')
                {
                    if (p + 1 >= json.Length)
                        throw new ContractValidationException("Truncated escape");
                    char esc = json[p + 1];
                    switch (esc)
                    {
                        case '\"': sb.Append('\"'); p += 2; break;
                        case '\\': sb.Append('\\'); p += 2; break;
                        case '/': sb.Append('/'); p += 2; break;
                        case 'b': sb.Append('\b'); p += 2; break;
                        case 'f': sb.Append('\f'); p += 2; break;
                        case 'n': sb.Append('\n'); p += 2; break;
                        case 'r': sb.Append('\r'); p += 2; break;
                        case 't': sb.Append('\t'); p += 2; break;
                        case 'u':
                            if (p + 5 >= json.Length)
                                throw new ContractValidationException("Truncated \\u escape");
                            var hex = json.Substring(p + 2, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw new ContractValidationException("Invalid \\u escape");
                            sb.Append((char)code);
                            p += 6;
                            break;
                        default:
                            throw new ContractValidationException($"Invalid escape \\{esc}");
                    }
                    continue;
                }
                sb.Append(c);
                p++;
            }
            throw new ContractValidationException("Unterminated JSON string");
        }

        private static void SkipWhitespace(string json, ref int p)
        {
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
        }

        private static bool SkipJsonValue(string json, ref int p)
        {
            if (p >= json.Length) return false;
            char c = json[p];
            if (c == '\"')
            {
                p++;
                while (p < json.Length)
                {
                    if (json[p] == '\\') { p += 2; continue; }
                    if (json[p] == '\"') { p++; return true; }
                    p++;
                }
                return false;
            }
            if (c == '{' || c == '[')
            {
                int depth = 1;
                p++;
                bool inString = false;
                while (p < json.Length && depth > 0)
                {
                    char ch = json[p];
                    if (inString)
                    {
                        if (ch == '\\') { p += 2; continue; }
                        if (ch == '\"') inString = false;
                        p++;
                        continue;
                    }
                    if (ch == '\"') { inString = true; p++; continue; }
                    if (ch == '{' || ch == '[') depth++;
                    else if (ch == '}' || ch == ']') depth--;
                    p++;
                }
                return depth == 0;
            }
            // Scalar (number / true / false / null): scan until delimiter
            while (p < json.Length)
            {
                char ch = json[p];
                if (ch == ',' || ch == '}' || ch == ']' || char.IsWhiteSpace(ch)) break;
                p++;
            }
            return true;
        }
    }
}
