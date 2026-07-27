
namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct ArrestedModalPayloadDto : IDomainDto
    {
        public int ChargesCount;
        public long AssetsSeizedSnapshot;
        public long WalletBalanceAfter;
        public string LastChoiceResult;
    }
}
