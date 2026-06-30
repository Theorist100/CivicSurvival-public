using Game;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Colossal.Logging;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.PowerGrid.Systems
{
    /// <summary>
    /// Aggregates all ExternalPowerSource components into a single ExternalPowerInput.
    /// This keeps PowerGridDataSystem unaware of who provides the bonus.
    /// </summary>
    [ActIndependent]
    public partial class ExternalPowerAggregationSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("ExternalPowerAggregation");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        private EntityQuery m_InputQuery;
        private EntityQuery m_SourceQuery;
        [System.NonSerialized]
        private CivicSingletonHandle<ExternalPowerInput> m_Input;
        private ComponentLookup<ExternalPowerInput> m_InputLookup;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_InputQuery = GetEntityQuery(ComponentType.ReadWrite<ExternalPowerInput>());
            m_SourceQuery = GetEntityQuery(
                ComponentType.ReadOnly<ExternalPowerSource>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );
            m_Input = CreateSingletonHandle<ExternalPowerInput>(m_InputQuery);
            m_InputLookup = GetComponentLookup<ExternalPowerInput>(false);
            EnsureInputEntity();
        }

        protected override void OnDestroy()
        {
            var inputEntity = m_Input.Entity;
            if (inputEntity != Entity.Null && EntityManager.Exists(inputEntity))
            {
                EntityManager.DestroyEntity(inputEntity);
                m_Input.Invalidate();
            }
            base.OnDestroy();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureInputEntity();
        }

        protected override void OnThrottledUpdate()
        {
            m_InputLookup.Update(this);
            if (!TryGetInputEntity(out var inputEntity))
                return;
            if (inputEntity == Entity.Null || !m_InputLookup.HasComponent(inputEntity))
                return;

            long totalBonus = 0;
            foreach (var source in SystemAPI.Query<RefRO<ExternalPowerSource>>().WithNone<Temp, Deleted, Destroyed>())
            {
                totalBonus += source.ValueRO.BonusMW;
            }

            m_InputLookup[inputEntity] = new ExternalPowerInput { BonusMW = ClampLongToInt(totalBonus) };
        }

        private static int ClampLongToInt(long value)
        {
            if (value > int.MaxValue)
                return int.MaxValue;
            if (value < int.MinValue)
                return int.MinValue;
            return checked((int)value);
        }

        private void EnsureInputEntity()
        {
            EnsureSingleton(ref m_Input, new ExternalPowerInput { BonusMW = 0 });
        }

        /// <summary>
        /// Synchronously aggregate restored ExternalPowerSource components into
        /// ExternalPowerInput.
        /// ORDER-INVARIANT: ExternalPowerSource is a mod-owned donor singleton.
        /// DonorConferenceSystem.OnLoadRestore restores BonusMW during PLVS
        /// RestoreSingletonOwners, and PowerGridDataSystem calls this seed during
        /// RunValidation, which is guaranteed to run after that owner phase.
        /// </summary>
        public void SeedFromRestoredSources(EntityManager em)
        {
            EnsureSingleton(ref m_Input, em, new ExternalPowerInput { BonusMW = 0 });
            m_InputLookup.Update(this);

            var inputEntity = m_Input.Entity;
            if (inputEntity == Entity.Null || !m_InputLookup.HasComponent(inputEntity))
            {
                if (!m_InputQuery.TryGetSingletonEntity<ExternalPowerInput>(out inputEntity))
                {
                    Log.Warn("ExternalPowerAggregationSystem.Seed: input singleton missing — abort");
                    return;
                }
            }

            long totalBonus = 0;
            using (var sources = m_SourceQuery.ToComponentDataArray<ExternalPowerSource>(Allocator.Temp))
            {
                for (int i = 0; i < sources.Length; i++)
                    totalBonus += sources[i].BonusMW;
            }

            int clamped = ClampLongToInt(totalBonus);
            m_InputLookup[inputEntity] = new ExternalPowerInput { BonusMW = clamped };
        }

        private bool TryGetInputEntity(out Entity entity)
        {
            entity = m_Input.Entity;
            if (entity != Entity.Null && m_InputLookup.HasComponent(entity))
            {
                return true;
            }

            return m_InputQuery.TryGetSingletonEntity<ExternalPowerInput>(out entity);
        }
    }
}
