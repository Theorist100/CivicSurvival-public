using System;
using System.Threading;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Simple circuit breaker to prevent continuous polling during network outages.
    /// After failureThreshold consecutive failures, blocks requests for cooldownSeconds.
    /// Resets on any successful call.
    ///
    /// Thread-safe: RecordSuccess/RecordFailure may be called from HTTP response
    /// callbacks on thread pool threads while CanProceed runs on main thread.
    /// m_CooldownUntilBits stored as int bit-pattern for Interlocked atomicity.
    /// TryBeginProbe uses CompareExchange to allow exactly one probe on cooldown expiry.
    /// </summary>
    public sealed class CircuitBreakerState
    {
        private const int PROBE_IN_FLIGHT = int.MaxValue;

        private int m_ConsecutiveFailures;
        private int m_CooldownUntilBits; // float stored as int bit-pattern for Interlocked
        private readonly int m_FailureThreshold;
        private readonly float m_CooldownSeconds;

        public CircuitBreakerState(int failureThreshold = 3, float cooldownSeconds = 300f)
        {
            m_FailureThreshold = failureThreshold;
            m_CooldownSeconds = cooldownSeconds;
        }

        /// <summary>
        /// Pure availability check. Does not acquire the cooldown-expiry probe.
        /// </summary>
        public bool CanProceed(float currentTime)
        {
            int failures = Volatile.Read(ref m_ConsecutiveFailures);
            if (failures < m_FailureThreshold) return true;
            if (failures == PROBE_IN_FLIGHT) return false;

            float cooldownUntil = BitConverter.Int32BitsToSingle(Volatile.Read(ref m_CooldownUntilBits));
            return currentTime >= cooldownUntil;
        }

        /// <summary>
        /// Acquire a dispatch lease. Call only when the caller is committed to
        /// sending; every successful lease must end in success, failure, or cancel.
        /// </summary>
        public bool TryBeginProbe(float currentTime)
            => TryBeginProbe(currentTime, out _);

        /// <summary>
        /// Acquire a dispatch lease with a request-start handle. Call only when
        /// the caller is committed to sending; every successful lease must end in
        /// success, failure, or cancel using the returned handle.
        /// </summary>
        public bool TryBeginProbe(float currentTime, out BreakerProbe probe)
        {
            probe = default;
            int failures = Volatile.Read(ref m_ConsecutiveFailures);
            if (failures < m_FailureThreshold)
            {
                probe = BreakerProbe.Start(currentTime);
                return true;
            }
            if (failures == PROBE_IN_FLIGHT) return false;

            float cooldownUntil = BitConverter.Int32BitsToSingle(Volatile.Read(ref m_CooldownUntilBits));
            if (currentTime < cooldownUntil) return false;

            int prev = Interlocked.CompareExchange(
                ref m_ConsecutiveFailures, PROBE_IN_FLIGHT, m_FailureThreshold);
            if (prev != m_FailureThreshold)
                return false;

            probe = BreakerProbe.Start(currentTime);
            return true;
        }

        /// <summary>Release an acquired cooldown probe when dispatch is cancelled before send.</summary>
        public void CancelProbe()
        {
            Interlocked.CompareExchange(ref m_ConsecutiveFailures, m_FailureThreshold, PROBE_IN_FLIGHT);
        }

        /// <summary>Release an acquired cooldown probe when dispatch is cancelled before send.</summary>
        public void CancelProbe(BreakerProbe probe)
        {
            CancelProbe();
        }

        /// <summary>Reset failure count on success.</summary>
        public void RecordSuccess()
        {
            Interlocked.Exchange(ref m_ConsecutiveFailures, 0);
        }

        /// <summary>Reset failure count on success.</summary>
        public void RecordSuccess(BreakerProbe probe)
        {
            RecordSuccess();
        }

        /// <summary>Clear breaker backoff for an explicit user reconnect or owner reset.</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref m_ConsecutiveFailures, 0);
            Interlocked.Exchange(ref m_CooldownUntilBits, 0);
        }

        /// <summary>
        /// Increment failure count and start cooldown if threshold reached.
        /// Note: increment and cooldown write are not atomic as a pair — a concurrent
        /// CanProceed may see threshold reached but stale m_CooldownUntilBits, allowing
        /// one extra probe. Accepted: window is one instruction wide, 300s cooldown
        /// makes a single leaked request benign.
        /// </summary>
        public void RecordFailure(float currentTime)
            => RecordFailure(BreakerProbe.Untracked(currentTime));

        public void RecordFailure(BreakerProbe probe)
        {
            while (true)
            {
                int current = Volatile.Read(ref m_ConsecutiveFailures);
                int next = current == PROBE_IN_FLIGHT || current >= m_FailureThreshold
                    ? m_FailureThreshold
                    : current + 1;

                if (Interlocked.CompareExchange(ref m_ConsecutiveFailures, next, current) != current)
                    continue;

                if (next >= m_FailureThreshold)
                {
                    float failureTime = GetFailureCompletionTime(probe);
                    Interlocked.Exchange(ref m_CooldownUntilBits,
                        BitConverter.SingleToInt32Bits(failureTime + m_CooldownSeconds));
                }
                return;
            }
        }

        /// <summary>Whether the breaker is currently open (blocking requests).</summary>
        public bool IsOpen => Volatile.Read(ref m_ConsecutiveFailures) >= m_FailureThreshold;

        private static float GetFailureCompletionTime(BreakerProbe probe)
        {
            if (!probe.HasStartTicks)
                return probe.RequestStartTime;

            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - probe.RequestStartTicks;
            if (elapsedTicks <= 0)
                return probe.RequestStartTime;

            double frequency = System.Diagnostics.Stopwatch.Frequency;
            if (frequency <= 0.0)
                return probe.RequestStartTime;

            double elapsedSeconds = elapsedTicks / frequency;
            if (elapsedSeconds <= 0.0 || elapsedSeconds > float.MaxValue)
                return probe.RequestStartTime;

            return probe.RequestStartTime + (float)elapsedSeconds;
        }

        public readonly struct BreakerProbe
        {
            private BreakerProbe(float requestStartTime, long requestStartTicks, bool hasStartTicks)
            {
                RequestStartTime = requestStartTime;
                RequestStartTicks = requestStartTicks;
                HasStartTicks = hasStartTicks;
            }

            public float RequestStartTime { get; }
            public long RequestStartTicks { get; }
            public bool HasStartTicks { get; }

            public static BreakerProbe Start(float requestStartTime)
                => new(requestStartTime, System.Diagnostics.Stopwatch.GetTimestamp(), true);

            public static BreakerProbe Untracked(float requestStartTime)
                => new(requestStartTime, 0, false);
        }
    }
}
