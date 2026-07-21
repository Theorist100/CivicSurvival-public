using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Cognitive
{
    /// <summary>
    /// One telecom/broadcast tower's coverage footprint for the radar "mental view":
    /// world ground position (X/Z) and signal <see cref="Range"/> in meters, read from
    /// the vanilla <c>TelecomFacility</c> set. Purely geometric — where the city is on the
    /// air, i.e. where our counter-propaganda broadcast reaches. Mirrors <c>AaCoverage</c>.
    /// </summary>
    public readonly struct BroadcastCoverage
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Range;

        public BroadcastCoverage(float x, float z, float range)
        {
            X = x;
            Z = z;
            Range = range;
        }
    }

    /// <summary>
    /// Read-only projection of the city's vanilla telecom towers as broadcast-coverage
    /// circles (position + range) for the radar mental-view "broadcast umbrella" overlay.
    /// Implemented by <c>TelecomBroadcastCacheSystem</c> — the single owner of the throttled
    /// telecom snapshot — so the ThreatUI radar draws the umbrella without importing the
    /// Cognitive domain (Axiom 5).
    ///
    /// Null-object: empty coverage list. When the reader is unavailable the radar simply
    /// draws no broadcast umbrella (fail-closed, no crash).
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.CognitiveName)]
    public interface IBroadcastCoverageReader
    {
        [NullReturnEmpty]
        IReadOnlyList<BroadcastCoverage> GetCoverage();
    }
}
