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
        private EntityQuery m_PowerGridQuery;
        private EntityQuery m_FuelSiphoningQuery;
        private EntityQuery m_EmergencyFundQuery;
        private EntityQuery m_EmergencyFundSettingsQuery;
        private EntityQuery m_WaveStateQuery;
        private EntityQuery m_ShadowWalletQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_PowerGridQuery = GetEntityQuery(ComponentType.ReadOnly<PowerGridSingleton>());
            m_FuelSiphoningQuery = GetEntityQuery(ComponentType.ReadOnly<FuelSiphoningSingleton>());
            m_EmergencyFundQuery = GetEntityQuery(ComponentType.ReadOnly<EmergencyFundSingleton>());
            m_EmergencyFundSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<EmergencyFundSettings>());
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_ShadowWalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());

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
        }

        protected override void OnPanelUpdate()
        {
            var emergencyFundGate = ActionGate.Resolve(ActionKey.EmergencyFundPreset, BuildShadowActionContext());
            var fuelSiphonGate = ActionGate.Resolve(ActionKey.FuelSiphonPreset, BuildShadowActionContext());
            var dto = new SchemesDto
            {
                EmergencyFundBalance = BalanceConfig.Current.EmergencyFund.InitialBalance,
                EmergencyFundAvailability = emergencyFundGate,
                FuelSiphonAvailability = fuelSiphonGate,
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

            dto.CorruptionWindowActive = CalculateCorruptionWindow();

            PublishWhenComplete(SchemesState, NoSourceChecks, () => dto);
        }

        private bool CalculateCorruptionWindow()
        {
            int balance = 0;
            int consumption = 0;
            if (m_PowerGridQuery.TryGetSingleton<PowerGridSingleton>(out var grid))
            {
                balance = grid.RawBalance; // FIX W1-M3: Pre-export balance (siphoning shouldn't hide corruption window)
                consumption = grid.Consumption;
            }
            GamePhase phase = GamePhase.Calm;
            if (m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState))
            {
                phase = waveState.CurrentPhase;
            }
            return CorruptionWindow.IsActive(balance, consumption, phase, out _);
        }

        private TriggerOutcome OnSetEmergencyFundWithdraw(int percent)
        {
            return CreateCorruptionSchemeRequest(CorruptionSchemeType.EmergencyFundWithdraw, percent);
        }

        private TriggerOutcome OnSetFuelSiphonPercent(int percent)
        {
            return CreateCorruptionSchemeRequest(CorruptionSchemeType.FuelSiphon, percent);
        }

        private TriggerOutcome CreateCorruptionSchemeRequest(CorruptionSchemeType schemeType, int percent)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info($"Corruption scheme rejected: request pipeline requires unpaused simulation for {schemeType}");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            if (percent > 0)
            {
                var key = schemeType == CorruptionSchemeType.FuelSiphon
                    ? ActionKey.FuelSiphonPreset
                    : ActionKey.EmergencyFundPreset;
                var gate = ActionGate.Resolve(key, BuildShadowActionContext());
                if (!gate.CanRun)
                {
                    return string.IsNullOrEmpty(gate.LockedReasonId)
                        ? TriggerOutcome.Reject(ReasonIds.MarketWalletUnavailable)
                        : TriggerOutcome.RejectRuntime(gate.LockedReasonId);
                }
            }

            if (percent > 0 && !CalculateCorruptionWindow())
            {
                return TriggerOutcome.Reject(ReasonIds.CorruptionWindowClosed);
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
