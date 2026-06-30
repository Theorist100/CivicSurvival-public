using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure verdict for the per-wave leak floor. Extracted from
    /// <c>InterceptProcessingSystem.ShouldLeak</c> so the in-game crisis sweep (Phase 7
    /// severity) can predict the guaranteed drone breach-through with the same code the runtime
    /// uses. The core is an integer hash (bit-identical cross-machine) and the only float step is
    /// a single multiply with truncation, so this verdict is deterministic on every platform —
    /// it survives a future server-authoritative PvP re-evaluation unchanged.
    /// </summary>
    internal static class LeakFloorLogic
    {
        // Fixed-point resolution for the per-target leak hash roll (roll in [0, RES) compared
        // against leakFraction * RES). 1000 gives 0.1% granularity on MaxWaveInterceptFraction.
        internal const uint LEAK_HASH_RESOLUTION = 1000u;

        /// <summary>
        /// Per-wave leak floor: the verdict is per TARGET (deterministic hash) so every drone
        /// aimed at a building shares it — a concentrated cluster lands whole or is fully stopped,
        /// never thinned to a lone survivor by interception. ~(1 - maxWaveInterceptFraction) of
        /// targets leak regardless of AA. Per-target rather than per-wave, but the wave's
        /// randomized target selection already moves the targeted set each wave, so the same
        /// buildings do not fall every time.
        ///
        /// Focus-cluster drones are NOT force-leaked here — they are interceptable but harder
        /// (reduced AA hit chance via FocusInterceptMultiplier in FireControlExecutor), so good
        /// AA placement thins them while the per-target floor still guarantees some leak through.
        ///
        /// Pure: no ECS, no config read (caller passes the fraction).
        /// </summary>
        public static bool Leaks(int targetBuildingIndex, float maxWaveInterceptFraction)
        {
            if (targetBuildingIndex <= 0)
                return false; // ground / unknown target — defended normally

            float maxFraction = math.clamp(maxWaveInterceptFraction, 0f, 1f);
            if (maxFraction >= 1f)
                return false;

            float leakFraction = 1f - maxFraction;
            uint roll = math.hash(new int2(targetBuildingIndex, 0)) % LEAK_HASH_RESOLUTION;
            return roll < (uint)(leakFraction * LEAK_HASH_RESOLUTION);
        }
    }
}
