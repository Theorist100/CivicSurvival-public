using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.Economy;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182
using Unity.Entities;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Resolves civilian repair payment synchronously in ModificationEnd.
    /// </summary>
    [ActIndependent]
    public partial class CivilianRepairPaymentSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("CivilianRepairPaymentSystem");

        private EntityQuery m_IntentQuery;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<CivilianRepairIntent>());
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            foreach (var intentRef in SystemAPI.Query<RefRW<CivilianRepairIntent>>())
            {
                ref var intent = ref intentRef.ValueRW;
                if (intent.Applied || intent.BudgetResolved)
                    continue;

                var category = intent.RepairType == RepairType.ShadowOps
                    ? BudgetCategory.ShadowOps
                    : BudgetCategory.Repairs;

                var result = BudgetTransactionResolver.Deduct(
                    World,
                    m_WalletService,
                    intent.Cost,
                    category,
                    "CivRepair");

                intent.BudgetResolved = true;
                intent.BudgetSucceeded = result.Succeeded;

                if (result.Succeeded)
                    Log.Info($"Budget resolved (success) for civilian repair building {intent.Building.Index}");
                else
                    Log.Warn($"Budget resolved (failed) for civilian repair building {intent.Building.Index}");
            }
        }
    }
}
