using System;
using Unity.Mathematics;

namespace CivicSurvival.Core.Types.Snapshots
{
    public readonly struct MapBoundsSnapshot : IEquatable<MapBoundsSnapshot>
    {
        public readonly float3 PlayableOffset;
        public readonly float2 PlayableArea;

        public MapBoundsSnapshot(float3 playableOffset, float2 playableArea)
        {
            PlayableOffset = playableOffset;
            PlayableArea = playableArea;
        }

        public static MapBoundsSnapshot Default => new(default, default);

        public bool Equals(MapBoundsSnapshot other)
            => PlayableOffset.Equals(other.PlayableOffset)
                && PlayableArea.Equals(other.PlayableArea);

        public override bool Equals(object? obj)
            => obj is MapBoundsSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(PlayableOffset, PlayableArea);

        public static bool operator ==(MapBoundsSnapshot left, MapBoundsSnapshot right)
            => left.Equals(right);

        public static bool operator !=(MapBoundsSnapshot left, MapBoundsSnapshot right)
            => !left.Equals(right);
    }
}
