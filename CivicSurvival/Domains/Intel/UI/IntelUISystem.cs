using System;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Intel;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Core.UI.B;
using UnityEntity = Unity.Entities.Entity;

namespace CivicSurvival.Domains.Intel.UI
{
    /// <summary>
    /// UI system for Intel-owned forecast, insider, and intel upgrade controls.
    /// </summary>
    [ActIndependent]
    public partial class IntelUISystem : CivicUIPanelSystem
    {
        private EntityQuery m_IntelQuery;
        private EntityQuery m_WalletQuery;
        private UnityEntity m_InsiderRequestEntity = UnityEntity.Null;
        private UnityEntity m_IntelUpgradeRequestEntity = UnityEntity.Null;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IntelQuery = GetEntityQuery(ComponentType.ReadOnly<IntelStateSingleton>());
            m_WalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());

            // Show-defaults convention: panel always renders with a neutral DTO when
            // producers are not yet available — no RequireForUpdate, no fail-loud
            // GetSingletonOrDefault (which threw [CRITICAL] producer-not-yet-run).

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(IntelState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.AddScenarioTrigger(PurchaseInsider, FeatureIds.Intel, Act.Crisis, RequestResultBridge.InsiderPurchase, OnPurchaseInsider);
            Triggers.AddScenarioTrigger(UpgradeIntel, FeatureIds.Intel, Act.Crisis, RequestResultBridge.IntelUpgrade, OnUpgradeIntel);
        }

        protected override void OnPanelUpdate()
        {
            ClearCompletedInsiderRequest();
            ClearCompletedIntelUpgradeRequest();

            var dto = new IntelDto
            {
                InsiderCost = BalanceConfig.Current.Economy.InsiderCost,
                InsiderRequestJson = RequestResultBridge.Get(RequestResultBridge.InsiderPurchase).ToJson(),
                IntelUpgradeRequestJson = RequestResultBridge.Get(RequestResultBridge.IntelUpgrade).ToJson()
            };

            FillIntelData(ref dto);

            PublishWhenComplete(IntelState, NoSourceChecks, () => dto);
        }

        private void FillIntelData(ref IntelDto dto)
        {
            var intel = m_IntelQuery.TryGetSingleton<IntelStateSingleton>(out var i)
                ? i : IntelStateSingleton.Default;

            dto.TensionLevel = intel.TensionLevel;
            dto.TensionStatus = intel.TensionStatus.ToString();
            dto.WaveTypePrediction = intel.WaveTypePrediction.ToString();
            dto.IsMassiveStrike = intel.IsMassiveStrikePredicted;

            dto.EnergyFocusRange = new FocusRangeDto(intel.EnergyFocusMin, intel.EnergyFocusMax);
            dto.InfraFocusRange = new FocusRangeDto(intel.InfraFocusMin, intel.InfraFocusMax);
            dto.ResidentialFocusRange = new FocusRangeDto(intel.ResidentialFocusMin, intel.ResidentialFocusMax);

            dto.TimeEstimate = BuildTimeEstimate(intel);
            dto.ThreatComposition = intel.ThreatComposition.ToString();
            dto.EstimatedShaheds = intel.EstimatedShaheds;
            dto.EstimatedBallistics = intel.EstimatedBallistics;
            dto.HasInsider = intel.HasInsider;

            float markup = GetSanctionsMarkup();
            dto.BaseInsiderCost = (int)Math.Clamp(intel.InsiderCost, 0L, int.MaxValue);
            dto.InsiderCost = (int)Math.Clamp(SanctionsCostHelper.ApplyMarkup(intel.InsiderCost, markup), 0L, int.MaxValue);
            FillInsiderEligibility(ref dto, intel);

            dto.TensionPriceMultiplier = intel.PriceMultiplier;
            dto.TensionPriceModifierPercent = intel.PriceModifierPercent;

            dto.IntelUpgradeLevel = intel.IntelUpgradeLevel;
            dto.IntelUpgradeCost = (int)Math.Clamp(SanctionsCostHelper.ApplyMarkup(intel.IntelUpgradeCost, markup), 0L, int.MaxValue);
            FillIntelUpgradeEligibility(ref dto, intel);
        }

        private static AttackTimeEstimateDto BuildTimeEstimate(IntelStateSingleton intel)
        {
            string status = intel.TimeEstimateStatus.ToString();
            if (status == "available" && intel.TimeEstimateMinHours >= 0f)
                return AttackTimeEstimateDto.Available(intel.TimeEstimateMinHours, intel.TimeEstimateMaxHours);
            return AttackTimeEstimateDto.WithStatus(status);
        }

