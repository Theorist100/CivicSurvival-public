namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Which crisis-sweep model the in-game sweep runs. Mirrors the three modes of the
    /// reference tool (<c>Tools/crisis_model.py:34-37</c>): a deterministic worst-case,
    /// a deterministic phase-pacing pass, and the stochastic Monte-Carlo severity timeline.
    /// Carried as a byte on <c>CrisisSweepRequest</c> and echoed on
    /// <c>CrisisSweepResultSingleton.Mode</c> so the panel/log read which model produced a verdict.
    /// </summary>
    public enum CrisisSweepMode : byte
    {
        /// <summary>Deterministic worst-case recoverability per archetype × population (no RNG, no policy).</summary>
        Invariant = 0,

        /// <summary>Deterministic phase-cycle timing per city size × season (no manpower, no intercept).</summary>
        Pacing = 1,

        /// <summary>Stochastic Monte-Carlo GridStress timeline → time-in-state (Surplus / Managed / Blackout).</summary>
        Severity = 2
    }
}
