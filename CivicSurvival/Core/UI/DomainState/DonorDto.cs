using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Donor conference domain DTO.
    /// Note: DonorDialogActive is also updated from trigger handlers.
    /// The system must track state in a field and rebuild JSON in OnPanelUpdate.
    ///
    /// Producer-readiness convention:
    /// • <c>ProducerReady</c> reports whether the soft cross-feature producer
    ///   (here: Diplomacy trust source) is initialised. Owning feature stays
    ///   open; only the soft producer may be missing.
    /// • <c>ProducerReasonId</c> is REQUIRED when ProducerReady is present. It
    ///   localises the specific "why not ready" message (UI_DONOR_TRUST_SOURCE_UNAVAILABLE
    ///   etc.); generic feature gating uses FeatureFallback instead and does
    ///   not need a per-DTO ReasonId.
    /// </summary>
    public partial struct DonorDto : IDomainDto
    {
        public int DonorUsesRemaining;
        public int DonorCooldownDays;
        public string DonorStatus;
        public int TrustIndex;
        public float ScandalPenalty;
        public string DonorExpectedAid;
        public bool DonorDialogActive;
        public bool ProducerReady;
        public bool TrustLocked;
        public string ProducerReasonId;
        [Attributes.DtoEligibility(typeof(DonorEligibility), nameof(DonorEligibility.CanDonateFunds), "DonorFundsLockedReasonId")]
        public bool DonorFundsAvailable;
        [Attributes.DtoEligibility(typeof(DonorEligibility), nameof(DonorEligibility.CanProvidePower), "DonorPowerLockedReasonId")]
        public bool DonorPowerAvailable;
        [Attributes.DtoEligibility(typeof(DonorEligibility), nameof(DonorEligibility.CanProvideDefense), "DonorDefenseLockedReasonId")]
        public bool DonorDefenseAvailable;
        public int DonorFundsAmount;
        public int DonorGeneratorCount;
        public int DonorGeneratorMW;
        public int DonorPatriotDays;
        public int AidTierId;
        public int AidFundsOffered;
        public int AidFundsAccessible;
        public bool PatriotOffered;
        public bool PatriotBlocked;
        public int TrustMessageId;
        public int BlockedReasonId;
        public bool HasBlockedItems;
        public int DonorActiveGenerators;
        public bool SanctionsActive;
        public int SanctionDaysRemaining;
        public int SanctionTradePenalty;
        public string DonorDialogRequestJson;
        public string DonorSelectionRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
