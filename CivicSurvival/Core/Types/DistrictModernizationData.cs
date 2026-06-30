namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// District-specific District Modernization state.
    /// Stored in Dictionary inside DistrictModernizationSystem, NOT as IComponentData.
    /// </summary>
    public struct DistrictModernizationData
    {
        public bool HasProgram;              // Program activated?
        public ContractorType Contractor;    // Honest or YourGuy
        public int ActivationDay;            // When program launched
        public int BuildingCount;            // Buildings equipped
        public int TotalCost;                // City budget spent
        public int KickbackEarned;           // If corrupt, amount to offshore
        public int ExpectedKickback;         // Pre-freeze kickback for investigation fine basis
        public int LastFireDay;              // For counterfeit fire tracking
        public int FireCount;                // Total fires so far

        public readonly bool HasValidContractor()
        {
            return IsValidContractor(Contractor);
        }

        public static bool IsValidContractor(ContractorType contractor)
        {
            return contractor == ContractorType.None
                || contractor == ContractorType.Honest
                || contractor == ContractorType.YourGuy;
        }
    }
}
