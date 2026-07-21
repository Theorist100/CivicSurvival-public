using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Placement;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.Systems.Placement
{
    /// <summary>
    /// Pause-safe payment resolver for building-placement intents. Resolves reserved credit
    /// and paid budget synchronously in ModificationEnd, matching vanilla tool apply semantics
    /// instead of waiting for GameSimulation. Generic: both the credit resolution and the
    /// budget deduction are delegated to the intent kind's handler.
    /// </summary>
    [ActIndependent]
    public partial class BuildingPlacementPaymentSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("BuildingPlacementPayment");

        private EntityQuery m_IntentQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<BuildingPlacementIntent>());
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            foreach (var intentRef in SystemAPI.Query<RefRW<BuildingPlacementIntent>>())
            {
                ref var intent = ref intentRef.ValueRW;
                if (intent.Applied)
                    continue;

                if (!BuildingPlacementHandlerRegistry.TryGet(intent.Kind, out var handler))
                    continue;

                if (intent.ReservedCreditKind != 0 && !intent.CreditResolved)
                    handler.ResolveCredit(World, ref intent);

                if (intent.RequiresBudget && !intent.BudgetResolved)
                    handler.ResolveBudget(World, ref intent);
            }
        }
    }
}
