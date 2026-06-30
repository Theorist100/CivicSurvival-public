using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Holds a set of post-load participants (validators, initializers, singleton
    /// owners, etc.) registered by their owning systems in OnCreate(). Tracks each
    /// participant's type name for diagnostics and, when an order accessor is
    /// supplied, exposes a stable sort by that order.
    ///
    /// One instance per participant kind in <see cref="PostLoadValidationSystem"/>.
    /// The execution loop (try/catch per participant, success/failure tally,
    /// summary log) lives in that system's RunAll helper, since logging context is
    /// shared across kinds; this type only owns membership and ordering.
    /// </summary>
    internal sealed class PostLoadRegistry<T> where T : class
    {
        private readonly List<T> m_Items;
        private readonly Dictionary<T, string> m_Names;
        private readonly string m_FallbackName;
        private readonly Func<T, int>? m_Order;

        /// <param name="capacity">Initial backing-list capacity.</param>
        /// <param name="fallbackName">Name reported when a participant is missing from the name cache.</param>
        /// <param name="order">Optional order accessor; lower runs earlier. Null = unordered (insertion order, no order suffix in logs).</param>
        public PostLoadRegistry(int capacity, string fallbackName, Func<T, int>? order = null)
        {
            m_Items = new List<T>(capacity);
            m_Names = new Dictionary<T, string>(capacity);
            m_FallbackName = fallbackName;
            m_Order = order;
        }

        public int Count => m_Items.Count;

        public IReadOnlyList<T> Items => m_Items;

        public void Register(T item)
        {
            if (!m_Items.Contains(item))
            {
                m_Items.Add(item);
                m_Names[item] = item.GetType().Name;
            }
        }

        public void Unregister(T item)
        {
            m_Items.Remove(item);
            m_Names.Remove(item);
        }

        public string NameOf(T item)
            => m_Names.TryGetValue(item, out var cachedName) ? cachedName : m_FallbackName;

        /// <summary>Order suffix for error logs (e.g. " (order=60)"), or empty when unordered.</summary>
        public string OrderSuffix(T item)
            => m_Order == null ? string.Empty : $" (order={m_Order(item)})";

        public void Clear()
        {
            m_Items.Clear();
            m_Names.Clear();
        }

        /// <summary>
        /// Stable insertion sort by the configured order accessor (lower runs earlier).
        /// No-op when this registry is unordered. Insertion sort keeps registration
        /// order among equal keys, which the hydration chains rely on.
        /// </summary>
        public void StableSort()
        {
            if (m_Order == null)
                return;

            for (int i = 1; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                int key = m_Order(item);
                int j = i - 1;
                while (j >= 0 && m_Order(m_Items[j]) > key)
                {
                    m_Items[j + 1] = m_Items[j];
                    j--;
                }
                m_Items[j + 1] = item;
            }
        }
    }
}
