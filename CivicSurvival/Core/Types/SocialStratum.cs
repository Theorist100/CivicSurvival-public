using Unity.Mathematics;

namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Social stratum axis — read-only classification of a household's economic class.
    /// Targeting foundation for cognitive attacks (replaces fragile district links).
    ///
    /// Derived from the vanilla wealth key (Game.UI.InGame.CitizenUIUtils.GetHouseholdWealthKey),
    /// whose thresholds are CitizenHappinessParameterData.m_WealthyMoneyAmount (int4 x/y/z/w).
    /// Vanilla returns FIVE keys; we fold them into THREE strata (balance decision, fixed):
    ///   Wretched + Poor       → Poor
    ///   Modest + Comfortable  → Middle
    ///   Wealthy               → Wealthy
    /// Unknown stays the zero value so a default/zeroed struct (e.g. just after load,
    /// before the resolver slot runs) reads as "not yet resolved", never a stale class.
    /// </summary>
    public enum SocialStratum : byte
    {
        Unknown = 0,
        Poor = 1,
        Middle = 2,
        Wealthy = 3
    }

    /// <summary>
    /// Folds the vanilla 5-key wealth classification into the 3-stratum axis.
    /// Pure arithmetic — Burst-safe, callable from jobs. Single source for the 5→3 mapping.
    /// </summary>
    public static class SocialStratumClassifier
    {
        /// <summary>
        /// Classify a household's total wealth into a <see cref="SocialStratum"/> using the
        /// vanilla thresholds (CitizenHappinessParameterData.m_WealthyMoneyAmount, int4 x/y/z/w).
        /// Mirrors CitizenUIUtils.GetHouseholdWealthKey, then folds 5 → 3:
        ///   wealth &lt; y  → Poor    (Wretched below x, Poor below y)
        ///   wealth &lt; w  → Middle  (Modest below z, Comfortable below w)
        ///   otherwise     → Wealthy
        /// </summary>
        public static SocialStratum Classify(int wealth, int4 wealthThresholds)
        {
            if (wealth < wealthThresholds.y)
                return SocialStratum.Poor;
            if (wealth < wealthThresholds.w)
                return SocialStratum.Middle;
            return SocialStratum.Wealthy;
        }
    }
}
