namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Discrete PSYOPS attack type — the carrier/flavour of a single enemy mental strike
    /// launched on top of the continuous reactive IPSO field (Tier 1). Each type maps to a
    /// distinct presentation carrier in later phases (Propaganda → emergency radio,
    /// Rumor → social feed, FakeVideo → visual overlay), but the spawn/lifecycle object is
    /// carrier-agnostic here.
    ///
    /// Distinct from <see cref="AttackCategory"/>: AttackCategory.Psyops is the axis a
    /// player counter-strike lowers (Kinetic/Cyber/Psyops); this enum is the kind of an
    /// incoming enemy psy-attack against a city <see cref="SocialStratum"/>.
    /// </summary>
    public enum PsyOpsAttackType : byte
    {
        /// <summary>Broadcast propaganda — broad, low-precision narrative pressure.</summary>
        Propaganda = 0,

        /// <summary>Targeted rumor — social-feed seeding aimed at a stratum's trust.</summary>
        Rumor = 1,

        /// <summary>Fabricated video — high-intensity disinformation artefact.</summary>
        FakeVideo = 2
    }
}
