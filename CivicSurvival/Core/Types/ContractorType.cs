namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Contractor choice for Shadow Procurement.
    /// Determines equipment quality and corruption consequences.
    /// </summary>
    public enum ContractorType : byte
    {
        None = 0,
        Honest = 1,      // Reliable equipment, no profit, +reputation
        YourGuy = 2      // Counterfeit equipment, 80% kickback, -reputation
    }
}
