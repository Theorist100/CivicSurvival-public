using System;
using System.Globalization;
using System.Text;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Fluent JSON builder for simple object/array serialization.
    /// Uses pooled StringBuilders for efficiency.
    ///
    /// Usage:
    /// <code>
    /// // Object
    /// JsonBuilder.Object()
    ///     .Add("x", 10)
    ///     .Add("y", 20.5f, "F1")
    ///     .Add("name", "test")
    ///     .Build();
    ///
    /// // Array of objects
    /// var array = JsonBuilder.Array();
    /// foreach (var item in items)
    /// {
    ///     array.AddItem(JsonBuilder.Object()
    ///         .Add("id", item.Id)
    ///         .Add("value", item.Value));
    /// }
    /// array.Build();
    /// </code>
    /// </summary>
    public class JsonBuilder
    {
        private readonly StringBuilder m_Sb;
        private readonly bool m_IsArray;
        private bool m_HasItems;
        private bool m_Built;
        private string? m_Result;  // Cache result to prevent pool corruption on repeated Build()

        // Pool of StringBuilders for efficiency
        private static readonly StringBuilder?[] s_Pool = new StringBuilder?[Engine.DataStructures.STRING_BUILDER_POOL_SIZE];
        private static int s_PoolIndex;

        private JsonBuilder(bool isArray)
        {
            m_IsArray = isArray;
            m_Sb = RentStringBuilder();
            m_Sb.Append(isArray ? "[" : "{");
        }

        /// <summary>
        /// Start building a JSON object {}.
        /// </summary>
#pragma warning disable CA1720 // Identifier contains type name - Object is the JSON term
        public static JsonBuilder Object() => new JsonBuilder(false);
#pragma warning restore CA1720

        /// <summary>
        /// Start building a JSON array [].
        /// </summary>
        public static JsonBuilder Array() => new JsonBuilder(true);

        /// <summary>
        /// Add an integer property.
        /// </summary>
        public JsonBuilder Add(string key, int value)
        {
            AppendSeparator();
            AppendPropertyName(key);
            m_Sb.Append(value);
            return this;
        }

        /// <summary>
        /// Add a long property.
        /// </summary>
        public JsonBuilder Add(string key, long value)
        {
            AppendSeparator();
            AppendPropertyName(key);
            m_Sb.Append(value);
            return this;
        }

        /// <summary>
        /// Add a float property with round-trip-safe default format.
        /// </summary>
        public JsonBuilder Add(string key, float value)
        {
            AppendSeparator();
            AppendPropertyName(key);
            AppendFinite(value, "G9");
            return this;
        }

        /// <summary>
        /// Add a float property with custom format (e.g., "F1", "F3").
        /// </summary>
        public JsonBuilder Add(string key, float value, string format)
        {
            AppendSeparator();
            AppendPropertyName(key);
            AppendFinite(value, format);
            return this;
        }

        /// <summary>
        /// Add a double property.
        /// </summary>
        public JsonBuilder Add(string key, double value, string format = "G17")
        {
            AppendSeparator();
            AppendPropertyName(key);
            AppendFinite(value, format);
            return this;
        }

        /// <summary>
        /// Add a boolean property.
        /// </summary>
        public JsonBuilder Add(string key, bool value)
        {
            AppendSeparator();
            AppendPropertyName(key);
            m_Sb.Append(value ? "true" : "false");
            return this;
        }

        /// <summary>
        /// Add a string property with proper JSON escaping. Null is emitted as an empty string by contract.
        /// </summary>
        public JsonBuilder Add(string key, string? value)
        {
            AppendSeparator();
            AppendPropertyName(key);
            m_Sb.Append('"');
            AppendEscaped(value ?? string.Empty);
            m_Sb.Append('"');
            return this;
        }

        /// <summary>
        /// Add a nested object or array.
        /// </summary>
        public JsonBuilder Add(string key, JsonBuilder nested)
        {
            if (nested == null) throw new ArgumentNullException(nameof(nested));
            string nestedJson = nested.Build();
            AppendSeparator();
            AppendPropertyName(key);
            m_Sb.Append(nestedJson);
            return this;
        }

        /// <summary>
        /// Add a pre-serialized JSON value. Empty or invalid-looking values are emitted as null.
        /// </summary>
        public JsonBuilder AddRaw(string key, string? json)
        {
            AppendSeparator();
            AppendPropertyName(key);
            m_Sb.Append(NormalizeRawValue(json));
            return this;
        }

        /// <summary>
        /// Add an item to array (for arrays only).
        /// </summary>
        public JsonBuilder AddItem(JsonBuilder item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            string itemJson = item.Build();
            AppendSeparator();
            m_Sb.Append(itemJson);
            return this;
        }

        /// <summary>
        /// Add a raw integer to array (for arrays only).
        /// </summary>
        public JsonBuilder AddItem(int value)
        {
            AppendSeparator();
            m_Sb.Append(value);
            return this;
        }

        /// <summary>
        /// Add a raw string to array (for arrays only).
        /// </summary>
        public JsonBuilder AddItem(string value)
        {
            AppendSeparator();
            m_Sb.Append('"');
            AppendEscaped(value);
            m_Sb.Append('"');
            return this;
        }

        /// <summary>
        /// Build the JSON string. Returns pooled StringBuilder for reuse.
        /// Safe to call multiple times - returns cached result.
        /// </summary>
        public string Build()
        {
            if (m_Built) return m_Result!;  // Return cached result, not potentially-reused StringBuilder

            m_Sb.Append(m_IsArray ? "]" : "}");
            m_Built = true;
            m_Result = m_Sb.ToString();  // Cache before returning to pool

            ReturnStringBuilder(m_Sb);
            return m_Result;
        }

        /// <summary>
        /// Empty array shortcut.
        /// </summary>
        public static string EmptyArray => "[]";

        /// <summary>
        /// Empty object shortcut.
        /// </summary>
        public static string EmptyObject => "{}";

        private void AppendSeparator()
        {
            if (m_Built) throw new InvalidOperationException("Cannot add to JsonBuilder after Build() has been called");
            if (m_HasItems) m_Sb.Append(',');
            m_HasItems = true;
        }

        private void AppendPropertyName(string key)
        {
            m_Sb.Append('"');
            AppendEscaped(key);
            m_Sb.Append("\":");
        }

        private void AppendFinite(float value, string format)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                m_Sb.Append('0');
                return;
            }
            m_Sb.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }

        private void AppendFinite(double value, string format)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                m_Sb.Append('0');
                return;
            }
            m_Sb.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }

        private static string NormalizeRawValue(string? json)
        {
            string raw = json ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return "null";

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c)) continue;
                if (c == '{' || c == '[' || c == '"' || c == '-' || (c >= '0' && c <= '9'))
                {
                    return raw;
                }
                if (StartsWithLiteral(raw, i, "true") ||
                    StartsWithLiteral(raw, i, "false") ||
                    StartsWithLiteral(raw, i, "null"))
                {
                    return raw;
                }
                return "null";
            }

            return "null";
        }

        private static bool StartsWithLiteral(string value, int offset, string literal)
        {
            if (value.Length - offset < literal.Length)
                return false;

            for (int i = 0; i < literal.Length; i++)
            {
                if (value[offset + i] != literal[i])
                    return false;
            }

            int end = offset + literal.Length;
            return end == value.Length || char.IsWhiteSpace(value[end]);
        }

        private void AppendEscaped(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                // Standardized: empty string for null/empty
                return;
            }

            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': m_Sb.Append("\\\""); break;
                    case '\\': m_Sb.Append("\\\\"); break;
                    case '\n': m_Sb.Append("\\n"); break;
                    case '\r': m_Sb.Append("\\r"); break;
                    case '\t': m_Sb.Append("\\t"); break;
                    case '\b': m_Sb.Append("\\b"); break;  // FIX: backspace
                    case '\f': m_Sb.Append("\\f"); break;  // FIX: form feed
                    default:
                        // FIX: Escape other control characters (U+0000-U+001F) as \uXXXX
                        if (c < 0x20)
                        {
                            m_Sb.Append("\\u");
                            m_Sb.Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            m_Sb.Append(c);
                        }
                        break;
                }
            }
        }

        private static StringBuilder RentStringBuilder()
        {
            lock (s_Pool)
            {
                if (s_PoolIndex > 0)
                {
                    s_PoolIndex--;
                    var sb = s_Pool[s_PoolIndex];
                    s_Pool[s_PoolIndex] = null!;
                    if (sb != null)
                    {
                        sb.Clear();
                        return sb;
                    }
                }
            }
            return new StringBuilder(Engine.DataStructures.STRING_BUILDER_SIZE);
        }

        private static void ReturnStringBuilder(StringBuilder sb)
        {
            if (sb.Capacity > Engine.DataStructures.STRING_BUILDER_MAX_CAPACITY) return; // Don't pool large builders

            lock (s_Pool)
            {
                if (s_PoolIndex < s_Pool.Length)
                {
                    s_Pool[s_PoolIndex] = sb;
                    s_PoolIndex++;
                }
            }
        }
    }
}
