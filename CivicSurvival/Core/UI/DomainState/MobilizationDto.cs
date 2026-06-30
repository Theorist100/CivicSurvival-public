using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Domain state DTO for MobilizationUISystem.
    /// JSON field names match the current React binding contract.
    ///
    /// Producer-readiness convention:
    /// • <c>SocialPenaltyProducerReady</c> reports whether the cross-feature
    ///   Cognitive/Wellbeing social-penalty producer is initialised. The
    ///   sub-system specific naming (vs bare <c>ProducerReady</c>) clarifies
    ///   that only the social-penalty pipeline depends on the soft producer —
    ///   core mobilisation works without it.
    /// • <c>SocialPenaltyReasonId</c> is REQUIRED because the UI surfaces a
    ///   specific localised reason (UI_MOB_SOCIAL_PENALTY_UNAVAILABLE) rather
    ///   than the generic dep-skipped copy.
    /// </summary>
    public partial struct MobilizationDto : IDomainDto
    {
        public int ManpowerAvailable;
        public int ManpowerUsed;
        public int ManpowerTotal;
        public int ManpowerPercent;
        public int ManpowerBasePool;
        public int ManpowerCasualties;
        public int ManpowerPatriotismFactor;
        public int ManpowerMoraleFactor;
        public int ManpowerFatigueFactor;
        public bool IsConscriptionActive;
        public bool IsWarFatigued;
        public bool IsManpowerCritical;
        public bool IsManpowerOvercommitted;
        public bool CallToArmsOnCooldown;
        public bool ConscriptionReactivationOnCooldown;
        public int PredictedConscriptionRelease;
        public bool SocialPenaltyProducerReady;
        public string SocialPenaltyReasonId;
        [Attributes.DtoEligibility(typeof(MobilizationEligibility), nameof(MobilizationEligibility.CanCallToArms), "CallToArmsLockedReasonId")]
        public bool CanCallToArms;
        [Attributes.DtoEligibility(typeof(MobilizationEligibility), nameof(MobilizationEligibility.CanToggleConscription), "ConscriptionLockedReasonId")]
        public bool CanToggleConscription;
        public int WarDay;
        public string CallToArmsRequestJson;
        public string ConscriptionToggleRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
