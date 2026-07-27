namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Countermeasures state machine phases.
    /// Moved from CountermeasuresSystem for DIP compliance.
    /// </summary>
    public enum CountermeasuresPhase : byte
    {
        Idle = 0,
        Suspicion = 1,
        Investigation = 2,
        WaitingForInvestigationChoice = 3,
        ArticlePublished = 4,
        WaitingForPoliceChoice = 5,
        PoliceInvestigation = 6,
        Arrested = 7
    }
}
