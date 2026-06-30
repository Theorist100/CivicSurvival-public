
namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct AttentionDto : IDomainDto
    {
        public float ShockLevel;
        public string ShockTier;
        public int CasualtiesThisWeek;
        public int BuildingsDestroyedThisWeek;
        public int CriticalHitsThisWeek;
        public long TotalCasualties;
        public long TotalBuildingsDestroyed;
        public long TotalCivilianBuildingsDestroyed;
        public long TotalCriticalHits;
        public bool ExodusActive;
        /// <summary>Resolved rate before die-hard/eligibility dampers.</summary>
        public float BaseExodusRatePercentPerDay;
        /// <summary>Percent of population leaving per day, not a 0-1 fraction.</summary>
        public float ExodusRatePercentPerDay;
        public int TotalExodus;
    }
}
