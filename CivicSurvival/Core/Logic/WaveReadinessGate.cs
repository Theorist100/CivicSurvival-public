using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure decision core for "is the city ready to be attacked again?" — the dynamic readiness
    /// gate that ends a Calm phase. Replaces the old static "calm duration elapsed" timer with a
    /// live assessment: the next wave launches only when the city has had time to recover the
    /// generation a prior wave knocked out, but never sits idle longer than a wave's size warrants.
    ///
    /// Three conditions must ALL hold (see <see cref="Evaluate"/>):
    ///   1. timeInPhase ≥ baseTempo        — the minimum cadence by city SIZE (nameplate), "harass".
    ///   2. lostFraction ≤ recoveredThr    — generation actually restored (damage-driven, not load).
    ///   3. timeInPhase ≥ T_recovered×(1+grace) — a grace window after recovery, "let them build".
    /// A ceiling (maxWaitSeconds) launches anyway if the player never repairs — harassment must
    /// continue; a player who chose not to recover does not earn permanent peace.
    ///
    /// WHERE THIS LIVES (variant D — formulas in Core, system = thin adapter): this is the single
    /// source of the launch decision, called today by <c>WaveScheduler</c> (the runtime adapter)
    /// and ready to be co-called by the forecast / server recompute later without a second copy.
    /// It is pure managed code — no ECS, no Unity, no snapshot read, no Burst — so there is no
    /// native-crash surface; the only risks are arithmetic and are guarded here (see C1/C2 below)
    /// so every caller is protected by one copy, not a clamp duplicated per adapter.
    /// </summary>
    public static class WaveReadinessGate
    {
        /// <summary>Sentinel for <see cref="WaveReadinessState.TRecovered"/>: not yet recovered this Calm.</summary>
        public const float NOT_RECOVERED = -1f;

        /// <summary>Sim-seconds per in-game hour, fixed by vanilla (decompile): kTicksPerDay 262144
        /// at 60 sim-ticks/sec ⇒ 262144 / 24 / 60. Converts repair-hours (game time) into the
        /// Calm timer's sim-second scale for the ceiling. NOT <see cref="Utils.GameRate.SECONDS_PER_HOUR"/>
        /// (that is game-seconds-per-game-hour = 3600, a different quantity).</summary>
        public const float SIM_SEC_PER_GAME_HOUR = 262144f / 24f / 60f; // ≈ 182.04

        public enum LaunchReason : byte
        {
            /// <summary>Default/uninitialised sentinel — never returned by <see cref="Evaluate"/>.</summary>
            None = 0,
            /// <summary>Not launching: minimum cadence (baseTempo) not yet reached.</summary>
            WaitingTempo,
            /// <summary>Not launching: generation still knocked out (lostFraction above threshold).</summary>
            WaitingRecovery,
            /// <summary>Not launching: recovered, but the post-recovery grace window has not elapsed.</summary>
            WaitingGrace,
            /// <summary>Launching: all three readiness conditions met.</summary>
            Ready,
            /// <summary>Launching: ceiling reached — the city never recovered, harassment resumes anyway.</summary>
            Ceiling,
        }

        public readonly struct Result
        {
            public readonly bool Launch;
            public readonly LaunchReason Reason;
            public readonly float LostFraction; // resolved (guarded) value, for diagnostics

            public Result(bool launch, LaunchReason reason, float lostFraction)
            {
                Launch = launch;
                Reason = reason;
                LostFraction = lostFraction;
            }
        }

        /// <summary>
        /// Evaluate the launch decision for one Calm tick and advance the gate state.
        ///
        /// <para>C1 (div-by-zero / NaN): <paramref name="nameplateMW"/> = 0 is the normal
        /// "power-capacity snapshot not ready / no plants" state. We treat nameplate ≤ 0 as
        /// "undamaged" (lostFraction = 0) rather than dividing — a missing snapshot must never
        /// block the attack. The result is also NaN/Inf-clamped.</para>
        ///
        /// <para>C2 (persisted-state soft-lock): <see cref="WaveReadinessState.TRecovered"/> is the
        /// only persisted field. A NaN there would make <c>timeInPhase ≥ T_recovered×grace</c>
        /// always false after load → the gate would never launch (silent soft-lock). We sanitize
        /// it on read here so a corrupt save self-heals.</para>
        /// </summary>
        /// <param name="state">Per-Calm gate state (T_recovered). Mutated. Reset to a fresh state
        /// (TRecovered = <see cref="NOT_RECOVERED"/>) by the caller on each Calm entry.</param>
        /// <param name="timeInPhaseSeconds">Sim-seconds since Calm began (the WaveScheduler timer).</param>
        /// <param name="baseTempoSeconds">Minimum cadence by city size (sim-seconds) — condition 1.</param>
        /// <param name="dispatchableMW">Live dispatchable capacity (damage-cut, not load-cut).</param>
        /// <param name="nameplateMW">Built grid nameplate (degradation does not touch it).</param>
        /// <param name="recoveredThreshold">lostFraction at/below which generation counts as restored.</param>
        /// <param name="graceFraction">Extra fraction of T_recovered to wait after recovery (e.g. 0.30).</param>
        /// <param name="maxWaitSeconds">Ceiling: launch even if never recovered (e.g. Municipal repair time).</param>
        public static Result Evaluate(
            ref WaveReadinessState state,
            float timeInPhaseSeconds,
            float baseTempoSeconds,
            int dispatchableMW,
            int nameplateMW,
            float recoveredThreshold,
            float graceFraction,
            float maxWaitSeconds)
        {
            // C2: sanitize persisted TRecovered — a NaN/Inf from a corrupt save would dead-lock the gate.
            if (!IsFinite(state.TRecovered))
                state.TRecovered = NOT_RECOVERED;

            float lostFraction = ComputeLostFraction(dispatchableMW, nameplateMW); // C1 inside

            // Condition 2 latch: record the FIRST moment generation is restored (R2: fix once;
            // during Calm there are no waves, so a later lostFraction rise is an anomaly we do not
            // re-latch on — the wave it would belong to is the one we are gating).
            bool recovered = lostFraction <= recoveredThreshold;
            if (recovered && state.TRecovered < 0f)
                state.TRecovered = timeInPhaseSeconds;

            // Ceiling: never recovered (or recovering too slowly) — harass anyway. Guards the
            // "broke player who never repairs" from earning permanent peace. Floored at baseTempo so
            // it is a TRUE upper bound: for a small city whose size-cadence (baseTempo) already runs
            // longer than the Municipal-repair ceiling, the wave still never launches before baseTempo
            // (and a large city still gets its recovery window between baseTempo and the ceiling).
            float effectiveCeiling = math.max(maxWaitSeconds, baseTempoSeconds);
            if (timeInPhaseSeconds >= effectiveCeiling)
                return new Result(true, LaunchReason.Ceiling, lostFraction);

            // Condition 1 — minimum cadence by size.
            if (timeInPhaseSeconds < baseTempoSeconds)
                return new Result(false, LaunchReason.WaitingTempo, lostFraction);

            // Condition 2 — generation restored.
            if (!recovered)
                return new Result(false, LaunchReason.WaitingRecovery, lostFraction);

            // Condition 3 — grace window after recovery (T_recovered × (1 + grace)).
            float graceDeadline = state.TRecovered * (1f + math.max(0f, graceFraction));
            if (timeInPhaseSeconds < graceDeadline)
                return new Result(false, LaunchReason.WaitingGrace, lostFraction);

            return new Result(true, LaunchReason.Ready, lostFraction);
        }

        /// <summary>
        /// Damage-driven loss fraction = 1 − dispatchable/nameplate, clamped to [0,1].
        /// C1: nameplate ≤ 0 (snapshot not ready / no plants) → 0 (undamaged), never a divide.
        /// Uses dispatchable (damage/collapse-cut), NOT production (which also follows load) so a
        /// healthy surplus city does not read as damaged.
        /// </summary>
        public static float ComputeLostFraction(int dispatchableMW, int nameplateMW)
        {
            if (nameplateMW <= 0)
                return 0f;

            float frac = 1f - (float)dispatchableMW / nameplateMW;
            if (!IsFinite(frac))
                return 0f;
            return math.clamp(frac, 0f, 1f);
        }

        /// <summary>
        /// Surcharge recovery factor ∈ [0,1] — how much of a wave-size surcharge a city has earned
        /// given its current damage. Linear ramp from full (lostFraction 0 → 1) to none
        /// (lostFraction ≥ <paramref name="lethalFraction"/> → 0), so the size surcharge (surplus +
        /// density) is shaved to zero on a wounded city and a struck city is NEVER finished off by
        /// escalation (CRISIS_MODEL §0 no-spiral). A separate, LOWER threshold than the frequency
        /// gate's RecoveredThreshold — escalation is dropped early, before the city turns fragile.
        /// Guards: <paramref name="lethalFraction"/> floored at <see cref="float.Epsilon"/> before
        /// the divide; NaN/Inf → 0 (fail-safe: no escalation rather than blind escalation).
        /// </summary>
        public static float RecoveryFactor(float lostFraction, float lethalFraction)
        {
            float denom = math.max(lethalFraction, float.Epsilon);
            float factor = 1f - lostFraction / denom;
            if (!IsFinite(factor))
                return 0f;
            return math.clamp(factor, 0f, 1f);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    /// <summary>
    /// Per-Calm mutable state of <see cref="WaveReadinessGate"/>. Plain struct (NOT an ECS
    /// component); the runtime adapter (<c>WaveScheduler</c>) owns one instance, persists
    /// <see cref="TRecovered"/> across save/load, and resets it on each Calm entry.
    /// </summary>
    public struct WaveReadinessState
    {
        /// <summary>Sim-seconds (since Calm start) when generation FIRST recovered this Calm, or
        /// <see cref="WaveReadinessGate.NOT_RECOVERED"/> (-1) until then. The only persisted field;
        /// sanitized on read in <see cref="WaveReadinessGate.Evaluate"/> (C2).</summary>
        public float TRecovered;

        public static WaveReadinessState Fresh => new WaveReadinessState { TRecovered = WaveReadinessGate.NOT_RECOVERED };
    }
}
