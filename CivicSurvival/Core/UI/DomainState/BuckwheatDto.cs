using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct BuckwheatDto : IDomainDto // buckwheat districts are serialized as buckwheatDistricts.
    {
        public double BuckwheatTons;
        public int ProcurementLevel;
        public int DailyCost;
        /// <summary>Base daily cost before sanctions markup.</summary>
        public int BaseDailyCost;
        [Attributes.DtoEligibility(typeof(BuckwheatEligibility), nameof(BuckwheatEligibility.CanDistribute), "DistributeLockedReasonId")]
        public bool CanDistribute;
        [Attributes.DtoEligibility(typeof(BuckwheatEligibility), nameof(BuckwheatEligibility.CanAffordProcurement), "AffordProcurementLockedReasonId")]
        public bool CanAffordProcurement;
        [Attributes.DtoEligibility(typeof(BuckwheatEligibility), nameof(BuckwheatEligibility.CanSetProcurement25), "Procurement25LockedReasonId")]
        public bool CanSetProcurement25;
        [Attributes.DtoEligibility(typeof(BuckwheatEligibility), nameof(BuckwheatEligibility.CanSetProcurement50), "Procurement50LockedReasonId")]
        public bool CanSetProcurement50;
        [Attributes.DtoEligibility(typeof(BuckwheatEligibility), nameof(BuckwheatEligibility.CanSetProcurement75), "Procurement75LockedReasonId")]
        public bool CanSetProcurement75;
        [Attributes.DtoEligibility(typeof(BuckwheatEligibility), nameof(BuckwheatEligibility.CanSetProcurement100), "Procurement100LockedReasonId")]
        public bool CanSetProcurement100;
        public string LastDistributeResultJson;
        public string ProcurementLevelRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
