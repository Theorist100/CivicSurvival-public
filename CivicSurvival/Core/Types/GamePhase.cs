namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Game phase for wave-based threat system.
    /// Calm -> Alert -> Attack -> Recovery -> Repeat
    /// </summary>
    public enum GamePhase : byte
    {
        Calm = 0,       // Build, stockpile, trade - no threats
        Alert = 1,      // Sirens, evacuation - threats incoming
        Attack = 2,     // Threats in the air - observe and survive
        Recovery = 3    // Assess damage, repair, mourn
    }
}
