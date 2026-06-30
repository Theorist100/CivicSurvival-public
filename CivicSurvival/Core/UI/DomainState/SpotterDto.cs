using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Spotters domain DTO for OpSec UI controls.
    /// </summary>
    public partial struct SpotterDto : IDomainDto
    {
        public int SpotterCount;
        public int SpotterPenaltyPercent;
        public int SpotterRawPenaltyPercent;
        public int SbuVisitCost;
        public int TotalSBUVisits;
        public int EvacuationCost;
        public int TotalEvacuations;
        public bool CounterOSINTActive;
        public int CounterOSINTDailyCost;
        [Attributes.DtoEligibility(typeof(SpotterEligibility), nameof(SpotterEligibility.CanPerformSBUVisit), "SbuVisitLockedReasonId")]
        public bool CanSbuVisit;
        [Attributes.DtoEligibility(typeof(SpotterEligibility), nameof(SpotterEligibility.CanPerformEvacuation), "EvacuationRunLockedReasonId")]
        public bool CanEvacuationRun;
        [Attributes.DtoEligibility(typeof(SpotterEligibility), nameof(SpotterEligibility.CanToggleCounterOSINT), "CounterOSINTLockedReasonId")]
        public bool CanToggleCounterOSINT;
        public string SpotterActionRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
