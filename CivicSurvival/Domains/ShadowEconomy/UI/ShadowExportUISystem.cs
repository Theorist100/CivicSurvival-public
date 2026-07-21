using Unity.Entities;
using Game;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.ShadowEconomy.UI
{
    /// <summary>
    /// UI system for shadow export data.
    /// ECS-Pure: Reads from ShadowExportState and ShadowWalletSingleton directly.
    ///
    /// Migrated from ShadowExportUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, RequireForUpdate, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.ShadowTradeExport)]
    public partial class ShadowExportUISystem : CivicUIPanelSystem
    {
        private EntityQuery m_ShadowTradeQuery;
        private EntityQuery m_WalletQuery;
        private IShadowTradeConsumerReadiness m_Readiness = null!;
        private Systems.ShadowTradeDailySystem? m_TradeSystem;

        // Manual delta fields removed — BindingRegistry handles change detection via string comparison

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ShadowTradeQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShadowExportState>());
            m_WalletQuery = GetEntityQuery(
                ComponentType.ReadOnly<ShadowWalletSingleton>());

            RequireForUpdate(m_ShadowTradeQuery);

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Readiness = ServiceRegistry.Instance.Require<IShadowTradeConsumerReadiness>();
            m_TradeSystem ??= FeatureRegistry.Instance.Require<Systems.ShadowTradeDailySystem>();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(ExportState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int>(
                SetExportPercent,
                FeatureIds.ShadowEconomy,
                RequestResultBridge.ShadowTradeExport,
                ActionKey.ShadowExport,
                BuildActionContext,
                OnSetExportPercent);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new ExportDto();
            var exportGate = ActionGate.Resolve(ActionKey.ShadowExport, BuildActionContext());
            dto.ExportAvailability = exportGate;
            dto.ShadowTradeExportRequestJson = RequestResultBridge.Get(RequestResultBridge.ShadowTradeExport).ToJson();

            if (m_ShadowTradeQuery.TryGetSingleton<ShadowExportState>(out var state))
            {
                dto.ExportPercent = state.ExportPercentage;
                dto.ExportedMW = state.ExportedMW;
                dto.DailyIncome = state.ExportDailyIncome;
            }

            if (m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet))
            {
                dto.OffshoreBalance = (double)wallet.Balance;
                dto.IsFrozen = wallet.IsFrozen;
                dto.FreezeReason = (int)wallet.FreezeReason;
            }

            PublishWhenComplete(ExportState, NoSourceChecks, () => dto);
        }

        private TriggerOutcome OnSetExportPercent(int percent)
        {
            // No pause reject here, by design — the export percent is applied synchronously
            // by the singleton owner (AXIOM 14 route 3), so the slider works while paused.
            if (!TryRequireShadowTradeConsumerReady(out var lockedReasonId))
                return TriggerOutcome.Reject(lockedReasonId);

            if (percent < 0 || percent > 100)
            {
                return TriggerOutcome.Reject(ReasonIds.MarketInvalidInput);
            }
            if (!m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out _))
            {
                if (Log.IsDebugEnabled) Log.Debug($"SetExportPercent({percent}) rejected: wallet missing");
                return TriggerOutcome.Reject(ReasonIds.MarketWalletUnavailable);
            }

            string failReason = "";
            if (m_TradeSystem == null || !m_TradeSystem.TryApplyExportPercentImmediate(percent, out failReason))
            {
                return string.IsNullOrEmpty(failReason)
                    ? TriggerOutcome.Reject(ReasonIds.MarketRequestFailed)
                    : TriggerOutcome.RejectRuntime(failReason);
            }

            if (Log.IsDebugEnabled) Log.Debug($"SetExportPercent({percent}) applied synchronously");
            return TriggerOutcome.SyncSuccess();
        }

        private bool TryRequireShadowTradeConsumerReady(out ReasonId lockedReasonId)
        {
            if (!SystemAPI.TryGetSingleton<CurrentActSingleton>(out _))
            {
                lockedReasonId = ReasonIds.ScenarioUnavailable;
                return false;
            }

            // No act gate: shadow export runs from PreWar so surplus can be sold and
            // offshore cash banked before the war.

            if (!m_Readiness.CanConsumeShadowTradeRequests)
            {
                lockedReasonId = ReasonIds.MarketStateUnavailable;
                return false;
            }

            lockedReasonId = ReasonId.None;
            return true;
        }

        private ActionContext BuildActionContext()
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            var ctx = new ActionContext(
                false,
                GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar);

            return m_WalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet)
                ? ctx.WithWallet(wallet)
                : ctx;
        }
    }
}
