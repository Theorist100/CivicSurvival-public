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
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
#pragma warning disable CIVIC182 // Phase-neutral budget mutation helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182
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
    /// Validates player AirDefenseActionRequests, pays them synchronously and applies
    /// the confirmed intent on SpotterAggregateSystem in the same tick.
    ///
    /// Pause-safe (AXIOM 14): registered in ModificationEnd; payment goes through
    /// BudgetTransactionResolver on the main thread, the domain mutation through the
    /// aggregate's managed drain methods. The recurring Counter-OSINT daily charge
    /// keeps its retained SpotterBudgetIntent pipeline in GameSimulation
    /// (SpotterAggregateSystem → SpotterBudgetIngressSystem) — this route no longer
    /// creates player budget intents.
    ///
    /// Does NOT write SpotterData or singletons directly — mutations go through
    /// SpotterAggregateSystem's apply methods (single-writer preserved).
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.SpotterAction)]
    [TransientConsumerReconcile(typeof(AirDefenseActionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: spotter mutations and payment happen only while this consumer processes the request, so pre-consume load loss is reissuable.")]
    public partial class SpotterCommandIngressSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("SpotterCommandIngress");

        private EntityQuery m_RequestQuery;
        private EntityQuery m_CurrentActQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private IShadowWalletService m_WalletService = NullShadowWalletService.Instance;
        [System.NonSerialized] private SpotterAggregateSystem? m_Aggregate;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;

        // Ephemeral targeting RNG — non-deterministic post-load is acceptable
#pragma warning disable CIVIC066
        private Unity.Mathematics.Random m_SpotterRandom;
#pragma warning restore CIVIC066

        protected override void OnCreate()
        {
            base.OnCreate();

            m_RequestQuery = GetEntityQuery(
                ComponentType.ReadWrite<AirDefenseActionRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_DependencyWire = new CivicDependencyWire(nameof(SpotterCommandIngressSystem));
            m_SpotterRandom = Unity.Mathematics.Random.CreateFromIndex((uint)(World.GetHashCode() ^ 0x53504F54));

            RequireForUpdate(m_RequestQuery);
            // CurrentActSingleton is foundational always-on (Scenario not gated). Command
            // validation needs the real act — never run it with fabricated PreWar.
            RequireForUpdate<CurrentActSingleton>();

            Log.Info("Created (synchronous payment, pause-safe)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Aggregate ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<SpotterAggregateSystem>());
            m_WalletService = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
        }

        protected override void OnUpdateImpl()
        {
            if (m_Aggregate == null) return;
            if (m_RequestQuery.IsEmpty) return;

            var ecb = m_ModificationEndBarrier.CreateCommandBuffer();

            // Batch-local reservation: prevents double-targeting in the same tick
            var reservedTargets = new NativeHashSet<long>(4, Allocator.Temp);
            var activeCandidates = new NativeList<Entity>(Allocator.Temp);
            BuildActiveSpotterCandidates(activeCandidates);
            bool counterOSINTHandled = false; // prevents double toggle in same tick

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<AirDefenseActionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                bool success = false;
                var req = request.ValueRO;
                var failReason = "";

                switch (req.Action)
                {
                    case AirDefenseActionType.PerformSBUVisit:
                        success = ProcessSBUVisit(ecb, activeCandidates, reservedTargets, out failReason);
                        break;

                    case AirDefenseActionType.PerformEvacuation:
                        success = ProcessEvacuation(ecb, activeCandidates, reservedTargets, out failReason);
                        break;

                    case AirDefenseActionType.ToggleCounterOSINT:
                        if (counterOSINTHandled)
                        {
                            failReason = ReasonIds.SpotterDuplicateAction;
                        }
                        else
                        {
                            success = ProcessToggleCounterOSINT(ecb, out failReason);
                            counterOSINTHandled = true;
                        }
                        break;

                    default:
                        Log.Warn($"Unknown AirDefenseActionType: {req.Action}");
                        failReason = ReasonIds.SpotterUnknownAction;
                        break;
                }

                if (success)
                {
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.SpotterAction, SystemAPI.Time.ElapsedTime);
                }
                else
                {
                    string resultReason = string.IsNullOrEmpty(failReason) ? ReasonIds.SpotterActionFailed : failReason;
                    RequestResultEmitter.Emit(
                        ecb,
                        meta.ValueRO,
                        RequestKind.SpotterAction,
                        RequestStatus.Failed,
                        ReasonId.FromRuntime(resultReason),
                        SystemAPI.Time.ElapsedTime);
                }

                ecb.DestroyEntity(entity);
            }

            reservedTargets.Dispose();
            activeCandidates.Dispose();
            m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private bool ProcessSBUVisit(EntityCommandBuffer ecb, NativeList<Entity> activeCandidates, NativeHashSet<long> reservedTargets, out string failReason)
        {
            failReason = "";
            if (!SystemAPI.TryGetSingleton<SpotterCountermeasuresState>(out var cmState))
            {
                Log.Error("SpotterCountermeasuresState not found!");
                failReason = ReasonIds.SpotterSystemUnavailable;
                return false;
            }

            // TotalSBUVisits is bumped synchronously by the apply below, so the fresh
            // singleton read already reflects earlier requests in this same batch —
            // no in-flight/offset bookkeeping needed.
            int cost = GetSBUCost(cmState.TotalSBUVisits);
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

            if (!TryPayAndApply(ecb, new SpotterBudgetIntent
                {
                    Action = AirDefenseActionType.PerformSBUVisit,
                    Target = EntityRef.FromEntity(target.Value),
                    Cost = cost
                }, "Spotter.SBU", out failReason))
            {
                return false;
            }

            reservedTargets.Add(PackEntityId(target.Value.Index, target.Value.Version));

            Log.Info($"SBUVisit applied, cost: ${cost}");
            return true;
        }

        /// <summary>
        /// Synchronous pay-then-apply (AXIOM 14): deduct through BudgetTransactionResolver
        /// on the main thread, then apply the confirmed intent on the aggregate in the same
        /// tick. The apply can only fail on state that changed inside this very tick
        /// (singleton missing) — that edge queues an ECB refund and reports failure.
        /// </summary>
        private bool TryPayAndApply(EntityCommandBuffer ecb, SpotterBudgetIntent intent, string source, out string failReason)
        {
            var payment = BudgetTransactionResolver.Deduct(
                World,
                m_WalletService,
                intent.Cost,
                BudgetCategory.SpotterOps,
                source);
            if (!payment.Succeeded)
            {
                failReason = ReasonIds.SpotterInsufficientFunds;
                Log.Warn($"{source} FAILED - payment rejected (${intent.Cost})");
                return false;
            }

            if (!m_Aggregate!.ApplyConfirmedBudgetIntent(intent))
            {
                BudgetTransactionResolver.QueueRefund(ecb, intent.Cost, source, BudgetIncomeKind.Refund);
                failReason = ReasonIds.SpotterActionFailed;
                Log.Warn($"{source}: apply failed after payment — refund queued (${intent.Cost})");
                return false;
            }

            failReason = "";
            return true;
        }

        private bool ProcessEvacuation(EntityCommandBuffer ecb, NativeList<Entity> activeCandidates, NativeHashSet<long> reservedTargets, out string failReason)
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

            if (!TryPayAndApply(ecb, new SpotterBudgetIntent
                {
                    Action = AirDefenseActionType.PerformEvacuation,
                    Target = EntityRef.FromEntity(target.Value),
                    Cost = cost
                }, "Spotter.Evac", out failReason))
            {
                return false;
            }

            reservedTargets.Add(PackEntityId(target.Value.Index, target.Value.Version));

            Log.Info($"Evacuation applied for spotter {target.Value.Index}, cost: ${cost}");
            return true;
        }

        private bool ProcessToggleCounterOSINT(EntityCommandBuffer ecb, out string failReason)
        {
            failReason = "";
            if (!SystemAPI.TryGetSingleton<SpotterCountermeasuresState>(out var cmState))
            {
                Log.Error("SpotterCountermeasuresState not found!");
                failReason = ReasonIds.SpotterSystemUnavailable;
                return false;
            }

            int dailyCost = BalanceConfig.Current.Spotter.CounterOsintDailyCost;
            if (GetCurrentAct() < Act.Crisis)
            {
                failReason = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }
            if (!SpotterEligibility.CanToggleCounterOSINT(
                    IsCountermeasuresClosed(),
                    cmState.CounterOSINTActive,
                    dailyCost,
                    World,
                    out failReason))
            {
                return false;
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
                return true;
            }

            if (!CanAffordSpotterOps(dailyCost, out failReason))
            {
                Log.Warn("Counter-OSINT - insufficient funds");
                return false;
            }

            if (!TryPayAndApply(ecb, new SpotterBudgetIntent
                {
                    Action = AirDefenseActionType.ToggleCounterOSINT,
                    Target = EntityRef.FromEntity(Entity.Null),
                    Cost = dailyCost
                }, "Spotter.ToggleOSINT", out failReason))
            {
                return false;
            }

            Log.Info($"Counter-OSINT enabled (${dailyCost}/day)");
            return true;
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
            // No in-flight player budget intents to exclude any more: payment and apply
            // are synchronous, so a targeted spotter is deactivated before the next
            // request in this same batch scans the candidates.
            foreach (var (spotter, entity) in
                SystemAPI.Query<RefRO<SpotterData>>().WithNone<Deleted, Destroyed>().WithEntityAccess())
            {
                if (spotter.ValueRO.IsActive && !spotter.ValueRO.IsEvacuating)
                    candidates.Add(entity);
            }
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
