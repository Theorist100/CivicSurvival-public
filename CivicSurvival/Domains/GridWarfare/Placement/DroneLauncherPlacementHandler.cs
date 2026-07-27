using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helpers live with the City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182

namespace CivicSurvival.Domains.GridWarfare.Placement
{
    /// <summary>
    /// GridWarfare consumer of the generic Core building-placement pipeline. Places a drone
    /// launcher — a field emplacement that outbound counter-strike drones launch from instead
    /// of the map centre. The whole transaction shape (multi-instance, shadow-only, no credit,
    /// no crew, no road snap) lives in <see cref="ShadowFieldLauncherPlacementHandler"/>; this
    /// class only names the drone-specific marker, cost, installation and reasons.
    /// </summary>
    [HandlesRequestKind(RequestKind.DroneLauncherPlacement)]
    public sealed class DroneLauncherPlacementHandler : ShadowFieldLauncherPlacementHandler
    {
        private static readonly LogContext LogChannel = new("DroneLauncherPlacement");

        // Fallback when the balance config is not yet loaded (mirrors the contract default).
        private const int FALLBACK_LAUNCHER_COST = 25000;

        public override BuildingPlacementKind Kind => BuildingPlacementKind.DroneLauncher;
        public override RequestKind RequestKind => RequestKind.DroneLauncherPlacement;
        public override string ResultBridgeKey => RequestResultBridge.DroneLauncherPlacement;
        public override ReasonId CancelReasonId => ReasonIds.DroneLauncherCancelled;

        protected override ReasonId PlacementFailedReasonId => ReasonIds.DroneLauncherPlacementFailed;
        protected override ReasonId InsufficientShadowFundsReasonId => ReasonIds.DroneLauncherInsufficientShadowFunds;
        protected override ReasonId BuildingLostReasonId => ReasonIds.DroneLauncherBuildingLost;

        protected override string LauncherLabel => "Drone launcher";
        protected override LogContext Log => LogChannel;
        protected override string BudgetRefundSource => BudgetSource.DroneLauncherInstallRefund;
        protected override string DeductOperationLabel => "DroneLauncherInstall";
        protected override string RefundOperationLabel => "DroneLauncherInstallShadowRefund";
        protected override string RefundOperationKeyPrefix => "DroneLauncherPlacementRefund";
        protected override string PrefabMarkerName => nameof(DroneLauncherPrefabData);

        protected override bool HasPrefabMarker(EntityManager em, Entity prefabEntity)
            => em.HasComponent<DroneLauncherPrefabData>(prefabEntity);

        protected override int ResolveCost()
        {
            var gw = BalanceConfig.Current?.GridWarfare;
            return gw?.DroneLauncherCost ?? FALLBACK_LAUNCHER_COST;
        }

        protected override void AddInstallation(EntityCommandBuffer ecb, Entity launcherEntity, in BuildingPlacementIntent intent, int paidShadow)
        {
            ecb.AddComponent(launcherEntity, new DroneLauncherInstallation
            {
                Building = intent.Building,
                PaidShadow = paidShadow
            });
        }
    }
}
