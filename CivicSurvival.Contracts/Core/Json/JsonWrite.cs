using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CivicSurvival.Core.Json
{
    /// <summary>
    /// Minimal JSON writing helpers for generated DTOs.
    /// No reflection, no Colossal.Json — direct StringBuilder operations.
    /// </summary>
    public static class JsonWrite
    {
        public static void AppendKey(StringBuilder sb, string name)
        {
            sb.Append('"').Append(name).Append("\":");
        }

        public static void AppendString(StringBuilder sb, string? value)
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
                        case '"': sb.Append("\\\""); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                            {
                                sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        public static void AppendInt(StringBuilder sb, int value)
        {
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public static void AppendLong(StringBuilder sb, long value)
        {
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public static void AppendFloat(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) { sb.Append('0'); return; }
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void AppendDouble(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { sb.Append('0'); return; }
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void AppendBool(StringBuilder sb, bool value)
        {
            sb.Append(value ? "true" : "false");
        }

        public static void AppendNull(StringBuilder sb)
        {
            sb.Append("null");
        }

        public static void AppendStringArray(StringBuilder sb, IReadOnlyList<string>? values)
        {
            sb.Append('[');
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendString(sb, values[i]);
                }
            }
            sb.Append(']');
        }
    }
}
