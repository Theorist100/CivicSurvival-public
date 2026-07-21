using System;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct AutoDispatchOwnershipSnapshot : IEquatable<AutoDispatchOwnershipSnapshot>
    {
        public AutoDispatchOwnershipSnapshot(bool enabled, int autoSheddedCount, bool isBlockedByVip, int revision)
        {
            Enabled = enabled;
            AutoSheddedCount = autoSheddedCount;
            IsBlockedByVip = isBlockedByVip;
            Revision = revision;
        }

        public bool Enabled { get; }

        /// <summary>
        /// Number of districts currently auto-shedded. Aggregate count — NOT a
        /// reliable dirty-key on its own: if district A is restored on the
        /// same tick that district B is auto-shedded, count is unchanged but
        /// the per-district identity set rotated. <see cref="Revision"/> is
        /// the version-bump signal.
        /// </summary>
        public int AutoSheddedCount { get; }

        public bool IsBlockedByVip { get; }

        /// <summary>
        /// Monotonically-increasing version that ticks whenever the
        /// auto-shedded district set changes identity (add, remove, or
        /// rotation). Combined with the count in <see cref="Equals"/> so any
        /// per-district consumer that caches a cursor across frames sees the
        /// rotation case.
        /// </summary>
        public int Revision { get; }

        public static AutoDispatchOwnershipSnapshot Empty { get; } = new(false, 0, false, 0);

        public bool Equals(AutoDispatchOwnershipSnapshot other)
            => Enabled == other.Enabled
               && AutoSheddedCount == other.AutoSheddedCount
               && IsBlockedByVip == other.IsBlockedByVip
               && Revision == other.Revision;

        public override bool Equals(object? obj)
            => obj is AutoDispatchOwnershipSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Enabled, AutoSheddedCount, IsBlockedByVip, Revision);

        public static bool operator ==(AutoDispatchOwnershipSnapshot left, AutoDispatchOwnershipSnapshot right)
            => left.Equals(right);

        public static bool operator !=(AutoDispatchOwnershipSnapshot left, AutoDispatchOwnershipSnapshot right)
            => !left.Equals(right);
    }
}
