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
        /// <summary>
        /// Whether every manpower number in this DTO came out of a rebuild on a city that
        /// answered. False means the panel is holding the last good figures and must render a
        /// dash rather than the counts and the percentage.
        ///
        /// A separate boolean, not an emptied number, and the contract is why: the UI DTO
        /// contract has no nullable numeric type at all, so "unknown" spelled as a number would
        /// have to be a sentinel (-1, or a bare 0), and a sentinel is read as a value by the
        /// first consumer that forgets it. The flag cannot be mistaken for a count.
        /// </summary>
        public bool IsManpowerMeasured;
        public int ManpowerAvailable;
        public int ManpowerUsed;
        public int ManpowerTotal;
        public int ManpowerPercent;
        public int ManpowerBasePool;
        public int ManpowerCasualties;
        public int ManpowerPatriotismFactor;
        /// <summary>Aggregate corruption the city can see (0-100) that produced
        /// <see cref="ManpowerPatriotismFactor"/>. Shown under the factor so the readout names its
        /// own cause — players could see "patriotism 50%" and had no way to learn what moved it.</summary>
        public int ManpowerCorruptionScore;
        public int ManpowerMoraleFactor;
        public int ManpowerFatigueFactor;
        public int ManpowerDodgerFactor;
        public int ManpowerDodgerCount;
        public int ManpowerDeferral;
        public int ManpowerDisability;
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
