using Game;
using Unity.Entities;
using CivicSurvival.Core.Features.Efficiency;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Writes fuel siphoning modifier into the generator efficiency buffer.
    /// Reads through FuelSiphoningSystem so pending deserialize mirrors are honored before OnLoadRestore.
    /// Throttled on same phase as Clear+Aggregate to prevent buffer accumulation.
    /// </summary>
    [ActIndependent]
    public partial class FuelSiphoningEfficiencyWriterSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("FuelSiphoningEfficiency");

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;
        protected override string ThrottlePhaseKey => nameof(GeneratorEfficiencyClearSystem);

        private EntityQuery m_EfficiencyQuery;
        private IFuelSiphoningStateReader? m_FuelSiphoningReader;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EfficiencyQuery = GetEntityQuery(
                ComponentType.ReadWrite<GeneratorEfficiency>(),
                ComponentType.ReadWrite<GeneratorEfficiencySource>()
            );
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_FuelSiphoningReader ??= ServiceRegistry.Instance.Require<IFuelSiphoningStateReader>();
        }

        protected override void OnThrottledUpdate()
        {
            if (m_EfficiencyQuery.IsEmptyIgnoreFilter)
                return;

            // Use TryGetSingletonEntity to avoid exception if multiple entities exist
            if (!m_EfficiencyQuery.TryGetSingletonEntity<GeneratorEfficiency>(out var entity))
            {
                Log.Warn("Multiple or no GeneratorEfficiency entities found");
                return;
            }

            if (!EntityManager.HasBuffer<GeneratorEfficiencySource>(entity)) return;
            var buffer = SystemAPI.GetBuffer<GeneratorEfficiencySource>(entity);
            var sourceId = new Unity.Collections.FixedString32Bytes("Corruption.FuelSiphoning");

            if (m_FuelSiphoningReader == null)
                return;

            if (m_FuelSiphoningReader.SiphonPercent <= 0)
                return;

            buffer.Add(new GeneratorEfficiencySource
            {
                SourceId = sourceId,
                Multiplier = m_FuelSiphoningReader.ConsumptionMultiplier
            });
        }
    }
}
