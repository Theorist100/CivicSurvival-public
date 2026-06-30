namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// What happens after player wins at day 365.
    /// None = not yet chosen, OneMoreYear = next victory at +365, Endless = no more victory checks.
    /// </summary>
    public enum PostVictoryMode
    {
        None = 0,
        OneMoreYear = 1,
        Endless = 2
    }
}
