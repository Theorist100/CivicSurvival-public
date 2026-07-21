namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Available choices during journalist investigation.
    /// </summary>
    public enum InvestigationChoice : byte
    {
        None = 0,
        Bribe = 1,      // $50k-200k, stops investigation, 20% backfire
        Censor = 2,     // -20 Reputation, journalist remembers
        Wait = 3,       // Article publishes, police activates
        Confess = 4     // -10 Happiness, softer article, no police
    }
}
