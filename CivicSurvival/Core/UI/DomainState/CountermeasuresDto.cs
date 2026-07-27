using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.UI.DomainState
{
    public partial struct CountermeasuresDto : IDomainDto
    {
        public int CorruptionScore;
        public int Heat;
        public string HeatLevel;
        public string CountermeasuresPhase;
        public int InvestigationProgress;
        public int ChargesCount;
        public int ProtestCount;
        public bool ChoiceRequired;
        public int ChoiceType;
        public int BribeCost;
        /// <summary>FIX S6-06: Base bribe cost before sanctions markup.</summary>
        public int BaseBribeCost;
        public ActionAvailabilityField BribeAvailability;
        public string LastChoiceResult;
        public string CurrentJournalist;
        public bool IsArrested;
        public long ArrestedAssetsSeized;
        public long ArrestedWalletAfter;
        public string BribeRiskWarning;
        public bool SanctionsSuppressingCorruption;
        public string LastChoiceRequestResultJson;
    }
}
