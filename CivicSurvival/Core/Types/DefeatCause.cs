namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Why the player lost.
    /// None = game still active, no defeat triggered.
    /// </summary>
    public enum DefeatCause : byte
    {
        None = 0,
        PopulationCollapse = 1,
        LostControl = 2,
        Arrested = 3
    }
}
