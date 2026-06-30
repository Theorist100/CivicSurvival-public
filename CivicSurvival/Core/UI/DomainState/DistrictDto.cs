namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// District wire payload for the Districts UI binding.
    /// Mirrors the wire shape declared in ui-dto.contract.yaml; ui-dto
    /// codegen owns the WriteTo partial in DomainDtoWriters.g.cs.
    ///
    /// 28 fields: identity + zoning + schedule + per-category MW +
    /// power flow + VIP/auto-shed/internet + threshold/penalty + blackout
    /// source. Populated by <c>DistrictDtoFactory.CreateFromSnapshot</c>
    /// from the immutable district state snapshot. Never serialized to
    /// .cok (UI binding only).
    /// </summary>
    public partial struct DistrictDto : IDomainDto
    {
        public int EntityIndex;
        public int EntityVersion;
        public string Name;
        public bool IsUnzoned;
        public bool ResidentialOff;
        public bool CommercialOff;
        public bool IndustrialOff;
        public bool OfficeOff;
        public bool ServicesOff;
        public int Schedule;
        public string ScheduleName;
        public bool ScheduleActive;

        // Power consumption data (MW)
        public int TotalMW;
        public int ResidentialMW;
        public int CommercialMW;
        public int IndustrialMW;
        public int OfficeMW;
        public int ServicesMW;
        public int Priority;

        /// <summary>
        /// Actually delivered to the district after vanilla flow distribution and our threshold cuts.
        /// Computed: (TotalMW * cityDeliveryRatio) - ThresholdCutMW. Clamped >= 0.
        /// </summary>
        public int DeliveredMW;

        /// <summary>MW lost in this district to threshold cuts (buildings receiving &lt; 90% zeroed).</summary>
        public int ThresholdCutMW;

        // VIP status (never loses power)
        public bool IsVIP;

        // VIP Bypass (Wealthy households never lose power)
        public bool IsVIPBypass;

        // Auto-Dispatch (auto-shedded due to grid stress)
        public bool IsAutoShedded;

        // Internet disabled (silences Valeras in district)
        public bool InternetDisabled;

        // Threshold operation: buildings cut in this district
        public int ThresholdCutBuildings;

        // Penalties (visual indicators for player)
        public float TotalHappinessPenalty;
        public float TotalCommercePenalty;

        // Blackout source (why the district is off)
        // Values: "none", "manual", "auto", "schedule"
        public string BlackoutSource;
    }
}
