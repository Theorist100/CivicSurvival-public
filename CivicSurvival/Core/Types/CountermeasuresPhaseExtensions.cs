namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Extension methods for CountermeasuresPhase enum.
    /// IsPoliceActive uses explicit match (not ordinal) because
    /// WaitingForPoliceChoice=5, PoliceInvestigation=6, Arrested=7 —
    /// ordinal >= would incorrectly include Arrested.
    /// </summary>
    public static class CountermeasuresPhaseExtensions
    {
        /// <summary>
        /// Whether police investigation is currently active (not yet resolved).
        /// Replaces the old PoliceState.Active / CountermeasuresState.PoliceActive field
        /// for cross-domain consumers that only have access to CoreFSM.
        /// </summary>
        public static bool IsPoliceActive(this CountermeasuresPhase phase)
            => phase == CountermeasuresPhase.WaitingForPoliceChoice
            || phase == CountermeasuresPhase.PoliceInvestigation;
    }
}
