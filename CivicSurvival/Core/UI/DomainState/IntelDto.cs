using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Intel domain DTO.
    /// Focus ranges and TimeEstimate are typed subtypes; the generated
    /// writer serializes them via DomainDtoWriters.g.cs.
    /// </summary>
    public partial struct IntelDto : IDomainDto
    {
        // Tension and forecast
        public int TensionLevel;
        public string TensionStatus;
        public string WaveTypePrediction;
        public bool IsMassiveStrike;

        // Typed forecast ranges
        public FocusRangeDto EnergyFocusRange;
        public FocusRangeDto InfraFocusRange;
        public FocusRangeDto ResidentialFocusRange;
        public AttackTimeEstimateDto TimeEstimate;

        // Threat composition
        public string ThreatComposition;
        public int EstimatedShaheds;
        public int EstimatedBallistics;

        // Insider
        public bool HasInsider;
        public int InsiderCost;
        [Attributes.DtoEligibility(typeof(IntelEligibility), nameof(IntelEligibility.CanBuyInsider), "InsiderLockedReasonId")]
        public bool CanBuyInsider;
        public int BaseInsiderCost;

        // Tension pricing
#pragma warning disable CIVIC167 // Multiplier (0.0-2.0), not monetary amount
        public float TensionPriceMultiplier;
#pragma warning restore CIVIC167
        public int TensionPriceModifierPercent;
        public string InsiderRequestJson;

        // Intel upgrade
        public int IntelUpgradeLevel;
        public int IntelUpgradeCost;
        [Attributes.DtoEligibility(typeof(IntelEligibility), nameof(IntelEligibility.CanUpgradeIntel), "IntelUpgradeLockedReasonId")]
        public bool CanUpgradeIntel;
        public string IntelUpgradeRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
