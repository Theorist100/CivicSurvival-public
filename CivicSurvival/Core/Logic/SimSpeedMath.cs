using System;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure conversions between vanilla simulation frames and wall-clock time.
    /// No side effects, no ECS dependencies.
    ///
    /// WHY: every "how long did this take" balance reading must be expressed in SIMULATION
    /// time, not wall-clock seconds. Vanilla runs <c>SIM_TICKS_PER_SECOND</c> ticks per
    /// simulation second and fits <c>60 × selectedSpeed</c> of them into a real second
    /// (<c>SimulationSystem.OnUpdate</c>, decompile: <c>num = floor(m_Timer * 60f)</c> with
    /// <c>m_Timer += deltaTime * selectedSpeed</c>), so the same wave measured at 4× occupies
    /// a third of the wall-clock seconds it takes at 1×. Prod telemetry showed exactly that
    /// spread — median wave 417 s at ~1×, 128 s at ~4× — which made wall-clock duration a
    /// readout of the player's speed setting rather than of the balance.
    /// </summary>
    public static class SimSpeedMath
    {
        /// <summary>
        /// Vanilla simulation ticks per simulation second (<c>SimulationSystem.OnUpdate</c>
        /// floors <c>m_Timer * 60f</c> into the per-frame tick count).
        /// </summary>
        public const float SIM_TICKS_PER_SECOND = 60f;

        /// <summary>
        /// Simulation seconds elapsed between two <c>SimulationSystem.frameIndex</c> readings.
        /// Unsigned subtraction handles the frameIndex wraparound (same form as
        /// <c>InterceptorCleanupSystem</c>); <paramref name="startFrame"/> 0 means "never
        /// anchored" and yields 0 — callers treat 0 as "not measured", NOT as a real zero.
        /// </summary>
        public static float SimSecondsBetween(uint startFrame, uint endFrame)
        {
            if (startFrame == 0u) return 0f;
            return (endFrame - startFrame) / SIM_TICKS_PER_SECOND;
        }

        /// <summary>
        /// Effective game speed over an interval = simulation seconds served per real second.
        /// This is the speed the player ACTUALLY got, not the selected setting: a paused or
        /// simulation-starved stretch drags it below <c>selectedSpeed</c>, which is the honest
        /// number for normalising a duration. Non-positive inputs give 0 ("not measured").
        /// </summary>
        public static float EffectiveSpeed(float simSeconds, double realSeconds)
        {
            if (simSeconds <= 0f || realSeconds <= 0.0) return 0f;
            return (float)(simSeconds / realSeconds);
        }

        /// <summary>
        /// Real seconds between two UTC stamps, floored at 0 so a clock adjustment mid-wave
        /// degrades to "not measured" instead of a negative speed.
        /// </summary>
        public static double RealSecondsBetween(DateTime startUtc, DateTime endUtc)
        {
            if (startUtc == default) return 0.0;
            double seconds = (endUtc - startUtc).TotalSeconds;
            return seconds > 0.0 ? seconds : 0.0;
        }
    }
}
