using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Components.Requests;
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

namespace CivicSurvival.Domains.Engineering.Systems
{
    /// <summary>
    /// Pause-safe payment resolver for plant-repair transaction intents.
    /// </summary>
    [ActIndependent]
    public partial class PlantRepairPaymentSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("PlantRepairPaymentSystem");

        private EntityQuery m_IntentQuery;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
        private CivicDependencyWire m_DependencyWire = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<RepairTransactionIntent>());
            m_DependencyWire = new CivicDependencyWire(nameof(PlantRepairPaymentSystem));
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_WalletService = m_DependencyWire.RequireWired(() => ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance));
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            foreach (var intentRef in SystemAPI.Query<RefRW<RepairTransactionIntent>>())
            {
                ref var intent = ref intentRef.ValueRW;
                if (intent.Applied || intent.BudgetResolved)
                    continue;

                long amount = intent.Cost;
                bool isShadowOps = intent.RepairType == RepairType.ShadowOps;
                if (isShadowOps)
                {
                    var affordability = m_WalletService.CanAffordWithPending(intent.Cost);
                    if (!affordability.Affordable)
                    {
                        intent.BudgetResolved = true;
                        intent.BudgetSucceeded = false;
                        Log.Warn($"Budget resolved (failed) for plant {intent.PlantId}");
                        continue;
                    }
                    amount = affordability.EffectiveCost;
                }

                var result = BudgetTransactionResolver.Deduct(
                    World,
                    m_WalletService,
                    amount,
                    isShadowOps ? BudgetCategory.ShadowOps : BudgetCategory.Repairs,
                    string.Concat(isShadowOps ? "ShadowOpsRepair:" : "Repair:", intent.PlantId.ToString()));

                intent.BudgetResolved = true;
                intent.BudgetSucceeded = result.Succeeded;
                if (result.Succeeded && amount <= int.MaxValue)
                    intent.Cost = System.Convert.ToInt32(amount);

                if (result.Succeeded)
                {
#pragma warning disable CIVIC022 // Terminal transition log only when an intent resolves, not per-frame steady state.
                    Log.Info($"Budget resolved (success) for plant {intent.PlantId}");
#pragma warning restore CIVIC022
                }
                else
                {
#pragma warning disable CIVIC022 // Terminal transition log only when an intent resolves, not per-frame steady state.
                    Log.Warn($"Budget resolved (failed) for plant {intent.PlantId}");
#pragma warning restore CIVIC022
                }
            }
        }
    }
}
