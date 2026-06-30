using Game;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Features.Wellbeing;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using Unity.Collections;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Cognitive.Core.Systems
{
    /// <summary>
    /// Sole owner of <see cref="HeroDeploymentState"/> singleton.
    ///
    /// Owns the entire hero ("The Voice") lifecycle: deploy / recall / mode switch,
    /// the budget round-trip via BudgetDeductRequest/Result, and the GretaDeployed
    /// penalty bookkeeping per district.
    ///
    /// Split out of CognitiveStateSystem so the cognitive infection/recovery loop
    /// owns its own singleton (CognitiveState) and the hero feature owns a separate
    /// one. CIVIC175 stays clean — each singleton has one writer.
    ///
    /// Ordered before CognitiveStateSystem so the throttled cognitive loop sees
    /// the latest HeroStatus when it computes effective rates.
    /// </summary>
    [SingletonOwner(typeof(HeroDeploymentState))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore | SingletonLifecyclePhase.ReconcileAfterLoad,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    [ActIndependent]
    [HandlesRequestKind(RequestKind.HeroAction)]
    [TransientConsumerReconcile(typeof(HeroActionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: hero state and retained budget intents are emitted only after this consumer runs; a pre-consume load has no committed side-effect.")]
    public partial class HeroDeploymentSystem : ThrottledSystemBase, IResettable, IPostLoadValidation, ICivicSingletonOwner<HeroDeploymentState>
    {
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        private const double PenaltySystemRetryDelaySeconds = 60d;

        private static readonly LogContext Log = new("HeroDeploymentSystem");

        private static string HeroStatusName(HeroStatus s) => s switch
        {
            HeroStatus.Inactive => "INACTIVE",
            HeroStatus.Deployed => "DEPLOYED",
            HeroStatus.Lecturing => "LECTURING",
            _ => "UNKNOWN"
        };

        private EntityQuery m_HeroStateQuery;
        private EntityQuery m_CognitiveStateQuery;
        private EntityQuery m_HeroActionRequestQuery;
        private EntityQuery m_ResolvedBudgetQuery;
        private EntityQuery m_PendingHeroDeployBudgetQuery;
        private EntityQuery m_CurrentActQuery;
        // Reused per-drain refund accumulator — avoids per-frame List allocation (CIVIC050).
        private readonly System.Collections.Generic.List<HeroRefundEntry> m_RefundsToQueue = new();
        private BufferLookup<CognitiveIntegrityBuffer> m_CogIntegrityBufferLookup;

        private DistrictPenaltySystem m_PenaltySystem = null!;
#pragma warning disable CIVIC324 // Runtime retry throttle; recomputed from live time and never part of hero save state.
        [System.NonSerialized] private double m_NextPenaltySystemRetryTime;
#pragma warning restore CIVIC324
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;

        protected override void OnCreate()
        {
            base.OnCreate();

            HeroDeploymentState.EnsureExists(EntityManager);

            m_HeroStateQuery = GetEntityQuery(ComponentType.ReadWrite<HeroDeploymentState>());
            m_CognitiveStateQuery = GetEntityQuery(ComponentType.ReadOnly<CognitiveState>());
            m_HeroActionRequestQuery = GetEntityQuery(ComponentType.ReadWrite<HeroActionRequest>());
            m_ResolvedBudgetQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<BudgetDeductResult>(),
                ComponentType.ReadOnly<HeroDeployIntent>(),
                ComponentType.ReadWrite<PendingPhase>());
            m_PendingHeroDeployBudgetQuery = GetEntityQuery(
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<HeroDeployIntent>(),
                ComponentType.Exclude<BudgetDeductResult>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_CogIntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            var eventBus = EventBus;
            if (eventBus != null)
                eventBus.Subscribe<DistrictLifecycleEvent>(OnDistrictLifecycle);


            Log.Info(" Created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            HeroDeploymentState.EnsureExists(EntityManager);
            if (!TryResolvePenaltySystem(force: true))
                Log.Warn(" DistrictPenaltySystem not found — Gerda penalty unavailable");
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
            {
                var eventBus = EventBus;
                eventBus?.Unsubscribe<DistrictLifecycleEvent>(OnDistrictLifecycle);
            }

            base.OnDestroy();
            Log.Info(" Destroyed");
        }

        protected override bool ShouldSkipUpdate()
        {
            // Bypass throttle when there is hero work pending — player input should not wait
            // up to UPDATE_INTERVAL_500_MS frames for a deploy/recall response.
            if (!m_HeroActionRequestQuery.IsEmpty || !m_ResolvedBudgetQuery.IsEmpty)
                ForceNextUpdate();
            return false;
        }

        protected override void OnThrottledUpdate()
        {
            if (!TryResolvePenaltySystem())
            {
                // Resolution still pending — OnStartRunning emitted the one-shot warn at boot,
                // the Try-method rate-limits actual lookups to once per 60s, and downstream
                // penalty paths (ApplyGerdaPenaltyToAllDistricts / OnDistrictLifecycle) all
                // defensively no-op on null. Continuing the update is safe.
            }

            // CIVIC185: keep buffer lookup current — ApplyGerdaPenaltyToAllDistricts may run from
            // event handlers (DistrictLifecycle, ResetState, ValidateAfterLoad) where stale lookup
            // would miss recent structural changes.
            m_CogIntegrityBufferLookup.Update(this);

            if (!SystemAPI.TryGetSingletonRW<HeroDeploymentState>(out var stateRef))
                return;

            DrainResolvedHeroRefundRequests();
            DrainResolvedHeroDeployRequests(ref stateRef);
            ProcessHeroActionRequests(ref stateRef);
        }

        // ============ Request Processing ============

        private void ProcessHeroActionRequests(ref RefRW<HeroDeploymentState> stateRef)
        {
            if (m_HeroActionRequestQuery.IsEmpty) return;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            bool hasPendingHeroDeployBudget = HasPendingHeroDeployBudget();

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<HeroActionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                FixedString64Bytes failReason = default;
                RequestStatus status = request.ValueRO.Action switch
                {
                    HeroActionType.Deploy => ProcessHeroDeploy(ref stateRef.ValueRW, request.ValueRO.Mode, meta.ValueRO, ref hasPendingHeroDeployBudget, out failReason),
                    HeroActionType.Recall => ProcessHeroRecall(ref stateRef.ValueRW, hasPendingHeroDeployBudget, out failReason)
                        ? RequestStatus.Success
                        : RequestStatus.Failed,
                    HeroActionType.SetMode => ProcessHeroSetMode(ref stateRef.ValueRW, request.ValueRO.Mode, meta.ValueRO, ref hasPendingHeroDeployBudget, out failReason),
                    _ => RequestStatus.Failed
                };

                switch (status)
                {
                    case RequestStatus.Success:
                        RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.HeroAction, SystemAPI.Time.ElapsedTime);
                        break;
                    case RequestStatus.Pending:
                        break;
                    default:
                        RequestResultEmitter.EmitFixedReason(ecb, meta.ValueRO, RequestKind.HeroAction, RequestStatus.Failed, failReason, SystemAPI.Time.ElapsedTime);
                        break;
                }
                ecb.DestroyEntity(entity);
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        /// <summary>
        /// Returns true if a hero deploy budget request exists but has not been resolved yet.
        /// Blocks Recall/SetMode while budget confirmation is pending.
        /// </summary>
        private bool HasPendingHeroDeployBudget()
        {
            return !m_PendingHeroDeployBudgetQuery.IsEmptyIgnoreFilter;
        }

        /// <summary>
        /// Drain resolved budget entities for hero deploy requests.
        /// Success applies the deferred hero state; failure emits a terminal request result.
        /// </summary>
        private void DrainResolvedHeroDeployRequests(ref RefRW<HeroDeploymentState> stateRef)
        {
            if (m_ResolvedBudgetQuery.IsEmpty) return;

            EntityCommandBuffer? ecb = null;
            var refundsToQueue = m_RefundsToQueue;
            refundsToQueue.Clear();

            foreach (var (_, result, intent, phase, entity) in
                SystemAPI.Query<RefRO<BudgetDeductRequest>, RefRO<BudgetDeductResult>, RefRW<HeroDeployIntent>, RefRW<PendingPhase>>()
                .WithEntityAccess())
            {
                ecb ??= m_GameSimulationEndBarrier.CreateCommandBuffer();
                bool hasMeta = SystemAPI.HasComponent<RequestMeta>(entity);
                var meta = hasMeta ? SystemAPI.GetComponent<RequestMeta>(entity) : default;
                if (phase.ValueRO.Value == PendingPhaseValue.Applied || phase.ValueRO.Value == PendingPhaseValue.Confirmed)
                {
                    ecb.Value.DestroyEntity(entity);
                    continue;
                }

                if (result.ValueRO.Succeeded)
                {
                    var mode = intent.ValueRO.Mode;
                    if (mode == HeroStatus.Inactive)
                    {
                        intent.ValueRW.DomainRejected = true;
                        intent.ValueRW.RefundQueued = result.ValueRO.Amount > 0;
                        refundsToQueue.Add(new HeroRefundEntry(result.ValueRO.Amount, ResolveHeroRefundOperationKey(entity, hasMeta, meta)));
                        phase.ValueRW.Value = PendingPhaseValue.Confirmed;
                        if (hasMeta)
                            RequestResultEmitter.EmitFixedReason(ecb.Value, meta, RequestKind.HeroAction, RequestStatus.Failed, ReasonIds.HeroBudgetPending.ToFixedString(), SystemAPI.Time.ElapsedTime);
                        ecb.Value.DestroyEntity(entity);
                        continue;
                    }

                    if (stateRef.ValueRO.HeroStatus == mode)
                    {
                        // Save/load may replay a retained result after the durable hero
                        // singleton was already updated but before ECB cleanup played back.
                        intent.ValueRW.DomainApplied = true;
                        phase.ValueRW.Value = PendingPhaseValue.Applied;
                        ecb.Value.DestroyEntity(entity);
                        continue;
                    }

                    if (stateRef.ValueRO.HeroStatus != HeroStatus.Inactive)
                    {
                        intent.ValueRW.DomainRejected = true;
                        intent.ValueRW.RefundQueued = result.ValueRO.Amount > 0;
                        refundsToQueue.Add(new HeroRefundEntry(result.ValueRO.Amount, ResolveHeroRefundOperationKey(entity, hasMeta, meta)));
                        phase.ValueRW.Value = PendingPhaseValue.Confirmed;
                        if (hasMeta)
                            RequestResultEmitter.EmitFixedReason(ecb.Value, meta, RequestKind.HeroAction, RequestStatus.Failed, ReasonIds.HeroBudgetPending.ToFixedString(), SystemAPI.Time.ElapsedTime);
                        ecb.Value.DestroyEntity(entity);
                        continue;
                    }

                    stateRef.ValueRW.HeroStatus = mode;
                    if (mode == HeroStatus.Deployed)
                        ApplyGerdaPenaltyToAllDistricts(register: true);

#pragma warning disable CIVIC062 // One-shot telemetry event, not per-frame
                    EventBus?.SafePublish(new HeroDeployedEvent(
                        HeroStatusName(mode),
                        ToHeroEventCost(result.ValueRO.Amount)), "HeroDeploymentSystem");
                    EventBus?.SafePublish(new HeroModeChangedEvent(
                        HeroStatusName(HeroStatus.Inactive),
                        HeroStatusName(mode)), "HeroDeploymentSystem");
#pragma warning restore CIVIC062
                    Log.Info("Hero deploy confirmed (budget paid)");
                    intent.ValueRW.DomainApplied = true;
                    phase.ValueRW.Value = PendingPhaseValue.Applied;
                    if (hasMeta)
                        RequestResultEmitter.EmitSuccess(ecb.Value, meta, RequestKind.HeroAction, SystemAPI.Time.ElapsedTime);
                }
                else
                {
                    Log.Warn($"Hero deploy budget deduction of ${result.ValueRO.Amount} failed");
                    intent.ValueRW.ChargeFailed = true;
                    phase.ValueRW.Value = PendingPhaseValue.Confirmed;
                    if (hasMeta)
                        RequestResultEmitter.EmitFixedReason(ecb.Value, meta, RequestKind.HeroAction, RequestStatus.Failed, ReasonIds.HeroInsufficientFunds.ToFixedString(), SystemAPI.Time.ElapsedTime);
                }

                ecb.Value.DestroyEntity(entity);
            }

            for (int i = 0; i < refundsToQueue.Count; i++)
                QueueHeroRefund(refundsToQueue[i].Amount, refundsToQueue[i].OperationKey);

            if (ecb.HasValue)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void DrainResolvedHeroRefundRequests()
        {
            EntityCommandBuffer? ecb = null;
            foreach (var (result, intent, phase, entity) in
                SystemAPI.Query<RefRO<BudgetAddFundsResult>, RefRW<HeroDeployIntent>, RefRW<PendingPhase>>()
                .WithAll<BudgetAddFundsRequest>()
                .WithEntityAccess())
            {
                if (!intent.ValueRO.IsRefundRequest)
                    continue;

                if (!result.ValueRO.Succeeded)
                {
                    intent.ValueRW.RefundFailed = true;
                    phase.ValueRW.Value = PendingPhaseValue.Confirmed;
                    Log.Warn($"Hero deploy retained refund failed: op={result.ValueRO.OperationKey.ToString()} amount=${result.ValueRO.Amount:N0}");

                    ecb ??= m_GameSimulationEndBarrier.CreateCommandBuffer();
                    if (SystemAPI.HasComponent<RequestMeta>(entity))
                    {
                        var meta = SystemAPI.GetComponent<RequestMeta>(entity);
                        RequestResultEmitter.EmitFixedReason(
                            ecb.Value,
                            meta,
                            RequestKind.HeroAction,
                            RequestStatus.Failed,
                            ReasonIds.InternalError.ToFixedString(),
                            SystemAPI.Time.ElapsedTime);
                    }
                    ecb.Value.DestroyEntity(entity);
                    continue;
                }

                phase.ValueRW.Value = PendingPhaseValue.Applied;
                ecb ??= m_GameSimulationEndBarrier.CreateCommandBuffer();
                ecb.Value.DestroyEntity(entity);
            }

            if (ecb.HasValue)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void QueueHeroRefund(long amount, string operationKey)
        {
            if (amount <= 0 || HasHeroRefundRequest(operationKey))
                return;

            // This request is created synchronously so a save between deploy-result
            // handling and ECB playback cannot lose the refund.
            if (!BudgetEmitter.TryQueueAddFundsImmediate(
                    World,
                    amount,
                    "HeroDeployRefund",
                    BudgetIncomeKind.Refund,
                    operationKey,
                    out var refundEntity,
                    BudgetResultMode.RetainResult,
                    SystemAPI.Time.ElapsedTime))
                return;

            EntityManager.AddComponentData(refundEntity, new HeroDeployIntent
            {
                Mode = HeroStatus.Inactive,
                RefundQueued = true,
                IsRefundRequest = true
            });
            EntityManager.AddComponentData(refundEntity, new PendingOperationTag());
            EntityManager.AddComponentData(refundEntity, new PendingPhase { Value = PendingPhaseValue.Queued });
        }

        private bool HasHeroRefundRequest(string operationKey)
        {
            foreach (var (request, intent) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<HeroDeployIntent>>())
            {
                if (intent.ValueRO.IsRefundRequest
                    && request.ValueRO.OperationKey.ToString() == operationKey)
                    return true;
            }

            return false;
        }

        private static string ResolveHeroRefundOperationKey(Entity budgetEntity, bool hasMeta, in RequestMeta meta)
            => hasMeta && meta.RequestId > 0
                ? $"HeroDeployRefund:{meta.RequestId}"
                : $"HeroDeployRefund:{budgetEntity.Index}:{budgetEntity.Version}";

        private readonly struct HeroRefundEntry
        {
            public readonly long Amount;
            public readonly string OperationKey;

            public HeroRefundEntry(long amount, string operationKey)
            {
                Amount = amount;
                OperationKey = operationKey;
            }
        }

        private static int ToHeroEventCost(long amount)
        {
            if (amount >= int.MaxValue)
                return int.MaxValue;
            if (amount <= int.MinValue)
                return int.MinValue;
            return checked((int)amount);
        }

        // ============ Hero Operations ============

        private RequestStatus ProcessHeroDeploy(
            ref HeroDeploymentState state,
            HeroStatus mode,
            in RequestMeta requestMeta,
            ref bool hasPendingHeroDeployBudget,
            out FixedString64Bytes failReason)
        {
            failReason = default;
            var currentAct = Act.Crisis;
            if (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
                currentAct = actSingleton.CurrentAct;

            if (!HeroEligibility.CanDeployHero(
                    currentAct,
                    hasPendingHeroDeployBudget,
                    mode,
                    state.HeroStatus,
                    state.HeroDeployCost,
                    World,
                    out var reasonId))
            {
                Log.Info($"Cannot deploy hero: {reasonId}");
                failReason = reasonId;
                return RequestStatus.Failed;
            }

            int cost = state.HeroDeployCost;
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            var budgetEntity = ecb.QueuePendingOperation(new HeroDeployIntent { Mode = mode });
            if (!BudgetEmitter.TryQueueDeductOnEntity(
                    World,
                    ecb,
                    budgetEntity,
                    cost,
                    BudgetCategory.CognitiveOps,
                    BudgetPriority.PlayerAction,
                    "Cognitive.HeroDeploy",
                    out _,
                    requestMeta,
                    BudgetResultMode.RetainResult))
            {
                ecb.DestroyEntity(budgetEntity);
                Log.Info($" Cannot deploy hero: insufficient funds (need ${cost:N0})");
                failReason = ReasonIds.HeroInsufficientFunds.ToFixedString();
                return RequestStatus.Failed;
            }

            string modeStr = mode == HeroStatus.Deployed ? "DEPLOYED (countering propaganda, -15% happiness)" : "LECTURING (boosting recovery)";
            Log.Info($" Hero {modeStr}, pending budget confirmation. Cost: ${cost:N0}");
            hasPendingHeroDeployBudget = true;

            return RequestStatus.Pending;
        }

        private bool ProcessHeroRecall(ref HeroDeploymentState state, bool hasPendingHeroDeployBudget, out FixedString64Bytes failReason)
        {
            failReason = default;

            if (hasPendingHeroDeployBudget)
            {
                Log.Info("Cannot recall hero: deploy budget confirmation pending");
                failReason = ReasonIds.HeroBudgetPending.ToFixedString();
                return false;
            }

            if (state.HeroStatus == HeroStatus.Inactive)
            {
                Log.Debug(" Hero not deployed, nothing to recall");
                failReason = ReasonIds.HeroNotDeployed.ToFixedString();
                return false;
            }

            if (state.HeroStatus == HeroStatus.Deployed)
                ApplyGerdaPenaltyToAllDistricts(register: false);

            state.HeroStatus = HeroStatus.Inactive;
            Log.Info(" Hero RECALLED");

            EventBus?.SafePublish(new HeroRecalledEvent(), "HeroDeploymentSystem");
            return true;
        }

        private RequestStatus ProcessHeroSetMode(
            ref HeroDeploymentState state,
            HeroStatus mode,
            in RequestMeta requestMeta,
            ref bool hasPendingHeroDeployBudget,
            out FixedString64Bytes failReason)
        {
            failReason = default;

            if (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton) && actSingleton.CurrentAct == Act.PreWar)
            {
                failReason = ReasonIds.HeroPrewarLocked.ToFixedString();
                return RequestStatus.Failed;
            }

            if (hasPendingHeroDeployBudget)
            {
                Log.Info("Cannot change hero mode: deploy budget confirmation pending");
                failReason = ReasonIds.HeroBudgetPending.ToFixedString();
                return RequestStatus.Failed;
            }

            if (mode == HeroStatus.Inactive)
                return ProcessHeroRecall(ref state, hasPendingHeroDeployBudget, out failReason)
                    ? RequestStatus.Success
                    : RequestStatus.Failed;

            if (state.HeroStatus == HeroStatus.Inactive)
                return ProcessHeroDeploy(ref state, mode, requestMeta, ref hasPendingHeroDeployBudget, out failReason);

            var previousMode = state.HeroStatus;

            if (previousMode == HeroStatus.Deployed && mode == HeroStatus.Lecturing)
                ApplyGerdaPenaltyToAllDistricts(register: false);
            else if (previousMode == HeroStatus.Lecturing && mode == HeroStatus.Deployed)
                ApplyGerdaPenaltyToAllDistricts(register: true);

            state.HeroStatus = mode;

            string modeStr = mode == HeroStatus.Deployed ? "DEPLOYED (-50% infection, -15% happiness)" : "LECTURING (+50% recovery)";
            Log.Info($" Hero mode changed to {modeStr}");

#pragma warning disable CIVIC062 // One-shot telemetry event, not per-frame
            EventBus?.SafePublish(new HeroModeChangedEvent(
                previousMode.ToString().ToUpperInvariant(),
                mode.ToString().ToUpperInvariant()), "HeroDeploymentSystem");
#pragma warning restore CIVIC062

            return RequestStatus.Success;
        }

        private bool TryResolvePenaltySystem(bool force = false)
        {
            if (m_PenaltySystem != null)
                return true;

            double now = SystemAPI.Time.ElapsedTime;
            if (!force && now < m_NextPenaltySystemRetryTime)
                return false;

            m_NextPenaltySystemRetryTime = now + PenaltySystemRetryDelaySeconds;
            m_PenaltySystem = World.GetExistingSystemManaged<DistrictPenaltySystem>();
            return m_PenaltySystem != null;
        }

        /// <summary>
        /// Apply or remove Gerda penalty to all tracked districts.
        /// "Lightning Rod" mechanic: city-wide annoyance when she's countering propaganda.
        /// Reads CognitiveIntegrityBuffer from CognitiveState entity (owned by CognitiveStateSystem).
        /// </summary>
        private void ApplyGerdaPenaltyToAllDistricts(bool register)
        {
            if (m_PenaltySystem == null)
            {
                Log.Warn(" PenaltySystem not available, cannot apply Gerda penalty");
                return;
            }

            if (!m_CognitiveStateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return;

            m_CogIntegrityBufferLookup.Update(this);
            if (!m_CogIntegrityBufferLookup.TryGetBuffer(stateEntity, out var integrityBuffer))
                return;

            int count = 0;
            for (int i = 0; i < integrityBuffer.Length; i++)
            {
                int districtIndex = integrityBuffer[i].DistrictIndex;
                if (register)
                    m_PenaltySystem.RegisterPenalty(districtIndex, PenaltySource.GretaDeployed);
                else
                    m_PenaltySystem.RemovePenalty(districtIndex, PenaltySource.GretaDeployed);
                count++;
            }

            string action = register ? "registered" : "removed";
            Log.Info($" Gerda penalty {action} for {count} districts");
        }

        // ============ District Lifecycle ============

        private void OnDistrictLifecycle(DistrictLifecycleEvent evt)
        {
            if (!Enabled) return;
            if (m_PenaltySystem == null)
            {
                Log.Warn(" PenaltySystem not available — skipping Gerda penalty for district lifecycle event");
                return;
            }
            if (!m_HeroStateQuery.TryGetSingleton<HeroDeploymentState>(out var state)) return;
            if (state.HeroStatus != HeroStatus.Deployed) return;

            if (evt.Lifecycle == DistrictLifecycle.Created)
                m_PenaltySystem.RegisterPenalty(evt.DistrictIndex, PenaltySource.GretaDeployed);
            else if (evt.Lifecycle == DistrictLifecycle.Destroyed)
                m_PenaltySystem.RemovePenalty(evt.DistrictIndex, PenaltySource.GretaDeployed);
        }

        // ============ IResettable ============

        public void ResetState()
        {
            m_NextPenaltySystemRetryTime = 0d;

            if (!m_HeroStateQuery.TryGetSingletonRW<HeroDeploymentState>(out var stateRef))
                return;

            // Remove GretaDeployed penalties if hero was active
            if (stateRef.ValueRO.HeroStatus == HeroStatus.Deployed)
                ApplyGerdaPenaltyToAllDistricts(register: false);

            stateRef.ValueRW = HeroDeploymentState.Default;
            Log.Info(" State reset");
        }

        public bool CanDeployHero(HeroStatus mode, out string reasonId)
        {
            reasonId = "";
            if (!m_HeroStateQuery.TryGetSingleton<HeroDeploymentState>(out var state))
            {
                reasonId = ReasonIds.HeroSystemUnavailable;
                return false;
            }

            var currentAct = Act.Crisis;
            if (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
                currentAct = actSingleton.CurrentAct;

            return HeroEligibility.CanDeployHero(
                currentAct,
                HasPendingHeroDeployBudget(),
                mode,
                state.HeroStatus,
                state.HeroDeployCost,
                World,
                out reasonId);
        }

        public bool CanRecallHero(out string reasonId)
        {
            reasonId = "";
            if (HasPendingHeroDeployBudget())
            {
                reasonId = ReasonIds.HeroBudgetPending;
                return false;
            }

            if (!m_HeroStateQuery.TryGetSingleton<HeroDeploymentState>(out var state)
                || state.HeroStatus == HeroStatus.Inactive)
            {
                reasonId = ReasonIds.HeroNotDeployed;
                return false;
            }

            return true;
        }

        public bool CanSetHeroMode(HeroStatus mode, out string reasonId)
        {
            if (mode == HeroStatus.Inactive)
                return CanRecallHero(out reasonId);

            reasonId = "";
            if (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton) && actSingleton.CurrentAct == Act.PreWar)
            {
                reasonId = ReasonIds.HeroPrewarLocked;
                return false;
            }

            if (HasPendingHeroDeployBudget())
            {
                reasonId = ReasonIds.HeroBudgetPending;
                return false;
            }

            if (!m_HeroStateQuery.TryGetSingleton<HeroDeploymentState>(out var state))
            {
                reasonId = ReasonIds.HeroSystemUnavailable;
                return false;
            }

            if (state.HeroStatus == HeroStatus.Inactive)
                return CanDeployHero(mode, out reasonId);

            return true;
        }

        // ============ IPostLoadValidation ============

        /// <summary>
        /// Restore GretaDeployed penalties immediately after load.
        /// HeroStatus is serialized; per-district penalty registrations are not.
        /// </summary>
        public void ValidateAfterLoad()
        {
            HeroDeploymentState.EnsureExists(EntityManager);
            if (!m_HeroStateQuery.TryGetSingletonEntity<HeroDeploymentState>(out var stateEntity)) return;

            if (!TryResolvePenaltySystem(force: true))
            {
                Log.Warn("DistrictPenaltySystem not found — cannot restore Gerda penalty");
                return;
            }

            if (!EntityManager.Exists(stateEntity)) return;
            var state = EntityManager.GetComponentData<HeroDeploymentState>(stateEntity);

            if (state.HeroStatus == HeroStatus.Deployed)
            {
                // HeroStatus is serialized source-of-truth; restore the matching
                // non-serialized penalty even if the current act is pre-Crisis.
                ApplyGerdaPenaltyToAllDistricts(register: true);
                Log.Info("Post-load: restored GretaDeployed penalties");
            }
        }
    }
}
