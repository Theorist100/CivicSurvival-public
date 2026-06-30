using CivicSurvival.Core.Types;

namespace CivicSurvival.Domains.Scenario.Data
{
    /// <summary>
    /// Dedicated state for the intro sequence (Cold Open "04:57 AM").
    /// Replaces the full ScenarioState in IntroScenarioSystem to prevent
    /// divergent state with ScenarioStateMachine (FIX 1-7).
    /// </summary>
    public struct IntroSequenceState
    {
        public bool IntroCompleted;
        public bool IsIntroPlaying;
        public IntroPhase IntroPhase;
        public float IntroTimer;
        public bool SkipIntro;
    }
}
