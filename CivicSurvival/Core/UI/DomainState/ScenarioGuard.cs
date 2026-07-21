using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Scenario/wave snapshot passed by phase-aware trigger dispatchers.
    /// Trigger bodies receive this only after the dispatcher has accepted the phase gate.
    /// Built fresh from live singletons at dispatch (click) time — see
    /// <c>CivicUIPanelSystem.TryCreateScenarioGuard</c> — so there is no stale-guard
    /// window between render and invocation to revalidate against.
    /// </summary>
    public readonly struct ScenarioGuard
    {
        public readonly Act Act;
        public readonly GamePhase Phase;

        internal ScenarioGuard(Act act, GamePhase phase)
        {
            Act = act;
            Phase = phase;
        }
    }
}
