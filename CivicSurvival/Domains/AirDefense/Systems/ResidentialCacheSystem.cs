using Game.Buildings;
using Game.Common;
using Game.Objects;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Owns the residential position cache used by AirDefenseOrchestrator's ResidentialCheckJob.
    /// Refreshes on building count change (every 2s check) or timer expiry (every 30s).
    /// Runs before AirDefenseOrchestrator each frame so the array is always ready for scheduling.
    /// </summary>
    [ActIndependent]
    public partial class ResidentialCacheSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ResidentialCacheSystem");

        private EntityQuery m_ResidentialQuery;

#pragma warning disable CIVIC278 // NativeArray has no Clear(); guarded by IsReady and count-based refresh
#pragma warning disable CIVIC150 // Derived cache — rebuilt from ECS query each session, not serialized by design
        private NativeArray<float3> m_Positions;
#pragma warning restore CIVIC150
#pragma warning restore CIVIC278

        private int m_CachedCount;
        private bool m_HasBuiltCache;
#pragma warning disable CIVIC150 // Timer fields — not serialized by design (cache is ephemeral)
        private float m_CacheTimer;
        private float m_CountCheckTimer;
#pragma warning restore CIVIC150
        private bool m_NeedsRefresh = true; // true on first frame — forces initial populate

        private const float CACHE_REFRESH_SECONDS = 30f;
        private const float COUNT_CHECK_SECONDS = 2f;

        /// <summary>Residential building positions, ready for use in Burst jobs. Valid only when IsReady.</summary>
        public NativeArray<float3> ResidentialPositions => m_Positions;

        /// <summary>True once the first cache population has completed.</summary>
        public bool IsReady => m_HasBuiltCache;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResidentialQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<ResidentialProperty>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );
        }

        protected override void OnDestroy()
        {
            if (m_Positions.IsCreated) m_Positions.Dispose();
            base.OnDestroy();
        }

        [CompletesDependency("OnUpdateImpl: CalculateEntityCount throttled by COUNT_CHECK_SECONDS timer for cache-invalidation change detection; no sync on the non-tick path")]
        protected override void OnUpdateImpl()
        {
#pragma warning disable CIVIC056 // Timers reset in RefreshIfPending() and ResetState() — not unbounded
            m_CacheTimer += SystemAPI.Time.DeltaTime;
            m_CountCheckTimer += SystemAPI.Time.DeltaTime;
#pragma warning restore CIVIC056

            if (m_CountCheckTimer >= COUNT_CHECK_SECONDS)
            {
                m_CountCheckTimer = 0f;
                if (m_ResidentialQuery.CalculateEntityCount() != m_CachedCount)
                    m_NeedsRefresh = true;
            }

            if (m_CacheTimer >= CACHE_REFRESH_SECONDS)
                m_NeedsRefresh = true;
        }

        /// <summary>
        /// Called by AirDefenseOrchestrator inside the safe window (after CompleteAndSwap, before new job scheduling).
        /// Refreshes the cache only when the previous frame's jobs have completed and no job is reading m_Positions.
        /// </summary>
        [CompletesDependency("RefreshIfPending: called inside AirDefenseOrchestrator's safe window (after CompleteAndSwap, before new job scheduling). ToComponentDataArray reads Transform synchronously, then stores only float3 positions for the next-frame Burst job; cache invalidation gated by RefreshIfPending check")]
        public void RefreshIfPending()
        {
            if (!m_NeedsRefresh && m_HasBuiltCache) return;
            m_NeedsRefresh = false;
            m_CacheTimer = 0f;

            if (m_Positions.IsCreated) m_Positions.Dispose();
            m_HasBuiltCache = false;

            var transforms = m_ResidentialQuery.ToComponentDataArray<Game.Objects.Transform>(Allocator.TempJob);
            try
            {
                // Build into a local, then publish to m_Positions in one assignment — the
                // freshly allocated array is never observed through the just-disposed field.
                var positions = new NativeArray<float3>(transforms.Length, Allocator.Persistent);
                for (int i = 0; i < transforms.Length; i++)
                    positions[i] = transforms[i].m_Position;

                m_Positions = positions;
                m_CachedCount = positions.Length;
                m_HasBuiltCache = true;
            }
            finally
            {
                transforms.Dispose();
            }

            if (Log.IsDebugEnabled) Log.Debug($"Cached {m_CachedCount} residential positions");
        }

        /// <summary>
        /// IResettable contract — drives the same teardown the engine triggers
        /// from this system's own SetDefaults / Deserialize. The NativeArray is
        /// rebuilt on the next OnUpdateImpl call.
        /// </summary>
        public void ResetState()
        {
            ResetTransientRuntimeState();
            Log.Info("Cache reset");
        }

        private void ResetTransientRuntimeState()
        {
            if (m_Positions.IsCreated) m_Positions.Dispose();
            m_CachedCount = 0;
            m_HasBuiltCache = false;
            m_CacheTimer = 0f;
            m_CountCheckTimer = 0f;
            m_NeedsRefresh = true;
        }
    }
}
