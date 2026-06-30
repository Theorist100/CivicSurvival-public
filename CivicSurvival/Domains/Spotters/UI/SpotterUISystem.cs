using System;
using Game;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Domains.Spotters.UI
{
    /// <summary>
    /// UI state and triggers for OpSec / spotter countermeasures.
    /// </summary>
    [ActIndependent]
    public partial class SpotterUISystem : CivicUIPanelSystem
    {
        private EndFrameBarrier m_EndFrameBarrier = null!;
        private EntityQuery m_SpotterStatsQuery;
        private EntityQuery m_SpotterPenaltyQuery;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SpotterStatsQuery = GetEntityQuery(ComponentType.ReadOnly<SpotterStatsSingleton>());
            m_SpotterPenaltyQuery = GetEntityQuery(ComponentType.ReadOnly<SpotterPenaltyState>());

            // Show-defaults convention (ThreatUISystem / AttentionUISystem reference):
            // this panel always renders and emits a neutral DTO when its producers are
            // not yet available — never RequireForUpdate-gated, never the fail-loud
            // GetSingletonOrDefault that threw [CRITICAL] when the Spotters feature was
            // available but its singleton had not been created yet.

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            Bindings.Add<string>(SpotterState, "{}");
        }

        protected override void ConfigureTriggers()
        {
            Triggers.AddScenarioTrigger(SbuVisit, FeatureIds.Spotters, Act.Crisis, RequestResultBridge.SpotterAction, OnSBUVisit);
            Triggers.AddScenarioTrigger(Evacuation, FeatureIds.Spotters, Act.Crisis, RequestResultBridge.SpotterAction, OnEvacuation);
            Triggers.AddScenarioTrigger(ToggleCounterOSINT, FeatureIds.Spotters, Act.Crisis, RequestResultBridge.SpotterAction, OnToggleCounterOSINT);
        }

        protected override void OnPanelUpdate()
        {
            var dto = new SpotterDto
            {
                SpotterActionRequestJson = RequestResultBridge.Get(RequestResultBridge.SpotterAction).ToJson()
            };

            var stats = m_SpotterStatsQuery.TryGetSingleton<SpotterStatsSingleton>(out var s)
                ? s : SpotterStatsSingleton.Default;
            dto.SpotterCount = stats.ActiveCount;
            dto.TotalSBUVisits = stats.TotalSBUVisits;
            dto.TotalEvacuations = stats.TotalEvacuations;
            dto.SbuVisitCost = stats.SBUCost;
            dto.EvacuationCost = stats.EvacuationCost;
            dto.CounterOSINTDailyCost = stats.CounterOSINTDailyCost;

            var penalty = m_SpotterPenaltyQuery.TryGetSingleton<SpotterPenaltyState>(out var p)
                ? p : SpotterPenaltyState.Default;
            dto.SpotterPenaltyPercent = (int)Math.Round(penalty.GlobalPenalty * 100);
            dto.SpotterRawPenaltyPercent = (int)Math.Round(penalty.RawPenalty * 100);
            dto.CounterOSINTActive = penalty.IsCounterOSINTActive;

            FillSpotterEligibility(ref dto);
            PublishWhenComplete(SpotterState, NoSourceChecks, () => dto);
        }

        private void FillSpotterEligibility(ref SpotterDto dto)
        {
            // Act-lock is NOT computed here. Can* is local-facts-only; the act overlay
            // (currentAct < Crisis) is applied by the frontend, click is gated by
            // AddScenarioTrigger, and the backend command system is the authoritative
            // act guard.
            bool countermeasuresClosed = !IsFeatureOpen("Countermeasures");
            var stats = m_SpotterStatsQuery.TryGetSingleton<SpotterStatsSingleton>(out var s)
                ? s : SpotterStatsSingleton.Default;
            int totalSpotters = stats.TotalCount;
            int activeCandidates = stats.ActionableCount;

            dto.CanSbuVisit = SpotterEligibility.CanPerformSBUVisit(
                totalSpotters,
                activeCandidates,
                dto.SbuVisitCost,
                World,
                out var sbuReason);
            dto.SbuVisitLockedReasonId = sbuReason;

            dto.CanEvacuationRun = SpotterEligibility.CanPerformEvacuation(
                totalSpotters,
                activeCandidates,
                dto.EvacuationCost,
                World,
                out var evacuationReason);
            dto.EvacuationRunLockedReasonId = evacuationReason;

            dto.CanToggleCounterOSINT = SpotterEligibility.CanToggleCounterOSINT(
                countermeasuresClosed,
                dto.CounterOSINTActive,
                dto.CounterOSINTDailyCost,
                World,
                out var counterReason);
            dto.CounterOSINTLockedReasonId = counterReason;
        }

        private TriggerOutcome OnSBUVisit(in ScenarioGuard guard)
        {
            Log.Info("SBU visit requested");
            return CreateSpotterActionRequest(AirDefenseActionType.PerformSBUVisit);
        }

        private TriggerOutcome OnEvacuation(in ScenarioGuard guard)
        {
            Log.Info("Evacuation requested");
            return CreateSpotterActionRequest(AirDefenseActionType.PerformEvacuation);
        }

        private TriggerOutcome OnToggleCounterOSINT(in ScenarioGuard guard)
        {
            if (!IsFeatureOpen("Countermeasures"))
            {
                Log.Warn("Counter-OSINT toggle rejected: Countermeasures feature is closed");
                return TriggerOutcome.Reject(ReasonIds.CountermeasuresLocked);
            }

            Log.Info("Counter-OSINT toggle requested");
            return CreateSpotterActionRequest(AirDefenseActionType.ToggleCounterOSINT);
        }

        private static bool IsFeatureOpen(string featureId)
        {
            if (!FeatureRegistry.IsInitialized)
                return true;

            return FeatureRegistry.Instance.IsAvailable(featureId);
        }

        private TriggerOutcome CreateSpotterActionRequest(AirDefenseActionType action)
        {
            if (TriggerOutcome.IsSimulationPaused(World))
            {
                Log.Info($"Spotter action rejected: budget pipeline requires unpaused simulation for {action}");
                return TriggerOutcome.Reject(ReasonIds.GamePaused);
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new AirDefenseActionRequest
            {
                Action = action
            });
            if (Log.IsDebugEnabled) Log.Debug($"Created Spotter-owned AirDefenseActionRequest: action={action}");
            return TriggerOutcome.HandOffToEcs(ecb, entity, SystemAPI.Time.ElapsedTime, TriggerOutcome.CurrentSimulationFrame(World));
        }
    }
}
