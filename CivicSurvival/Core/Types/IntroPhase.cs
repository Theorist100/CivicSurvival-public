namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Current phase of the intro sequence "04:57 AM".
    /// Shared type: used by Scenario (state machine) and serialization.
    /// </summary>
    public enum IntroPhase : byte
    {
        None = 0,        // Not started or already completed
        Modal = 1,       // Showing intro modal, waiting for "Accept Reality" click
        Silence = 2,     // 2 seconds of silence after button click
        Siren = 3,       // Air raid siren starts, camera pans to power plant
        Attack = 4,      // Threats spawn
        Reveal = 5,      // HUD appears, player regains control
        Done = 6,        // Intro completed

        // Runs between Modal and Silence: the activists warn that nothing covers the sky and
        // the HUD is handed over early so the player can actually put guns up before the first
        // strike. Numbered after Done because serialized values are stable — the sequence order
        // is the state machine's, not the enum's.
        ActivistCall = 7
    }

    /// <summary>
    /// Sequence questions about an <see cref="IntroPhase"/>.
    ///
    /// Ask here instead of comparing enum values: numeric order stopped matching sequence
    /// order when ActivistCall was appended (serialized values are stable, so it could not be
    /// inserted between Modal and Silence). A <c>phase &lt; IntroPhase.Attack</c> test would
    /// silently read the activists' window — which runs BEFORE the strike — as being past it.
    /// </summary>
    public static class IntroPhases
    {
        /// <summary>True while the opening strike has not been launched yet.</summary>
        public static bool IsBeforeStrike(IntroPhase phase) =>
            phase is IntroPhase.Modal or IntroPhase.ActivistCall or IntroPhase.Silence or IntroPhase.Siren;

        /// <summary>
        /// True once the player holds the HUD. Handed over at the activists' call and never
        /// taken back — the warning is worthless if the city cannot act on it.
        /// </summary>
        public static bool ShowsHud(IntroPhase phase) =>
            phase is IntroPhase.ActivistCall or IntroPhase.Silence or IntroPhase.Siren
                  or IntroPhase.Attack or IntroPhase.Reveal or IntroPhase.Done;
    }
}
