using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Cognitive warfare domain DTO.
    /// cognitiveDistricts stays as separate ValueBinding with custom IWriter.
    /// </summary>
    public partial struct CognitiveDto : IDomainDto
    {
        // Core state
        public bool CognitiveActive;
        public float InfectionRate;
        public float RecoveryRate;
        public float PenaltyThreshold;
        public int TotalDistricts;
        public int CompromisedDistricts;

        // Hero unit
        public int HeroStatus;
        public int HeroDeployCost;
        public float HeroInfectionReduction;
        public float HeroRecoveryBonus;
        public string HeroActionRequestJson;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanDeployHero), "DeployHeroLockedReasonId")]
        public bool CanDeployHero;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanRecallHero), "RecallHeroLockedReasonId")]
        public bool CanRecallHero;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanSetHeroCounter), "SetHeroCounterLockedReasonId")]
        public bool CanSetHeroCounter;
        [Attributes.DtoEligibility(typeof(HeroEligibility), nameof(HeroEligibility.CanSetHeroLecturing), "SetHeroLecturingLockedReasonId")]
        public bool CanSetHeroLecturing;
        public int ProtestRisk;
        public string DominantNarrative;
        public float AvgIntegrity;

        // Household stats
        public int TotalHouseholds;
        public float AvgInfection;
        public float AvgResistance;
        public float AvgTrauma;
        public int HouseholdsUnderBlackout;
        public int HouseholdsWithEnvy;
        public int HouseholdsUnderImpact;
        public int HouseholdsInfected;

        // Blackout vulnerability
        public int VulnerableHouseholds;
        public float AvgBlackoutHours;
        public float BlackoutVulnerability;

        // Internet mode
        public int InternetMode;
        public float CommercePenalty;
        public string InternetModeRequestJson;

        // IPSO (enemy propaganda)
        public bool IpsoActive;
        public int IpsoIntensity;
        public int IpsoDistrictCount;
        public int IpsoTotalDistricts;

        // Telemarathon
        public bool TelemarathonActive;
        public int NarrativeMode;
        public float MediaTrust;
        public bool IsInShock;
        public float ShockHoursRemaining;
        public float AudienceFatigue;
        public string TelemarathonModeRequestJson;
        public string TelemarathonActiveRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
