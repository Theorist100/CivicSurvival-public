using Game;
using Game.Simulation;
using CivicSurvival.Core.Features.Wellbeing;
using Colossal.Serialization.Entities;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Cognitive;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Cognitive.Ops.Countermeasures;
using CivicSurvival.Domains.Cognitive.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.Cognitive.Ops.Systems
{
    /// <summary>
    /// Buckwheat System - "The Buckwheat Protocol" (social stabilization countermeasure).
    /// Part of Cognitive domain: distributing aid improves mental health.
    ///
    /// The most cynical gameplay loop in city-builder history:
    /// 1. Steal from budget (Shadow Export, Ghost Employees, etc.)
    /// 2. Accumulate offshore funds
    /// 3. Buy "humanitarian aid" (buckwheat) - ONLY with shadow money!
    /// 4. Distribute food kits to districts
    /// 5. Happiness rises, babushkas praise the mayor
    /// 6. Repeat
    ///
    /// ECS-PURE: Scalar state stored in BuckwheatSingleton.
    /// Dictionary state (district effects, cooldowns) remains here.
    /// Wallet deductions use ShadowWalletService + ECB (single-writer pattern).
    ///
    /// "I steal not for myself, I steal to buy you buckwheat!"
    /// — Every populist mayor ever
    ///
    /// S17a-11 ACCEPTED: Buckwheat ignores Telemarathon trust — by design; operates via shadow money independently.
    /// </summary>
    [HandlesRequestKind(RequestKind.AidDistribution)]
    [TransientConsumerReconcile(typeof(AidDistributionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: buckwheat reserve, penalties, trust request and result event are emitted only after this consumer runs; pre-consume load loss is reissuable.")]
    public partial class BuckwheatSystem : ThrottledSystemBase, IDefaultSerializable, IResettable, IPostLoadValidation, IBuckwheatAidReader, IActGatedSystem
    {
        private const float LARGE_PROCUREMENT_THRESHOLD_TONS = 20.0f;
        private const float SATIRE_PROCUREMENT_THRESHOLD_TONS = 5f;

        private static readonly LogContext Log = new("BuckwheatSystem");

        public Act MinActiveAct => Act.Crisis;
        public ActGateState GateState => m_Gate?.State ?? ActGateState.AwaitingActState;

        // ============================================================================
        // ECS STATE
        // ============================================================================

        private EntityQuery m_SingletonQuery;
        private EntityQuery m_ConfigQuery;
        private ComponentLookup<BuckwheatSingleton> m_BuckwheatLookup;
        private BufferLookup<PenaltyRequest> m_PenaltyRequestBufferLookup;

        // Dictionary state (can't be in IComponentData - must be managed)
        // Track which districts have active food aid effect
        // Stores expiry as TotalGameHours (cumulative hours since game start)
        // Keys are district indices
        [NonEntityIndex] private readonly Dictionary<int, float> m_DistrictAidExpiry = new();

        // FIX HA-004: Track last distribution time per district for cooldown
        // Separate from effect duration - allows redistribution before effect expires
        [NonEntityIndex] private readonly Dictionary<int, float> m_LastDistributionTime = new();

        // PERF M2.4: Cached list for expired districts (avoids allocation per expiry check)
        private readonly List<int> m_ExpiredDistricts = new();

        // Dependencies (initialized in OnCreate/OnStartRunning)
        // Note: Wallet deductions use ShadowWalletService.CanAffordWithPending + ECB
        // MIGRATION: PenaltyRequest buffer replaces direct m_Penalties calls
        private EntityQuery m_PenaltyRequestQuery;
        private EntityQuery m_PendingProcurementQuery;
        private EntityQuery m_ResolvedProcurementQuery;
        private EntityQuery m_CurrentActQuery;
        private ActGateController m_Gate = null!;
        [System.NonSerialized] private bool m_ForceInitialActiveTick;

        // SR-007 fix: Cache time providers to avoid ServiceRegistry lookup per frame
        private IDistrictStateReader? m_DistrictState;
        private GameTimeSystem? m_TimeProvider;

        // Deferred entity destruction
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;

        // Update throttling
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        // ============================================================================
        // PROPERTIES (read from singleton)
        // ============================================================================

        private BuckwheatSingleton ReadSingleton()
        {
            // TOCTOU FIX: Use TryGetSingleton instead of IsEmpty + GetSingleton
            if (!m_SingletonQuery.TryGetSingleton<BuckwheatSingleton>(out var singleton))
                return BuckwheatSingleton.Default;
            return singleton;
        }

        private BuckwheatConfig ReadConfig()
        {
            if (!m_ConfigQuery.TryGetSingleton<BuckwheatConfig>(out var config))
                return BuckwheatConfig.Default;
            return config;
        }

        public float BuckwheatTons => ReadSingleton().BuckwheatTons;
        public int ProcurementLevel => ReadConfig().ProcurementLevel;
        public int DailyCost => BuckwheatSingleton.DailyCost(ReadConfig().ProcurementLevel);
        public bool CanDistribute => ReadSingleton().CanDistribute;

        public bool CanAffordProcurement
        {
            get
            {
                var config = ReadConfig();
                return BuckwheatEligibility.CanAffordProcurement(config.ProcurementLevel, World, out _);
            }
        }


        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            BuckwheatSingleton.EnsureExists(EntityManager);

            m_SingletonQuery = GetEntityQuery(
                ComponentType.ReadWrite<BuckwheatSingleton>()
            );

            m_ConfigQuery = GetEntityQuery(
                ComponentType.ReadWrite<BuckwheatConfig>()
            );

            // Query for PenaltyRequest buffer singleton (Data-Driven Commands)
            m_PenaltyRequestQuery = GetEntityQuery(ComponentType.ReadOnly<PenaltyRequestSingleton>());
            m_PendingProcurementQuery = GetEntityQuery(
                ComponentType.ReadOnly<BuckwheatProcurementIntent>(),
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.Exclude<BudgetDeductResult>());
            m_ResolvedProcurementQuery = GetEntityQuery(
                ComponentType.ReadOnly<BuckwheatProcurementIntent>(),
                ComponentType.ReadOnly<BudgetDeductRequest>(),
                ComponentType.ReadOnly<BudgetDeductResult>());
            m_BuckwheatLookup = GetComponentLookup<BuckwheatSingleton>(false);
            m_PenaltyRequestBufferLookup = GetBufferLookup<PenaltyRequest>(false);

            // SR-007 fix: Cache time providers upfront (lazy-retry in OnUpdateImpl)
            m_TimeProvider = GameTimeSystem.Instance;

            // Deferred entity destruction
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // FIX CM-011: Subscribe to district lifecycle for dictionary cleanup
            SubscribeRequired<DistrictLifecycleEvent>(OnDistrictLifecycle);
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_Gate = CreateGate();

            // Producer-side registration MUST happen in OnCreate, not OnStartRunning:
            // OnStartRunning fires only on first Update, which never arrives if
            // GameSimulation phase never ticks (e.g. UI consumer in MainMenu hits us first).
            ServiceRegistry.Instance.Register<IBuckwheatAidReader>(this);

            Log.Info($"{nameof(BuckwheatSystem)} created (gate awaits act state)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // Note: Penalty operations now use PenaltyRequest buffer (Data-Driven Commands)
            // Note: Trust modifications use TrustModificationRequest (Data-Driven Commands)
            // Narrative posts publish straight onto News/Social events via EventBus.
        }

        protected override void OnThrottledUpdate()
        {
            m_BuckwheatLookup.Update(this);
            m_PenaltyRequestBufferLookup.Update(this);

            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null) { Log.Error("[BuckwheatSystem] TimeProvider unavailable"); return; }
            if (m_DistrictState == null)
                m_DistrictState = ServiceRegistry.Instance.Require<IDistrictStateReader>();
            if (m_DistrictState == null) { Log.Error("[BuckwheatSystem] DistrictState unavailable"); return; }
            float totalHours = m_TimeProvider.Current.TotalGameHours;

            ProcessResolvedProcurements();
            ProcessAidDistributionRequests();
            ProcessProcurement(totalHours);
            ProcessAidExpiry(totalHours);
        }

        protected override bool ShouldSkipUpdate()
        {
            m_Gate.ReconcileFromSingleton(m_CurrentActQuery);

            if (m_Gate.State != ActGateState.Active)
                return true;

            if (m_ForceInitialActiveTick)
            {
                m_ForceInitialActiveTick = false;
                ForceNextUpdate();
            }

            return false;
        }

        /// <summary>
        /// Process AidDistributionRequest ephemeral entities.
        /// Data-Driven Commands pattern - UI creates entity, domain system processes.
        /// </summary>
        private void ProcessAidDistributionRequests()
        {
            EntityCommandBuffer ecb = default;
            bool hasEcbCommands = false;

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<AidDistributionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcbCommands)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcbCommands = true;
                }

                bool success = TryDistributeToDistrict(request.ValueRO.DistrictIndex, out FixedString64Bytes failReason);
                if (success)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.AidDistribution, SystemAPI.Time.ElapsedTime);
                else
                    RequestResultEmitter.EmitFixedReason(ecb, meta.ValueRO, RequestKind.AidDistribution, RequestStatus.Failed, failReason, SystemAPI.Time.ElapsedTime);

                ecb.DestroyEntity(entity);
                if (Log.IsDebugEnabled) Log.Debug($" Processed AidDistributionRequest: district {request.ValueRO.DistrictIndex} = {(success ? "Success" : "Failed")}");
            }

            if (hasEcbCommands)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        protected override void OnDestroy()
        {
            // CM-005 FIX: Clear dictionaries on destroy
            m_DistrictAidExpiry.Clear();
            m_LastDistributionTime.Clear();

            // FIX CM-011: Unsubscribe from district lifecycle
            if (ServiceRegistry.IsInitialized)
            {
                UnsubscribeSafe<DistrictLifecycleEvent>(OnDistrictLifecycle);
            }

            // Unregister from ServiceRegistry
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IBuckwheatAidReader>(this);
            }

            m_DistrictState = null;
            m_TimeProvider = null;

            Log.Info($"{nameof(BuckwheatSystem)} destroyed");
            base.OnDestroy();
        }

        // ============================================================================
        // IResettable
        // ============================================================================

        public void ResetState()
        {
            // SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 2: OnCreate doesn't re-run on new-game.
            BuckwheatSingleton.EnsureExists(EntityManager);
            // TOCTOU FIX: Use TryGetSingletonEntity instead of IsEmpty + GetSingletonEntity
            if (m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var entity))
            {
                EntityManager.SetComponentData(entity, BuckwheatSingleton.Default);
            }
            if (m_ConfigQuery.TryGetSingletonEntity<BuckwheatConfig>(out var configEntity))
            {
                EntityManager.SetComponentData(configEntity, BuckwheatConfig.Default);
            }

            // Reset local dictionaries
            m_DistrictAidExpiry.Clear();
            m_LastDistributionTime.Clear();  // FIX HA-004
            m_ExpiredDistricts.Clear();
            m_Gate = CreateGate();
            m_ForceInitialActiveTick = false;
            DestroyPendingProcurements();
        }

        // ============================================================================
        // CORE LOGIC
        // ============================================================================

        /// <summary>
        /// Process buckwheat procurement every PROCUREMENT_INTERVAL_HOURS.
        /// Deducts from offshore account via ECB, adds to buckwheat reserve.
        /// Single-writer: ShadowWalletService.CanAffordWithPending + ECB deduct.
        /// </summary>
        // FIX H10: Uses TotalGameHours instead of cyclic GameHour (0-24)
        // Cyclic hour capped hoursSinceLast at 24, blocking ProcurementIntervalHours > 24
        private void ProcessProcurement(float currentHour)
        {
            // TOCTOU FIX: Use TryGetSingletonEntity instead of IsEmpty + GetSingletonEntity
            if (!m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var entity))
                return;

            if (!m_PendingProcurementQuery.IsEmpty || !m_ResolvedProcurementQuery.IsEmpty)
                return;

            var singleton = m_BuckwheatLookup[entity];

            var config = ReadConfig();
            if (config.ProcurementLevel <= 0)
                return;

            // FIX H10: Simple subtraction — TotalGameHours is monotonic, no day-wrap needed
            var haCfg = BalanceConfig.Current.HumanitarianAid;
            if (singleton.LastProcurementHour <= 0f)
            {
                singleton.LastProcurementHour = currentHour;
                m_BuckwheatLookup[entity] = singleton;
                return;
            }

            float interval = Math.Max(haCfg.ProcurementIntervalHours, 0.1f);
            float hoursSinceLast = Math.Max(currentHour - singleton.LastProcurementHour, 0f);
            int intervalsDue = Math.Min((int)Math.Floor(hoursSinceLast / interval), 2);
            if (intervalsDue <= 0)
                return;

            // Calculate tons to procure this interval
            float intervalsPerDay = Math.Max(GameRate.HOURS_PER_DAY / interval, 1f);
            float tonsThisInterval = haCfg.TonsPerDayAt100
                                     * config.ProcurementLevel / 100f
                                     / intervalsPerDay
                                     * intervalsDue;
            float procurementHour = Math.Min(singleton.LastProcurementHour + interval * intervalsDue, currentHour);

            int baseCost = BuckwheatEligibility.CalculateProcurementBaseCost(config.ProcurementLevel, intervalsDue);
            if (baseCost <= 0)
            {
                ApplyProcurement(entity, singleton, tonsThisInterval, procurementHour, 0);
                return;
            }

            if (!BuckwheatEligibility.CanAffordProcurement(config.ProcurementLevel, intervalsDue, World, out _, out _))
            {
                Log.Warn($" Procurement FAILED: insufficient funds or frozen (need ~${baseCost:N0}). " +
                         $"Consider reducing procurement level from {config.ProcurementLevel}%.");
                return;
            }

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    baseCost,
                    BudgetCategory.ShadowOps,
                    BudgetPriority.DailyCost,
                    "BuckwheatProcurement",
                    out var budgetEntity,
                    out long cost,
                    BudgetResultMode.RetainResult))
            {
                Log.Warn($" Procurement FAILED: insufficient funds or frozen (need ~${baseCost:N0}). " +
                         $"Consider reducing procurement level from {config.ProcurementLevel}%.");
                return;
            }

            // Retained BudgetDeductRequest — BudgetResolutionSystem is the single wallet writer.
            ecb.AddComponent(budgetEntity, new BuckwheatProcurementIntent
            {
                Tons = tonsThisInterval,
                ProcurementHour = procurementHour,
                Cost = cost
            });
            m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void ProcessResolvedProcurements()
        {
            if (m_ResolvedProcurementQuery.IsEmpty)
                return;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();

            foreach (var (intent, result, budgetEntity) in
                SystemAPI.Query<RefRO<BuckwheatProcurementIntent>, RefRO<BudgetDeductResult>>()
                .WithAll<BudgetDeductRequest>()
                .WithEntityAccess())
            {
                if (result.ValueRO.Succeeded
                    && m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var singletonEntity))
                {
                    var singleton = m_BuckwheatLookup[singletonEntity];
                    ApplyProcurement(singletonEntity, singleton, intent.ValueRO.Tons, intent.ValueRO.ProcurementHour, result.ValueRO.Amount);
                }
                else if (!result.ValueRO.Succeeded)
                {
                    Log.Warn($"BuckwheatSystem: procurement budget denied (${intent.ValueRO.Cost:N0}); reserve unchanged");
                }

                ecb.DestroyEntity(budgetEntity);
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void ApplyProcurement(Entity entity, BuckwheatSingleton singleton, float tonsThisInterval, float procurementHour, long cost)
        {
            if (!m_BuckwheatLookup.HasComponent(entity))
                return;

            singleton.BuckwheatTons += tonsThisInterval;
            singleton.LastProcurementHour = procurementHour;
            m_BuckwheatLookup[entity] = singleton;

            Log.Info($"BuckwheatSystem: Procured {tonsThisInterval:F2} tons for ${cost:N0}. " +
                     $"Reserve: {singleton.BuckwheatTons:F2} tons");

            // Telemetry: record procurement
            EventBus?.SafePublish(new BuckwheatProcuredEvent(tonsThisInterval, (int)Math.Min(cost, int.MaxValue), singleton.BuckwheatTons), "BuckwheatSystem");

            // City alert about suspicious procurement volume.
            // @CityAlert is an official-feed handle → Herald (content-stable NewsPostEvent).
            if (tonsThisInterval >= LARGE_PROCUREMENT_THRESHOLD_TONS)
            {
                string procMessage = BuckwheatMessages.GetProcurementMessage(tonsThisInterval);
                EventBus?.SafePublish(new NewsPostEvent(
                    NotificationIdHelper.ContentId("@CityAlert", procMessage, string.Empty, Engine.Narrative.NEWS_CONTENT_BUCKET_SECONDS),
                    NewsAuthorRegistry.GetDisplayName("@CityAlert"),
                    procMessage,
                    string.Empty,
                    SocialMood.Warning,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    "official"), "BuckwheatSystem");
            }

            // FIX HA-002: satire for medium procurements (exclusive with large alert above).
            // @ValeraKhrush / @StudentOlya are citizen handles → CHIPPER (SocialPostEvent).
            else if (tonsThisInterval >= SATIRE_PROCUREMENT_THRESHOLD_TONS)
            {
                bool useValera = tonsThisInterval >= 10f;

                EventBus?.SafePublish(new SocialPostEvent(
                    useValera ? "@ValeraKhrush" : "@StudentOlya",
                    useValera ? BuckwheatMessages.GetValeraMessage() : BuckwheatMessages.GetStudentMessage(),
                    SocialMood.Suspicious), "BuckwheatSystem");
            }
        }

        /// <summary>
        /// Check and remove expired food aid effects from districts.
        /// Uses TotalGameHours for accurate expiry regardless of duration.
        ///
        /// MIGRATION: Uses PenaltyRequest buffer (Data-Driven Commands pattern).
        /// PERF M2.4: Uses cached m_ExpiredDistricts list to avoid allocations.
        /// </summary>
        private void ProcessAidExpiry(float currentTotalHours)
        {
            if (m_DistrictAidExpiry.Count == 0)
                return;

            // Get PenaltyRequest buffer (Data-Driven Commands)
            if (!m_PenaltyRequestQuery.TryGetSingletonEntity<PenaltyRequestSingleton>(out var singletonEntity))
            {
                if (Log.IsDebugEnabled) Log.Debug("BuckwheatSystem: PenaltyRequest singleton not ready - cannot process aid expiry");
                return;
            }
            if (!m_PenaltyRequestBufferLookup.HasBuffer(singletonEntity)) return;
            m_ExpiredDistricts.Clear();

            foreach (var kvp in m_DistrictAidExpiry)
            {
                int districtIndex = kvp.Key;
                float expiryTotalHours = kvp.Value;

                if (currentTotalHours >= expiryTotalHours)
                {
                    m_ExpiredDistricts.Add(districtIndex);
                }
            }

            foreach (int districtIndex in m_ExpiredDistricts)
            {
                m_DistrictAidExpiry.Remove(districtIndex);
                AppendPenaltyRequest(singletonEntity, new PenaltyRequest
                {
                    DistrictIndex = districtIndex,
                    Source = PenaltySource.FoodAidProvided,
                    IsRemoval = true
                });
                if (Log.IsDebugEnabled) Log.Debug($"BuckwheatSystem: Food aid effect expired in district {districtIndex}");
            }
        }

        // ============================================================================
        // PUBLIC API
        // ============================================================================

        public bool CanDistributeToDistrict(int districtIndex, out string reasonId)
        {
            reasonId = "";
            if (!IsCurrentActOpenForBuckwheat(out reasonId))
                return false;

            if (!IsValidDistrictIndex(districtIndex))
            {
                reasonId = ReasonIds.ReliefInvalidDistrict;
                return false;
            }

            if (!m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var entity))
            {
                reasonId = ReasonIds.ReliefSystemNotReady;
                return false;
            }

            var singleton = m_BuckwheatLookup[entity];
            if (!singleton.CanDistribute)
            {
                reasonId = ReasonIds.ReliefNotEnoughReserve;
                return false;
            }

            if (!m_PenaltyRequestQuery.TryGetSingletonEntity<PenaltyRequestSingleton>(out var penaltySingletonEntity)
                || !m_PenaltyRequestBufferLookup.HasBuffer(penaltySingletonEntity))
            {
                reasonId = ReasonIds.ReliefSystemNotReady;
                return false;
            }

            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null)
            {
                reasonId = ReasonIds.ReliefSystemNotReady;
                return false;
            }

            float currentTotalHours = m_TimeProvider.Current.TotalGameHours;
            var haCfg = BalanceConfig.Current.HumanitarianAid;
            if (m_LastDistributionTime.TryGetValue(districtIndex, out float lastDistributionTime)
                && currentTotalHours - lastDistributionTime < haCfg.DistributionCooldownHours)
            {
                reasonId = ReasonIds.ReliefDistrictCooldown;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Distribute food aid to a district.
        /// FIX HA-004: Uses separate cooldown (4h) and effect duration (24h).
        /// Allows multiple distributions before effect expires.
        ///
        /// MIGRATION: Uses PenaltyRequest buffer (Data-Driven Commands pattern).
        /// </summary>
        // FIX M18: Made private — only called internally from ProcessAidDistributionRequests
        private bool TryDistributeToDistrict(int districtIndex, out FixedString64Bytes failReason)
        {
            failReason = default;
            if (!IsValidDistrictIndex(districtIndex))
            {
                Log.Warn($"BuckwheatSystem: invalid district index {districtIndex}");
                failReason = ReasonIds.ReliefInvalidDistrict.ToFixedString();
                return false;
            }

            // TOCTOU FIX: Use TryGetSingletonEntity instead of IsEmpty + GetSingletonEntity
            if (!m_SingletonQuery.TryGetSingletonEntity<BuckwheatSingleton>(out var entity))
            {
                Log.Warn("BuckwheatSystem: Singleton not ready");
                failReason = ReasonIds.ReliefSystemNotReady.ToFixedString();
                return false;
            }

            var singleton = m_BuckwheatLookup[entity];

            if (!singleton.CanDistribute)
            {
                if (Log.IsDebugEnabled) Log.Debug($"BuckwheatSystem: Cannot distribute - only {singleton.BuckwheatTons:F2} tons available");
                failReason = ReasonIds.ReliefNotEnoughReserve.ToFixedString();
                return false;
            }

            // Get PenaltyRequest buffer (Data-Driven Commands)
            if (!m_PenaltyRequestQuery.TryGetSingletonEntity<PenaltyRequestSingleton>(out var penaltySingletonEntity))
            {
                Log.Warn("BuckwheatSystem: PenaltyRequest singleton not ready");
                failReason = ReasonIds.ReliefSystemNotReady.ToFixedString();
                return false;
            }

            // FIX H9: Check buffer BEFORE any state mutations (deductions, cooldowns)
            // Entity can exist without buffer during act transition or early load
            if (!m_PenaltyRequestBufferLookup.HasBuffer(penaltySingletonEntity))
            {
                Log.Warn("BuckwheatSystem: PenaltyRequest buffer missing — distribution aborted");
                failReason = ReasonIds.ReliefSystemNotReady.ToFixedString();
                return false;
            }

            m_TimeProvider ??= GameTimeSystem.Instance;
            if (m_TimeProvider == null) { Log.Error("[BuckwheatSystem] TimeProvider unavailable"); failReason = ReasonIds.ReliefSystemNotReady.ToFixedString(); return false; }
            float currentTotalHours = m_TimeProvider.Current.TotalGameHours;

            var haCfg = BalanceConfig.Current.HumanitarianAid;

            // FIX HA-004: Check distribution cooldown (4 hours)
            // This is SEPARATE from effect duration (24 hours)
            if (m_LastDistributionTime.TryGetValue(districtIndex, out float lastDistributionTime))
            {
                float hoursSinceLastDistribution = currentTotalHours - lastDistributionTime;
                if (hoursSinceLastDistribution < haCfg.DistributionCooldownHours)
                {
                    float remainingCooldown = haCfg.DistributionCooldownHours - hoursSinceLastDistribution;
                    if (Log.IsDebugEnabled) Log.Debug($"BuckwheatSystem: District {districtIndex} cooldown active ({remainingCooldown:F1}h remaining)");
                    failReason = ReasonIds.ReliefDistrictCooldown.ToFixedString();
                    return false;
                }
            }

            // Deduct buckwheat from singleton
            singleton.BuckwheatTons -= haCfg.TonsPerDistribution;
            m_BuckwheatLookup[entity] = singleton;

            // Update last distribution time for cooldown tracking
            m_LastDistributionTime[districtIndex] = currentTotalHours;

            // Apply or refresh happiness bonus (negative penalty = positive effect)
            AppendPenaltyRequest(penaltySingletonEntity, new PenaltyRequest
            {
                DistrictIndex = districtIndex,
                Source = PenaltySource.FoodAidProvided,
                IsRemoval = false
            });

            // Set/refresh effect expiry using TotalGameHours for accurate timing
            float expiryTotalHours = currentTotalHours + haCfg.EffectDurationHours;
            m_DistrictAidExpiry[districtIndex] = expiryTotalHours;

            // Boost reputation/trust via ECS request (no cross-domain import)
            var ecbTrust = m_GameSimulationEndBarrier.CreateCommandBuffer();
            var trustRequest = ecbTrust.CreateEntity();
            ecbTrust.AddComponent(trustRequest, new TrustModificationRequest
            {
                Amount = haCfg.TrustBonus,
                Reason = "Food aid distributed"
            });
            RequestMetaWriter.AddInternal(ecbTrust, trustRequest, nameof(TrustModificationRequest), districtIndex.ToString());
            m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);

            // FIX HA-002: Trigger satire messages
            if (m_DistrictState != null)
            {
                string districtName = m_DistrictState.GetDistrictName(districtIndex);

                // Babushka praising mayor (smugly grateful). @BabcyaZina is a citizen handle →
                // CHIPPER (SocialPostEvent).
                EventBus?.SafePublish(new SocialPostEvent(
                    "@BabcyaZina",
                    BuckwheatMessages.GetBabushkaMessage(districtName),
                    SocialMood.Neutral), "BuckwheatSystem");
            }

            Log.Info($"BuckwheatSystem: Distributed 1 ton to district {districtIndex}. " +
                     $"Reserve: {singleton.BuckwheatTons:F2} tons. Effect expires in {haCfg.EffectDurationHours}h");

            // Telemetry: record distribution
            // FIX CW-02: SafePublish for error resilience
            EventBus?.SafePublish(new BuckwheatDistributedEvent(districtIndex, singleton.BuckwheatTons, haCfg.TrustBonus), "BuckwheatSystem");

            return true;
        }

        // ============================================================================
        // EVENT HANDLERS
        // ============================================================================

        private ActGateController CreateGate()
            => new(
                isOpenFor: act => act >= Act.Crisis,
                onTransition: HandleGateTransition);

        private void HandleGateTransition(ActGateState old, ActGateState next, bool isInitial)
        {
            if (next == ActGateState.Active)
            {
                if (isInitial)
                {
                    m_ForceInitialActiveTick = true;
                }
                else
                {
                    ResetThrottleCounter();
                    ForceNextUpdate();
                    Log.Info("[Buckwheat] Gate opened");
                }
                return;
            }

            if (next == ActGateState.Inactive && !isInitial)
            {
                EmitOrphanPenaltyRemovals();
                m_DistrictAidExpiry.Clear();
                m_LastDistributionTime.Clear();
                m_ExpiredDistricts.Clear();
                Log.Info("[Buckwheat] Gate closed");
            }
        }

        private void EmitOrphanPenaltyRemovals()
        {
            if (m_DistrictAidExpiry.Count == 0)
                return;

            m_PenaltyRequestBufferLookup.Update(this);
            if (!m_PenaltyRequestQuery.TryGetSingletonEntity<PenaltyRequestSingleton>(out var singletonEntity)
                || !m_PenaltyRequestBufferLookup.HasBuffer(singletonEntity))
            {
                Log.Warn("[Buckwheat] PenaltyRequest singleton/buffer not ready - orphan penalty removal could not be emitted");
                return;
            }

            foreach (int districtIndex in m_DistrictAidExpiry.Keys)
            {
                AppendPenaltyRequest(singletonEntity, new PenaltyRequest
                {
                    DistrictIndex = districtIndex,
                    Source = PenaltySource.FoodAidProvided,
                    IsRemoval = true
                });
            }

            Log.Info($"[Buckwheat] Removed {m_DistrictAidExpiry.Count} orphaned FoodAid penalties");
        }

        private bool IsCurrentActOpenForBuckwheat(out string reasonId)
        {
            if (!m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton))
            {
                reasonId = ReasonIds.ReliefSystemNotReady;
                return false;
            }

            if (actSingleton.CurrentAct < Act.Crisis)
            {
                reasonId = ReasonIds.ActLockedFor(Act.Crisis);
                return false;
            }

            reasonId = "";
            return true;
        }

        /// <summary>
        /// FIX CM-011: Clean up dictionaries when district is destroyed.
        /// Prevents memory leak from dangling district entries.
        /// </summary>

        private void OnDistrictLifecycle(DistrictLifecycleEvent evt)
        {
            if (evt.Lifecycle != DistrictLifecycle.Destroyed)
                return;
            ForceNextUpdate();

            int districtIndex = evt.DistrictIndex;

            if (m_DistrictAidExpiry.Remove(districtIndex))
            {
                if (Log.IsDebugEnabled) Log.Debug($" Cleaned up aid expiry for destroyed district {districtIndex}");
            }

            if (m_LastDistributionTime.Remove(districtIndex))
            {
                if (Log.IsDebugEnabled) Log.Debug($" Cleaned up distribution time for destroyed district {districtIndex}");
            }
        }

        private void AppendPenaltyRequest(Entity singletonEntity, PenaltyRequest request)
        {
            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            ecb.AppendToBuffer(singletonEntity, request);
            m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void DestroyPendingProcurements()
        {
            int destroyed = 0;

            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<BuckwheatProcurementIntent>>()
                .WithEntityAccess())
            {
                EntityManager.DestroyEntity(entity);
                destroyed++;
            }

            if (destroyed > 0 && Log.IsDebugEnabled)
                Log.Debug($"BuckwheatSystem: destroyed {destroyed} pending procurement request(s) during reset");
        }

        // District identity is the raw entity index (Unzoned = 0); never a small
        // dense index. The old `< 500` cap silently refused buckwheat aid to every
        // district whose entity index reached 500+ (G7 Cluster-A runtime sibling).
        private static bool IsValidDistrictIndex(int districtIndex)
            => districtIndex >= 0;
    }
}
