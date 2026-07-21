using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Zero-alloc enum-to-string lookup. Caches names at first use per type.
    /// Usage: EnumName&lt;MyEnum&gt;.Get(value) instead of value.ToString()
    /// </summary>
#pragma warning disable CA1000 // Static members on generic type is the design pattern here
#pragma warning disable CA1308 // Lower() intentionally returns lowercase for JSON serialization
    public static class EnumName<T> where T : struct, Enum
    {
#pragma warning disable CIVIC148 // Cache derived from compile-time enum values — immutable after init
        private static readonly Dictionary<T, string> s_Names = BuildCache();
        private static readonly Dictionary<T, string> s_LowerNames = BuildLowerCache();
#pragma warning restore CIVIC148

        private static Dictionary<T, string> BuildCache()
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var dict = new Dictionary<T, string>(values.Length);
            foreach (var v in values)
                dict[v] = v.ToString();
            return dict;
        }

        private static Dictionary<T, string> BuildLowerCache()
        {
            var values = (T[])Enum.GetValues(typeof(T));
            var dict = new Dictionary<T, string>(values.Length);
            foreach (var v in values)
#pragma warning disable CIVIC062 // Init-time allocation (static ctor, cached in dictionary)
                dict[v] = v.ToString().ToLowerInvariant();
#pragma warning restore CIVIC062
            return dict;
        }

        /// <summary>Get cached name for enum value (no allocation).</summary>
        public static string Get(T value)
            => s_Names.TryGetValue(value, out var name) ? name : value.ToString();

        /// <summary>Get cached lowercase name for enum value (no allocation).</summary>
        public static string Lower(T value)
#pragma warning disable CIVIC062 // Fallback path: only allocates for values not in cache
            => s_LowerNames.TryGetValue(value, out var name) ? name : value.ToString().ToLowerInvariant();
#pragma warning restore CIVIC062
    }
}
