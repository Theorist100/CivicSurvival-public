using System;

namespace CivicSurvival.Core.Types.Snapshots
{
    /// <summary>
    /// Immutable snapshot of climate state.
    /// Thread-safe for concurrent reads.
    /// </summary>
    public readonly struct ClimateSnapshot : IEquatable<ClimateSnapshot>
    {
        private const float DEFAULT_TEMPERATURE = 15f;
        /// <summary>Current temperature in Celsius.</summary>
        public readonly float Temperature;

        /// <summary>Current season index (0=Spring, 1=Summer, 2=Fall, 3=Winter).</summary>
        public readonly int Season;

        /// <summary>Current season name ("Spring", "Summer", "Fall", "Winter").</summary>
        public readonly string SeasonName;

        public ClimateSnapshot(float temperature, int season, string seasonName)
        {
            Temperature = temperature;
            Season = season;
            SeasonName = seasonName;
        }

        /// <summary>Default snapshot with mild spring weather.</summary>
        public static ClimateSnapshot Default => new(DEFAULT_TEMPERATURE, 0, "Spring");

        public bool Equals(ClimateSnapshot other)
            => Temperature.Equals(other.Temperature)
                && Season == other.Season
                && string.Equals(SeasonName, other.SeasonName, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is ClimateSnapshot other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Temperature,
                Season,
                SeasonName is null ? 0 : StringComparer.Ordinal.GetHashCode(SeasonName));

        public static bool operator ==(ClimateSnapshot left, ClimateSnapshot right)
            => left.Equals(right);

        public static bool operator !=(ClimateSnapshot left, ClimateSnapshot right)
            => !left.Equals(right);
    }
}
