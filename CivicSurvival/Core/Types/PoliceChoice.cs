namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Available choices during police investigation.
    /// </summary>
    public enum PoliceChoice : byte
    {
        None = 0,
        Cooperate = 1,  // Provide access - if low evidence: cleared; if high: arrest
        Destroy = 2,    // Destroy evidence - 50% works, 50% double punishment
        Bribe = 3       // $200k - 30% honest cop = double punishment
    }
}
