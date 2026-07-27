using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Countermeasures.Logic
{
    /// <summary>
    /// Static helpers for CountermeasuresCoreFsm derived values.
    /// Replaces CountermeasuresServiceAdapter derived property calculations.
    ///
    /// Usage: CountermeasuresHelper.GetHeatLevel(core.Heat)
    /// </summary>
    public static class CountermeasuresHelper
    {
        /// <summary>
        /// Get heat level localization key.
        /// Thresholds from BalanceConfig.Current.Countermeasures.
        /// </summary>
        public static string GetHeatLevel(float heat)
        {
            var cfg = BalanceConfig.Current.Countermeasures;
            return heat switch
            {
                _ when heat >= cfg.HeatCriticalThreshold => ReasonIds.CounterHeatLevelCritical,
                _ when heat >= cfg.HeatDangerThreshold => ReasonIds.CounterHeatLevelDanger,
                _ when heat >= cfg.HeatWarningThreshold => ReasonIds.CounterHeatLevelWarning,
                _ => ReasonIds.CounterHeatLevelSafe
            };
        }

        /// <summary>
        /// Get phase localization key.
        /// </summary>
        public static string GetPhaseName(CountermeasuresPhase phase) => phase switch
        {
            CountermeasuresPhase.Idle => ReasonIds.CounterPhaseIdle,
            CountermeasuresPhase.Suspicion => ReasonIds.CounterPhaseSuspicion,
            CountermeasuresPhase.Investigation => ReasonIds.CounterPhaseInvestigation,
            CountermeasuresPhase.WaitingForInvestigationChoice => ReasonIds.CounterPhaseWaitingDecision,
            CountermeasuresPhase.ArticlePublished => ReasonIds.CounterPhaseArticlePublished,
            CountermeasuresPhase.WaitingForPoliceChoice => ReasonIds.CounterPhasePoliceDecision,
            CountermeasuresPhase.PoliceInvestigation => ReasonIds.CounterPhaseUnderInvestigation,
            CountermeasuresPhase.Arrested => ReasonIds.CounterPhaseArrested,
            _ => ReasonIds.CounterPhaseUnknown
        };

        /// <summary>
        /// Check if player choice is required.
        /// </summary>
        public static bool ChoiceRequired(CountermeasuresPhase phase)
            => phase == CountermeasuresPhase.WaitingForInvestigationChoice ||
               phase == CountermeasuresPhase.WaitingForPoliceChoice;

        /// <summary>
        /// Get UI choice category.
        /// </summary>
        public static CountermeasureChoiceUiType GetChoiceType(CountermeasuresPhase phase)
        {
            switch (phase)
            {
                case CountermeasuresPhase.WaitingForInvestigationChoice:
                    return CountermeasureChoiceUiType.Investigation;
                case CountermeasuresPhase.WaitingForPoliceChoice:
                    return CountermeasureChoiceUiType.Police;
                case CountermeasuresPhase.Idle:
                case CountermeasuresPhase.Suspicion:
                case CountermeasuresPhase.Investigation:
                case CountermeasuresPhase.ArticlePublished:
                case CountermeasuresPhase.PoliceInvestigation:
                case CountermeasuresPhase.Arrested:
                    return CountermeasureChoiceUiType.None;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(phase), phase, "Unknown countermeasures phase");
            }
        }

        /// <summary>
        /// Get base bribe cost for current phase (before sanctions markup).
        /// </summary>
        public static int GetBaseBribeCost(CountermeasuresCoreFsm core, CmInvestigationState inv)
        {
            if (core.CurrentPhase == CountermeasuresPhase.WaitingForInvestigationChoice)
                return inv.BribeCost;
            if (core.CurrentPhase == CountermeasuresPhase.WaitingForPoliceChoice)
                return BalanceConfig.Current.Countermeasures.PoliceBribeCost;
            return 0;
        }

        /// <summary>
        /// Get effective bribe cost with sanctions markup applied.
        /// FIX S4-05: Single source of truth — used by both UI and charge path.
        /// </summary>
        public static int GetBribeCost(CountermeasuresCoreFsm core, CmInvestigationState inv, float sanctionsMarkup)
        {
            int baseCost = GetBaseBribeCost(core, inv);
            return SanctionsCostHelper.ApplyMarkup(baseCost, sanctionsMarkup);
        }
    }
}
