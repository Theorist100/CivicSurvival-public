using System;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Adapters;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.PsyImpact;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned host for <see cref="EntityCountProbe"/>. Owns ten per-World
    /// EntityQueries for the diagnostic snapshot; queries die with the world via
    /// ECS lifecycle (EntityManager owns query lifetime).
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class EntityCountProbeHost : SystemBase
    {
        [System.NonSerialized] private EntityCountProbeFacade? m_Facade;
        private EntityQuery m_ThreatsQuery;
        private EntityQuery m_DebrisQuery;
        private EntityQuery m_OnFireQuery;
        private EntityQuery m_DestroyedQuery;
        private EntityQuery m_ThreatPositionQuery;
        private EntityQuery m_PsyStateQuery;
        private EntityQuery m_SpotterQuery;
        private EntityQuery m_BackupPowerQuery;
        private EntityQuery m_EquipmentWearQuery;
        private EntityQuery m_VanillaTotalQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            var em = EntityManager;

#pragma warning disable CIVIC209 // Diagnostic queries — created once per world, ECS lifecycle owns them
#pragma warning disable CIVIC340 // Probe counts intentionally use absent-or-disabled lifecycle semantics for live threat totals.
            m_ThreatsQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<ActiveThreat>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<PendingDestruction>());
            m_DebrisQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FallingDebris>(),
                ComponentType.Exclude<Deleted>());
            m_OnFireQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<OnFire>(),
                ComponentType.Exclude<Deleted>());
            m_DestroyedQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Destroyed>(),
                ComponentType.Exclude<Deleted>());
            m_ThreatPositionQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<ThreatPosition>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<PendingDestruction>());
#pragma warning restore CIVIC340
            m_PsyStateQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HouseholdPsyState>(),
                ComponentType.Exclude<Deleted>());
            m_SpotterQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<SpotterData>(),
                ComponentType.Exclude<Deleted>());
            m_BackupPowerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BackupPower>(),
                ComponentType.Exclude<Deleted>());
            m_EquipmentWearQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<EquipmentWear>(),
                ComponentType.Exclude<Deleted>());
            m_VanillaTotalQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.Exclude<Deleted>());
#pragma warning restore CIVIC209

            if (ServiceRegistry.IsInitialized)
            {
                m_Facade = ServiceRegistry.Instance.Get<EntityCountProbeFacade>();
                if (m_Facade != null) m_Facade.CurrentHost = this;
            }
        }

        protected override void OnDestroy()
        {
            if (m_Facade != null && ReferenceEquals(m_Facade.CurrentHost, this))
                m_Facade.CurrentHost = null;
            m_Facade = null;
            base.OnDestroy();
        }

        protected override void OnUpdate() { /* host carries no per-frame work */ }

        internal EntityCountProbe.Counts Snapshot()
        {
            return new EntityCountProbe.Counts
            {
                Valid = true,
                ThreatsAlive = SafeCount(m_ThreatsQuery),
                DebrisFalling = SafeCount(m_DebrisQuery),
                VanillaOnFire = SafeCount(m_OnFireQuery),
                VanillaDestroyed = SafeCount(m_DestroyedQuery),
                TotalModEntities = SafeCount(m_ThreatPositionQuery),
                PsyStateEntities = SafeCount(m_PsyStateQuery),
                SpotterEntities = SafeCount(m_SpotterQuery),
                BackupPowerEntities = SafeCount(m_BackupPowerQuery),
                EquipmentWearEntities = SafeCount(m_EquipmentWearQuery),
                VanillaTotalEntities = SafeCount(m_VanillaTotalQuery)
            };
        }

        private static int SafeCount(EntityQuery q)
            => q.IsEmptyIgnoreFilter ? 0 : q.CalculateEntityCount();
    }
}
