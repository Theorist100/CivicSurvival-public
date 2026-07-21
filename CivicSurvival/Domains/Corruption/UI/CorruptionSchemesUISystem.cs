using System;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Base;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

using CivicSurvival.Core.Services;
namespace CivicSurvival.Domains.Corruption.UI
{
    /// <summary>
    /// UI system for Corruption Schemes.
    /// Uses ECS-pure singleton pattern (no cross-domain service dependencies).
    ///
    /// Migrated from CorruptionSchemesUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    public partial class CorruptionSchemesUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_FuelSiphoningQuery;
        private EntityQuery m_DraftExemptionQuery;
        private EntityQuery m_EmergencyFundQuery;
        private EntityQuery m_EmergencyFundSettingsQuery;
        private EntityQuery m_ShadowWalletQuery;
        private EntityQuery m_ConstructionKickbackQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_FuelSiphoningQuery = GetEntityQuery(ComponentType.ReadOnly<FuelSiphoningSingleton>());
            m_DraftExemptionQuery = GetEntityQuery(ComponentType.ReadOnly<DraftExemptionSingleton>());
            m_EmergencyFundQuery = GetEntityQuery(ComponentType.ReadOnly<EmergencyFundSingleton>());
            m_EmergencyFundSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<EmergencyFundSettings>());
            m_ShadowWalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());
            m_ConstructionKickbackQuery = GetEntityQuery(ComponentType.ReadOnly<ConstructionKickbackSingleton>());

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(SchemesState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(SetEmergencyFundWithdraw, FeatureIds.Corruption, RequestResultBridge.CorruptionScheme, OnSetEmergencyFundWithdraw);
            Triggers.Add<int>(SetFuelSiphonPercent, FeatureIds.Corruption, RequestResultBridge.CorruptionScheme, OnSetFuelSiphonPercent);
            Triggers.Add<int>(SetDraftDeferralPercent, FeatureIds.Corruption, RequestResultBridge.CorruptionScheme, OnSetDraftDeferralPercent);
            Triggers.Add<int>(SetConstructionKickbackPercent, FeatureIds.Corruption, RequestResultBridge.CorruptionScheme, OnSetConstructionKickbackPercent);
        }

        protected override void OnPanelUpdate()
        {
            var emergencyFundGate = ActionGate.Resolve(ActionKey.EmergencyFundPreset, BuildShadowActionContext());
            var fuelSiphonGate = ActionGate.Resolve(ActionKey.FuelSiphonPreset, BuildShadowActionContext());
            var draftDeferralGate = ActionGate.Resolve(ActionKey.DraftDeferralPreset, BuildShadowActionContext());
            var constructionKickbackGate = ActionGate.Resolve(ActionKey.ConstructionKickbackPreset, BuildShadowActionContext());
            var dto = new SchemesDto
            {
                EmergencyFundBalance = BalanceConfig.Current.EmergencyFund.InitialBalance,
                EmergencyFundAvailability = emergencyFundGate,
                FuelSiphonAvailability = fuelSiphonGate,
                DraftDeferralAvailability = draftDeferralGate,
                ConstructionKickbackAvailability = constructionKickbackGate,
                CorruptionSchemeRequestJson = RequestResultBridge.Get(RequestResultBridge.CorruptionScheme).ToJson()
            };

            if (m_EmergencyFundQuery.TryGetSingleton<EmergencyFundSingleton>(out var efSingleton))
            {
                dto.EmergencyFundBalance = efSingleton.CurrentBalance;
            }
            if (m_EmergencyFundSettingsQuery.TryGetSingleton<EmergencyFundSettings>(out var efConfig))
            {
                dto.EmergencyFundWithdraw = efConfig.WithdrawPercent;
            }

            if (m_FuelSiphoningQuery.TryGetSingleton<FuelSiphoningSingleton>(out var fsSingleton))
            {
                dto.FuelSiphonPercent = fsSingleton.SiphonPercent;
            }

            if (m_DraftExemptionQuery.TryGetSingleton<DraftExemptionSingleton>(out var deSingleton))
            {
                dto.DraftDeferralPercent = deSingleton.RatePercent;
                dto.DraftDisabilityHeadcount = deSingleton.DisabilityHeadcount;
            }

            if (m_ConstructionKickbackQuery.TryGetSingleton<ConstructionKickbackSingleton>(out var ckSingleton))
            {
                dto.ConstructionKickbackPercent = ckSingleton.KickbackPercent;
                dto.ConstructionKickbackPending = ckSingleton.PendingPayout;
            }

            PublishWhenComplete(SchemesState, NoSourceChecks, () => dto);
        }

        private TriggerOutcome OnSetEmergencyFundWithdraw(int percent)
        {
            return CreateCorruptionSchemeRequest(CorruptionSchemeType.EmergencyFundWithdraw, percent);
        }

        private TriggerOutcome OnSetFuelSiphonPercent(int percent)
        {
            return CreateCorruptionSchemeRequest(CorruptionSchemeType.FuelSiphon, percent);
        }

        private TriggerOutcome OnSetDraftDeferralPercent(int percent)
        {
            return CreateCorruptionSchemeRequest(CorruptionSchemeType.DraftDeferral, percent);
        }

        private TriggerOutcome OnSetConstructionKickbackPercent(int percent)
        {
            return CreateCorruptionSchemeRequest(CorruptionSchemeType.ConstructionKickback, percent);
        }

        private TriggerOutcome CreateCorruptionSchemeRequest(CorruptionSchemeType schemeType, int percent)
        {
            // No pause reject here, by design — the consumer (CorruptionSchemeRequestSystem)
            // runs in ModificationEnd, which ticks while paused (Axiom 14).
            if (percent > 0)
            {
                var key = schemeType switch
                {
                    CorruptionSchemeType.None => throw new ArgumentOutOfRangeException(
                        nameof(schemeType),
                        schemeType,
                        "CorruptionSchemeType.None is not executable"),
                    CorruptionSchemeType.FuelSiphon => ActionKey.FuelSiphonPreset,
                    CorruptionSchemeType.DraftDeferral => ActionKey.DraftDeferralPreset,
                    CorruptionSchemeType.EmergencyFundWithdraw => ActionKey.EmergencyFundPreset,
                    CorruptionSchemeType.ConstructionKickback => ActionKey.ConstructionKickbackPreset,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(schemeType),
                        schemeType,
                        "Unknown corruption scheme type — extend the ActionKey mapping when adding new enum values")
                };
                var gate = ActionGate.Resolve(key, BuildShadowActionContext());
                if (!gate.CanRun)
                {
                    return string.IsNullOrEmpty(gate.LockedReasonId)
                        ? TriggerOutcome.Reject(ReasonIds.MarketWalletUnavailable)
                        : TriggerOutcome.RejectRuntime(gate.LockedReasonId);
                }
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new CorruptionSchemeRequest
            {
                SchemeType = schemeType,
                Percent = percent
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created CorruptionSchemeRequest: {schemeType} = {percent}%");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }

        private ActionContext BuildShadowActionContext()
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            var ctx = new ActionContext(
                hasWaveState: false,
                currentPhase: GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar);

            return m_ShadowWalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet)
                ? ctx.WithWallet(wallet)
                : ctx;
        }

    }
}
