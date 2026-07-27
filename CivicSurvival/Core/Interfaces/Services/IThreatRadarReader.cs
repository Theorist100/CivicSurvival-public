using System;
using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using Unity.Mathematics;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Interfaces.Services
{
    public readonly struct ThreatRadarSnapshot : IEquatable<ThreatRadarSnapshot>
    {
        public ThreatRadarSnapshot(
            IReadOnlyList<RadarThreatDto> threats,
            IReadOnlyList<RadarTargetDto> targets,
            IReadOnlyList<RadarDefenseDto> defenses)
        {
            Threats = threats ?? System.Array.Empty<RadarThreatDto>();
            Targets = targets ?? System.Array.Empty<RadarTargetDto>();
            Defenses = defenses ?? System.Array.Empty<RadarDefenseDto>();
        }

        public IReadOnlyList<RadarThreatDto> Threats { get; }
        public IReadOnlyList<RadarTargetDto> Targets { get; }
        public IReadOnlyList<RadarDefenseDto> Defenses { get; }

        public static ThreatRadarSnapshot Empty { get; } =
            new(System.Array.Empty<RadarThreatDto>(), System.Array.Empty<RadarTargetDto>(), System.Array.Empty<RadarDefenseDto>());

        public bool Equals(ThreatRadarSnapshot other)
            => SequenceEquals(Threats, other.Threats)
                && SequenceEquals(Targets, other.Targets)
                && SequenceEquals(Defenses, other.Defenses);

        public override bool Equals(object? obj)
            => obj is ThreatRadarSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Threats is null ? 0 : Threats.Count,
                Targets is null ? 0 : Targets.Count,
                Defenses is null ? 0 : Defenses.Count);


        public static bool operator ==(ThreatRadarSnapshot left, ThreatRadarSnapshot right)
            => left.Equals(right);

        public static bool operator !=(ThreatRadarSnapshot left, ThreatRadarSnapshot right)
            => !left.Equals(right);

        private static bool SequenceEquals<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.Count != right.Count) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < left.Count; i++)
            {
                if (!comparer.Equals(left[i], right[i]))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Read-only interface for accessing radar threat data.
    /// Used by CameraTrackingSystem and ThreatUISystem without importing Services layer.
    /// Null-object: empty threat/target lists; GetMapBounds returns
    /// default((float3, float3)) — UI treats `min == max` as "bounds not ready".
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.ThreatsAirDefenseName)]
    public interface IThreatRadarReader
    {
        [NullReturnNull]
        IVersionedView<ThreatRadarSnapshot>? RadarView { get; }

        [NullReturnEmpty]
        IReadOnlyList<RadarThreatDto> GetRadarThreats();

        [NullReturnEmpty]
        IReadOnlyList<RadarTargetDto> GetRadarTargets();

        [NullReturnEmpty]
        IReadOnlyList<RadarDefenseDto> GetRadarDefenses();

        (float3 min, float3 max) GetMapBounds();
    }
}
