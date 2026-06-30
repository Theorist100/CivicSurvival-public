using Game;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.Spotters;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Spotters.Systems
{
    /// <summary>
    /// Validates player AirDefenseActionRequests, creates budget deduct requests,
    /// and enqueues SpotterCommands for SpotterAggregateSystem.
    ///
    /// Does NOT write SpotterData or singletons — sole writer is SpotterAggregateSystem.
    /// Uses reservedTargets NativeHashSet for batch-local double-action protection.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.SpotterAction)]
    [TransientConsumerReconcile(typeof(AirDefenseActionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: spotter commands and any retained budget intents are emitted only while this consumer processes the request, so pre-consume load loss is reissuable.")]
    public partial class SpotterCommandIngressSystem : ThrottledSystemBase
    {
        private static readonly LogContext Log = new("SpotterCommandIngress");

        private EntityQuery m_RequestQuery;
        private EntityQuery m_CurrentActQuery;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        [System.NonSerialized] private SpotterAggregateSystem? m_Aggregate;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;

        // Ephemeral targeting RNG — non-deterministic post-load is acceptable
#pragma warning disable CIVIC066
        private Unity.Mathematics.Random m_SpotterRandom;
#pragma warning restore CIVIC066

        /// <summary>Shared phase key — fires on same frame as SpotterAggregateSystem.</summary>
        protected override string ThrottlePhaseKey => "SpotterPipeline";

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<AirDefenseActionRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_DependencyWire = new CivicDependencyWire(nameof(SpotterCommandIngressSystem));
            m_SpotterRandom = Unity.Mathematics.Random.CreateFromIndex((uint)(World.GetHashCode() ^ 0x53504F54));

            // CurrentActSingleton is foundational always-on (Scenario not gated). Command
            // validation needs the real act — never run it with fabricated PreWar.
            RequireForUpdate<CurrentActSingleton>();

            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Aggregate ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<SpotterAggregateSystem>());
        }

        protected override void OnThrottledUpdate()
        {
            if (m_Aggregate == null) return;
            if (m_RequestQuery.IsEmpty) return;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();

            // Batch-local reservation: prevents double-targeting in same throttled tick
            var reservedTargets = new NativeHashSet<long>(4, Allocator.Temp);
            var activeCandidates = new NativeList<Entity>(Allocator.Temp);
            BuildActiveSpotterCandidates(activeCandidates);
            bool counterOSINTHandled = false; // prevents double toggle in same tick
            int visitCountOffset = 0; // tracks within-tick SBU visits for correct cost step

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<AirDefenseActionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                RequestStatus status = RequestStatus.Failed;
                var req = request.ValueRO;
                var failReason = "";

                switch (req.Action)
                {
                    case AirDefenseActionType.PerformSBUVisit:
                        status = ProcessSBUVisit(ecb, activeCandidates, reservedTargets, meta.ValueRO, ref visitCountOffset, out failReason)
                            ? RequestStatus.Pending
                            : RequestStatus.Failed;
                        break;

                    case AirDefenseActionType.PerformEvacuation:
                        status = ProcessEvacuation(ecb, activeCandidates, reservedTargets, meta.ValueRO, out failReason)
                            ? RequestStatus.Pending
                            : RequestStatus.Failed;
                        break;

                    case AirDefenseActionType.ToggleCounterOSINT:
                        if (counterOSINTHandled)
                        {
                            failReason = ReasonIds.SpotterDuplicateAction;
                        }
                        else
                        {
                            status = ProcessToggleCounterOSINT(ecb, meta.ValueRO, out failReason);
                            counterOSINTHandled = true;
                        }
                        break;

                    default:
                        Log.Warn($"Unknown AirDefenseActionType: {req.Action}");
                        failReason = ReasonIds.SpotterUnknownAction;
                        break;
                }

                string resultReason = "";
                if (status == RequestStatus.Failed)
                    resultReason = string.IsNullOrEmpty(failReason) ? ReasonIds.SpotterActionFailed : failReason;

                switch (status)
                {
                    case RequestStatus.Success:
                        RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.SpotterAction, SystemAPI.Time.ElapsedTime);
                        break;
                    case RequestStatus.Pending:
                        break;
                    default:
                        RequestResultEmitter.Emit(
                            ecb,
                            meta.ValueRO,
                            RequestKind.SpotterAction,
                            status,
                            ReasonId.FromRuntime(resultReason),
                            SystemAPI.Time.ElapsedTime);
                        break;
                }

                ecb.DestroyEntity(entity);
            }

            reservedTargets.Dispose();
            activeCandidates.Dispose();
            m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private bool ProcessSBUVisit(EntityCommandBuffer ecb, NativeList<Entity> activeCandidates, NativeHashSet<long> reservedTargets, in RequestMeta requestMeta, ref int visitCountOffset, out string failReason)
        {
            failReason = "";
            if (!SystemAPI.TryGetSingleton<SpotterCountermeasuresState>(out var cmState))
            {
                Log.Error("SpotterCountermeasuresState not found!");
                failReason = ReasonIds.SpotterSystemUnavailable;
                return false;
            }

            int cost = GetSBUCost(cmState.TotalSBUVisits + CountInFlightSBUVisits() + visitCountOffset);
            if (GetCurrentAct() < Act.Crisis)
            {
                failReason = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }
            if (!SpotterEligibility.CanPerformSBUVisit(
                    activeCandidates.Length,
                    activeCandidates.Length,
                    cost,
                    World,
                    out failReason))
            {
                return false;
            }

            Entity? target = FindActiveSpotter(activeCandidates);
            if (target == null)
            {
                failReason = reservedTargets.Count > 0 ? ReasonIds.SpotterAllReservedThisTick : ReasonIds.SpotterNoActiveTargets;
                Log.Info($"SBUVisit - {failReason}");
                return false;
            }

            if (!CanAffordSpotterOps(cost, out failReason))
            {
                Log.Warn($"SBUVisit FAILED - need ${cost}");
                return false;
            }

            var budgetEntity = ecb.QueuePendingOperation(new SpotterBudgetIntent
            {
                Action = AirDefenseActionType.PerformSBUVisit,
                Target = EntityRef.FromEntity(target.Value),
                Cost = cost,
                RefundOperationKey = new FixedString128Bytes(RefundOperationKey(requestMeta, AirDefenseActionType.PerformSBUVisit))
            });
            if (!BudgetEmitter.TryQueueDeductOnEntity(
                    World,
                    ecb,
                    budgetEntity,
                    cost,
                    BudgetCategory.SpotterOps,
                    BudgetPriority.PlayerAction,
                    "Spotter.SBU",
                    out _,
                    requestMeta,
                    BudgetResultMode.RetainResult))
            {
                ecb.DestroyEntity(budgetEntity);
                Log.Warn($"SBUVisit FAILED - need ${cost}");
                failReason = ReasonIds.SpotterInsufficientFunds;
                return false;
            }

            reservedTargets.Add(PackEntityId(target.Value.Index, target.Value.Version));
            visitCountOffset++;

            Log.Info($"SBUVisit queued, cost: ${cost}");
            return true;
        }

        private bool ProcessEvacuation(EntityCommandBuffer ecb, NativeList<Entity> activeCandidates, NativeHashSet<long> reservedTargets, in RequestMeta requestMeta, out string failReason)
        {
            failReason = "";
            int cost = BalanceConfig.Current.Spotter.EvacuationCost;
            if (GetCurrentAct() < Act.Crisis)
            {
                failReason = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }
            if (!SpotterEligibility.CanPerformEvacuation(
                    activeCandidates.Length,
                    activeCandidates.Length,
                    cost,
                    World,
                    out failReason))
            {
                return false;
            }

            Entity? target = FindActiveSpotter(activeCandidates);
            if (target == null)
            {
                failReason = reservedTargets.Count > 0 ? ReasonIds.SpotterAllReservedThisTick : ReasonIds.SpotterNoActiveTargets;
                Log.Info($"Evacuation - {failReason}");
                return false;
            }

            if (!CanAffordSpotterOps(cost, out failReason))
            {
                Log.Warn($"Evacuation FAILED - need ${cost}");
                return false;
            }

            var budgetEntity = ecb.QueuePendingOperation(new SpotterBudgetIntent
            {
                Action = AirDefenseActionType.PerformEvacuation,
                Target = EntityRef.FromEntity(target.Value),
                Cost = cost,
                RefundOperationKey = new FixedString128Bytes(RefundOperationKey(requestMeta, AirDefenseActionType.PerformEvacuation))
            });
            if (!BudgetEmitter.TryQueueDeductOnEntity(
                    World,
                    ecb,
                    budgetEntity,
                    cost,
                    BudgetCategory.SpotterOps,
                    BudgetPriority.PlayerAction,
                    "Spotter.Evac",
                    out _,
                    requestMeta,
                    BudgetResultMode.RetainResult))
            {
                ecb.DestroyEntity(budgetEntity);
                Log.Warn($"Evacuation FAILED - need ${cost}");
                failReason = ReasonIds.SpotterInsufficientFunds;
                return false;
            }

            reservedTargets.Add(PackEntityId(target.Value.Index, target.Value.Version));

            Log.Info($"Evacuation pending budget confirmation for spotter {target.Value.Index}, cost: ${cost}");
            return true;
        }

        private RequestStatus ProcessToggleCounterOSINT(EntityCommandBuffer ecb, in RequestMeta requestMeta, out string failReason)
        {
            failReason = "";
            if (!SystemAPI.TryGetSingleton<SpotterCountermeasuresState>(out var cmState))
            {
                Log.Error("SpotterCountermeasuresState not found!");
                failReason = ReasonIds.SpotterSystemUnavailable;
                return RequestStatus.Failed;
            }

            int dailyCost = BalanceConfig.Current.Spotter.CounterOsintDailyCost;
            if (GetCurrentAct() < Act.Crisis)
            {
                failReason = ReasonIds.ActLockedFor(Act.Crisis);
                return RequestStatus.Failed;
            }
            if (!SpotterEligibility.CanToggleCounterOSINT(
                    IsCountermeasuresClosed(),
                    cmState.CounterOSINTActive,
                    dailyCost,
                    World,
                    out failReason))
            {
                return RequestStatus.Failed;
            }

            if (cmState.CounterOSINTActive)
            {
                m_Aggregate!.EnqueueCommand(new SpotterCommand
                {
                    Type = SpotterCommandType.DisableCounterOSINT,
                    NarrativeHint = NarrativeTrigger.CounterOsintStop,
                    HasNarrativeHint = true
                });
                Log.Info("Counter-OSINT disable queued");
                return RequestStatus.Success;
            }

            if (HasPendingCounterOSINTToggleBudget())
            {
                failReason = ReasonIds.SpotterDuplicateAction;
                return RequestStatus.Failed;
            }

            if (!CanAffordSpotterOps(dailyCost, out failReason))
            {
                Log.Warn("Counter-OSINT - insufficient funds");
                return RequestStatus.Failed;
            }

            var budgetEntity = ecb.QueuePendingOperation(new SpotterBudgetIntent
            {
                Action = AirDefenseActionType.ToggleCounterOSINT,
                Target = EntityRef.FromEntity(Entity.Null),
                Cost = dailyCost,
                RefundOperationKey = new FixedString128Bytes(RefundOperationKey(requestMeta, AirDefenseActionType.ToggleCounterOSINT))
            });
            if (!BudgetEmitter.TryQueueDeductOnEntity(
                    World,
                    ecb,
                    budgetEntity,
                    dailyCost,
                    BudgetCategory.SpotterOps,
                    BudgetPriority.PlayerAction,
                    "Spotter.ToggleOSINT",
                    out _,
                    requestMeta,
                    BudgetResultMode.RetainResult))
            {
                ecb.DestroyEntity(budgetEntity);
                Log.Warn("Counter-OSINT - insufficient funds");
                failReason = ReasonIds.SpotterInsufficientFunds;
                return RequestStatus.Failed;
            }

            // Enable deferred to SpotterBudgetIngressSystem on budget confirmation
            Log.Info($"Counter-OSINT budget request queued (${dailyCost}/day)");
            return RequestStatus.Pending;
        }

        private bool HasPendingCounterOSINTToggleBudget()
        {
            foreach (var intent in SystemAPI.Query<RefRO<SpotterBudgetIntent>>()
                .WithAll<BudgetDeductRequest>()
                .WithNone<BudgetDeductResult>())
            {
                if (intent.ValueRO.Action == AirDefenseActionType.ToggleCounterOSINT)
                    return true;
            }

            return false;
        }

        private bool CanAffordSpotterOps(long cost, out string failReason)
        {
            if (cost <= 0)
            {
                failReason = ReasonIds.SpotterConfigError;
                return false;
            }

            if (!CityBudgetService.CanAffordWithPending(World, cost))
            {
                failReason = ReasonIds.SpotterInsufficientFunds;
                return false;
            }

            failReason = "";
            return true;
        }

        private static string RefundOperationKey(in RequestMeta requestMeta, AirDefenseActionType action)
            => $"SpotterRefund:{requestMeta.RequestId}:{requestMeta.CreatedFrame}:{(int)action}";

        private Act GetCurrentAct()
        {
            return m_CurrentActQuery.GetSingleton<CurrentActSingleton>().CurrentAct;
        }

        private static bool IsCountermeasuresClosed()
        {
            return FeatureRegistry.IsInitialized && FeatureRegistry.Instance.IsUnavailable("Countermeasures", out _);
        }

        /// <summary>
        /// Find a random active spotter entity, excluding already-reserved targets.
        /// </summary>
        private void BuildActiveSpotterCandidates(NativeList<Entity> candidates)
        {
            foreach (var (spotter, entity) in
                SystemAPI.Query<RefRO<SpotterData>>().WithNone<Deleted, Destroyed>().WithEntityAccess())
            {
                if (spotter.ValueRO.IsActive && !spotter.ValueRO.IsEvacuating && !HasPendingSpotterTarget(entity))
                    candidates.Add(entity);
            }
        }

        private int CountInFlightSBUVisits()
        {
            int count = 0;
            foreach (var intent in SystemAPI.Query<RefRO<SpotterBudgetIntent>>()
                .WithAll<BudgetDeductRequest>())
            {
                var value = intent.ValueRO;
                if (value.Action == AirDefenseActionType.PerformSBUVisit
                    && !value.DomainApplied
                    && !value.DomainRejected
                    && !value.ChargeFailed)
                    count++;
            }
            return count;
        }

        private bool HasPendingSpotterTarget(Entity target)
        {
            foreach (var intent in SystemAPI.Query<RefRO<SpotterBudgetIntent>>()
                .WithAll<BudgetDeductRequest>())
            {
                var value = intent.ValueRO;
                if ((value.Action == AirDefenseActionType.PerformSBUVisit
                        || value.Action == AirDefenseActionType.PerformEvacuation)
                    && !value.DomainApplied
                    && !value.DomainRejected
                    && !value.ChargeFailed
                    && value.Target.Index == target.Index
                    && value.Target.Version == target.Version)
                    return true;
            }
            return false;
        }

        private Entity? FindActiveSpotter(NativeList<Entity> candidates)
        {
            if (candidates.Length == 0)
                return null;

            int index = m_SpotterRandom.NextInt(0, candidates.Length);
            var result = candidates[index];
            candidates.RemoveAtSwapBack(index);
            return result;
        }

        private static int GetSBUCost(int totalVisits)
        {
            var cfg = BalanceConfig.Current.Spotter;
#pragma warning disable CIVIC067 // Intentional step function: cost increases every 5 visits
            int cost = cfg.SbuBaseCost + (totalVisits / 5) * cfg.SbuCostIncrement;
#pragma warning restore CIVIC067
            return math.min(cost, cfg.SbuMaxCost);
        }

        private static long PackEntityId(int index, int version)
        {
            return ((long)index << 32) | (uint)version;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Log.Info("Destroyed");
        }
    }
}
