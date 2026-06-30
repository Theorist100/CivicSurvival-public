using System;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Cognitive;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Domains.Cognitive.UI
{
    /// <summary>
    /// UI command owner for the Buckwheat Protocol.
    /// The visible controls live under the Cognitive feature, so their triggers
    /// are registered by the Cognitive UI module rather than Corruption.
    /// </summary>
    [ActIndependent]
    public partial class BuckwheatUISystem : CivicUIPanelSystem
    {
        private const int PROCUREMENT_LEVEL_25 = 25;
        private const int PROCUREMENT_LEVEL_50 = 50;
        private const int PROCUREMENT_LEVEL_75 = 75;
        private const int PROCUREMENT_LEVEL_100 = 100;

        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_BuckwheatQuery;
        private EntityQuery m_BuckwheatConfigQuery;
        private EntityQuery m_WalletQuery;
        private IBuckwheatAidReader m_BuckwheatAidReader = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_BuckwheatQuery = GetEntityQuery(ComponentType.ReadOnly<BuckwheatSingleton>());
            m_BuckwheatConfigQuery = GetEntityQuery(ComponentType.ReadOnly<BuckwheatConfig>());
            m_WalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_BuckwheatAidReader = ServiceRegistry.Instance.Require<IBuckwheatAidReader>();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(BuckwheatState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(SetProcurementLevel, FeatureIds.Cognitive, RequestResultBridge.ProcurementLevel, OnSetProcurementLevel);
            Triggers.Add<int>(DistributeAid, FeatureIds.Cognitive, RequestResultBridge.DistributeAid, OnDistributeAid);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new BuckwheatDto();

            if (m_BuckwheatQuery.TryGetSingleton<BuckwheatSingleton>(out var singleton))
            {
                dto.BuckwheatTons = singleton.BuckwheatTons;
                dto.CanDistribute = singleton.CanDistribute;
            }

            // City-wide relief: index 0 = whole-city / no-district aggregate.
            dto.CanDistribute = m_BuckwheatAidReader.CanDistributeToDistrict(0, out var distributeReason);
            dto.DistributeLockedReasonId = distributeReason;

            if (m_BuckwheatConfigQuery.TryGetSingleton<BuckwheatConfig>(out var config))
            {
                dto.ProcurementLevel = config.ProcurementLevel;
                float markup = 0f;
                if (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var walletForCost))
                    markup = walletForCost.SanctionsMarkup;
                dto.BaseDailyCost = BuckwheatSingleton.DailyCost(config.ProcurementLevel);
                dto.DailyCost = BuckwheatSingleton.DailyCostWithMarkup(config.ProcurementLevel, markup);
            }

            FillProcurementEligibility(ref dto);
            dto.LastDistributeResultJson = RequestResultBridge.Get(RequestResultBridge.DistributeAid).ToJson();
            dto.ProcurementLevelRequestJson = RequestResultBridge.Get(RequestResultBridge.ProcurementLevel).ToJson();

            PublishWhenComplete(BuckwheatState, NoSourceChecks, () => dto);
        }

        private TriggerOutcome OnSetProcurementLevel(int percent)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Buckwheat procurement rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new BuckwheatProcurementLevelRequest
            {
                Percent = percent
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created Buckwheat procurement request: {percent}%");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnDistributeAid(int districtIndex)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("Buckwheat aid distribution rejected: request pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new AidDistributionRequest
            {
                DistrictIndex = districtIndex
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created Buckwheat distribution request: district {districtIndex}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private void FillProcurementEligibility(ref BuckwheatDto dto)
        {
            dto.CanSetProcurement25 = CanSetProcurementLevel(PROCUREMENT_LEVEL_25, dto.ProcurementLevel, out var reason25);
            dto.Procurement25LockedReasonId = reason25;
            dto.CanSetProcurement50 = CanSetProcurementLevel(PROCUREMENT_LEVEL_50, dto.ProcurementLevel, out var reason50);
            dto.Procurement50LockedReasonId = reason50;
            dto.CanSetProcurement75 = CanSetProcurementLevel(PROCUREMENT_LEVEL_75, dto.ProcurementLevel, out var reason75);
            dto.Procurement75LockedReasonId = reason75;
            dto.CanSetProcurement100 = CanSetProcurementLevel(PROCUREMENT_LEVEL_100, dto.ProcurementLevel, out var reason100);
            dto.Procurement100LockedReasonId = reason100;

            dto.CanAffordProcurement = BuckwheatEligibility.CanAffordProcurement(
                dto.ProcurementLevel,
                intervalsDue: 2,
                World,
                out var affordReason,
                out _);
            dto.AffordProcurementLockedReasonId = affordReason;
        }

        private bool CanSetProcurementLevel(int targetLevel, int currentLevel, out string reasonId)
        {
            return BuckwheatEligibility.CanSelectProcurementLevel(targetLevel, currentLevel, World, out reasonId);
        }
    }
}
