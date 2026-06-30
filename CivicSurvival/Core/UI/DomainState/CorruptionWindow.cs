using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.UI.DomainState
{
    public static class CorruptionWindow
    {
        private const float SurplusThresholdRatio = 0.2f;

        public static bool IsActive(int rawBalance, int consumption, GamePhase phase, out string reasonId)
        {
            bool hasSurplus = consumption > 0 && rawBalance > consumption * SurplusThresholdRatio;
            bool isSafe = phase == GamePhase.Calm || phase == GamePhase.Recovery;
            if (!hasSurplus || !isSafe)
            {
                reasonId = ReasonIds.CorruptionWindowClosed;
                return false;
            }

            reasonId = "";
            return true;
        }
    }
}
