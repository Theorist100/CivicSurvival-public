using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Attention.Data;

namespace CivicSurvival.Domains.Attention.Systems
{
    /// <summary>
    /// Calculates shock decay over time. READ-ONLY — does not write to WorldShockState.
    /// WorldShockSystem reads DecayDelta/AdvanceLastUpdateTime and applies the single write.
    ///
    /// Throttled: only checks once per game hour (not every frame).
    /// Performance: ~0ms most frames (early exit on timer check).
    /// </summary>
    [ActIndependent]
#pragma warning disable CIVIC076 // Runs every frame by design (decay timer check is O(1))
    public partial class WorldShockDecaySystem : CivicSystemBase
#pragma warning restore CIVIC076
    {
        private static readonly LogContext Log = new("WorldShockDecaySystem");

        private double m_LastCheckTime;
        private static readonly CatchUpPolicy s_DecayCatchUpPolicy = CreateDecayCatchUpPolicy();

        // Check decay once per game hour (not every frame)
        private const double CHECK_INTERVAL_HOURS = 1.0;

        // ===== Output deltas (read by WorldShockSystem) =====

        /// <summary>Shock decay this frame (negative or 0). Reset each frame.</summary>
        public float DecayDelta { get; private set; }

        /// <summary>New LastUpdateTime to write (0 = no change). Reset each frame.</summary>
        public double AdvanceLastUpdateTime { get; private set; }

        // Preserve historical PERF.log marker name (without "System" suffix) so
        // existing dashboards / analyzers do not see a renamed entry after the
        // GameSystemBase -> CivicSystemBase migration. CivicSystemBase wraps
        // OnUpdateImpl with PerformanceProfiler.Measure(ProfileName) automatically.
        protected override string ProfileName => "WorldShockDecay.OnUpdate";

        protected override void OnCreate()
        {
            base.OnCreate();
            Log.Info("Created — hourly decay check (read-only, no WorldShockState write)");
        }

        protected override void OnUpdateImpl()
        {
            // Reset deltas each frame
            DecayDelta = 0f;
            AdvanceLastUpdateTime = 0.0;

            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null) return;

            double currentTime = timeProvider.Current.TotalGameHours;

            // Init: seed baseline so first throttle check passes immediately (no 1-hour skip after load)
            if (m_LastCheckTime <= 0.0)
                m_LastCheckTime = currentTime - CHECK_INTERVAL_HOURS;

            // Backward-time guard: after load, game time may be before our stored time
            if (currentTime < m_LastCheckTime)
            {
                m_LastCheckTime = currentTime;
                return;
            }

            // Throttle: only check once per game hour
            if (currentTime - m_LastCheckTime < CHECK_INTERVAL_HOURS)
                return;

            // Read state (ReadOnly — no write)
            if (!SystemAPI.TryGetSingleton<WorldShockState>(out var state)) return;

            // No decay if shock is already 0
            // FIX S09_RAG1:F02: Still advance LastUpdateTime to prevent stale baseline
            // (without this, FirstStrike after 200h causes daysSinceUpdate=8.3 → instant massive decay)
            if (state.ShockLevel <= 0f)
            {
                if (state.LastUpdateTime < currentTime)
                    AdvanceLastUpdateTime = currentTime;
                if (AdvanceLastUpdateTime > 0.0)
                    m_LastCheckTime = currentTime;
                return;
            }

            // No decay for 24h after tragedy
            double hoursSinceTragedy = currentTime - state.LastTragedyTime;
            if (hoursSinceTragedy < BalanceConfig.Current.Attention.DecayPauseHours)
            {
                // Always advance LastUpdateTime during grace so decay doesn't "catch up" when grace ends.
                // Previous guard (< LastTragedyTime) only advanced once, then blocked further advances
                // → daysSinceUpdate accumulated during remaining grace → spike on grace end.
                AdvanceLastUpdateTime = currentTime;
                m_LastCheckTime = currentTime;
                return;
            }

            // Calculate decay (once per day)
            double rawDaysSinceUpdate = GameRate.DayFractionFromHours(currentTime - state.LastUpdateTime);
            if (rawDaysSinceUpdate >= 1.0)
            {
                double maxDaysToApply = GameRate.DayFractionFromHours(s_DecayCatchUpPolicy.BoundHours);
                double daysToApply = math.min(maxDaysToApply, rawDaysSinceUpdate);
                float decay = state.DecayPerDay * (float)daysToApply;
                DecayDelta = -decay;
                AdvanceLastUpdateTime = state.LastUpdateTime + daysToApply * GameRate.HOURS_PER_DAY;
                m_LastCheckTime = currentTime;

                if (decay > 0 && state.ShockLevel > 0)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"Decay delta: -{decay:F1}%");
                }
            }
            else
            {
                m_LastCheckTime = currentTime;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        private static CatchUpPolicy CreateDecayCatchUpPolicy()
        {
            return GameDurationHours.TryCreate(GameRate.HOURS_PER_DAY, out var oneGameDay)
                ? CatchUpPolicy.Bounded(oneGameDay)
                : CatchUpPolicy.None;
        }
    }
}
