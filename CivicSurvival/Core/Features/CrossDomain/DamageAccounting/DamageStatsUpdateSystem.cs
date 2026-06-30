using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Features.CrossDomain.DamageAccounting
{
    /// <summary>
    /// Updates ThreatDamageStatsSingleton from vanilla entity queries.
    /// Counts Building+Destroyed and Building+OnFire entities.
    ///
    /// Extracted from ThreatDamageSystem to Core so readers (CityStabilitySystem)
    /// can use RegisterAfter] without cross-domain imports.
    ///
    /// Throttled at 500ms — matches CityStabilitySystem read interval.
    /// NOTE: No ISerializable — stats are derived, not persisted.
    /// </summary>
    [SingletonOwner(typeof(ThreatDamageStatsSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = false,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    [ActIndependent]
    public partial class DamageStatsUpdateSystem : ThrottledSystemBase, ICivicSingletonOwner<ThreatDamageStatsSingleton>
    {
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        // Shared phase key with CityStabilitySystem — ensures RegisterAfter] ordering
        // is effective (both systems fire on the same throttled tick).
        internal const string PHASE_KEY = "DamageStats";
        protected override string ThrottlePhaseKey => PHASE_KEY;

        private EntityQuery m_StatsQuery;
        private EntityQuery m_DestroyedBuildingsQuery;
        private EntityQuery m_OnFireBuildingsQuery;
        private CivicSingletonHandle<ThreatDamageStatsSingleton> m_StatsSingleton;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_StatsQuery = GetEntityQuery(
                ComponentType.ReadWrite<ThreatDamageStatsSingleton>()
            );
            m_StatsSingleton = CreateSingletonHandle<ThreatDamageStatsSingleton>(m_StatsQuery);
            EnsureStatsSingleton(EntityManager);

            m_DestroyedBuildingsQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Destroyed>(),
                ComponentType.Exclude<Deleted>()
            );

            m_OnFireBuildingsQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<OnFire>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            EnsureStatsSingleton(EntityManager);
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            EnsureStatsSingleton(entityManager);
        }

        [CompletesDependency("OnThrottledUpdate stats publish: BuildingsDestroyed/OnFire counts are read once per throttle tick to drive the damage-stats singleton; CalculateEntityCount reads chunk metadata, accepted small sync amortised over throttle interval")]
        protected override void OnThrottledUpdate()
        {
            if (!m_StatsQuery.TryGetSingletonRW<ThreatDamageStatsSingleton>(out var singleton))
                return;

            singleton.ValueRW.BuildingsDestroyed = m_DestroyedBuildingsQuery.CalculateEntityCount();
            singleton.ValueRW.BuildingsOnFire = m_OnFireBuildingsQuery.CalculateEntityCount();
        }

        protected override void OnDestroy()
        {
            if (m_StatsSingleton.IsCreated && !m_StatsSingleton.Query.IsEmpty)
                EntityManager.DestroyEntity(m_StatsSingleton.Query);
            m_StatsSingleton.Invalidate();

            base.OnDestroy();
        }

        private void EnsureStatsSingleton(EntityManager entityManager)
        {
            EnsureSingleton(ref m_StatsSingleton, entityManager, ThreatDamageStatsSingleton.Default);
        }
    }
}
