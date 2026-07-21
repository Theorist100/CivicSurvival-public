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
        Done = 6         // Intro completed
    }
}
