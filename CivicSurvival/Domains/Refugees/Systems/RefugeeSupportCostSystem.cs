using System;
using Colossal.UI.Binding;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Refugees.Data;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Types;
using B = CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Refugees.Systems
{
    /// <summary>
    /// Periodically deducts refugee support costs from city budget.
    /// Each refugee household receives daily support payment.
    ///
    /// CDI-3: Refugees → Budget Impact
    /// Problem: Refugees occupy housing, use services, but zero budget impact.
    /// Solution: Daily deduction based on refugee household count.
    ///
    /// UI Bindings:
    /// - refugeeHouseholdCount: Number of refugee households in city
    ///
    /// R3-C-12: Reported as "reads stale costs (2.5s)" — not accurate. System reads EntityQuery
    /// counts (always fresh) and config values (always fresh). The "staleness" is the deduction
    /// interval itself (RefugeeSupportIntervalHours) — by design, not a data freshness issue.
    ///
    /// S16-01 ACCEPTED: No birth-time proration for mid-interval spawns. 24h billing cycle
    /// charges based on current headcount — standard utility billing model. Prorate = overengineering.
    /// </summary>
    [ActIndependent]
    public partial class RefugeeSupportCostSystem : ThrottledUISystemBase
    {
        private static readonly LogContext Log = new("RefugeeSupportCostSystem");

        private EntityQuery m_RefugeeHouseholdQuery;
        private EntityQuery m_ScenarioQuery;
#pragma warning disable CIVIC324 // Ephemeral scenario-gate controller; recreated by OnCreate, reset paths, Deserialize, and ValidateAfterLoad.
        [System.NonSerialized] private ActGateController m_Gate = null!;
#pragma warning restore CIVIC324
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private double m_LastDeductionGameHours;
        private int m_LastRefugeeCount;
        private long m_LastDeductionAmount;

        private ProfiledBinding<int> m_RefugeeCountBinding = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RefugeeHouseholdQuery = GetEntityQuery(
                ComponentType.ReadOnly<RefugeeHousehold>(),
                ComponentType.Exclude<Deleted>()
            );

            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            m_RefugeeCountBinding = new ProfiledBinding<int>(B.Group, B.RefugeeHouseholdCount, 0);
            AddBinding(m_RefugeeCountBinding.Binding);

            InitializeGate();

            Log.Info("Created (scenario-gated)");
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ApplyExternalState(IsVillageScenario());
            return m_Gate.State != ActGateState.Active;
        }

        [CompletesDependency("OnThrottledUpdate refugee count update: CalculateEntityCount runs once per throttle tick to drive m_RefugeeCountBinding UI surface; sync amortised over throttle interval")]
        protected override void OnThrottledUpdate()
        {
            int currentRefugeeCount = m_RefugeeHouseholdQuery.CalculateEntityCount();
            m_RefugeeCountBinding.Update(currentRefugeeCount);

            // LOAD-INVARIANT: runtime ticks can precede GameTime activation after load/hot-reload.
            if (!GameTimeSystem.TryGetGameHours(out var currentGameHours))
                return;
            var scenarioCfg = BalanceConfig.Current.Scenario;
            float interval = scenarioCfg.RefugeeSupportIntervalHours;

            if (currentGameHours < m_LastDeductionGameHours)
            {
                Log.Warn($"Support timer ahead of game time ({m_LastDeductionGameHours:F1}h > {currentGameHours:F1}h), re-anchoring");
                m_LastDeductionGameHours = currentGameHours;
                return;
            }

            double elapsedHours = currentGameHours - m_LastDeductionGameHours;
            if (elapsedHours < interval)
                return;

            m_LastDeductionGameHours = currentGameHours;

            if (currentRefugeeCount == 0)
                return;

            int costPerHousehold = scenarioCfg.RefugeeSupportPerHouseholdPerDay;
            // Prorate per-day cost by actual interval (interval=12h → charge half; interval=48h → charge double)
            float intervalFraction = GameRate.DayFractionFromHours(interval);
            long totalCost = (long)Math.Round(currentRefugeeCount * costPerHousehold * intervalFraction);

            if (!CanDeductRefugeeSupport(totalCost))
            {
                m_LastRefugeeCount = currentRefugeeCount;
                m_LastDeductionAmount = totalCost;
                Log.Warn($"Skipped refugee support deduction: insufficient funds (${totalCost:N0})");
                return;
            }

            // ECB request — BudgetResolutionSystem processes next frame
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            bool queued = BudgetEmitter.TryQueueDeduct(
                World,
                ecb,
                totalCost,
                BudgetCategory.RefugeeSupport,
                BudgetPriority.DailyCost,
                "RefugeeSupportCostSystem",
                BudgetResultMode.FireAndForget);

            m_LastRefugeeCount = currentRefugeeCount;
            m_LastDeductionAmount = totalCost;
            if (queued)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Queued ${totalCost:N0} deduction for {currentRefugeeCount} refugee households");
            }
            else
            {
                Log.Warn($"Skipped refugee support deduction: insufficient funds (${totalCost:N0})");
            }
        }

        private bool CanDeductRefugeeSupport(long totalCost)
        {
            return totalCost > 0 && CityBudgetService.CanAffordWithPending(World, totalCost);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        /// <summary>Anchor the deduction timer to current game time when enabled externally.</summary>
        public void AnchorDeductionTimer()
        {
            // LOAD-INVARIANT: external gate transitions may fire before GameTime activation.
            if (GameTimeSystem.TryGetGameHours(out var currentGameHours))
                m_LastDeductionGameHours = currentGameHours;
        }

        private void InitializeGate()
        {
            m_Gate = new ActGateController(
                // Documentation-only predicate; ApplyExternalState(IsVillageScenario()) is the live path.
                isOpenFor: _ => false,
                onTransition: HandleGateTransition);
        }

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
            {
                if (!isInitial)
                {
                    if (m_LastDeductionGameHours <= 0.0)
                        AnchorDeductionTimer();
                    ResetThrottleCounter();
                    ForceNextUpdate();
                    Log.Info("[RefugeeSupportCost] Scenario gate opened (real transition)");
                }

                if (m_LastDeductionGameHours <= 0.0)
                    AnchorDeductionTimer();
                return;
            }

            if (next == ActGateState.Inactive && !isInitial)
            {
                m_LastRefugeeCount = 0;
                m_LastDeductionAmount = 0;
                m_RefugeeCountBinding.Update(0);

                Log.Info("[RefugeeSupportCost] Scenario gate closed (real transition)");
            }
        }

        private bool IsVillageScenario()
        {
            return m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var scenario)
                && scenario.ScenarioType == ScenarioType.Village;
        }

        /// <summary>Current refugee household count (for API).</summary>
        public int RefugeeCount => m_LastRefugeeCount;

        /// <summary>Last deduction amount (for API).</summary>
        public long LastDeductionAmount => m_LastDeductionAmount;
    }
}
