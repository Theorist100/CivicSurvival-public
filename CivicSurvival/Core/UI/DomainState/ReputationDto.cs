
namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct ReputationDto : IDomainDto
    {
        public float TrustLevel;
        public string TrustTier;
        public bool IsFrozenOut;
        public float OfferFrequencyMult;
    }
}
