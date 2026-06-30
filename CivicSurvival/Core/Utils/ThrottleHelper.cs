namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Reusable throttle logic for frame-based updates.
    /// Use via composition to avoid duplicating throttle code.
    ///
    /// Usage:
    /// <code>
    /// private ThrottleHelper m_Throttle = new(30);
    ///
    /// protected override void OnUpdate()
    /// {
    ///     if (m_Throttle.ShouldUpdate())
    ///     {
    ///         // Your logic here
    ///     }
    /// }
    /// </code>
    ///
    /// Frame throttle only: this is not a domain-time deadline and must not be
    /// persisted as scheduler state. Counter is saturated at int.MaxValue and
    /// reset before overflow, which is harmless for throttling cadence.
    /// </summary>
    public struct ThrottleHelper
    {
        private int m_Counter;
        private readonly int m_Interval;
        private readonly int m_PhaseCounter;
        private bool m_ForceFire;

        public ThrottleHelper(int interval) : this(interval, 0) { }

        /// <summary>
        /// Creates a throttle helper with optional phase offset for stagger.
        /// phase=0 → fires on first call (backward compatible).
        /// phase=N → fires after N+1 calls, distributing load across ticks.
        /// </summary>
        public ThrottleHelper(int interval, int phase)
        {
            m_ForceFire = false;
            m_Interval = interval > 0 ? interval : 1;
            int p = phase % m_Interval;
            // phase=0 → counter=interval-1 → fires immediately (backward compat)
            // phase=N → counter=(interval-1-N)%interval → fires after N+1 calls
            m_PhaseCounter = (m_Interval - 1 - p + m_Interval) % m_Interval;
            m_Counter = m_PhaseCounter;
        }

        /// <summary>
        /// Call each frame. Returns true when interval reached.
        /// Resets counter automatically when true is returned.
        /// </summary>
        public bool ShouldUpdate()
        {
            if (m_ForceFire)
            {
                m_ForceFire = false;
                m_Counter = m_PhaseCounter;
                return true;
            }

            m_Counter = m_Counter == int.MaxValue ? 0 : m_Counter + 1;
            if (m_Counter >= m_Interval)
            {
                m_Counter = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Force next ShouldUpdate() to return true regardless of counter.
        /// Use for critical state changes (phase transitions, grid collapse)
        /// that can't wait for the natural throttle interval.
        /// </summary>
        public void ForceNextFire() => m_ForceFire = true;

        /// <summary>
        /// Reset counter to the configured stagger phase.
        /// Use ForceNextFire() when the caller needs an immediate one-shot update.
        /// </summary>
        public void Reset()
        {
            m_ForceFire = false;
            m_Counter = m_PhaseCounter;
        }

        /// <summary>
        /// Reset counter so the next natural fire happens after one full interval.
        /// Use after out-of-band work has already run and a close staggered follow-up
        /// would duplicate that work.
        /// </summary>
        public void ResetToFullInterval()
        {
            m_ForceFire = false;
            m_Counter = 0;
        }

        /// <summary>
        /// Current interval setting.
        /// </summary>
        public int Interval => m_Interval;
    }
}
