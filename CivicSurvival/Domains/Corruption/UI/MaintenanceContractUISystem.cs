using System;
using System.Text;
using CivicSurvival.Core.UI;
using Game.Buildings;
using Game.Common;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Components.Domain.Corruption;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Corruption.Data;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

using CivicSurvival.Core.Services;
using CivicSurvival.Domains.Corruption.Systems;
namespace CivicSurvival.Domains.Corruption.UI
{
    /// <summary>
    /// UI system for procurement contract offers (Phase B corruption).
    /// Query-based reads, no IMaintenanceContractService dependency.
    ///
    /// Migrated from MaintenanceContractUIPanel → CivicUIPanelSystem.
    /// Gains: auto-disposed EntityQueries, proper ECS lifecycle.
    /// </summary>
    [ActIndependent]
    public partial class MaintenanceContractUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_ShadowWalletQuery;
        private MaintenanceContractSystem? m_StateSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_ShadowWalletQuery = GetEntityQuery(ComponentType.ReadOnly<ShadowWalletSingleton>());

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateSystem ??= FeatureRegistry.Instance.Require<MaintenanceContractSystem>();
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(MaintenanceState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add<int, int, int>(AcceptOfficialContract, FeatureIds.Corruption, RequestResultBridge.MaintenanceContract, OnAcceptOfficial);
            Triggers.Add<int, int, int>(AcceptShadyContract, FeatureIds.Corruption, RequestResultBridge.MaintenanceContract, OnAcceptShady);
            Triggers.Add<int, int>(DeclineProcurement, FeatureIds.Corruption, RequestResultBridge.MaintenanceContract, OnDecline);
        }

        protected override void OnPanelUpdate()
        {
            var snapshot = m_StateSystem?.GetUiSnapshot() ?? MaintenanceContractUiSnapshot.Empty;

            var dto = new MaintenanceDto
            {
                PendingProcurementOfferJson = BuildPendingOfferJson(snapshot),
                ShadyContractCount = snapshot.ShadyContractCount,
                TotalContractCount = snapshot.TotalContractCount,
                ActiveContractsJson = snapshot.ActiveContractsJson,
                MaintenanceContractRequestJson = RequestResultBridge.Get(RequestResultBridge.MaintenanceContract).ToJson()
            };

            PublishWhenComplete(MaintenanceState, NoSourceChecks, () => dto);
        }

        private TriggerOutcome OnAcceptOfficial(int entityIndex, int entityVersion, int expectedPrice)
        {
            return CreateContractResponse(entityIndex, entityVersion, ContractResponseType.AcceptOfficial, expectedPrice);
        }

        private TriggerOutcome OnAcceptShady(int entityIndex, int entityVersion, int expectedPrice)
        {
            return CreateContractResponse(entityIndex, entityVersion, ContractResponseType.AcceptShady, expectedPrice);
        }

        private TriggerOutcome OnDecline(int entityIndex, int entityVersion)
        {
            return CreateContractResponse(entityIndex, entityVersion, ContractResponseType.Decline, 0);
        }

        private TriggerOutcome CreateContractResponse(int entityIndex, int entityVersion, ContractResponseType responseType, int expectedPrice)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info($"ContractResponse rejected: request pipeline requires unpaused simulation for {responseType}");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            ActionAvailabilityField shadyGate = default;
            bool exactPendingOffer = false;
            foreach (var (_, pendingEntity) in
                SystemAPI.Query<RefRO<PendingProcurement>>()
                    .WithAll<Building>()
                    .WithNone<Deleted>()
                    .WithEntityAccess())
            {
                if (pendingEntity.Index == entityIndex && pendingEntity.Version == entityVersion)
                {
                    exactPendingOffer = true;
                    if (responseType == ContractResponseType.AcceptShady)
                        shadyGate = ActionGate.Resolve(ActionKey.ShadyContractAccept, BuildShadowActionContext(proposedCost: 0));
                    break;
                }
            }

            if (!exactPendingOffer)
            {
                Log.Warn($"ContractResponse skipped: entity {entityIndex}v{entityVersion} no longer in pending offers");
                return TriggerOutcome.Reject(ReasonIds.ContractOfferStale);
            }

            if (responseType == ContractResponseType.AcceptShady && !shadyGate.CanRun)
            {
                return string.IsNullOrEmpty(shadyGate.LockedReasonId)
                    ? TriggerOutcome.Reject(ReasonIds.MarketWalletUnavailable)
                    : TriggerOutcome.RejectRuntime(shadyGate.LockedReasonId!);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new ContractResponse
            {
                BuildingEntityIndex = entityIndex,
                BuildingEntityVersion = entityVersion,
                ResponseType = responseType,
                ExpectedPrice = expectedPrice
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created ContractResponse: entity={entityIndex}v{entityVersion}, type={responseType}");
            return TriggerOutcome.HandOffToEcs(
                ecb,
                entity,
                SystemAPI.Time.ElapsedTime,
                TriggerOutcome.CurrentSimulationFrame(World),
                discriminatorKind: "offerKey",
                discriminatorValue: $"{entityIndex}:{entityVersion}");
        }

        private string BuildPendingOfferJson(in MaintenanceContractUiSnapshot snapshot)
        {
            _ = nameof(ContractResponse.ExpectedPrice);
            if (!snapshot.HasPendingOffer)
                return "null";

            var pending = snapshot.PendingOffer;
            var shadyGate = ActionGate.Resolve(ActionKey.ShadyContractAccept, BuildShadowActionContext(proposedCost: 0));
            var entry = new PendingProcurementOfferEntry
            {
                EntityIndex = pending.EntityIndex,
                EntityVersion = pending.EntityVersion,
                Service = pending.Service.ToString(),
                ContractType = pending.ContractType.ToString(),
                OfficialVendorName = ContractVendors.GetVendorNameByHash(pending.OfficialVendorHash, pending.ContractType, false),
                ShadyVendorName = ContractVendors.GetVendorNameByHash(pending.ShadyVendorHash, pending.ContractType, true),
                OfficialPrice = pending.OfficialPrice,
                ShadyPrice = pending.ShadyPrice,
                KickbackOffer = pending.KickbackOffer,
                OfficialQuality = pending.OfficialQuality,
                ShadyQuality = pending.ShadyQuality,
                CanAcceptShady = shadyGate.CanRun,
                AcceptShadyLockedReasonId = shadyGate.LockedReasonId,
                AcceptShadyEffectiveCost = shadyGate.EffectiveCost > int.MaxValue
                    ? int.MaxValue
                    : (int)shadyGate.EffectiveCost,
                BuildingName = pending.BuildingName,
            };
            var sb = new StringBuilder(512);
            entry.WriteTo(sb);
            return sb.ToString();
        }

        private ActionContext BuildShadowActionContext(long proposedCost)
        {
            bool hasActSingleton = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton);
            var ctx = new ActionContext(
                hasWaveState: false,
                currentPhase: GamePhase.Calm,
                hasActSingleton,
                hasActSingleton ? actSingleton.CurrentAct : Act.PreWar).WithCost(proposedCost);

            return m_ShadowWalletQuery.TryGetSingleton<ShadowWalletSingleton>(out var wallet)
                ? ctx.WithWallet(wallet)
                : ctx;
        }

    }
}
