using System;

namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct PlantRepairIntentSnapshot : IEquatable<PlantRepairIntentSnapshot>
    {
        public PlantRepairIntentSnapshot(int pendingIntentCount, int revision)
        {
            PendingIntentCount = pendingIntentCount;
            Revision = revision;
        }

        /// <summary>
        /// Number of plants currently holding a pending repair intent. The
        /// payload UI uses to display per-plant pending markers — but NOT a
        /// reliable dirty-key on its own: if plant A finishes a repair on the
        /// same frame plant B opens one the count is unchanged though the
        /// per-plant set rotated. <see cref="Revision"/> is the version-bump
        /// signal; <see cref="PendingIntentCount"/> is the data.
        /// </summary>
        public int PendingIntentCount { get; }

        /// <summary>
        /// Monotonically-increasing version that ticks on every semantic
        /// mutation of the pending-set (schedule success, drain remove,
        /// hydrate/reset). Forces <see cref="Infrastructure.VersionedView{T}"/>
        /// to bump even when <see cref="PendingIntentCount"/> repeats — UI
        /// observers (PowerGridUISystem) need this to re-evaluate per-plant
        /// state through <c>HasPendingRepairIntent</c>.
        /// </summary>
        public int Revision { get; }

        public static PlantRepairIntentSnapshot Empty { get; } = new(0, 0);

        public bool Equals(PlantRepairIntentSnapshot other)
            => PendingIntentCount == other.PendingIntentCount && Revision == other.Revision;

        public override bool Equals(object? obj)
            => obj is PlantRepairIntentSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(PendingIntentCount, Revision);

        public static bool operator ==(PlantRepairIntentSnapshot left, PlantRepairIntentSnapshot right)
            => left.Equals(right);

        public static bool operator !=(PlantRepairIntentSnapshot left, PlantRepairIntentSnapshot right)
            => !left.Equals(right);
    }
}
