using Unity.Entities;
using CivicSurvival.Core.Components.Lifecycle;

namespace CivicSurvival.Core.Components.Domain.Power
{
    /// <summary>
    /// One hourly bucket of the 24-hour rolling demand window. 24 elements live in a
    /// <see cref="DynamicBuffer{T}"/> on the <see cref="DemandPeakSingleton"/> entity (same
    /// shape as <see cref="DistrictPowerEntry"/>). Each bucket holds the maximum wanted demand
    /// (kW) observed during the game-hour it represents; the bucket the cursor entered is the
    /// oldest (24h ago) and is zeroed on hour rollover so the window is a TRUE sliding 24h max,
    /// not an exponential decay.
    /// </summary>
    [InternalBufferCapacity(DemandPeakSingleton.BUCKETS)]
    public struct DemandPeakBucket : IBufferElementData
    {
        /// <summary>Maximum wanted demand (kW) seen during this bucket's game-hour.</summary>
        public int PeakKW;
    }

    /// <summary>
    /// Marker / scalar component on the entity that carries the
    /// <see cref="DynamicBuffer{DemandPeakBucket}"/> ring. The peak demand base for the Фаза 1
    /// saturation ratio is <c>max</c> over the 24 buckets (computed on the fly by the resolver),
    /// so it is NOT stored here — a cached scalar would be a second source of truth that could
    /// drift on load.
    ///
    /// Persisted (the ring must survive save/load: a reloaded spam city with an empty ring would
    /// see a low ratio → degradation disabled → the spam immunity returns). Owned by
    /// <c>PowerCapacityResolverSystem</c> (it already reads <c>PowerGridSingleton.Demand</c> and
    /// samples the ring inside <c>ApplySaturationInertia</c> — zero new systems). Lives in Core so
    /// Фаза 7 (Waves) can read the peak without importing the Engineering domain (Axiom 5).
    /// </summary>
    public struct DemandPeakSingleton : IComponentData
    {
        /// <summary>24 hourly buckets = a 24-hour sliding window.</summary>
        public const int BUCKETS = 24;

        /// <summary>Index of the bucket the current game-hour maps to (hour mod 24).</summary>
        public int CursorHour;

        /// <summary>
        /// Game-hour timestamp of the last sample (<c>GameTimeSystem.TotalGameHours</c>, NOT
        /// <c>ElapsedTime</c>). Persisted directly in game-hours — drives both hour-advance
        /// detection (how many buckets to clear on rollover) and post-load staleness reconcile.
        /// </summary>
        public double LastSampleGameHours;

        /// <summary>
        /// Ensure the singleton entity exists carrying both the marker component and a
        /// <see cref="DynamicBuffer{DemandPeakBucket}"/> initialised to 24 zero buckets.
        /// Mirrors <c>DistrictPowerSystem.EnsureDistrictPowerShape</c>: the buffer shape is
        /// established via the <see cref="EnsureSingletonPolicy{T}.EnsureShape"/> hook.
        /// </summary>
        public static Entity EnsureExists(EntityManager em)
        {
            var policy = new EnsureSingletonPolicy<DemandPeakSingleton>
            {
                EnsureShape = EnsureRingShape
            };
            return CivicSingleton.Ensure(em, default(DemandPeakSingleton), policy);
        }

        private static void EnsureRingShape(EntityManager em, Entity e)
        {
            if (!em.HasBuffer<DemandPeakBucket>(e))
            {
                var buffer = em.AddBuffer<DemandPeakBucket>(e);
                for (int i = 0; i < BUCKETS; i++)
                    buffer.Add(new DemandPeakBucket { PeakKW = 0 });
                return;
            }

            // Buffer present but wrong length (legacy save / partial create) → normalise to 24.
            var existing = em.GetBuffer<DemandPeakBucket>(e);
            if (existing.Length == BUCKETS)
                return;
            existing.Clear();
            for (int i = 0; i < BUCKETS; i++)
                existing.Add(new DemandPeakBucket { PeakKW = 0 });
        }
    }
}
