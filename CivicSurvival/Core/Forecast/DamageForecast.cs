using Unity.Mathematics;
using CivicSurvival.Core.Logic;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer wave-damage model: the bridge from the air-defense leak fraction to generation
    /// loss. A wave strikes the unprotected share of the live fleet (round(nPlants · leak)), each
    /// struck plant eating up to <c>perTarget</c> hits at the nameplate-scaled per-hit loss; a freshly
    /// damaged plant is queued for repair. NOT new balance arithmetic — the per-hit slice comes from
    /// <see cref="RepairForecast.LossPerHit"/>, the leak from <see cref="AirDefenseForecast.Leak"/>.
    /// </summary>
    public static class DamageForecast
    {
        /// <summary>Apply one wave's strikes to the live fleet. <paramref name="rng"/> is advanced by
        /// ref so the per-run plant selection sequence is preserved (mirrors the runtime's randomized
        /// target selection, letting repairs catch live plants).</summary>
        public static void ApplyWave(
            ref ForecastState state, ref Random rng,
            float leak, int perTarget, float perHitLoss, float plantMW, bool repairEnabled)
        {
            // Plants struck this wave = the unprotected share of the live fleet. Round so even a thin
            // leak eventually touches a plant; clamp to the live plant count.
            int targetsThisWave = math.clamp((int)math.round(state.NPlants * leak), 0, state.NPlants);

            // Which plant a leaked drone strikes — weighted by nameplate MW, the SAME cumulative-walk
            // rule the runtime spawner uses (DamageTargetingMath.WeightedPick). Before TD-1 this was a
            // uniform rng.NextInt(NPlants): a megacity with one dominant station had its damage
            // under-estimated because the giant plant was no likelier to be hit than a small turbine.
            // Weights are full nameplate (PlantCapMW when Tier-0 live sizes are present, else the flat
            // plantMW scalar). NPlants is clamped to MAX_PLANTS (256), so a stack buffer is bounded and
            // never heap-allocates per run.
            // FORECAST-APPROX: weights use FULL nameplate, not the runtime's residual nameplate
            // (origKW·(1 − operational − disaster) clamped). Residual-MW weighting is TD-3 (deferred):
            // the forecast holds no per-plant accumulated-damage residual, only PlantCapMW.
            System.Span<int> weights = stackalloc int[state.NPlants];
            int totalWeight = 0;
            for (int p = 0; p < state.NPlants; p++)
            {
                int w = (int)math.round(RepairForecast.PlantCap(state, p, plantMW));
                if (w < 0) w = 0;
                weights[p] = w;
                totalWeight += w;
            }

            for (int targetIdx = 0; targetIdx < targetsThisWave; targetIdx++)
            {
                // Uniform fallback when every plant has non-positive weight (degenerate sizing),
                // matching the runtime selector's totalWeight<=0 branch.
                int p = totalWeight <= 0
                    ? rng.NextInt(state.NPlants)
                    : DamageTargetingMath.WeightedPick(weights, totalWeight, rng.NextInt(totalWeight));
                int applied = 0;
                while (applied < perTarget && state.PlantDamage[p] < 1f)
                {
                    float before = state.PlantDamage[p];
                    float nd = math.min(1f, before + perHitLoss);
                    state.LostMW += (nd - before) * RepairForecast.PlantCap(state, p, plantMW);
                    state.PlantDamage[p] = nd;
                    applied++;
                    if (repairEnabled && state.RepairDone[p] < 0f)   // < 0f == "not under repair" (REPAIR_NONE sentinel)
                        RepairForecast.Enqueue(ref state, p); // O(1) deduped enqueue; dispatched when a slot frees
                }
            }
        }
    }
}
