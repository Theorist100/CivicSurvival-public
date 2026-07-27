using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services.Countermeasures;
using CivicSurvival.Core.Types;
using Unity.Mathematics;
using Math = System.Math;
using Random = Unity.Mathematics.Random;

namespace CivicSurvival.Core.Services.Countermeasures
{
    /// <summary>
    /// Pure state-transition predicates and math for Countermeasures FSM.
    /// All methods are static, side-effect-free, and operate only on ref structs + config.
    ///
    /// Caller: CountermeasuresUpdateSystem (sole caller)
    /// </summary>
    public static class CountermeasuresRules
    {
        private const float CORRUPTION_PER_PROTEST = 5f;

        // ============================================================================
        // HEAT
        // ============================================================================

        /// <summary>
        /// Update heat based on corruption tiers. Pure function: reads config, writes core.Heat.
        /// </summary>
        public static void UpdateHeat(ref CountermeasuresCoreFsm core, float gameDayFraction)
        {
            var balance = BalanceConfig.Current;
            var cfg = balance.Countermeasures;
            var corr = balance.Corruption;

            float heatChange;

            if (core.CorruptionScore >= corr.LevelCorrupt) // 75+
            {
                heatChange = cfg.HeatGainTier3 * gameDayFraction;
            }
            else if (core.CorruptionScore >= cfg.InvestigationThreshold) // 50-75
            {
                heatChange = cfg.HeatGainTier2 * gameDayFraction;
            }
            else if (core.CorruptionScore >= cfg.SuspicionThreshold) // 25-50
            {
                heatChange = cfg.HeatGainTier1 * gameDayFraction;
            }
            else // < 25 - decay
            {
                heatChange = -cfg.HeatDecayRate * gameDayFraction;
            }

            core.Heat = Math.Max(0f, Math.Min(cfg.HeatMax, core.Heat + heatChange));
        }

        /// <summary>Corruption penalty from active protests.</summary>
        public static float GetCorruptionFromProtests(int activeProtests)
        {
            return activeProtests * CORRUPTION_PER_PROTEST;
        }

        /// <summary>Apply the common "case resolved" cooldown and heat relief.</summary>
        public static void ApplyResolutionCooldown(ref CountermeasuresCoreFsm core, CountermeasuresConfig cfg)
        {
            core.NextEventHour = core.GameHour + Math.Max(0f, cfg.EventCooldownHours);
            core.Heat = Math.Max(0f, core.Heat - Math.Max(0f, cfg.HeatRefundOnResolve));
        }

        // ============================================================================
        // INVESTIGATION PREDICATES
        // ============================================================================

        /// <summary>
        /// Should investigation start? Heat-based chance with per-second scaling.
        /// Mutates inv.RngState (RNG consumption).
        /// </summary>
        public static bool ShouldStartInvestigation(ref CountermeasuresCoreFsm core, ref CmInvestigationState inv, CountermeasuresConfig cfg, float deltaSeconds)
        {
            if (inv.Active) return false;
            if (core.GameHour < core.NextEventHour) return false;

            if (core.Heat < cfg.HeatMinForInvestigation) return false;

            var rng = new Random(inv.RngState);
            float heatChancePerSec = cfg.InvestigationBaseChance * (core.Heat / math.max(cfg.HeatMax, 1f));
            bool result = rng.NextFloat() < 1f - math.pow(math.max(0f, 1f - heatChancePerSec), deltaSeconds);
            inv.RngState = rng.state;
            return result;
        }

        /// <summary>
        /// Calculate investigation progress from elapsed hours.
        /// Returns new progress value (0-100). Does NOT set milestones or waiting state.
        /// </summary>
        public static int CalculateInvestigationProgress(in CountermeasuresCoreFsm core, in CmInvestigationState inv, CountermeasuresConfig cfg)
        {
            float hoursSinceStart = Math.Max(0f, core.GameHour - inv.StartHour);
            // DIV-ZERO FIX: Guard against misconfigured HoursPerProgress
            float hoursPerProgress = Math.Max(cfg.HoursPerProgress, 0.1f);
            int newProgress = Math.Clamp((int)Math.Round(hoursSinceStart / hoursPerProgress * 10f), 0, 100);

            // Higher corruption = faster progress
            if (core.CorruptionScore > cfg.InvestigationSpeedThreshold)
            {
                newProgress = Math.Min(100, (int)Math.Round(newProgress * cfg.InvestigationSpeedMultiplier));
            }

            return newProgress;
        }

        // ============================================================================
        // POLICE PREDICATES
        // ============================================================================

        /// <summary>
        /// Should police investigation start? Random chance with per-second scaling.
        /// Mutates police.RngState (RNG consumption).
        /// </summary>
        public static bool ShouldStartPolice(ref CountermeasuresCoreFsm core, ref CmPoliceState police, CountermeasuresConfig cfg, float deltaSeconds)
        {
            if (police.Active) return false;
            if (core.CorruptionScore < cfg.InvestigationThreshold) return false;
            if (core.GameHour < core.NextEventHour) return false;

            var rng = new Random(police.RngState);
            // H15: correct per-second scaling — 1-(1-p)^delta stays probabilistic at any game speed
            bool result = rng.NextFloat() < 1f - math.pow(math.max(0f, 1f - cfg.PoliceStartChance), deltaSeconds);
            police.RngState = rng.state;
            return result;
        }

        // ============================================================================
        // PROTEST MATH
        // ============================================================================

        /// <summary>
        /// Update protest cooldown and decay timers. Pure state mutation.
        /// </summary>
        public static void UpdateProtestTimers(ref CmProtestState protest, CountermeasuresConfig cfg, float deltaSeconds)
        {
            // Handle cooldown (time-based, FPS-independent)
            if (protest.CooldownSeconds > 0f)
            {
                // F-S1-01 FIX: Clamp to zero to prevent negative overshoot at high delta.
                protest.CooldownSeconds = Math.Max(0f, protest.CooldownSeconds - deltaSeconds);
            }

            // Handle decay (time-based, FPS-independent)
            if (protest.ActiveProtests > 0 && protest.CooldownSeconds <= 0f)
            {
                float decayThreshold = Math.Max(cfg.ProtestDecaySeconds, 1f); // FIX S5-01: Guard zero config
#pragma warning disable CIVIC056 // Resets at decayThreshold — bounded accumulator, not unbounded
                protest.DecaySeconds += deltaSeconds;
#pragma warning restore CIVIC056
                // FIX S21-#4: while instead of if — multiple protests may decay in one high-delta tick
                while (protest.DecaySeconds >= decayThreshold && protest.ActiveProtests > 0)
                {
                    protest.DecaySeconds -= decayThreshold;
                    protest.ActiveProtests--;
                }
                if (protest.ActiveProtests <= 0)
                    protest.DecaySeconds = 0f;
            }
        }

        /// <summary>
        /// Roll protest chance (per-second scaling). Returns true if protest should trigger.
        /// Mutates protest.RngState (RNG consumption).
        /// </summary>
        // REMOVED: Dead code — replaced by inline single-stream RNG in CountermeasuresUpdateSystem.CheckForProtests (H9 contract)
    }
}
