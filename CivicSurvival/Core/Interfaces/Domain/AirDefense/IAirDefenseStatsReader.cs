using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.AirDefense
{
    /// <summary>
    /// Read-only projection of the live air-defense fleet (total + per-type installation counts)
    /// for the crisis-sweep forecast. Implemented by AirDefenseStateSystem — the single owner of the
    /// per-type UI stats model — so the Core/Forecast layer reads the real fleet without importing
    /// AirDefense.Systems directly (Axiom 5). Separate from <see cref="IAirDefenseCreditsReader"/>:
    /// that surface exposes placement credits, this one exposes the placed-and-built fleet.
    ///
    /// Null-object: <c>default(AirDefenseFleetView)</c> is the all-zero fleet (fail-closed). When
    /// AirDefense is unavailable the forecast sees zero live AA and falls back to the archetype model
    /// (FREE Heritage grant), byte-identical to the pre-live verdict.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IAirDefenseStatsReader
    {
        /// <summary>Live fleet snapshot at call time. Zero-count for every type before a city is
        /// loaded or while the AA set is empty.</summary>
        AirDefenseFleetView Fleet { get; }
    }
}
