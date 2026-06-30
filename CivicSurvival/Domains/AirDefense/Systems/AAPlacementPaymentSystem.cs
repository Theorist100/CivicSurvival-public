using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182
using Unity.Entities;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Pause-safe payment resolver for AA placement intents. It resolves credits and
    /// paid placement budget synchronously in ModificationEnd, matching vanilla tool
    /// apply semantics instead of waiting for GameSimulation.
    /// </summary>
    [ActIndependent]
    public partial class AAPlacementPaymentSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("AAPlacementPaymentSystem");

        private EntityQuery m_IntentQuery;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
#pragma warning disable CIVIC229 // System reference — single-writer service call, no state ownership
        private AirDefenseStateSystem m_StateSystem = null!;
#pragma warning restore CIVIC229

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<AAPlacementIntent>());
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            m_StateSystem ??= FeatureRegistry.Instance.Require<AirDefenseStateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            foreach (var intentRef in SystemAPI.Query<RefRW<AAPlacementIntent>>())
            {
                ref var intent = ref intentRef.ValueRW;
                if (intent.Applied)
                    continue;

                if (intent.ReservedCreditKind != AAPlacementCreditKind.None && !intent.CreditResolved)
                    m_StateSystem.ResolvePlacementCredit(ref intent);

                if (intent.RequiresBudget && !intent.BudgetResolved)
                {
                    var result = BudgetTransactionResolver.Deduct(
                        World,
                        m_WalletService,
                        intent.Cost,
                        BudgetCategory.AirDefense,
                        "AAInstall");

                    intent.BudgetResolved = true;
                    intent.BudgetSucceeded = result.Succeeded;

                    if (result.Succeeded)
                        Log.Info($"Budget resolved (success) for building {intent.Building.Index}:{intent.Building.Version}");
                    else
                        Log.Warn($"Budget resolved (failed) for building {intent.Building.Index}:{intent.Building.Version}");
                }
            }
        }
    }
}
