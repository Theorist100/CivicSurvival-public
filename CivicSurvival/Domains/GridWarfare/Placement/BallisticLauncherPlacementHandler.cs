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
    /// GridWarfare consumer of the generic Core building-placement pipeline. Places a ballistic
    /// launcher — a field emplacement that outbound ballistic rounds launch from instead of the
    /// map centre. Same transaction shape as the drone launcher (see
    /// <see cref="ShadowFieldLauncherPlacementHandler"/>): multi-instance, shadow-only, no credit,
    /// no crew, no road snap. Costs more than the drone launcher because a ballistic round carries
    /// far more damage per launch.
    /// </summary>
    [HandlesRequestKind(RequestKind.BallisticLauncherPlacement)]
    public sealed class BallisticLauncherPlacementHandler : ShadowFieldLauncherPlacementHandler
    {
        private static readonly LogContext LogChannel = new("BallisticLauncherPlacement");

        // Fallback when the balance config is not yet loaded (mirrors the contract default).
        private const int FALLBACK_LAUNCHER_COST = 60000;

        public override BuildingPlacementKind Kind => BuildingPlacementKind.BallisticLauncher;
        public override RequestKind RequestKind => RequestKind.BallisticLauncherPlacement;
        public override string ResultBridgeKey => RequestResultBridge.BallisticLauncherPlacement;
        public override ReasonId CancelReasonId => ReasonIds.BallisticLauncherCancelled;

        protected override ReasonId PlacementFailedReasonId => ReasonIds.BallisticLauncherPlacementFailed;
        protected override ReasonId InsufficientShadowFundsReasonId => ReasonIds.BallisticLauncherInsufficientShadowFunds;
        protected override ReasonId BuildingLostReasonId => ReasonIds.BallisticLauncherBuildingLost;

        protected override string LauncherLabel => "Ballistic launcher";
        protected override LogContext Log => LogChannel;
        protected override string BudgetRefundSource => BudgetSource.BallisticLauncherInstallRefund;
        protected override string DeductOperationLabel => "BallisticLauncherInstall";
        protected override string RefundOperationLabel => "BallisticLauncherInstallShadowRefund";
        protected override string RefundOperationKeyPrefix => "BallisticLauncherPlacementRefund";
        protected override string PrefabMarkerName => nameof(BallisticLauncherPrefabData);

        protected override bool HasPrefabMarker(EntityManager em, Entity prefabEntity)
            => em.HasComponent<BallisticLauncherPrefabData>(prefabEntity);

        protected override int ResolveCost()
        {
            var gw = BalanceConfig.Current?.GridWarfare;
            return gw?.BallisticLauncherCost ?? FALLBACK_LAUNCHER_COST;
        }

        protected override void AddInstallation(EntityCommandBuffer ecb, Entity launcherEntity, in BuildingPlacementIntent intent, int paidShadow)
        {
            ecb.AddComponent(launcherEntity, new BallisticLauncherInstallation
            {
                Building = intent.Building,
                PaidShadow = paidShadow
            });
        }
    }
}
