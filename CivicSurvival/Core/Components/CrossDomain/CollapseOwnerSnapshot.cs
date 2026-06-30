using System;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct CollapseOwnerSnapshot : IEquatable<CollapseOwnerSnapshot>
    {
        public CollapseOwnerSnapshot(int ownerCount, int revision)
        {
            OwnerCount = ownerCount;
            Revision = revision;
        }

        /// <summary>
        /// Number of collapsed-producer entities. Aggregate count — NOT a
        /// reliable dirty-key on its own: if producer A is restored on the
        /// same tick that producer B collapses, count is unchanged but the
        /// per-building identity set rotated. <see cref="Revision"/> is the
        /// version-bump signal.
        /// </summary>
        public int OwnerCount { get; }

        /// <summary>
        /// Monotonically-increasing version that ticks whenever the
        /// per-building collapsed set changes identity (add, remove, or
        /// rotation). Combined with <see cref="OwnerCount"/> in
        /// <see cref="Equals"/> so any per-building consumer that caches a
        /// cursor across frames sees the rotation case.
        /// </summary>
        public int Revision { get; }

        public static CollapseOwnerSnapshot Empty { get; } = new(0, 0);

        public bool Equals(CollapseOwnerSnapshot other)
            => OwnerCount == other.OwnerCount && Revision == other.Revision;

        public override bool Equals(object? obj)
            => obj is CollapseOwnerSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(OwnerCount, Revision);

        public static bool operator ==(CollapseOwnerSnapshot left, CollapseOwnerSnapshot right)
            => left.Equals(right);

        public static bool operator !=(CollapseOwnerSnapshot left, CollapseOwnerSnapshot right)
            => !left.Equals(right);
    }
}
