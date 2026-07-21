using System;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Types.Snapshots
{
    public readonly struct LightingPhaseSnapshot : IEquatable<LightingPhaseSnapshot>
    {
        public readonly LightingPhase Phase;

        public LightingPhaseSnapshot(LightingPhase phase)
        {
            Phase = phase;
        }

        public bool IsDawnOrDuskLaunchWindow
            => Phase == LightingPhase.Dawn
                || Phase == LightingPhase.Sunrise
                || Phase == LightingPhase.Sunset
                || Phase == LightingPhase.Dusk;

        public static LightingPhaseSnapshot Default => new(LightingPhase.Unknown);

        public bool Equals(LightingPhaseSnapshot other) => Phase == other.Phase;

        public override bool Equals(object? obj)
            => obj is LightingPhaseSnapshot other && Equals(other);

        public override int GetHashCode() => (int)Phase;

        public static bool operator ==(LightingPhaseSnapshot left, LightingPhaseSnapshot right)
            => left.Equals(right);

        public static bool operator !=(LightingPhaseSnapshot left, LightingPhaseSnapshot right)
            => !left.Equals(right);
    }
}
