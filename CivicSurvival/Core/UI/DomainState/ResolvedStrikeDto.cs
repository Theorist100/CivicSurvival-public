namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// One resolved outbound counter-strike outcome for the War Room STRIKE OUTCOMES ledger.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto codegen owns the
    /// WriteTo partial in DomainDtoWriters.g.cs. <see cref="Axis"/> is the lowercase axis name
    /// (physical/digital/social); <see cref="Seed"/> is the launch-frozen strike seed (the
    /// launch↔arrival correlator); <see cref="NoEffect"/> marks a strike that landed on the
    /// axis's invulnerable reserve (zero delta by construction — the UI renders a neutral
    /// "no effective target" row, not a success); <see cref="TargetId"/> is the mirror-city
    /// target the strike resolved onto (EnemyTarget.NoTargetId = 65535 when nothing resolved),
    /// so the ledger names the SAME target the strike card showed.
    /// </summary>
    public partial struct ResolvedStrikeDto
    {
        public string Axis;
        public bool Intercepted;
        public bool NoEffect;
        public float OldValue;
        public float NewValue;
        public long Seed;
        public int TargetId;
    }
}
