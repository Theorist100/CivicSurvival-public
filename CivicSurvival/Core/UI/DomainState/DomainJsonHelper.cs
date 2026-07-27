// CA1720: Int/Float/Double are intentional short names for JSON serialization methods
#pragma warning disable CA1720

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Zero-allocation JSON serializer for domain state DTOs.
    /// Uses ThreadStatic StringBuilder + ref struct writer.
    /// ~2μs per DTO serialization.
    /// </summary>
    public static class DomainJsonHelper
    {
        [ThreadStatic] private static StringBuilder? s_Builder;

        /// <summary>
        /// Get a cleared StringBuilder from thread-local cache.
        /// Typical usage: var sb = GetBuilder(); dto.WriteTo(sb); string json = sb.ToString();
        /// </summary>
        public static StringBuilder GetBuilder()
        {
            var sb = s_Builder ??= new StringBuilder(1024);
            sb.Clear();
            return sb;
        }

        /// <summary>
        /// Stack-only JSON object writer. Handles comma separation automatically.
        /// Usage:
        /// <code>
        /// var w = new JsonWriter(sb);
        /// w.Int("count", 42);
        /// w.Bool("active", true);
        /// w.Str("name", "test");
        /// w.End();
        /// </code>
        /// </summary>
        public ref struct JsonWriter
        {
            private readonly StringBuilder _sb;
            private bool _needsComma;
            private bool _outerNeedsComma;
            private bool _ended;

            public JsonWriter(StringBuilder sb)
            {
                _sb = sb;
                _needsComma = false;
                _outerNeedsComma = false;
                _ended = false;
                sb.Append('{');
            }

            /// <summary>
            /// Open a nested object under the given key. Pair with <see cref="EndObject"/>
            /// before writing any other fields on the outer object. Single level of nesting
            /// only; the generator emits one outer + one inner writer pair per subtype.
            /// </summary>
            public void BeginObject(string name)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append('{');
                _outerNeedsComma = _needsComma;
                _needsComma = false;
            }

            /// <summary>
            /// Close the nested object opened by the most recent <see cref="BeginObject"/>.
            /// </summary>
            public void EndObject()
            {
                _sb.Append('}');
                _needsComma = _outerNeedsComma;
                _outerNeedsComma = false;
            }

            public void Int(string name, int value)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append(value);
            }

            /// <summary>
            /// Writes a JSON number. JavaScript parses numbers as IEEE-754 doubles; use Str for values that can exceed 2^53.
            /// </summary>
            public void Long(string name, long value)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append(value);
            }

            public void Bool(string name, bool value)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append(value ? "true" : "false");
            }

            public void Float(string name, float value)
            {
                Sep();
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                AppendName(_sb, name);
                _sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
            }

            public void Double(string name, double value)
            {
                Sep();
                if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
                AppendName(_sb, name);
                _sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
            }

            public void Str(string name, string value)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append('"');
                AppendEscaped(_sb, value);
                _sb.Append('"');
            }

            /// <summary>
            /// Append pre-serialized JSON value (array, object, or null). No quoting.
            /// </summary>
            public void Raw(string name, string json)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append(NormalizeRawValue(json));
            }

            public void Raw(string name, ActionAvailabilityField value)
            {
                Sep();
                AppendName(_sb, name);
                _sb.Append("{\"CanRun\":");
                _sb.Append(value.CanRun ? "true" : "false");
                _sb.Append(",\"LockedReasonId\":\"");
                AppendEscaped(_sb, value.LockedReasonId ?? "");
                _sb.Append("\",\"EffectiveCost\":");
                _sb.Append(value.EffectiveCost);
                _sb.Append('}');
            }

            /// <summary>
            /// Emit a JSON object whose keys are the dictionary keys and whose values are int.
            /// Skips writing the field entirely if the dictionary is null.
            /// </summary>
            public void DictInt(string name, IReadOnlyDictionary<string, int>? values)
            {
                if (values == null) return;
                Sep();
                AppendName(_sb, name);
                _sb.Append('{');
                bool first = true;
                foreach (var pair in values)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    _sb.Append('"');
                    AppendEscaped(_sb, pair.Key);
                    _sb.Append("\":");
                    _sb.Append(pair.Value);
                }
                _sb.Append('}');
            }

            /// <summary>
            /// Emit a JSON object whose keys are the dictionary keys and whose values are long.
            /// Skips writing the field entirely if the dictionary is null.
            /// </summary>
            public void DictLong(string name, IReadOnlyDictionary<string, long>? values)
            {
                if (values == null) return;
                Sep();
                AppendName(_sb, name);
                _sb.Append('{');
                bool first = true;
                foreach (var pair in values)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    _sb.Append('"');
                    AppendEscaped(_sb, pair.Key);
                    _sb.Append("\":");
                    _sb.Append(pair.Value);
                }
                _sb.Append('}');
            }

            /// <summary>
            /// Emit a JSON object whose keys are the dictionary keys and whose values are escaped strings.
            /// Skips writing the field entirely if the dictionary is null.
            /// </summary>
            public void DictStr(string name, IReadOnlyDictionary<string, string>? values)
            {
                if (values == null) return;
                Sep();
                AppendName(_sb, name);
                _sb.Append('{');
                bool first = true;
                foreach (var pair in values)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    _sb.Append('"');
                    AppendEscaped(_sb, pair.Key);
                    _sb.Append("\":\"");
                    AppendEscaped(_sb, pair.Value ?? string.Empty);
                    _sb.Append('"');
                }
                _sb.Append('}');
            }

            public void End()
            {
                if (_ended)
                    throw new InvalidOperationException("JsonWriter.End called twice");
                _ended = true;
                _sb.Append('}');
            }

            private void Sep()
            {
                if (_ended)
                    throw new InvalidOperationException("JsonWriter used after End");
                if (_needsComma) _sb.Append(',');
                else _needsComma = true;
            }

            private static string NormalizeRawValue(string json)
            {
                if (string.IsNullOrWhiteSpace(json)) return "null";

                for (int i = 0; i < json.Length; i++)
                {
                    char c = json[i];
                    if (char.IsWhiteSpace(c)) continue;
                    if (c == '{' || c == '[' || c == '"' || c == 't' || c == 'f' || c == 'n' || c == '-' ||
                        (c >= '0' && c <= '9'))
                    {
                        return json;
                    }

                    throw new ArgumentException($"Raw JSON value starts with invalid character '{c}'", nameof(json));
                }

                return "null";
            }

            private static void AppendName(StringBuilder sb, string name)
            {
                sb.Append('"');
                AppendEscaped(sb, name);
                sb.Append("\":");
            }

            private static void AppendEscaped(StringBuilder sb, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c < '\u0020')
                                sb.Append("\\u").Append(((int)c).ToString("X4"));
                            else
                                sb.Append(c);
                            break;
                    }
                }
            }
        }
    }
}