        private void FillInsiderEligibility(ref IntelDto dto, IntelStateSingleton intel)
        {
            dto.CanBuyInsider = IntelEligibility.CanBuyInsider(
                intel.HasInsider,
                intel.InsiderCost,
                World,
                out var reasonId,
                out var effectiveCost);
            dto.InsiderLockedReasonId = reasonId;
            if (dto.CanBuyInsider)
                dto.InsiderCost = (int)Math.Clamp(effectiveCost, 0L, int.MaxValue);
        }

        private void FillIntelUpgradeEligibility(ref IntelDto dto, IntelStateSingleton intel)
        {
            dto.CanUpgradeIntel = IntelEligibility.CanUpgradeIntel(
                intel.IsMaxIntelUpgrade,
                intel.IntelUpgradeCost,
                World,
                out var reasonId,
                out var effectiveCost);
            dto.IntelUpgradeLockedReasonId = reasonId;
            if (dto.CanUpgradeIntel)
                dto.IntelUpgradeCost = (int)Math.Clamp(effectiveCost, 0L, int.MaxValue);
        }

        private float GetSanctionsMarkup()
            => (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var sw)
                ? sw : ShadowWalletSingleton.Default).SanctionsMarkup;

        private TriggerOutcome OnPurchaseInsider(in ScenarioGuard guard)
        {
            if (HasLiveInsiderRequest())
                return TriggerOutcome.RejectToastOnly(ReasonIds.IntelRequestPending);

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("PurchaseInsider rejected: budget pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new IntelPurchaseRequest
            {
                PurchaseType = IntelPurchaseType.Insider,
                ExpectedCost = GetDisplayedInsiderCost()
            });
            m_InsiderRequestEntity = entity;
            if (Log.IsDebugEnabled) Log.Debug("Created IntelPurchaseRequest: Insider");
            return TriggerOutcome.HandOffToEcs(EntityManager, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private TriggerOutcome OnUpgradeIntel(in ScenarioGuard guard)
        {
            if (HasLiveIntelUpgradeRequest())
                return TriggerOutcome.RejectToastOnly(ReasonIds.IntelRequestPending);

            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info("UpgradeIntel rejected: budget pipeline requires unpaused simulation");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new IntelPurchaseRequest
            {
                PurchaseType = IntelPurchaseType.Upgrade,
                ExpectedCost = GetDisplayedIntelUpgradeCost()
            });
            m_IntelUpgradeRequestEntity = entity;
            if (Log.IsDebugEnabled) Log.Debug("Created IntelPurchaseRequest: Upgrade");
            return TriggerOutcome.HandOffToEcs(EntityManager, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private bool HasLiveIntelUpgradeRequest()
        {
            if (m_IntelUpgradeRequestEntity == UnityEntity.Null) return false;
            if (EntityManager.Exists(m_IntelUpgradeRequestEntity)) return true;
            m_IntelUpgradeRequestEntity = UnityEntity.Null;
            return false;
        }

        private bool HasLiveInsiderRequest()
        {
            if (m_InsiderRequestEntity == UnityEntity.Null) return false;
            if (EntityManager.Exists(m_InsiderRequestEntity)) return true;
            m_InsiderRequestEntity = UnityEntity.Null;
            return false;
        }

        private void ClearCompletedIntelUpgradeRequest()
        {
            if (m_IntelUpgradeRequestEntity != UnityEntity.Null && !EntityManager.Exists(m_IntelUpgradeRequestEntity))
            {
                m_IntelUpgradeRequestEntity = UnityEntity.Null;
            }
        }

        private void ClearCompletedInsiderRequest()
        {
            if (m_InsiderRequestEntity != UnityEntity.Null && !EntityManager.Exists(m_InsiderRequestEntity))
            {
                m_InsiderRequestEntity = UnityEntity.Null;
            }
        }

        private long GetDisplayedInsiderCost()
        {
            long baseCost = BalanceConfig.Current.Economy.InsiderCost;
            // NO_MIGRATE: displayed fallback intentionally keeps configured base cost when Intel state is absent.
            if (m_IntelQuery.TryGetSingleton<IntelStateSingleton>(out var intel))
                baseCost = intel.InsiderCost;

            return SanctionsCostHelper.ApplyMarkup(baseCost, GetSanctionsMarkup());
        }

        private long GetDisplayedIntelUpgradeCost()
        {
            // NO_MIGRATE: panel hides upgrade cost when Intel state is absent.
            if (!m_IntelQuery.TryGetSingleton<IntelStateSingleton>(out var intel))
                return 0;

            return SanctionsCostHelper.ApplyMarkup(intel.IntelUpgradeCost, GetSanctionsMarkup());
        }
    }
}
