using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Interfaces.Domain.Cognitive
{
    /// <summary>
    /// One social stratum's psy posture — defence, enemy pressure, and demographic size. The defence
    /// side: <see cref="Coverage"/> is the aggregate control the active countermeasures hold over the
    /// stratum (Buckwheat→poor, Telemarathon→middle, hero per-stratum tilt — via <c>StratumDefense</c>),
    /// and can be negative where a backfire pair (e.g. Buckwheat→wealthy) actively erodes control; the
    /// per-type cover fields express how well the active hero archetype counters each discrete attack
    /// <see cref="PsyOpsAttackType"/> (the rock-paper-scissors gap) — the enemy weights its dominant raid
    /// type toward the LEAST-covered type (gap-targeting). The pressure/size side (<see cref="Infection"/>,
    /// <see cref="Households"/>) lets Core consumers read enemy penetration and stratum weight without
    /// importing <c>Domains.Cognitive</c> (Axiom 5) — used by the Attention psy-exodus driver (Phase 14).
    /// </summary>
    public readonly struct StratumCoverage
    {
        public readonly SocialStratum Stratum;
        public readonly float Coverage;        // aggregate stratum control [-1..1] (negative = backfire)
        public readonly float PropagandaCover; // 0..1 — hero counter vs Propaganda
        public readonly float RumorCover;       // 0..1 — hero counter vs Rumor
        public readonly float FakeVideoCover;   // 0..1 — hero counter vs FakeVideo

        /// <summary>
        /// Fraction of this stratum's households inside telecom coverage [0..1] — the spatial
        /// read of "which class sits in the coverage holes". Our broadcast counter-reach is network-bound
        /// (a stratum near 0 here is largely undefended); the enemy floor lands regardless (Decision 3).
        /// </summary>
        public readonly float CoverageFraction;

        /// <summary>
        /// Average enemy infection of this stratum [0..1] — the enemy-sway proxy (infection <em>is</em>
        /// the penetration; there is no separate "sway" field). Source: the aggregator's per-stratum
        /// read model. Drives the Phase 14 poor-psy exodus rate.
        /// </summary>
        public readonly float Infection;

        /// <summary>
        /// Household count of this stratum — the demographic weight. Kept as a float so ratio math
        /// (poor share of the city) needs no cast; the underlying count is integer.
        /// </summary>
        public readonly float Households;

        public StratumCoverage(SocialStratum stratum, float coverage, float propagandaCover, float rumorCover, float fakeVideoCover, float coverageFraction, float infection, float households)
        {
            Stratum = stratum;
            Coverage = coverage;
            PropagandaCover = propagandaCover;
            RumorCover = rumorCover;
            FakeVideoCover = fakeVideoCover;
            CoverageFraction = coverageFraction;
            Infection = infection;
            Households = households;
        }
    }

    /// <summary>
    /// Read-only projection of the city's per-stratum cognitive-defence coverage,
    /// rebuilt each tick from the active countermeasures (hero archetype + Telemarathon + Buckwheat)
    /// via <c>StratumDefense</c>. Modelled on <c>IAirDefenseCoverageReader</c> / <c>LiveAACacheSystem</c>:
    /// the Core/CrossDomain consumers (the PSYOPS raid launcher's gap-targeting, and a later UI
    /// overlay) read the posture without importing <c>Domains.Cognitive</c> (Axiom 5).
    ///
    /// Null-object: empty list → the gap-targeter sees "no coverage anywhere" (fail-closed, the
    /// enemy is free to pick any seam), never a crash.
    /// </summary>
    [GenerateNullObject]
    [OwnedByFeatureId(FeatureIds.CognitiveName)]
    public interface ICognitiveCoverageReader
    {
        [NullReturnEmpty]
        IReadOnlyList<StratumCoverage> GetCoverage();
    }
}
