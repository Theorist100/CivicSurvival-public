using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.AirDefense
{
    /// <summary>
    /// One air-defense installation's coverage footprint for the radar overlay: world
    /// ground position (X/Z) and engagement <see cref="Range"/> in meters. Purely
    /// geometric — no ammo/crew/cooldown — so the radar draws the static defended area,
    /// not live readiness.
    /// </summary>
    public readonly struct AaCoverage
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Range;

        public AaCoverage(float x, float z, float range)
        {
            X = x;
            Z = z;
            Range = range;
        }
    }

    /// <summary>
    /// Read-only projection of the live air-defense fleet's coverage circles (position +
    /// range) for the radar "defended zone" overlay. Implemented by
    /// <c>LiveAACacheSystem</c> — the single owner of the order-version-gated live AA
    /// snapshot — so the Core/CrossDomain radar reader draws the umbrella without importing
    /// AirDefense.Systems directly (Axiom 5).
    ///
    /// Null-object: empty coverage list. When AirDefense is unavailable the radar simply
    /// draws no defended zone (fail-closed, no crash).
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.AirDefenseName)]
    public interface IAirDefenseCoverageReader
    {
        [NullReturnEmpty]
        IReadOnlyList<AaCoverage> GetCoverage();
    }
}
