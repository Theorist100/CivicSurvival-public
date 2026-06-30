using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer infrastructure-repair model: owns the per-plant damage/repair timeline of the
    /// severity sweep — reset, complete-due, dispatch — plus the nameplate-scaled per-hit loss
    /// (PlantHitMath mirror). Operates on the shared <see cref="ForecastState"/> (the per-plant arrays
    /// are the composer's reused scratch). NOT new balance arithmetic — the repair durations and the
    /// hit-loss slice come from config, the same values RepairPaymentHelper / PlantHitMath use.
    /// </summary>
    public static class RepairForecast
    {
        /// <summary>Sentinel for <see cref="ForecastState.RepairDone"/>: the plant is not under repair.
        /// Completion times are always &gt; 0, so a negative value reads as "free".</summary>
        public const float REPAIR_NONE = -1f;

        /// <summary>Nameplate (MW) of plant <paramref name="p"/>: the real per-plant size from the
        /// Tier-0 live scratch when present, else the single discretised <paramref name="plantMW"/>
        /// (archetype-fallback — byte-identical to the pre-Tier-0 equal-plant model).</summary>
        internal static float PlantCap(in ForecastState state, int p, float plantMW)
            => state.PlantCapMW != null ? state.PlantCapMW[p] : plantMW;

        /// <summary>Reset the per-plant scratch for one run: zero damage, clear the repair state and the
        /// queued-membership flag for the live plants, and rewind the repair ring (the preallocated
        /// arrays are reused, never re-new'd).</summary>
        public static void Reset(ref ForecastState state)
        {
            for (int p = 0; p < state.NPlants; p++)
            {
                state.PlantDamage[p] = 0f;
                state.RepairDone[p] = REPAIR_NONE;
                state.RepairQueued[p] = false;
            }
            state.RepairHead = 0;
            state.RepairCount = 0;
        }

        /// <summary>Enqueue a freshly damaged plant for repair (O(1), deduped by RepairQueued). No-op
        /// when the plant is already queued. The ring never overflows: at most NPlants distinct ids are
        /// live at once and the buffer is sized to MAX_PLANTS ≥ NPlants.</summary>
        public static void Enqueue(ref ForecastState state, int p)
        {
            if (state.RepairQueued[p])
                return;
            int cap = state.RepairQueue.Length;
            int tail = (state.RepairHead + state.RepairCount) % cap;
            state.RepairQueue[tail] = p;
            state.RepairCount++;
            state.RepairQueued[p] = true;
        }

        /// <summary>Repairs whose completion time has arrived this tick fully zero the plant and free a
        /// slot. Linear scan over the live plants (n ≤ MAX_PLANTS, no heap, no per-tick allocation).</summary>
        public static void CompleteDue(ref ForecastState state, float plantMW, float t, bool repairEnabled)
        {
            if (!repairEnabled)
                return;

            for (int p = 0; p < state.NPlants; p++)
            {
                if (state.RepairDone[p] >= 0f && state.RepairDone[p] <= t)   // >= 0f == "under repair" (REPAIR_NONE is -1f; completion times are always > 0)
                {
                    state.LostMW -= state.PlantDamage[p] * PlantCap(state, p, plantMW);
                    state.PlantDamage[p] = 0f;
                    state.RepairDone[p] = REPAIR_NONE;
                    state.ActiveRepairs--;
                }
            }
            if (state.LostMW < 0f)
                state.LostMW = 0f;
        }

        /// <summary>Queued plants enter repair as slots free in the inter-wave lull, bounded by the
        /// concurrent-repair cash gate.</summary>
        public static void Dispatch(ref ForecastState state, int maxRepairs, float repHours, float t, bool repairEnabled)
        {
            if (!repairEnabled)
                return;

            // Drain the FIFO ring (O(1) per plant — pop the head, advance modulo capacity) instead of the
            // old List.RemoveAt(0) (O(n) shift). RepairQueued[p] is cleared on pop so a re-queue after a
            // later completion stays O(1).
            int cap = state.RepairQueue.Length;
            while (state.ActiveRepairs < maxRepairs && state.RepairCount > 0)
            {
                int p = state.RepairQueue[state.RepairHead];
                state.RepairHead = (state.RepairHead + 1) % cap;
                state.RepairCount--;
                state.RepairQueued[p] = false;
                if (state.PlantDamage[p] > 0f && state.RepairDone[p] < 0f)   // < 0f == "not under repair" (REPAIR_NONE sentinel)
                {
                    state.RepairDone[p] = t + repHours;
                    state.ActiveRepairs++;
                }
            }
        }

        /// <summary>Nameplate-scaled per-hit loss fraction: slice = max(HitDamageMW, HitFleetSharePercent ·
        /// fleet), fraction = clamp(slice / plantMW, [min, max]). Now a thin config-unwrap over
        /// <see cref="PlantHitMath.LossPerHitMW"/> (the single owner of the missile-hit math) — the sweep
        /// no longer re-derives it. Byte-identical to the former inline copy (same MW-float expressions).</summary>
        public static float LossPerHit(RemoteBalanceConfig cfg, float plantMW, float fleetMW)
        {
            var r = cfg.Repair;
            return PlantHitMath.LossPerHitMW(
                plantMW, r.HitDamageMW, r.HitFleetSharePercent, fleetMW, r.MinHitLossPercent, r.MaxHitLossPercent);
        }
    }
}
