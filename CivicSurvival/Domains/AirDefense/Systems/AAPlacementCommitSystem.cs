using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
#pragma warning disable CIVIC182 // Phase-neutral budget refund helper lives with City budget service implementation.
using CivicSurvival.Services.City;
#pragma warning restore CIVIC182
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// Pause-safe AA placement commit. Converts resolved placement intents into
    /// AirDefenseInstallation entities in ModificationEnd, so UI placement does not
    /// depend on GameSimulation ticks.
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.AirDefensePlacement)]
    public partial class AAPlacementCommitSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("AAPlacementCommitSystem");

        private EntityQuery m_IntentQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private readonly System.Collections.Generic.HashSet<long> m_ProcessedBuildings = new();
        [System.NonSerialized] private int m_ProcessedBuildingsFrame = -1;
#pragma warning disable CIVIC229 // System reference — credit refund stays with the singleton owner
        private AirDefenseStateSystem m_StateSystem = null!;
#pragma warning restore CIVIC229

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<AAPlacementIntent>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            Log.Info("Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_StateSystem ??= FeatureRegistry.Instance.Require<AirDefenseStateSystem>();
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            m_StorageInfoLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);

            int frame = UnityEngine.Time.frameCount;
            if (frame != m_ProcessedBuildingsFrame)
            {
                m_ProcessedBuildings.Clear();
                m_ProcessedBuildingsFrame = frame;
            }

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            foreach (var (intentRef, entity) in
                SystemAPI.Query<RefRW<AAPlacementIntent>>()
                .WithEntityAccess())
            {
                ref var intent = ref intentRef.ValueRW;

                // A terminal reject (ApplyResolved && !ApplySucceeded — both serialized, so they
                // survive load unlike the runtime-only Applied guard) must NEVER re-enter the apply
                // path. Otherwise a DuplicateBuilding reject that survived a save (its refund could
                // not settle because the budget host was not ready, or its destroy ECB had not
                // played back yet) would re-apply after load and create a second installation once
                // the same-frame dedup set clears. SettleRejectRefund is re-entrant: it retries any
                // still-owed refund, then emits the terminal result and destroys the intent.
                // A terminal APPLY keeps ApplySucceeded=true, so it falls through to the Applied=false
                // reprocess that re-creates an installation lost to a pre-playback save — that
                // save-safe path must stay intact.
                if (intent.ApplyResolved && !intent.ApplySucceeded)
                {
                    if (!ecbCreated)
                    {
                        ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                        ecbCreated = true;
                    }
                    SettleRejectRefund(ecb, entity, ref intent);
                    continue;
                }

                if (intent.Applied)
                    continue;

                if (intent.RequiresBudget && !intent.BudgetResolved)
                    continue;

                if (intent.ReservedCreditKind != AAPlacementCreditKind.None && !intent.CreditResolved)
                    continue;

                if (!ecbCreated)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                intentRef.ValueRW.Applied = true;

                if (intent.RequiresBudget && !intent.BudgetSucceeded)
                {
                    RejectIntent(ecb, entity, ref intent, "BudgetFailed");
                    continue;
                }

                if (intent.ReservedCreditKind != AAPlacementCreditKind.None && !intent.CreditSucceeded)
                {
                    RejectIntent(ecb, entity, ref intent, "CreditFailed");
                    continue;
                }

                if (!BuildingExists(intent.Building.Index, intent.Building.Version))
                {
                    RejectIntent(ecb, entity, ref intent, "BuildingMissingBeforeApply");
                    continue;
                }

                if (!m_ProcessedBuildings.Add(BuildingIdentityKey.Pack(intent.Building.Index, intent.Building.Version)))
                {
                    RejectIntent(ecb, entity, ref intent, "DuplicateBuilding");
                    continue;
                }

                ApplyIntent(ecb, entity, ref intent);
            }

            if (ecbCreated)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private bool BuildingExists(int buildingIndex, int buildingVersion)
        {
            var building = new Entity { Index = buildingIndex, Version = buildingVersion };
            return m_StorageInfoLookup.Exists(building)
                && !m_DeletedLookup.HasComponent(building)
                && !m_DestroyedLookup.HasComponent(building);
        }

        private void ApplyIntent(EntityCommandBuffer ecb, Entity intentEntity, ref AAPlacementIntent intent)
        {
            intent.ApplyResolved = true;
            intent.ApplySucceeded = true;
            intent.TerminalReason = AAPlacementTerminalReason.Applied;

            // New installations start partially loaded so a freshly built gun cannot
            // bypass the graduated calm-phase refill (build → snap-full → demolish loop).
            // The rest of the magazine is delivered by the trickle pipeline in calm.
            // The Patriot is exempt: at $100k + crew 15 the build-snap-demolish loop is
            // closed by price, not a partial start, and a $100k purchase must work at once.
            // Clamp guarantees the CurrentAmmo <= MaxAmmo invariant regardless of the configured
            // fraction (a balance tweak putting a fraction outside [0,1] must not overfill).
            var balance = BalanceConfig.Current;
            float startFraction = intent.ResolvedType == AAType.PatriotSAM
                ? balance.AAUnits.PatriotStartAmmoFraction
                : balance.AirDefense.StartAmmoFraction;
            int startAmmo = System.Math.Clamp(
                (int)System.Math.Round(intent.MaxAmmo * startFraction),
                0,
                intent.MaxAmmo);

            // Refund base: only a budget-paid placement refunds cash on demolition.
            // Credit/Heritage placements (RequiresBudget == false) stamp PaidBudget = 0 so
            // selling them prints no money. PlacedGameHours seeds the vanilla-style refund
            // decay (persisted TotalGameHours, not ElapsedTime which resets on load).
            int paidBudget = intent.RequiresBudget ? intent.Cost : 0;
            // If the clock is not active yet, stamp 0 — refund decay then treats the unit as
            // long-placed (no refund), the safe direction.
            if (!GameTimeSystem.TryGetGameHours(out float placedGameHours))
                placedGameHours = 0f;

            var aaEntity = ecb.CreateEntity();
            ecb.AddComponent(aaEntity, new AirDefenseInstallation
            {
                Building = intent.Building,
                Type = intent.ResolvedType,
                Range = intent.Range,
                InterceptChanceShahed = intent.InterceptChanceShahed,
                InterceptChanceBallistic = intent.InterceptChanceBallistic,
                CurrentAmmo = startAmmo,
                MaxAmmo = intent.MaxAmmo,
                CooldownDuration = intent.CooldownDuration,
                CrewAssigned = 0,
                CrewRequired = intent.CrewRequired,
                PaidBudget = paidBudget,
                PlacedGameHours = placedGameHours
            });

            ecb.AddComponent(aaEntity, new AirDefenseCooldown { ReadyAtGameSeconds = 0 });

            ecb.AddComponent(aaEntity, new RequestCrewTag
            {
                CrewRequired = intent.CrewRequired
            });

            ecb.AddComponent<Simulate>(aaEntity);
            m_StateSystem.RecordUiStatsInstallationAdded(intent.ResolvedType, startAmmo, intent.MaxAmmo);

            if (intent.RequestId != 0)
            {
                EmitReportedPlacementResult(ecb, intent.RequestId, RequestStatus.Success, ReasonId.None);
                RequestResultBridge.PublishTerminalForBegun(
                    RequestResultBridge.AirDefensePlacement,
                    intent.RequestId,
                    RequestStatus.Success);
            }

            ecb.DestroyEntity(intentEntity);
            Log.Info($"{intent.ResolvedType} confirmed: building={intent.Building.Index}, crew={intent.CrewRequired}, cost={intent.Cost}");
        }

        private void RejectIntent(EntityCommandBuffer ecb, Entity intentEntity, ref AAPlacementIntent intent, string reason)
        {
            intent.ApplyResolved = true;
            intent.ApplySucceeded = false;
            intent.TerminalReason = MapTerminalReason(reason);

            if (ShouldDeletePlacedObjectOnReject(intent.TerminalReason))
                DeletePlacedObjectIfPresent(ecb, intent);

            SettleRejectRefund(ecb, intentEntity, ref intent);
        }

        /// <summary>
        /// Settles a terminal reject: emits the player-facing result, refunds the compensating
        /// value (credit OR budget — mutually exclusive per intent), and destroys the intent.
        /// Re-entrant across the OnUpdateImpl top-of-loop retry and the 2x-3x same-frame replays.
        ///
        /// The terminal emit is synchronous and pause-safe (the button settles the same tick),
        /// gated on the persisted <see cref="AAPlacementIntent.TerminalEmitted"/> so it fires
        /// exactly once. The budget refund is deferred through BudgetResolutionSystem (the single
        /// writer S2490 established) — it is observed via its retained BudgetAddFundsResult, so
        /// RefundResolved is set on observation, never optimistically. The intent is destroyed only
        /// once BOTH the terminal answer is emitted AND the refund has settled; if the refund is
        /// still draining it stays terminal-but-unrefunded (ApplyResolved &amp;&amp; !ApplySucceeded
        /// &amp;&amp; !RefundResolved) without re-opening the apply path, and the retry drains it next tick.
        /// </summary>
        private void SettleRejectRefund(EntityCommandBuffer ecb, Entity intentEntity, ref AAPlacementIntent intent)
        {
            if (!intent.RefundResolved
                && intent.ReservedCreditKind != AAPlacementCreditKind.None
                && intent.CreditSucceeded)
            {
                // Credit refund is a synchronous write to the owner-maintained credits singleton.
                intent.RefundSucceeded = m_StateSystem.RefundPlacementCredit(intent.ReservedCreditKind, intent.PlacementId);
                intent.RefundResolved = true;
                if (!intent.RefundSucceeded)
                    // AirDefenseCreditsSingleton is single-owner and never destroyed, so a failed
                    // refund here means that invariant was violated. Retrying cannot resurrect the
                    // singleton — it would only loop the intent forever — so settle terminally and
                    // surface the violation instead of silently leaking the placement credit.
                    Log.Error($"AA placement credit refund failed: credits singleton unavailable (building={intent.Building.Index}, reason={intent.TerminalReason}) — single-owner invariant violated");
            }

            // Budget refund is owed only for rejects that happen AFTER a successful payment
            // (building lost or duplicate); BudgetFailed/CreditFailed never paid. Gate on the
            // persisted TerminalReason (not the call-time string) so the retry path matches.
            bool budgetRefundOwed =
                intent.RequiresBudget
                && intent.BudgetSucceeded
                && (intent.TerminalReason == AAPlacementTerminalReason.BuildingMissingBeforeApply
                    || intent.TerminalReason == AAPlacementTerminalReason.DuplicateBuilding)
                && intent.Cost > 0;

            if (!intent.RefundResolved && budgetRefundOwed)
            {
                if (TryDrainBudgetRefundResult(ecb, ref intent, out var refundSucceeded))
                {
                    // Result observed — settle on the actual outcome (no optimistic completion).
                    intent.RefundSucceeded = refundSucceeded;
                    intent.RefundResolved = true;
                    if (refundSucceeded)
                        Log.Info($"Budget refund ${intent.Cost} for building {intent.Building.Index} (reason: {intent.TerminalReason})");
                    else
                        Log.Warn($"Budget refund terminal failure for building {intent.Building.Index} (reason: {intent.TerminalReason})");
                }
                else
                {
                    // Not drained yet (request still queued for the GameSimulation drain, or the
                    // budget host is not ready on the first post-load tick). Queue once and leave
                    // RefundResolved=false; the top-of-loop retry drains it next tick. The terminal
                    // emit below is deliberately NOT gated on this — the player sees the reject
                    // immediately (in pause), while the money settles later.
                    QueueBudgetRefundIfMissing(ecb, in intent);
                }
            }

            if (!intent.RefundResolved && !budgetRefundOwed)
            {
                // No compensating value is owed for this terminal path.
                intent.RefundResolved = true;
                intent.RefundSucceeded = true;
            }

            if (!intent.TerminalEmitted)
            {
                if (intent.RequestId != 0)
                {
                    var reasonId = TerminalReasonToReasonId(intent.TerminalReason);
                    EmitReportedPlacementResult(ecb, intent.RequestId, RequestStatus.Failed, reasonId);
                    RequestResultBridge.PublishTerminalForBegun(
                        RequestResultBridge.AirDefensePlacement,
                        intent.RequestId,
                        RequestStatus.Failed,
                        reasonId.ToString());
                }

                intent.TerminalEmitted = true;
                Log.Warn($"AA placement rejected: building={intent.Building.Index}, reason={intent.TerminalReason}");
            }

            // Destroy only once the answer is emitted AND the refund has settled. TerminalEmitted
            // blocks a second emit on the 2x-3x same-frame replays / post-load ApplyResolved
            // re-entry, and the destroy waits for RefundResolved, so neither emit nor destroy doubles.
            if (intent.RefundResolved && intent.TerminalEmitted)
                ecb.DestroyEntity(intentEntity);
        }

        /// <summary>
        /// Observes the retained BudgetAddFundsResult for this intent's refund request (matched by
        /// the per-placement OperationKey, not by building) and destroys the result entity. Returns
        /// false until BudgetResolutionSystem has drained the request — in pause it never ticks, so
        /// the refund settles after unpause while the terminal emit has already answered the UI.
        /// </summary>
        private bool TryDrainBudgetRefundResult(EntityCommandBuffer ecb, ref AAPlacementIntent intent, out bool succeeded)
        {
            succeeded = false;
            string operationKey = BudgetRefundOperationKey(intent.PlacementId);
            foreach (var (requestRef, resultRef, entity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                succeeded = resultRef.ValueRO.Succeeded;
                ecb.DestroyEntity(entity);
                return true;
            }

            return false;
        }

        private void QueueBudgetRefundIfMissing(EntityCommandBuffer ecb, in AAPlacementIntent intent)
        {
            string operationKey = BudgetRefundOperationKey(intent.PlacementId);
            foreach (var requestRef in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (requestRef.ValueRO.OperationKey.ToString() == operationKey)
                    return;
            }

            // A non-positive cost simply means there is no refund to queue (the only
            // failure mode), so the bool is discarded intentionally.
            _ = BudgetEmitter.TryQueueAddFunds(
                ecb,
                intent.Cost,
                BudgetSource.AAInstallRefund,
                BudgetIncomeKind.Refund,
                operationKey,
                out _,
                BudgetResultMode.RetainResult);
        }

        private static string BudgetRefundOperationKey(int placementId)
            => $"AAPlacementRefund:{placementId}";

        private void DeletePlacedObjectIfPresent(EntityCommandBuffer ecb, in AAPlacementIntent intent)
        {
            var building = intent.Building.ToEntity();
            if (!m_StorageInfoLookup.Exists(building)
                || m_DeletedLookup.HasComponent(building)
                || m_DestroyedLookup.HasComponent(building))
            {
                return;
            }

            ecb.AddComponent<Deleted>(building);
        }

        private static bool ShouldDeletePlacedObjectOnReject(AAPlacementTerminalReason reason)
        {
            // DuplicateBuilding rejects a second intent that targets an already accepted
            // installation; the visible object belongs to the earlier successful intent.
            return reason != AAPlacementTerminalReason.DuplicateBuilding;
        }

        private void EmitReportedPlacementResult(
            EntityCommandBuffer ecb,
            int requestId,
            RequestStatus status,
            ReasonId reasonId)
        {
            var resultEntity = status == RequestStatus.Success
                ? RequestResultEmitter.EmitSuccess(
                    ecb,
                    requestId,
                    RequestKind.AirDefensePlacement,
                    SystemAPI.Time.ElapsedTime)
                : RequestResultEmitter.Emit(
                    ecb,
                    requestId,
                    RequestKind.AirDefensePlacement,
                    status,
                    reasonId,
                    SystemAPI.Time.ElapsedTime);
            ecb.AddComponent<Reported>(resultEntity);
        }

        private static ReasonId TerminalReasonToReasonId(AAPlacementTerminalReason reason)
        {
            if (reason == AAPlacementTerminalReason.BudgetFailed)
                return ReasonIds.AaBudgetFailed;
            if (reason == AAPlacementTerminalReason.BuildingMissingBeforeApply)
                return ReasonIds.AaBuildingLost;
            if (reason == AAPlacementTerminalReason.DuplicateBuilding)
                return ReasonIds.AaDuplicate;
            return ReasonIds.AaPlacementFailed;
        }

        private static AAPlacementTerminalReason MapTerminalReason(string raw)
        {
            if (raw == "BudgetFailed")
                return AAPlacementTerminalReason.BudgetFailed;
            if (raw == "CreditFailed")
                return AAPlacementTerminalReason.CreditFailed;
            if (raw == "BuildingMissingBeforeApply")
                return AAPlacementTerminalReason.BuildingMissingBeforeApply;
            if (raw == "DuplicateBuilding")
                return AAPlacementTerminalReason.DuplicateBuilding;
            return AAPlacementTerminalReason.PlacementFailed;
        }
    }
}
