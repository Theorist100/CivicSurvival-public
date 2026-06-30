using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Shared helper for narrative event argument dictionaries.
    /// Eliminates duplicate CreateSingleArgDict() across Refugee systems.
    /// </summary>
    public static class NarrativeArgs
    {
        public static IReadOnlyDictionary<string, string> OneArg(int value)
        {
            return new SingleArgDictionary("arg0", value.ToString(CultureInfo.InvariantCulture));
        }

        private sealed class SingleArgDictionary : IReadOnlyDictionary<string, string>
        {
            private readonly string m_Key;
            private readonly string m_Value;

            public SingleArgDictionary(string key, string value)
            {
                m_Key = key;
                m_Value = value;
            }

            public int Count => 1;
            public IEnumerable<string> Keys { get { yield return m_Key; } }
            public IEnumerable<string> Values { get { yield return m_Value; } }
            public string this[string key] => key == m_Key ? m_Value : throw new KeyNotFoundException(key);

            public bool ContainsKey(string key) => key == m_Key;

            public bool TryGetValue(string key, out string value)
            {
                if (key == m_Key)
                {
                    value = m_Value;
                    return true;
                }

                value = string.Empty;
                return false;
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                yield return new KeyValuePair<string, string>(m_Key, m_Value);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
