using System;
using System.Collections.Generic;
using System.Threading;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Snapshot returned by <see cref="IVersionedView{TSnapshot}.Observe"/>.
    /// </summary>
    public readonly struct ViewSnapshot<TSnapshot>
    {
        public ViewSnapshot(TSnapshot value, int version, bool changed)
        {
            Value = value;
            Version = version;
            Changed = changed;
        }

        public readonly TSnapshot Value;
        public readonly int Version;
        public readonly bool Changed;
    }

    /// <summary>
    /// Consumer-facing view over a producer-published snapshot.
    /// </summary>
    public interface IVersionedView<TSnapshot>
    {
        int Version { get; }

        ViewSnapshot<TSnapshot> Observe(ref int observerVersion);
    }

    /// <summary>
    /// Single-writer, multi-reader versioned snapshot primitive.
    /// Producers publish immutable snapshots; readers observe through an opaque
    /// integer cursor passed by reference.
    /// </summary>
    public sealed class VersionedView<TSnapshot> : IVersionedView<TSnapshot>
    {
        private sealed class State
        {
            public State(int version, TSnapshot value, bool hasValue)
            {
                Version = version;
                Value = value;
                HasValue = hasValue;
            }

            public readonly int Version;
            public readonly TSnapshot Value;
            public readonly bool HasValue;
        }

        private State m_State;

        /// <summary>
        /// Seed the view with an explicit initial value. Required for snapshot
        /// types whose <c>default(T)</c> is invalid — for example a readonly
        /// struct with <c>IReadOnlyList&lt;…&gt;</c> auto-properties: <c>default(T)</c>
        /// leaves those properties null, and a consumer that <c>Observe()</c>s
        /// before the producer's first <c>Publish</c> would dereference null
        /// (the bug fixed in this commit was exactly that path:
        /// <c>VersionedView&lt;CivilianDamageSnapshot&gt;</c> → PowerGridUISystem
        /// NRE on <c>Buildings.Count</c>).
        ///
        /// The parameterless ctor was removed deliberately so the compiler
        /// forces every call site to choose an initial value.
        /// </summary>
        public VersionedView(TSnapshot initial)
        {
            m_State = new State(0, initial, true);
        }

        public int Version => Volatile.Read(ref m_State).Version;

        public void Publish(TSnapshot next)
        {
            Publish(next, EqualityComparer<TSnapshot>.Default);
        }

        public void Publish(TSnapshot next, IEqualityComparer<TSnapshot> comparer)
        {
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));

            State current = Volatile.Read(ref m_State);
            if (current.HasValue && comparer.Equals(current.Value, next))
            {
                return;
            }

            var published = new State(NextVersion(current.Version), next, true);
            Volatile.Write(ref m_State, published);
        }

        public ViewSnapshot<TSnapshot> Observe(ref int observerVersion)
        {
            State current = Volatile.Read(ref m_State);
            bool changed = observerVersion != current.Version;
            if (changed)
            {
                observerVersion = current.Version;
            }

            return new ViewSnapshot<TSnapshot>(current.Value, current.Version, changed);
        }

        private static int NextVersion(int version)
        {
            return version == int.MaxValue ? 1 : version + 1;
        }
    }

    public static class VersionedViewComparers
    {
        private const int HashSeed = 17;
        private const int HashMultiplier = 31;

        public static IEqualityComparer<IReadOnlyList<TItem>> ReadOnlyList<TItem>()
        {
            return ReadOnlyListEqualityComparer<TItem>.Default;
        }

        public static IEqualityComparer<IReadOnlyList<TItem>> ReadOnlyList<TItem>(
            IEqualityComparer<TItem> itemComparer)
        {
            if (itemComparer == null) throw new ArgumentNullException(nameof(itemComparer));
            return new ReadOnlyListEqualityComparer<TItem>(itemComparer);
        }

        private sealed class ReadOnlyListEqualityComparer<TItem> : IEqualityComparer<IReadOnlyList<TItem>>
        {
            public static readonly ReadOnlyListEqualityComparer<TItem> Default =
                new(EqualityComparer<TItem>.Default);

            private readonly IEqualityComparer<TItem> m_ItemComparer;

            public ReadOnlyListEqualityComparer(IEqualityComparer<TItem> itemComparer)
            {
                m_ItemComparer = itemComparer;
            }

            public bool Equals(IReadOnlyList<TItem>? x, IReadOnlyList<TItem>? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                if (x.Count != y.Count) return false;

                for (int i = 0; i < x.Count; i++)
                {
                    if (!m_ItemComparer.Equals(x[i], y[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(IReadOnlyList<TItem> obj)
            {
                if (obj == null) throw new ArgumentNullException(nameof(obj));

                unchecked
                {
                    int hash = HashSeed;
                    for (int i = 0; i < obj.Count; i++)
                    {
                        hash = (hash * HashMultiplier) + GetItemHashCode(obj[i]);
                    }

                    return hash;
                }
            }

            private int GetItemHashCode(TItem item)
            {
                return ReferenceEquals(item, null) ? 0 : m_ItemComparer.GetHashCode(item);
            }
        }
    }
}
