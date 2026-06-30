using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Lightweight allocation-light JSON reader without Colossal.Json reflection.
    /// Targets simple cases — flat dictionaries, single top-level fields, raw substrings.
    /// </summary>
    internal static class JsonStream
    {
        private static readonly LogContext Log = new("JsonStream");
        private const int UnicodeEscapeSequenceLength = 6;

        public static void SkipWhitespace(string json, ref int p)
        {
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
        }

        /// <summary>
        /// Skip a single JSON value (object/array/string/number/bool/null) starting at p.
        /// Advances p past the value. Returns false if malformed.
        /// </summary>
        public static bool SkipJsonValue(string json, ref int p)
        {
            if (p >= json.Length) return false;
            char c = json[p];
            if (c == '"')
            {
                p++;
                while (p < json.Length)
                {
                    if (json[p] == '\\') { p += 2; continue; }
                    if (json[p] == '"') { p++; return true; }
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
                        if (ch == '"') inString = false;
                        p++;
                        continue;
                    }
                    if (ch == '"') { inString = true; p++; continue; }
                    if (ch == '{' || ch == '[') depth++;
                    else if (ch == '}' || ch == ']') depth--;
                    p++;
                }
                return depth == 0;
            }
            // Scalar: number/true/false/null — scan until delimiter
            while (p < json.Length)
            {
                char ch = json[p];
                if (ch == ',' || ch == '}' || ch == ']' || char.IsWhiteSpace(ch)) break;
                p++;
            }
            return true;
        }

        /// <summary>
        /// Read a JSON string literal starting at p (which must point to opening quote).
        /// Advances p past the closing quote. Returns unescaped value.
        /// </summary>
        public static string ReadJsonString(string json, ref int p)
        {
            if (p >= json.Length || json[p] != '"')
                throw new FormatException("Expected JSON string");
            p++;
            int start = p;
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '"')
                {
                    var fast = json.Substring(start, p - start);
                    p++;
                    return fast;
                }
                if (c == '\\') return ReadJsonStringSlow(json, ref p, start);
                p++;
            }
            throw new FormatException("Unterminated JSON string");
        }

        private static string ReadJsonStringSlow(string json, ref int p, int start)
        {
            var sb = new StringBuilder(json.Length - start);
            sb.Append(json, start, p - start);
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '"') { p++; return sb.ToString(); }
                if (c == '\\')
                {
                    if (p + 1 >= json.Length) throw new FormatException("Truncated escape");
                    char esc = json[p + 1];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); p += 2; break;
                        case '\\': sb.Append('\\'); p += 2; break;
                        case '/': sb.Append('/'); p += 2; break;
                        case 'b': sb.Append('\b'); p += 2; break;
                        case 'f': sb.Append('\f'); p += 2; break;
                        case 'n': sb.Append('\n'); p += 2; break;
                        case 'r': sb.Append('\r'); p += 2; break;
                        case 't': sb.Append('\t'); p += 2; break;
                        case 'u':
                            if (p + 5 >= json.Length) throw new FormatException("Truncated \\u escape");
                            var hex = json.Substring(p + 2, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw new FormatException("Invalid \\u escape");
                            sb.Append((char)code);
                            p += UnicodeEscapeSequenceLength;
                            break;
                        default:
                            throw new FormatException("Invalid escape \\" + esc);
                    }
                    continue;
                }
                sb.Append(c);
                p++;
            }
            throw new FormatException("Unterminated JSON string");
        }

        /// <summary>
        /// Find a top-level string field by key. Returns null if absent or value is not a string.
        /// Safe for malformed input — returns null instead of throwing.
        /// </summary>
        public static string? TryReadStringField(string json, string key)
        {
            try
            {
                int p = 0;
                SkipWhitespace(json, ref p);
                if (p >= json.Length || json[p] != '{') return null;
                p++;
                while (p < json.Length)
                {
                    SkipWhitespace(json, ref p);
                    if (p < json.Length && json[p] == '}') return null;
                    if (p >= json.Length || json[p] != '"') return null;
                    var fieldKey = ReadJsonString(json, ref p);
                    SkipWhitespace(json, ref p);
                    if (p >= json.Length || json[p] != ':') return null;
                    p++;
                    SkipWhitespace(json, ref p);
                    if (fieldKey == key)
                    {
                        if (p >= json.Length || json[p] != '"') return null;
                        return ReadJsonString(json, ref p);
                    }
                    if (!SkipJsonValue(json, ref p)) return null;
                    SkipWhitespace(json, ref p);
                    if (p < json.Length && json[p] == ',') { p++; continue; }
                    return null;
                }
                return null;
            }
            catch (FormatException ex)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"TryReadStringField ignored malformed JSON for key '{key}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find a top-level numeric/boolean scalar field by key and return its raw token
        /// (e.g. "3", "true"). Returns null if absent or the value is a string/object/array.
        /// Safe for malformed input — returns null instead of throwing.
        /// </summary>
        public static string? TryReadScalarField(string json, string key)
        {
            try
            {
                int p = 0;
                SkipWhitespace(json, ref p);
                if (p >= json.Length || json[p] != '{') return null;
                p++;
                while (p < json.Length)
                {
                    SkipWhitespace(json, ref p);
                    if (p < json.Length && json[p] == '}') return null;
                    if (p >= json.Length || json[p] != '"') return null;
                    var fieldKey = ReadJsonString(json, ref p);
                    SkipWhitespace(json, ref p);
                    if (p >= json.Length || json[p] != ':') return null;
                    p++;
                    SkipWhitespace(json, ref p);
                    if (fieldKey == key)
                    {
                        // Scalar tokens only: numbers, true/false/null. Strings/objects/arrays → null.
                        if (p >= json.Length) return null;
                        char c = json[p];
                        if (c == '"' || c == '{' || c == '[') return null;
                        int start = p;
                        while (p < json.Length)
                        {
                            char t = json[p];
                            if (t == ',' || t == '}' || t == ']' || char.IsWhiteSpace(t)) break;
                            p++;
                        }
                        return p > start ? json.Substring(start, p - start) : null;
                    }
                    if (!SkipJsonValue(json, ref p)) return null;
                    SkipWhitespace(json, ref p);
                    if (p < json.Length && json[p] == ',') { p++; continue; }
                    return null;
                }
                return null;
            }
            catch (FormatException ex)
            {
                if (Log.IsDebugEnabled)
                    Log.Debug($"TryReadScalarField ignored malformed JSON for key '{key}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find a top-level integer field by key. Returns <paramref name="fallback"/> if the
        /// field is absent, non-numeric, or malformed.
        /// </summary>
        public static int TryReadIntField(string json, string key, int fallback = 0)
        {
            var token = TryReadScalarField(json, key);
            return token != null
                && int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        /// <summary>
        /// Find a top-level boolean field by key. Returns <paramref name="fallback"/> if the
        /// field is absent, non-boolean, or malformed.
        /// </summary>
        public static bool TryReadBoolField(string json, string key, bool fallback = false)
        {
            var token = TryReadScalarField(json, key);
            if (token == "true") return true;
            if (token == "false") return false;
            return fallback;
        }

        /// <summary>
        /// Parse a flat dictionary { "k1": "v1", "k2": "v2", ... }. Values must be strings.
        /// Throws FormatException on malformed input.
        /// </summary>
        public static Dictionary<string, string> ParseFlatStringDict(string json)
        {
            var result = new Dictionary<string, string>();
            int p = 0;
            SkipWhitespace(json, ref p);
            if (p >= json.Length || json[p] != '{')
                throw new FormatException("Expected JSON object");
            p++;
            while (p < json.Length)
            {
                SkipWhitespace(json, ref p);
                if (p < json.Length && json[p] == '}') return result;
                if (p >= json.Length || json[p] != '"')
                    throw new FormatException("Expected key (string)");
                var key = ReadJsonString(json, ref p);
                SkipWhitespace(json, ref p);
                if (p >= json.Length || json[p] != ':')
                    throw new FormatException("Expected ':' after key '" + key + "'");
                p++;
                SkipWhitespace(json, ref p);
                if (p >= json.Length)
                    throw new FormatException("Unexpected end after key '" + key + "'");
                if (json[p] == '"')
                {
                    result[key] = ReadJsonString(json, ref p);
                }
                else if (json[p] == 'n' && p + 4 <= json.Length && json.Substring(p, 4) == "null")
                {
                    result[key] = string.Empty;
                    p += 4;
                }
                else
                {
                    throw new FormatException("Value for key '" + key + "' must be a string");
                }
                SkipWhitespace(json, ref p);
                if (p < json.Length && json[p] == ',') { p++; continue; }
                if (p < json.Length && json[p] == '}') return result;
                throw new FormatException("Expected ',' or '}'");
            }
            throw new FormatException("Unterminated JSON object");
        }
    }
}
