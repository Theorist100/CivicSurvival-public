using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Waves.Logic
{
    /// <summary>
    /// Target selection logic for threat spawning. Reads cached building lists
    /// via <see cref="IThreatTargetSource"/>; never touches Transform writers
    /// directly. Phase 5 pattern: this class executes zero ECS queries — the
    /// producer system rebuilds the cache off the per-tick path.
    /// </summary>
    public class ThreatTargetSelector
    {
        private static readonly LogContext Log = new("ThreatTargetSelector");

        // PERF: Static fallback chains to avoid per-call heap allocation
        private static readonly TargetCategory[] s_EnergyFallback = { TargetCategory.Energy, TargetCategory.Critical, TargetCategory.Service, TargetCategory.Civilian };
        private static readonly TargetCategory[] s_CriticalFallback = { TargetCategory.Critical, TargetCategory.Energy, TargetCategory.Service, TargetCategory.Civilian };
        private static readonly TargetCategory[] s_ServiceFallback = { TargetCategory.Service, TargetCategory.Energy, TargetCategory.Critical, TargetCategory.Civilian };
        private static readonly TargetCategory[] s_CivilianFallback = { TargetCategory.Civilian, TargetCategory.Energy, TargetCategory.Critical, TargetCategory.Service };

        private SerializableRandom m_Random;

        // Reused weight scratch for the Core cumulative-walk pick: avoids a per-call heap
        // allocation while letting the selection rule live once in DamageTargetingMath. This
        // class is a managed consumer (never a Burst job), so a managed field is fine; it grows
        // only when a larger candidate set than ever seen appears (city target lists, not bounded
        // by MAX_PLANTS), then is reused. Capacity reused; only the first candidates.Length entries
        // are valid on any call.
        private int[] m_WeightScratch = System.Array.Empty<int>();

        /// <summary>
        /// Cache source. Settable so the owning system can lazy-resolve via
        /// <c>ServiceRegistry</c> after construction — Phase 5 producer/consumer
        /// systems may register out of order.
        /// </summary>
        public IThreatTargetSource? Source { get; set; }

        public ThreatTargetSelector(SerializableRandom random, IThreatTargetSource? source)
        {
            m_Random = random;
            Source = source;
        }

        /// <summary>
        /// Get random state (call after each use to propagate changes back to caller).
        /// </summary>
        public SerializableRandom GetRandom() => m_Random;

        /// <summary>
        /// Set random state (call before use to sync state from caller).
        /// </summary>
        public void SetRandom(SerializableRandom random) => m_Random = random;

        /// <summary>
        /// Find target using fallback chain with saturation check.
        /// Chain: PreferredCategory -> Energy -> Critical -> Service -> Residential -> (null)
        /// </summary>
        public (Entity entity, float3 position, TargetCategory category) FindTargetWithFallback(
            TargetCategory preferred,
            NativeHashMap<Entity, int> hitCount,
            int maxThreatsPerTarget,
            bool concentrate = false)
        {
            // PERF: Use static arrays to avoid per-call heap allocation
            TargetCategory[] fallbackChain = preferred switch
            {
                TargetCategory.Energy => s_EnergyFallback,
                TargetCategory.Critical => s_CriticalFallback,
                TargetCategory.Service => s_ServiceFallback,
                TargetCategory.Civilian => s_CivilianFallback,
                _ => s_EnergyFallback
            };

            foreach (var category in fallbackChain)
            {
                var targets = GetTargetsForCategory(category);

                if (!targets.IsCreated || targets.Length == 0)
                {
                    if (targets.IsCreated) targets.Dispose();
                    continue;
                }

                var target = SelectUnsaturatedTarget(targets, hitCount, maxThreatsPerTarget, concentrate);
                targets.Dispose();

                if (target.Entity != Entity.Null)
                {
                    return (target.Entity, target.Position, category);
                }
            }

            // No targets found in any category
            return (Entity.Null, float3.zero, TargetCategory.Civilian);
        }

        /// <summary>
        /// Select a target that hasn't been over-saturated.
        /// In spread mode returns a random target below MAX_THREATS_PER_TARGET — drones
        /// scatter into isolated fires. In concentrate mode returns the most-hit target
        /// still below the cap, so consecutive drones pile onto the same building until it
        /// saturates and only then move on — clusters accumulate to a real demolition.
        /// If all saturated, returns a random target to spread the overflow evenly.
        /// All random picks are weighted by <see cref="TargetData.WeightMW"/> (residual
        /// plant nameplate vs the flat non-plant weight), so a wave's energy strikes land
        /// on the stations that actually carry the grid instead of 105 MW wind turbines.
        /// </summary>
        public TargetData SelectUnsaturatedTarget(NativeList<TargetData> targets, NativeHashMap<Entity, int> hitCount, int maxThreatsPerTarget, bool concentrate = false)
        {
            if (targets.Length == 0)
                return new TargetData { Entity = Entity.Null };

            int maxHits = maxThreatsPerTarget;

            // First pass: collect unsaturated targets
            var unsaturated = new NativeList<int>(Allocator.Temp);

            try
            {
                int focusIdx = -1;
                int focusHits = -1;
                for (int i = 0; i < targets.Length; i++)
                {
                    hitCount.TryGetValue(targets[i].Entity, out int hits);

                    if (hits < maxHits)
                    {
                        unsaturated.Add(i);
                        // Track the closest-to-saturation candidate for concentrate mode.
                        if (hits > focusHits)
                        {
                            focusHits = hits;
                            focusIdx = i;
                        }
                    }
                }

                if (unsaturated.Length > 0)
                {
                    // Concentrate: finish the most-progressed cluster before opening a new one.
                    if (concentrate && focusHits >= 1)
                        return targets[focusIdx];

                    // Concentrate-mode cluster seeding: prefer a high-value target (a power
                    // plant inside the Energy pool) so a small wave's cluster demolishes a
                    // generator — a deterministic grid deficit — rather than a transformer
                    // whose loss may reroute around a redundant grid path. Random among the
                    // high-value candidates so the same generator doesn't die every wave.
                    if (concentrate)
                    {
                        var highValue = new NativeList<int>(Allocator.Temp);
                        try
                        {
                            for (int u = 0; u < unsaturated.Length; u++)
                            {
                                if (targets[unsaturated[u]].IsHighValue)
                                    highValue.Add(unsaturated[u]);
                            }
                            if (highValue.Length > 0)
                                return targets[SelectWeightedIndex(targets, highValue)];
                        }
                        finally
                        {
                            if (highValue.IsCreated) highValue.Dispose();
                        }
                    }

                    // Spread (and concentrate seeding with no high-value candidate): pick a
                    // weighted-random unsaturated target so the same buildings don't die
                    // every wave but big stations stay proportionally more exposed.
                    int idx = SelectWeightedIndex(targets, unsaturated);
                    return targets[idx];
                }
                else
                {
                    // All saturated - pick weighted-RANDOM target to spread the overflow
                    // BUG FIX: Was returning same minHitIndex for all overflow, causing 264 drones to hit one building
                    var allIndices = new NativeList<int>(targets.Length, Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < targets.Length; i++)
                            allIndices.Add(i);
                        int randomIdx = SelectWeightedIndex(targets, allIndices);
                        if (Log.IsDebugEnabled) Log.Debug($" All {targets.Length} targets saturated (maxHits={maxHits}), picking random idx={randomIdx}");
                        return targets[randomIdx];
                    }
                    finally
                    {
                        if (allIndices.IsCreated) allIndices.Dispose();
                    }
                }
            }
            finally
            {
                if (unsaturated.IsCreated) unsaturated.Dispose();
            }
        }

        /// <summary>
        /// Weighted random pick among <paramref name="candidates"/> (indices into
        /// <paramref name="targets"/>), proportional to <see cref="TargetData.WeightMW"/>.
        /// Integer weights keep <see cref="SerializableRandom"/> usage identical to the
        /// previous uniform pick (one Next(int,int) call — no serialization change).
        /// Falls back to a uniform pick when the accumulated weight is not positive.
        /// The cumulative-walk that maps the roll to an index lives in
        /// <see cref="DamageTargetingMath.WeightedPick"/> — the single home shared with the
        /// severity-forecast plant pick (TD-1). This method owns only the candidate→weight
        /// gather and the RNG draw; the selection arithmetic is Core's.
        /// </summary>
        private int SelectWeightedIndex(NativeList<TargetData> targets, NativeList<int> candidates)
        {
            // Defensive guard — every caller checks its candidate list first, and
            // SelectUnsaturatedTarget early-returns when targets is empty, so index 0
            // is always a valid targets index in any reachable scenario.
            if (candidates.Length == 0)
                return 0;

            // Gather the non-negative weights into the reused scratch (grows only on a
            // bigger-than-ever candidate set), preserving the previous max(WeightMW, 0) clamp.
            if (m_WeightScratch.Length < candidates.Length)
                m_WeightScratch = new int[candidates.Length];
            int totalWeight = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                int w = math.max(targets[candidates[i]].WeightMW, 0);
                m_WeightScratch[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0)
                return candidates[m_Random.Next(0, candidates.Length)];

            int roll = m_Random.Next(0, totalWeight);
            int pick = DamageTargetingMath.WeightedPick(
                new System.ReadOnlySpan<int>(m_WeightScratch, 0, candidates.Length), totalWeight, roll);
            return candidates[pick];
        }

        /// <summary>
        /// Get all targets for a specific category. Allocates a fresh NativeList
        /// (Allocator.Temp) populated from the cached read-only view; caller
        /// disposes. The cache itself never touches Transform writers from the
        /// caller's tick — the cost was paid in <see cref="ThreatTargetCacheSystem"/>'s
        /// throttled refresh.
        /// </summary>
        public NativeList<TargetData> GetTargetsForCategory(TargetCategory category)
        {
            if (Source == null || !Source.IsReady)
                return default;

            NativeArray<TargetData>.ReadOnly view = category switch
            {
                TargetCategory.Energy => Source.Energy,
                TargetCategory.Critical => Source.Critical,
                TargetCategory.Service => Source.Service,
                TargetCategory.Civilian => Source.Civilian,
                _ => default
            };

            // Skip allocation for empty categories — avoids wasted Allocator.Temp per drone
            if (view.Length == 0)
                return default;

            return CopyToTempList(view);
        }

        /// <summary>
        /// Get strategic targets for ballistic missiles (no residential).
        /// Allocates a fresh NativeList (Allocator.Temp); caller disposes.
        /// </summary>
        public NativeList<TargetData> GetStrategicTargets()
        {
            if (Source == null || !Source.IsReady)
                return new NativeList<TargetData>(Allocator.Temp);

            int total = Source.Energy.Length + Source.Critical.Length + Source.Service.Length;
            var targets = new NativeList<TargetData>(total, Allocator.Temp);

            AppendView(targets, Source.Energy);
            AppendView(targets, Source.Critical);
            AppendView(targets, Source.Service);
            // NO Civilian — ballistics don't waste on civilian targets

            return targets;
        }

        private static NativeList<TargetData> CopyToTempList(NativeArray<TargetData>.ReadOnly view)
        {
            var list = new NativeList<TargetData>(view.Length, Allocator.Temp);
            for (int i = 0; i < view.Length; i++)
                list.Add(view[i]);
            return list;
        }

        private static void AppendView(NativeList<TargetData> dest, NativeArray<TargetData>.ReadOnly view)
        {
            for (int i = 0; i < view.Length; i++)
                dest.Add(view[i]);
        }
    }
}
