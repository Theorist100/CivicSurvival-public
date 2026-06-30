namespace CivicSurvival.Core.Components.CrossDomain
{
    public readonly struct ResidentPopulationSnapshot
    {
        public static readonly ResidentPopulationSnapshot Empty = new(0, 0, 0, 0, 0);

        public ResidentPopulationSnapshot(
            int version,
            int eligibleHouseholdCount,
            int aliveResidentCitizens,
            int homelessHouseholdCount,
            int movedInHouseholdCount)
        {
            Version = version;
            EligibleHouseholdCount = eligibleHouseholdCount;
            AliveResidentCitizens = aliveResidentCitizens;
            HomelessHouseholdCount = homelessHouseholdCount;
            MovedInHouseholdCount = movedInHouseholdCount;
        }

        public int Version { get; }
        public int EligibleHouseholdCount { get; }
        public int AliveResidentCitizens { get; }
        public int HomelessHouseholdCount { get; }
        public int MovedInHouseholdCount { get; }
    }
}
