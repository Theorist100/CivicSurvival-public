using System;

namespace CivicSurvival.Core.Types.Snapshots
{
    public readonly struct AreaCollectSnapshot : IEquatable<AreaCollectSnapshot>
    {
        public readonly bool DistrictsUpdated;

        public AreaCollectSnapshot(bool districtsUpdated)
        {
            DistrictsUpdated = districtsUpdated;
        }

        public static AreaCollectSnapshot Default => new(false);

        public bool Equals(AreaCollectSnapshot other)
            => DistrictsUpdated == other.DistrictsUpdated;

        public override bool Equals(object? obj)
            => obj is AreaCollectSnapshot other && Equals(other);

        public override int GetHashCode() => DistrictsUpdated.GetHashCode();

        public static bool operator ==(AreaCollectSnapshot left, AreaCollectSnapshot right)
            => left.Equals(right);

        public static bool operator !=(AreaCollectSnapshot left, AreaCollectSnapshot right)
            => !left.Equals(right);
    }
}
