using System;
using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Interfaces.Threats
{
    public readonly struct ThreatTargetSnapshot : IEquatable<ThreatTargetSnapshot>
    {
        public ThreatTargetSnapshot(IReadOnlyList<ThreatTargetDto> targets)
        {
            Targets = targets ?? System.Array.Empty<ThreatTargetDto>();
        }

        public IReadOnlyList<ThreatTargetDto> Targets { get; }

        public static ThreatTargetSnapshot Empty { get; } =
            new(System.Array.Empty<ThreatTargetDto>());

        public bool Equals(ThreatTargetSnapshot other)
        {
            if (ReferenceEquals(Targets, other.Targets)) return true;
            if (Targets is null || other.Targets is null) return false;
            if (Targets.Count != other.Targets.Count) return false;
            var comparer = EqualityComparer<ThreatTargetDto>.Default;
            for (int i = 0; i < Targets.Count; i++)
            {
                if (!comparer.Equals(Targets[i], other.Targets[i]))
                    return false;
            }
            return true;
        }

        public override bool Equals(object? obj)
            => obj is ThreatTargetSnapshot other && Equals(other);

        public override int GetHashCode()
            => Targets is null ? 0 : Targets.Count;

        public static bool operator ==(ThreatTargetSnapshot left, ThreatTargetSnapshot right)
            => left.Equals(right);

        public static bool operator !=(ThreatTargetSnapshot left, ThreatTargetSnapshot right)
            => !left.Equals(right);
    }

    /// <summary>
    /// Main-thread reader for the latest throttled threat-target snapshot.
    /// Implementations publish complete snapshots through <see cref="TargetsView"/>.
    /// Consumers should observe with a local cursor and should not retain
    /// snapshot lists across frames.
    /// Null-object: TargetsView=null.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.WavesName)]
    public interface IThreatTargetReader
    {
        [NullReturnNull]
        IVersionedView<ThreatTargetSnapshot>? TargetsView { get; }
    }
}
