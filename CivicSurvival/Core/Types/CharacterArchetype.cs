namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Character archetype for reaction filtering.
    /// </summary>
    public enum CharacterArchetype : byte
    {
        None = 0,
        Corrupt = 1,
        HonestOfficial = 2,
        Journalist = 3,
        Citizen = 4,
        Worker = 5,
        ConspiracyTheorist = 6
    }
}
