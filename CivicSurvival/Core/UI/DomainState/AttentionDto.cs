
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
        /// <summary>Phase 14C — base rate attributed to the kinetic (WorldShock) cause (%/day).</summary>
        public float KineticRatePercentPerDay;
        /// <summary>Phase 14C — base rate attributed to the psy-war (Poor sway) cause (%/day).</summary>
        public float PsyRatePercentPerDay;
        public int TotalExodus;
        /// <summary>Phase 16 "Batalion Monako" — cumulative Wealthy families fled as the targeted stream.</summary>
        public int MonacoFamiliesFled;
        /// <summary>Phase 16 "Batalion Monako" — cumulative household capital (currency) that fled with them.</summary>
        public long MonacoCapitalFled;
    }
}
