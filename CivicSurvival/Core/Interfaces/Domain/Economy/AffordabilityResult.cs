namespace CivicSurvival.Core.Interfaces.Domain.Economy
{
    /// <summary>
    /// Result of an affordability check that needs to communicate both the
    /// decision and the post-markup cost.
    ///
    /// <c>default(AffordabilityResult)</c> means not affordable / unavailable —
    /// the canonical answer for null-object implementations and closed-feature
    /// callers. Producers compute <see cref="EffectiveCost"/> after applying
    /// sanctions markup; consumers that pass to <c>RegisterPendingDeduction</c>
    /// or to a deduct request must use that value, not the base cost.
    /// </summary>
    public readonly struct AffordabilityResult
    {
        public bool Affordable { get; }
        public long EffectiveCost { get; }

        public AffordabilityResult(bool affordable, long effectiveCost)
        {
            Affordable = affordable;
            EffectiveCost = effectiveCost;
        }

        public static AffordabilityResult Free => new(true, 0);
        public static AffordabilityResult Unavailable => default;
    }
}
