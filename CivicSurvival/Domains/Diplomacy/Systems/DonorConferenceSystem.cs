using System;
using Colossal.Serialization.Entities;
using Game;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Components.Domain.Diplomacy;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Domain.AirDefense;
using CivicSurvival.Core.Interfaces.Domain.GridWarfare;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Domains.Diplomacy.Data;
using CivicSurvival.Domains.Diplomacy.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.UI;
using Game.Simulation;

namespace CivicSurvival.Domains.Diplomacy.Systems
{
    /// <summary>
    /// Donor Conference System - orchestrates international aid mechanics.
    /// Implements IDonorService.
    ///
    /// Eligibility:
    /// - Crisis > 60% (blackouts affecting population)
    /// - Max 2 uses per game
    /// - 30-day cooldown between conferences
    /// - Min game day 30
    ///
    /// Trust = 100 - CorruptionScore - ScandalPenalty
    ///
    /// Dependencies (ECS singletons):
    /// - CrisisStateSingleton: crisis level metrics (from CrisisMonitorSystem)
    /// - ScandalStateSingleton: scandal penalty (from ScandalSystem)
    /// - CountermeasuresState: corruption score
    /// - ShockStateSingleton: world attention level
    ///
    /// EDA: Publishes DonorEvent(PatriotReceived/PatriotExpired) → AirDefenseSystem subscribes
    /// </summary>
    [SingletonOwner(typeof(DonorSanctionsSingleton))]
    [SingletonOwner(typeof(ExternalPowerSource))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    [HandlesRequestKind(RequestKind.DonorDialog)]
    [HandlesRequestKind(RequestKind.DonorSelection)]
    [TransientConsumerReconcile(typeof(DonorDialogRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: dialog state changes only when this consumer runs; a load before processing leaves no durable donor-conference side-effect.")]
    [TransientConsumerReconcile(typeof(DonorSelectionRequest), ReconcileMode.ExplicitlyLossyAndSafe,
        Justification = "Transient UI command: aid effects and retained credit/fund requests are emitted only after selection processing, so pre-consume load loss is reissuable.")]
    public partial class DonorConferenceSystem : CivicSystemBase, IDefaultSerializable, IResettable,
        ICivicSingletonOwner<DonorSanctionsSingleton>, ICivicSingletonOwner<ExternalPowerSource>
    {
        private static readonly LogContext Log = new("DonorConferenceSystem");
        // Config accessors - delegate to BalanceConfig.Current.Diplomacy
        private static int MAX_USES => BalanceConfig.Current.Diplomacy.ConferenceMaxUses;
        private static float COOLDOWN_DAYS => BalanceConfig.Current.Diplomacy.ConferenceCooldownDays;
        private static float CRISIS_THRESHOLD => BalanceConfig.Current.Diplomacy.CrisisThreshold;
        private static int MIN_GAME_DAY => BalanceConfig.Current.Diplomacy.MinGameDay;
        // R3-B-3 FIX: Cap generators to prevent unbounded accumulation (20 × 50MW = 1000MW max)
        private const int MAX_GENERATORS = 20;

        // State
        private DonorConferenceStateData m_State;
        [NonSerialized] private bool m_ConferenceDialogActive;
        [NonSerialized] private Act m_LastSeenAct;
        [NonSerialized] private bool m_HasSeenAct;
        private Act m_LastReplenishedAct;
        private bool m_HasLastReplenishedAct;
        private int m_GeneratorDecayCounter;

        // ECS singleton queries
        private EntityQuery m_ShockQuery;
        private EntityQuery m_CrisisQuery;
        private EntityQuery m_ScandalQuery;
        private EntityQuery m_CountermeasuresQuery;
        private EntityQuery m_HeroStateQuery;
        private EntityQuery m_CurrentActQuery;
        private EntityQuery m_DialogRequestQuery;
        private EntityQuery m_SelectionRequestQuery;
        private EntityQuery m_DonorSanctionsQuery;  // FIX H59
        private EntityQuery m_DonorFundsGrantQuery;
        private EntityQuery m_DonorFundsGrantAnyQuery;
        [NonSerialized]
        private CivicSingletonHandle<DonorSanctionsSingleton> m_DonorSanctions;
        private ComponentLookup<DonorSanctionsSingleton> m_DonorSanctionsLookup;
        private IAirDefenseCreditsReader m_AirDefenseCreditsReader = null!;
        private ICounterAttackArsenalService m_Arsenal = null!;

        // ECS
        private EntityQuery m_ExternalPowerSourceQuery;
        [NonSerialized] private CivicSingletonHandle<ExternalPowerSource> m_ExternalPowerSource;
        private ComponentLookup<ExternalPowerSource> m_ExternalPowerLookup;

        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;

        // Timing
        private int m_GameDay;

        // FIX M-100: Trust penalty accumulated from import discovery events
        private float m_ImportTrustPenalty;

        /// <summary>
        /// Static instance for cross-system access (UI panels, other systems).
        /// NOT A BUG: Standard CS2/Unity ECS pattern - systems are singletons by design.
        /// All game systems use this pattern (GameTimeSystem.Instance, BlackoutSystem.Instance, etc.)
        /// </summary>
        public static DonorConferenceSystem? Instance { get; private set; }
        private static bool s_NullWarned;
        private static void ResetNullWarned() => s_NullWarned = false;

        // Static accessors for UI - log once on null instead of silent default
        public static DonorConferenceStateData State
        {
            get
            {
                if (Instance != null) return Instance.m_State;
                WarnNull();
                return DonorConferenceStateData.CreateDefault(MAX_USES);
            }
        }

        public static bool DialogActive
        {
            get
            {
                if (Instance != null) return Instance.m_ConferenceDialogActive;
                WarnNull();
                return false;
            }
        }

        private static void WarnNull()
        {
            if (s_NullWarned) return;
            s_NullWarned = true;
            Log.Error("[DonorConferenceSystem] Instance null — diplomacy accessors returning defaults");
        }

        // Public properties (previously IDonorService implementation)
        public bool IsConferenceAvailable => GetConferenceStatus() == ConferenceStatus.Available;
        public int UsesRemaining => m_State.UsesRemaining;
        public float CooldownDays => m_State.CooldownDaysRemaining;
        public int ActiveGenerators => m_State.ActiveGenerators;

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            ResetNullWarned();

            m_State = DonorConferenceStateData.CreateDefault(MAX_USES);

            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // ECS singleton queries
            m_ShockQuery = GetEntityQuery(ComponentType.ReadOnly<ShockStateSingleton>());
            m_CrisisQuery = GetEntityQuery(ComponentType.ReadOnly<CrisisStateSingleton>());
            m_ScandalQuery = GetEntityQuery(ComponentType.ReadOnly<ScandalStateSingleton>());
            m_CountermeasuresQuery = GetEntityQuery(ComponentType.ReadOnly<CountermeasuresCoreFsm>());
            m_HeroStateQuery = GetEntityQuery(ComponentType.ReadOnly<HeroDeploymentState>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_DialogRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<DonorDialogRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_SelectionRequestQuery = GetEntityQuery(
                ComponentType.ReadOnly<DonorSelectionRequest>(),
                ComponentType.ReadOnly<RequestMeta>());
            m_ExternalPowerSourceQuery = GetEntityQuery(
                ComponentType.ReadWrite<ExternalPowerSource>(),
                ComponentType.ReadOnly<ExternalPowerDonorTag>());
            m_ExternalPowerSource = CreateSingletonHandle<ExternalPowerSource>(m_ExternalPowerSourceQuery);
            m_ExternalPowerLookup = GetComponentLookup<ExternalPowerSource>(false);
            RestoreExternalPowerSourceEntity();
            UpdateExternalPowerSource();

            m_DonorSanctionsQuery = GetEntityQuery(ComponentType.ReadWrite<DonorSanctionsSingleton>());
            m_DonorFundsGrantQuery = GetEntityQuery(
                ComponentType.ReadWrite<DonorFundsGrantIntent>());
            m_DonorFundsGrantAnyQuery = GetEntityQuery(
                ComponentType.ReadWrite<DonorFundsGrantIntent>());
            RequireAnyForUpdate(m_DialogRequestQuery, m_SelectionRequestQuery, m_DonorFundsGrantAnyQuery);
            m_DonorSanctions = CreateSingletonHandle<DonorSanctionsSingleton>(m_DonorSanctionsQuery);
            m_DonorSanctionsLookup = GetComponentLookup<DonorSanctionsSingleton>(false);
            RestoreDonorSanctionsEntity();

            SubscribeRequired<DayChangedEvent>(OnDayChanged, DayEventPriority.StateChange);
            SubscribeRequired<ShadowNarrativeEvent>(OnShadowNarrative);

            Log.Info($"{nameof(DonorConferenceSystem)} created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            // CIVIC403: cross-feature service lookup must happen after FeatureRegistry boot
            // (which completes between OnCreate sweeps and the first OnStartRunning).
            m_AirDefenseCreditsReader = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullAirDefenseCreditsReader.Instance);
            m_Arsenal = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCounterAttackArsenalService.Instance);
            RestoreDonorSanctionsEntity();
            RestoreExternalPowerSourceEntity();
            SeedActBaselineFromSingleton(force: false);
        }

        protected override void OnUpdateImpl()
        {
            DrainResolvedDonorFundsGrants();
            ReconcileActTransition();

            // R3-D-4: Defense-in-depth act guard. Requests are still drained so
            // stale pre-Crisis entities cannot execute after a later act transition.
#pragma warning disable CIVIC070 // Act guard — CurrentActSingleton changes at act transitions only
            bool isCrisisAct = SystemAPI.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                && actSingleton.CurrentAct >= Act.Crisis;
#pragma warning restore CIVIC070

            // Process Data-Driven Requests
            if (!m_DialogRequestQuery.IsEmptyIgnoreFilter)
                ProcessDialogRequests(isCrisisAct);
            if (!m_SelectionRequestQuery.IsEmptyIgnoreFilter)
                ProcessSelectionRequests(isCrisisAct);
        }

        private void ProcessDialogRequests(bool isCrisisAct)
        {
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<DonorDialogRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                bool accepted = true;
                var failReason = ReasonId.None;
                switch (request.ValueRO.Action)
                {
                    case DonorDialogAction.Open:
                        if (!isCrisisAct)
                        {
                            accepted = false;
                            failReason = ReasonIds.DonorPrecrisisLocked;
                            Log.Info("Dialog Open rejected: pre-Crisis act");
                            break;
                        }
                        if (m_ConferenceDialogActive)
                        {
                            accepted = false;
                            failReason = ReasonIds.DonorDialogAlreadyOpen;
                            Log.Info("Dialog Open rejected: already active");
                            break;
                        }
                        var dialogStatus = GetConferenceStatus();
                        if (dialogStatus == ConferenceStatus.Available)
                        {
                            OpenConferenceDialog();
                        }
                        else
                        {
                            accepted = false;
                            failReason = dialogStatus == ConferenceStatus.TrustSourceUnavailable
                                ? ReasonIds.DonorTrustSourceUnavailable
                                : ReasonIds.DonorConferenceUnavailable;
                            Log.Info($"Dialog Open rejected: {dialogStatus}");
                        }
                        break;
                    case DonorDialogAction.Close:
                        CloseConferenceDialog();
                        break;
                    default:
                        Log.Warn($"Unknown DonorDialogAction: {request.ValueRO.Action}");
                        accepted = false;
                        failReason = ReasonIds.DonorUnknownDialogAction;
                        break;
                }

                if (accepted)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.DonorDialog, SystemAPI.Time.ElapsedTime);
                else
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorDialog, RequestStatus.Failed, failReason, SystemAPI.Time.ElapsedTime);
                ecb.DestroyEntity(entity);
            }

            if (hasEcb)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void ProcessSelectionRequests(bool isCrisisAct)
        {
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;

            bool selectionAcceptedOrDeferredThisPass = false;

            foreach (var (request, meta, entity) in
                SystemAPI.Query<RefRO<DonorSelectionRequest>, RefRO<RequestMeta>>()
                .WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                if (!isCrisisAct)
                {
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, ReasonIds.DonorPrecrisisLocked, SystemAPI.Time.ElapsedTime);
                    Log.Info("Selection rejected: pre-Crisis act");
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (!m_ConferenceDialogActive)
                {
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, ReasonIds.DonorNoActiveDialog, SystemAPI.Time.ElapsedTime);
                    Log.Info("Selection rejected: no active donor dialog");
                    ecb.DestroyEntity(entity);
                    continue;
                }

                var status = GetConferenceStatus();
                if (status != ConferenceStatus.Available)
                {
                    var reason = status == ConferenceStatus.TrustSourceUnavailable
                        ? ReasonIds.DonorTrustSourceUnavailable
                        : ReasonIds.DonorConferenceUnavailable;
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, reason, SystemAPI.Time.ElapsedTime);
                    Log.Info($"Selection rejected: {status}");
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (selectionAcceptedOrDeferredThisPass)
                {
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, ReasonIds.DonorConferenceUnavailable, SystemAPI.Time.ElapsedTime);
                    Log.Info("Selection rejected: donor selection already accepted or deferred this update");
                    ecb.DestroyEntity(entity);
                    continue;
                }

                var selectionType = request.ValueRO.SelectionType;
                if (selectionType != DonorSelectionType.Funds
                    && selectionType != DonorSelectionType.Power
                    && selectionType != DonorSelectionType.Defense)
                {
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, ReasonIds.DonorUnknownSelection, SystemAPI.Time.ElapsedTime);
                    Log.Warn($"Unknown DonorSelectionType: {selectionType}");
                    ecb.DestroyEntity(entity);
                    continue;
                }
                DonorAidType aidType;
                switch (selectionType)
                {
                    case DonorSelectionType.Funds:
                        aidType = DonorAidType.Funds;
                        break;
                    case DonorSelectionType.Power:
                        aidType = DonorAidType.Power;
                        break;
                    case DonorSelectionType.Defense:
                        aidType = DonorAidType.Defense;
                        break;
                    default:
                        RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, ReasonIds.DonorUnknownSelection, SystemAPI.Time.ElapsedTime);
                        Log.Warn($"Unknown DonorSelectionType: {selectionType}");
                        ecb.DestroyEntity(entity);
                        continue;
                }

                var result = SelectAid(aidType, meta.ValueRO);
                bool deferredFundsGrant = result.Success
                    && result.AidType == DonorAidType.Funds
                    && result.FundsReceived > 0;
                if (result.Success)
                    selectionAcceptedOrDeferredThisPass = true;

                if (result.Success && !deferredFundsGrant)
                    RequestResultEmitter.EmitSuccess(ecb, meta.ValueRO, RequestKind.DonorSelection, SystemAPI.Time.ElapsedTime);
                else if (result.Success)
                {
                    // Donor funds are terminal only after the retained budget add-funds result is observed.
                }
                else
                    RequestResultEmitter.Emit(ecb, meta.ValueRO, RequestKind.DonorSelection, RequestStatus.Failed, ReasonIds.DonorSelectionRejected, SystemAPI.Time.ElapsedTime);
                if (!result.Success)
                    Log.Info($"Selection {aidType} failed: Success=false");

                ecb.DestroyEntity(entity);
            }

            if (hasEcb)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void SeedActBaselineFromSingleton(bool force)
        {
            if (!force && m_HasSeenAct)
                return;

            if (!TryReadCurrentAct(out var current))
                return;

            m_LastSeenAct = current;
            m_HasSeenAct = true;
        }

        private void ReconcileActTransition()
        {
            if (!TryReadCurrentAct(out var current))
                return;

            if (!m_HasSeenAct)
            {
                m_LastSeenAct = current;
                m_HasSeenAct = true;
                return;
            }

            if (current == m_LastSeenAct)
                return;

            var previous = m_LastSeenAct;
            m_LastSeenAct = current;
            OnObservedActTransition(previous, current);
        }

        private void ResetActBaseline()
        {
            m_LastSeenAct = default;
            m_HasSeenAct = false;
        }

        private bool TryReadCurrentAct(out Act current)
        {
            if (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var singleton))
            {
                current = singleton.CurrentAct;
                return true;
            }

            current = default;
            return false;
        }

        private void OnObservedActTransition(Act previous, Act current)
        {
            _ = previous;

            // Reset conference state on a real act transition only.
            m_ConferenceDialogActive = false;

            // S6-04 FIX: Replenish conference uses on act boundary.
            // Each major transition gives the player fresh diplomatic options.
            // NOTE: Sanctions state (SanctionsActive, SanctionDaysRemaining, TradePenalty) intentionally
            // persists through act transitions — diplomatic consequences outlast phase boundaries.
            int maxUses = MAX_USES;
            if (current >= Act.Adaptation)
            {
                if (TryMarkActReplenished(current))
                {
                    // Full replenish on Adaptation+ (long-term phase, full diplomacy access)
                    m_State.UsesRemaining = maxUses;
                    if (!m_State.SanctionsActive)
                        m_State.CooldownDaysRemaining = 0;
                    Log.Info($"[DonorConference] Act {current} — full replenish ({maxUses} uses)");
                }
                else
                {
                    Log.Info($"[DonorConference] Act {current} already replenished, uses={m_State.UsesRemaining}");
                }
            }
            else if (current == Act.Exodus && m_State.UsesRemaining < 1)
            {
                if (TryMarkActReplenished(current))
                {
                    // Partial replenish on Exodus (ensure at least 1 use)
                    m_State.UsesRemaining = Math.Max(1, maxUses / 2);
                    if (!m_State.SanctionsActive)
                        m_State.CooldownDaysRemaining = 0;
                    Log.Info($"[DonorConference] Exodus — partial replenish ({m_State.UsesRemaining} uses)");
                }
                else
                {
                    Log.Info($"[DonorConference] Exodus already replenished, uses={m_State.UsesRemaining}");
                }
            }
            else
            {
                Log.Info($"[DonorConference] Act → {current}, uses={m_State.UsesRemaining}");
            }

            if (m_State.ActiveGenerators <= 0)
                m_GeneratorDecayCounter = 0;
        }

        private bool TryMarkActReplenished(Act act)
        {
            if (m_HasLastReplenishedAct && m_LastReplenishedAct == act)
                return false;

            m_LastReplenishedAct = act;
            m_HasLastReplenishedAct = true;
            return true;
        }

        protected override void OnDestroy()
        {
            // Unsubscribe from events
            UnsubscribeSafe<DayChangedEvent>(OnDayChanged);
            UnsubscribeSafe<ShadowNarrativeEvent>(OnShadowNarrative);

            // ECS-01 FIX: Destroy external power source entity to prevent memory leak
            var externalPowerSourceEntity = m_ExternalPowerSource.Entity;
            if (externalPowerSourceEntity != Entity.Null && EntityManager.Exists(externalPowerSourceEntity))
            {
                EntityManager.DestroyEntity(externalPowerSourceEntity);
                m_ExternalPowerSource.Invalidate();
            }

            var donorSanctionsEntity = m_DonorSanctions.Entity;
            if (donorSanctionsEntity != Entity.Null && EntityManager.Exists(donorSanctionsEntity))
            {
                EntityManager.DestroyEntity(donorSanctionsEntity);
                m_DonorSanctions.Invalidate();
            }

            Instance = null;
            Log.Info($"{nameof(DonorConferenceSystem)} destroyed");
            base.OnDestroy();
        }

        // ============================================================================
        // IResettable
        // ============================================================================

        public void ResetState()
        {
            ResetBootDefaultsFields();

            // Clear ECS power source to match reset state
            m_ExternalPowerSource.Invalidate();
            ClearExternalPowerSource();

            // A2 FIX 2c: Reset sanctions singleton
            m_DonorSanctions.Invalidate();
            WriteSanctionsSingleton(false, 0f);
        }

        private void ResetBootDefaultsFields()
        {
            m_State = DonorConferenceStateData.CreateDefault(MAX_USES);
            m_ConferenceDialogActive = false;
            m_GeneratorDecayCounter = 0;
            m_GameDay = 0;
            ResetActBaseline();
            m_LastReplenishedAct = default;
            m_HasLastReplenishedAct = false;
            m_ImportTrustPenalty = 0f;
        }

        // ============================================================================
        // DAY TRANSITION
        // ============================================================================

        private void OnDayChanged(DayChangedEvent evt)
        {
            ReconcileActTransition();
            if (!TryReadCurrentAct(out var currentAct) || currentAct < Act.Crisis) return;
            if (evt.DayNumber <= m_GameDay) return;

            using var _ = PerformanceProfiler.Measure("DonorConference.OnDayChanged");
            m_GameDay = evt.DayNumber;
            var diplomacyConfig = BalanceConfig.Current.Diplomacy;

            // Decrement cooldown (1 day per event)
            if (m_State.CooldownDaysRemaining > 0)
            {
                m_State.CooldownDaysRemaining = Math.Max(0, m_State.CooldownDaysRemaining - 1f);
                if (Log.IsDebugEnabled) Log.Debug($"[DonorConference] Day {evt.DayNumber}: Cooldown {m_State.CooldownDaysRemaining:F1} days remaining");
            }

            // Generator decay — lose 1 generator every N days
            int decayInterval = diplomacyConfig.GeneratorDecayIntervalDays;
            if (m_State.ActiveGenerators > 0 && decayInterval > 0)
            {
                m_GeneratorDecayCounter++;
                if (m_GeneratorDecayCounter >= decayInterval)
                {
                    m_GeneratorDecayCounter = 0;
                    m_State.ActiveGenerators--;
                    UpdateExternalPowerSource();
                    Log.Info($"[DonorConference] Generator expired — {m_State.ActiveGenerators} remaining");
                }
            }

            // Decrement sanctions timer
            if (m_State.SanctionsActive && m_State.SanctionDaysRemaining > 0)
            {
                m_State.SanctionDaysRemaining = Math.Max(0f, m_State.SanctionDaysRemaining - 1f);
                if (m_State.SanctionDaysRemaining <= 0)
                {
                    OnSanctionsExpired();
                }
                else
                {
                    if (Log.IsDebugEnabled) Log.Debug($"[DonorConference] Day {evt.DayNumber}: Sanctions {m_State.SanctionDaysRemaining:F1} days remaining");
                }
            }

            if (m_ImportTrustPenalty > 0f)
            {
                float decay = GameRate.ScalePerDay(
                    diplomacyConfig.ImportTrustPenaltyDecayPerDay,
                    GameRate.SECONDS_PER_DAY);
                m_ImportTrustPenalty = Math.Max(0f, m_ImportTrustPenalty - decay);
            }
        }

        /// <summary>
        /// FIX M-100: Accumulate trust penalty when shadow import is discovered.
        /// Adds to the scandal penalty used in trust calculation.
        /// </summary>
        private void OnShadowNarrative(ShadowNarrativeEvent evt)
        {
            if (evt.Type != ShadowNarrativeEventType.ImportDiscovered) return;
            if (evt.TrustDecrease <= 0f) return;

            m_ImportTrustPenalty = Math.Min(m_ImportTrustPenalty + evt.TrustDecrease, 100f);
            Log.Info($"Import discovered — donor trust -{evt.TrustDecrease:F1} (total import penalty: {m_ImportTrustPenalty:F1})");
        }

        // ============================================================================
        // EXTERNAL POWER SOURCE
        // ============================================================================

        private void RestoreExternalPowerSourceEntity()
        {
            if (m_ExternalPowerSourceQuery == default)
            {
                m_ExternalPowerSourceQuery = GetEntityQuery(
                    ComponentType.ReadWrite<ExternalPowerSource>(),
                    ComponentType.ReadOnly<ExternalPowerDonorTag>());
                m_ExternalPowerSource = CreateSingletonHandle<ExternalPowerSource>(m_ExternalPowerSourceQuery);
            }

            EnsureSingleton(
                ref m_ExternalPowerSource,
                new ExternalPowerSource { BonusMW = 0 },
                EnsureExternalPowerSourceShape);
        }

        private void RestoreExternalPowerSourceEntity(EntityManager entityManager)
        {
            if (m_ExternalPowerSourceQuery == default)
            {
                m_ExternalPowerSourceQuery = GetEntityQuery(
                    ComponentType.ReadWrite<ExternalPowerSource>(),
                    ComponentType.ReadOnly<ExternalPowerDonorTag>());
                m_ExternalPowerSource = CreateSingletonHandle<ExternalPowerSource>(m_ExternalPowerSourceQuery);
            }

            EnsureSingleton(
                ref m_ExternalPowerSource,
                entityManager,
                new ExternalPowerSource { BonusMW = 0 },
                EnsureExternalPowerSourceShape);
        }

        private Entity ResolveExternalPowerSourceEntity()
        {
            return ResolveSingletonReadOnly(ref m_ExternalPowerSource);
        }

        private static void EnsureExternalPowerSourceShape(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<ExternalPowerDonorTag>(entity))
                em.AddComponentData(entity, new ExternalPowerDonorTag());
            em.SetName(entity, "ExternalPowerSource.Donors");
        }

        private void UpdateExternalPowerSource()
        {
            var externalPowerSourceEntity = ResolveExternalPowerSourceEntity();
            m_ExternalPowerLookup.Update(this);
            if (externalPowerSourceEntity == Entity.Null || !m_ExternalPowerLookup.HasComponent(externalPowerSourceEntity))
                return;

            int mwEach = m_State.GeneratorMW > 0 ? m_State.GeneratorMW : BalanceConfig.Current.Diplomacy.GeneratorMw;
            int bonusMW = m_State.ActiveGenerators * mwEach;
            m_ExternalPowerLookup[externalPowerSourceEntity] = new ExternalPowerSource { BonusMW = bonusMW };
        }

        private void ClearExternalPowerSource()
        {
            var externalPowerSourceEntity = ResolveExternalPowerSourceEntity();
            m_ExternalPowerLookup.Update(this);
            if (externalPowerSourceEntity == Entity.Null || !m_ExternalPowerLookup.HasComponent(externalPowerSourceEntity))
                return;

            m_ExternalPowerLookup[externalPowerSourceEntity] = new ExternalPowerSource { BonusMW = 0 };
        }

        // ============================================================================
        // STATUS CHECK
        // ============================================================================

        /// <summary>
        /// Check if conference can be called.
        /// </summary>
        public ConferenceStatus GetConferenceStatus()
        {
            if (!TryReadCurrentAct(out var currentAct) || currentAct < Act.Crisis)
                return ConferenceStatus.TooEarlyInGame;

            // Sanctions first — diplomatic consequence should not be masked by uses/cooldown
            if (m_State.SanctionsActive)
                return ConferenceStatus.Sanctions;

            if (m_State.UsesRemaining <= 0)
                return ConferenceStatus.NoUsesRemaining;

            if (m_State.CooldownDaysRemaining > 0)
                return ConferenceStatus.Cooldown;

            if (HasInFlightDonorFundsGrant())
                return ConferenceStatus.Cooldown;

            if (m_GameDay < MIN_GAME_DAY)
                return ConferenceStatus.TooEarlyInGame;

            float crisisLevel = GetCrisisLevel();
            if (crisisLevel < CRISIS_THRESHOLD)
                return ConferenceStatus.CrisisTooLow;

            if (!TryGetTrustInputs(out _, out _, out _))
                return ConferenceStatus.TrustSourceUnavailable;

            return ConferenceStatus.Available;
        }

        /// <summary>
        /// Get status as string for UI.
        /// </summary>
        public static string GetStatusString()
        {
            var dc = Instance;
            if (dc == null) return "unavailable";

            var status = dc.GetConferenceStatus();
            return status switch
            {
                ConferenceStatus.Available => "available",
                ConferenceStatus.Cooldown => "cooldown",
                ConferenceStatus.Sanctions => "sanctions",
                ConferenceStatus.NoUsesRemaining => "no_uses",
                ConferenceStatus.CrisisTooLow => "crisis_low",
                ConferenceStatus.TooEarlyInGame => "too_early",
                ConferenceStatus.TrustSourceUnavailable => "trust_locked",
                _ => "unavailable"
            };
        }

        // ============================================================================
        // DIALOG CONTROL
        // ============================================================================

        /// <summary>
        /// Open conference dialog (player chooses aid type).
        /// </summary>
        public void OpenConferenceDialog()
        {
            if (GetConferenceStatus() != ConferenceStatus.Available)
            {
                Log.Warn("OpenConferenceDialog called when not available");
                return;
            }

            if (!TryGetTrustInputs(out var corruption, out var scandalPenalty, out var trustReason))
            {
                Log.Warn($"OpenConferenceDialog rejected: trust source unavailable ({trustReason})");
                return;
            }

            m_ConferenceDialogActive = true;
            Log.Info("Donor Conference dialog opened");

            TrustLevel trust = ResolveTrustLevel(corruption, scandalPenalty);

            EventBus?.SafePublish(new DonorEvent(DonorEventType.ConferenceCalled, Trust: trust), "DonorConferenceSystem");
        }

        /// <summary>
        /// Close dialog without choosing.
        /// </summary>
        public void CloseConferenceDialog()
        {
            m_ConferenceDialogActive = false;
        }

        // ============================================================================
        // AID SELECTION
        // ============================================================================

        private DonorConferenceResult SelectAid(DonorAidType aidType, RequestMeta requestMeta)
        {
            m_ConferenceDialogActive = false;

            // Get trust level from corruption + scandal penalty
            if (!TryGetTrustInputs(out var corruption, out var scandalPenalty, out var trustReason))
            {
                Log.Warn($"Donor selection rejected: trust source unavailable ({trustReason})");
                return new DonorConferenceResult
                {
                    Success = false,
                    AidType = aidType,
                    DonorMessage = DonorMessageIds.RefusalTrustSourceUnavailable,
                    DonorSpeaker = "Donor Coordination Desk"
                };
            }
            TrustLevel trust = ResolveTrustLevel(corruption, scandalPenalty);
            AidTier shockTier = GetShockTier();

            Log.Info($"Donor Conference: Corruption={corruption:F1}%, Scandal={scandalPenalty:F1}, Trust={trust}, ShockTier={shockTier}, Requested={aidType}");

            DonorAidType originalAidType = aidType;

            // Check if aid type is available at this trust level and shock tier
            if (!DonorAidCalculator.IsAidAvailable(trust, aidType, shockTier))
            {
                Log.Warn($"Aid type {aidType} not available at trust level {trust}");

                // If refused entirely, apply sanctions
                if (trust == TrustLevel.Refused)
                {
                    return ApplyRefusedResult(trust, aidType);
                }

                // Otherwise, downgrade to best available
                // W6-01 FIX: Defense blocked by ShockTier (Full trust, but not GlobalShock) → downgrade to Power
                if (aidType == DonorAidType.Defense && trust == TrustLevel.Full && shockTier != AidTier.GlobalShock)
                {
                    Log.Info($"W6-01: Defense downgraded to Power — ShockTier {shockTier} < GlobalShock");
                    aidType = DonorAidType.Power;
                }
                else if (aidType == DonorAidType.Defense && trust == TrustLevel.Partial)
                {
                    aidType = DonorAidType.Power;
                }
                else if (aidType == DonorAidType.Defense && trust == TrustLevel.Minimal)
                {
                    Log.Info("Defense downgraded to Funds — Minimal trust");
                    aidType = DonorAidType.Funds;
                }
                // Re-check after downgrade: Power requires Headlines+ shock tier
                if (aidType == DonorAidType.Power && !DonorAidCalculator.IsAidAvailable(trust, aidType, shockTier))
                {
                    Log.Info($"Power unavailable at ShockTier {shockTier}, downgraded to Funds");
                    aidType = DonorAidType.Funds;
                }
            }

            // Defense cap is a structural no-op, so reject before spending a conference use.
            // Other cap checks keep their historical consumed-use semantics.
            if (aidType == DonorAidType.Defense && !CanAcceptDefenseAid(out var defenseBlockReason, out var defenseBlockMessage))
            {
                Log.Warn(defenseBlockReason);
                var capResult = new DonorConferenceResult { Success = false, AidType = aidType, DonorMessage = defenseBlockMessage, DonorSpeaker = "NATO Representative" };
                EventBus?.SafePublish(new DonorEvent(DonorEventType.Refused, Trust: TrustLevel.Refused, Message: capResult.DonorMessage));
                return capResult;
            }

            if (aidType != DonorAidType.Funds)
                ConsumeConferenceUse();

            // R5-F2 FIX: Block Power aid if generator cap already reached — prevents
            // wasting a conference use for zero benefit (Math.Min cap silently discards).
            if (aidType == DonorAidType.Power && m_State.ActiveGenerators >= MAX_GENERATORS)
            {
                Log.Warn($"Power aid blocked — generator cap reached ({MAX_GENERATORS}), use consumed");
                var capResult = new DonorConferenceResult { Success = false, AidType = aidType, DonorMessage = DonorMessageIds.RefusalGeneratorCap, DonorSpeaker = "USAID Director" };
                EventBus?.SafePublish(new DonorEvent(DonorEventType.Refused, Trust: TrustLevel.Refused, Message: capResult.DonorMessage));
                return capResult;
            }

            // Calculate result — single source of truth (shock + trust → amounts)
            var result = AidMatrixCalculator.ToConferenceResult(shockTier, trust, aidType);

            // L-38 FIX: If aid was downgraded, publish a stable message id so the UI/narrative
            // can localize the explanation for receiving a different aid type.
            if (aidType != originalAidType)
                result.DonorMessage = result.Success
                    ? DonorMessageIds.AidDefenseDowngraded
                    : DonorMessageIds.RefusalDefenseUnavailable;

            if (aidType == DonorAidType.Funds)
            {
                if (result.Success)
                {
                    if (QueueDonorFundsGrant(result, requestMeta))
                    {
                        Log.Info($"Queued durable donor funds grant ${result.FundsReceived:N0}");
                        return result;
                    }

                    return new DonorConferenceResult
                    {
                        Success = false,
                        AidType = aidType,
                        DonorMessage = DonorMessageIds.RefusalTrustUnavailable,
                        DonorSpeaker = "Donor Coordination Desk"
                    };
                }

                ConsumeConferenceUse();
                return result;
            }

            // Apply effects
            ApplyAidEffects(result);

            // Trigger notifications
            TriggerNotifications(result);

            Log.Info($"Donor Conference complete: {aidType}, Success={result.Success}");

            return result;
        }

        private void ConsumeConferenceUse()
        {
            m_State.UsesRemaining = Math.Max(0, m_State.UsesRemaining - 1);
            m_State.CooldownDaysRemaining = COOLDOWN_DAYS;
        }

        private bool QueueDonorFundsGrant(in DonorConferenceResult result, in RequestMeta requestMeta)
        {
            if (result.FundsReceived <= 0)
                return false;
            if (HasInFlightDonorFundsGrant())
                return false;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            string operationKey = DonorFundsOperationKey(requestMeta, result.FundsReceived);
            var ownerEntity = ecb.CreateEntity();
            ecb.AddComponent(ownerEntity, new DonorFundsGrantIntent
            {
                Amount = result.FundsReceived,
                RequestId = requestMeta.RequestId,
                OperationKey = new FixedString128Bytes(operationKey),
                DonorMessage = new FixedString128Bytes(result.DonorMessage ?? string.Empty),
                TerminalResolved = false,
                TerminalSucceeded = false,
                TerminalApplied = false
            });
            EnsureDonorFundsGrantTransport(ecb, result.FundsReceived, operationKey);
            // ECB is recorded synchronously on the main thread (no scheduled producer job),
            // so there is no producer handle to register — the barrier plays the buffer back itself.
            return true;
        }

        private void DrainResolvedDonorFundsGrants()
        {
            if (m_DonorFundsGrantQuery.IsEmpty)
                return;

            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            foreach (var (intentRef, entity) in
                SystemAPI.Query<RefRW<DonorFundsGrantIntent>>()
                .WithEntityAccess())
            {
                if (!hasEcb)
                {
                    ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    hasEcb = true;
                }

                var intent = intentRef.ValueRO;
                string operationKey = ResolveDonorFundsOperationKey(in intent);
                if (intent.OperationKey.Length == 0)
                {
                    intent.OperationKey = new FixedString128Bytes(operationKey);
                    intentRef.ValueRW = intent;
                }

                if (SystemAPI.HasComponent<BudgetAddFundsRequest>(entity)
                    && !SystemAPI.HasComponent<BudgetAddFundsResult>(entity))
                {
                    var owner = ecb.CreateEntity();
                    ecb.AddComponent(owner, intent);
                    ecb.RemoveComponent<DonorFundsGrantIntent>(entity);
                    Log.Warn($"Migrated donor funds grant owner off retained budget request: op={operationKey}");
                    continue;
                }

                if (TryFindDonorFundsGrantResult(operationKey, out var resultEntity, out var result))
                {
                    if (intent.TerminalResolved)
                    {
                        ecb.DestroyEntity(resultEntity);
                        continue;
                    }

                    if (result.Succeeded && result.AppliedAmount > 0)
                    {
                        if (!intent.TerminalApplied)
                        {
                            ApplyConfirmedDonorFundsGrant(intent, result.AppliedAmount);
                            EmitDonorFundsSelectionResult(ecb, intent, RequestStatus.Success, ReasonId.None);
                            intent.TerminalApplied = true;
                        }
                    }
                    else
                    {
                        Log.Warn($"Donor funds grant failed before terminal donor use: op={operationKey} requested=${intent.Amount:N0} applied=${result.AppliedAmount:N0}");
                        EmitDonorFundsSelectionResult(ecb, intent, RequestStatus.Failed, ReasonIds.DonorSelectionRejected);
                    }

                    intent.TerminalResolved = true;
                    intent.TerminalSucceeded = result.Succeeded;
                    intentRef.ValueRW = intent;
                    ecb.DestroyEntity(resultEntity);
                    continue;
                }

                if (intent.TerminalResolved)
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (!HasDonorFundsGrantTransport(operationKey))
                {
                    EnsureDonorFundsGrantTransport(ecb, intent.Amount, operationKey);
                    Log.Warn($"Re-issued donor funds grant transport: op={operationKey} amount=${intent.Amount:N0}");
                }
            }

            if (hasEcb)
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
        }

        private void ApplyConfirmedDonorFundsGrant(in DonorFundsGrantIntent intent, long appliedAmount)
        {
            ConsumeConferenceUse();
            ApplyDebtReliefIfEligible();

            var result = new DonorConferenceResult
            {
                Success = true,
                AidType = DonorAidType.Funds,
                FundsReceived = appliedAmount,
                DonorMessage = intent.DonorMessage.ToString(),
                DonorSpeaker = "Donor Coordination Desk"
            };

            ApplyAidEffects(result);
            TriggerNotifications(result);
            Log.Info($"Donor Conference complete: {DonorAidType.Funds}, Success=True, requested=${intent.Amount:N0}, confirmed=${appliedAmount:N0}");
        }

        private bool HasInFlightDonorFundsGrant()
        {
            // At most one DonorFundsGrantIntent exists (queued on the owner entity, gated by this
            // check). Cached-query singleton read is context-free, unlike SystemAPI.Query, so it
            // stays correct when reached from the sync UI status path (GetConferenceStatus).
            return m_DonorFundsGrantQuery.TryGetSingleton<DonorFundsGrantIntent>(out var intent)
                && !intent.TerminalResolved;
        }

        private void EnsureDonorFundsGrantTransport(EntityCommandBuffer ecb, long amount, string operationKey)
        {
            if (amount <= 0 || HasDonorFundsGrantTransport(operationKey))
                return;

            // amount > 0 is guaranteed by the guard above, so the queue always succeeds;
            // the bool is discarded intentionally (the only failure mode is amount <= 0).
            _ = BudgetEmitter.TryQueueAddFunds(
                ecb,
                amount,
                BudgetSource.DonorAid,
                BudgetIncomeKind.DonorOrEmergencyCredit,
                operationKey,
                out _,
                BudgetResultMode.RetainResult);
        }

        private bool HasDonorFundsGrantTransport(string operationKey)
        {
            foreach (var request in SystemAPI.Query<RefRO<BudgetAddFundsRequest>>())
            {
                if (request.ValueRO.OperationKey.ToString() == operationKey)
                    return true;
            }

            return false;
        }

        private bool TryFindDonorFundsGrantResult(string operationKey, out Entity resultEntity, out BudgetAddFundsResult result)
        {
            foreach (var (requestRef, resultRef, entity) in
                SystemAPI.Query<RefRO<BudgetAddFundsRequest>, RefRO<BudgetAddFundsResult>>()
                .WithEntityAccess())
            {
                if (requestRef.ValueRO.OperationKey.ToString() != operationKey)
                    continue;

                resultEntity = entity;
                result = resultRef.ValueRO;
                return true;
            }

            resultEntity = Entity.Null;
            result = default;
            return false;
        }

        private void EmitDonorFundsSelectionResult(EntityCommandBuffer ecb, in DonorFundsGrantIntent intent, RequestStatus status, ReasonId reason)
        {
            if (intent.RequestId <= 0)
                return;
            // RequestResultEvent is terminal-only — Pending is tracked through the durable
            // operation bridge, never emitted here (callers pass Success/Failed only).
            if (status == RequestStatus.Pending)
                return;

            RequestResultEmitter.Emit(
                ecb,
                intent.RequestId,
                RequestKind.DonorSelection,
                status,
                reason,
                SystemAPI.Time.ElapsedTime);
        }

        private void ApplyDebtReliefIfEligible()
        {
            var debtConfig = BalanceConfig.Current.Debt;
            float shockLevel = GetShockLevel();
            long totalDebt = CityDebtService.GetTotalDebt();

            if (shockLevel < debtConfig.ReliefShockThreshold || totalDebt <= 0)
                return;

            long forgiven = CityDebtService.ApplyDebtRelief(debtConfig.ReliefPercent);
            if (forgiven > 0)
                Log.Info($"Debt relief: ${forgiven:N0} forgiven (shock={shockLevel:F1}%)");
        }

        private static string DonorFundsOperationKey(in RequestMeta requestMeta, long amount)
        {
            int requestId = requestMeta.RequestId;
            uint frame = requestMeta.CreatedFrame;
            return $"DonorFunds:{requestId}:{frame}:{amount}";
        }

        private static string ResolveDonorFundsOperationKey(in DonorFundsGrantIntent intent)
        {
            string stored = intent.OperationKey.ToString();
            if (!string.IsNullOrEmpty(stored))
                return stored;

            return $"DonorFunds:Legacy:{intent.RequestId}:{intent.Amount}";
        }

        private bool IsDonorPatriotCreditCapReached()
            => m_AirDefenseCreditsReader.IsDonorPatriotCreditCapReached;

        private bool CanAcceptDefenseAid(out string blockReason, out string donorMessage)
        {
            if (!m_AirDefenseCreditsReader.IsAvailable)
            {
                blockReason = "Defense aid blocked — AirDefense credit consumer unavailable";
                donorMessage = DonorMessageIds.RefusalDefenseUnavailable;
                return false;
            }

            if (IsDonorPatriotCreditCapReached())
            {
                blockReason = "Defense aid blocked — donor Patriot credit cap reached";
                donorMessage = DonorMessageIds.RefusalPatriotCap;
                return false;
            }

            blockReason = "";
            donorMessage = "";
            return true;
        }

        private DonorConferenceResult ApplyRefusedResult(TrustLevel trust, DonorAidType aidType)
        {
            // Still consume a use
            m_State.UsesRemaining = Math.Max(0, m_State.UsesRemaining - 1);
            m_State.CooldownDaysRemaining = COOLDOWN_DAYS;

            var result = AidMatrixCalculator.ToConferenceResult(GetShockTier(), trust, aidType);
            // W6-FIX (S1-01): Apply sanctions ONLY if not already active to prevent Death Spiral / Timer Overwrite
            if (!m_State.SanctionsActive)
            {
                m_State.ApplySanction(Math.Max(result.SanctionDays, 1), result.TradePenalty);

                // A2 FIX 2c: Write singleton (replaces L1↔L1 EventBus state sync)
                WriteSanctionsSingleton(true, result.TradePenalty);

                EventBus?.SafePublish(new DonorEvent(DonorEventType.SanctionsApplied, Days: result.SanctionDays, Penalty: result.TradePenalty));
            }
            else
            {
                Log.Info("S1-01 FIX: Refused while under sanctions. Use consumed, timer NOT reset.");
            }

            // Keep EventBus for L3 consumers (DonorNarrativeResolver, TelemetryEventListener)
            EventBus?.SafePublish(new DonorEvent(DonorEventType.Refused, Trust: TrustLevel.Refused, Message: result.DonorMessage));

            Log.Warn($"Donor Conference REFUSED! Sanctions for {result.SanctionDays} days");

            return result;
        }

        private void ApplyAidEffects(DonorConferenceResult result)
        {
            if (!result.Success)
            {
                return;
            }

            EventBus?.SafePublish(new DonorEvent(
                DonorEventType.AidPackageReceived,
                Message: result.DonorMessage), "DonorConferenceSystem");

            switch (result.AidType)
            {
                case DonorAidType.Funds:
                    // EDA: FundsReceived event handled by DonorFundsHandlerSystem
                    // No direct call to CityBudgetService
                    break;

                case DonorAidType.Power:
                    // Internal state - generators are owned by this system
                    // R3-B-3 FIX: Cap to MAX_GENERATORS to prevent unbounded accumulation
                    m_State.ActiveGenerators = Math.Min(m_State.ActiveGenerators + result.GeneratorsReceived, MAX_GENERATORS);
                    if (result.GeneratorMW > 0) m_State.GeneratorMW = result.GeneratorMW;
                    m_GeneratorDecayCounter = 0; // Reset decay counter so first decay fires after full interval
                    UpdateExternalPowerSource();
                    break;

                case DonorAidType.Defense:
                    // Create ECS request for AirDefenseStateSystem to grant donor Patriot credits
                    var ecbLocal = m_GameSimulationEndBarrier.CreateCommandBuffer();
                    var creditEntity = ecbLocal.CreateEntity();
                    ecbLocal.AddComponent(creditEntity, new GrantDonorPatriotCreditsRequest { Credits = 1 });
                    RequestMetaWriter.AddInternal(ecbLocal, creditEntity, nameof(GrantDonorPatriotCreditsRequest), nameof(DonorAidType.Defense));
                    Log.Info("Donor Patriot credit granted (1 free placement)");
                    // Channel (donors): a defense package also bundles counter-attack
                    // munitions. The aid is paid for diplomatically, so this is a free grant
                    // (no budget gate) — CounterAttackArsenalSystem applies the stock next tick.
                    GrantDonorArsenal(ecbLocal);
                    break;
                default:
                    Log.Warn($"[ApplyAidEffects] Unhandled {nameof(DonorAidType)}: {result.AidType}");
                    break;
            }
        }

        /// <summary>
        /// Queue free counter-attack arsenal grants bundled into a defense aid package.
        /// Reuses the caller's GameSimulationEndBarrier ECB. Crosses to GridWarfare only
        /// through Core (ICounterAttackArsenalService + Core ArsenalProcurementEmitter) —
        /// no Domain→Domain import (Axiom 5). Fail-closed: a null-object arsenal service
        /// allocates batch id 0, which QueueFreeGrant treats as a normal (free) batch the
        /// owner system drops if the singleton is missing.
        /// </summary>
        private void GrantDonorArsenal(EntityCommandBuffer ecb)
        {
            if (!m_Arsenal.IsAvailable)
            {
                Log.Info("Donor arsenal grant skipped: arsenal service unavailable");
                return;
            }

            // Distinct batch id per kind (the owner pipeline keys stock grants by batch).
            // Only mint an id + queue a batch when the grant is non-zero: AllocateProcurementBatchId
            // runs a sync-point batch scan, so a 0-tuned grant would burn an id and a sync for a
            // no-op (QueueFreeGrant early-returns on count <= 0 anyway).
            var gw = BalanceConfig.Current.GridWarfare;
            int droneGrant = gw.DonorArsenalDroneGrant;
            int ballisticGrant = gw.DonorArsenalBallisticGrant;
            if (droneGrant > 0)
                ArsenalProcurementEmitter.QueueFreeGrant(
                    ecb, m_Arsenal.AllocateProcurementBatchId(), ArsenalKind.Drone, droneGrant);
            if (ballisticGrant > 0)
                ArsenalProcurementEmitter.QueueFreeGrant(
                    ecb, m_Arsenal.AllocateProcurementBatchId(), ArsenalKind.Ballistic, ballisticGrant);

            if (droneGrant <= 0 && ballisticGrant <= 0)
            {
                Log.Info("Donor defense package: arsenal grant tuned to 0, nothing queued");
                return;
            }

            Log.Info($"Donor defense package: free arsenal grant {droneGrant}x Drone + {ballisticGrant}x Ballistic");
        }

        private void TriggerNotifications(DonorConferenceResult result)
        {
            if (!result.Success)
            {
                return;
            }

            switch (result.AidType)
            {
                case DonorAidType.Funds:
                    EventBus?.SafePublish(new DonorEvent(
                        DonorEventType.FundsReceived,
                        Amount: result.FundsReceived,
                        Message: result.DonorMessage));
                    break;

                case DonorAidType.Power:
                    EventBus?.SafePublish(new DonorEvent(
                        DonorEventType.GeneratorsReceived,
                        Count: result.GeneratorsReceived,
                        MWEach: result.GeneratorMW,
                        Message: result.DonorMessage));
                    break;

                case DonorAidType.Defense:
                    EventBus?.SafePublish(new DonorEvent(
                        DonorEventType.PatriotReceived,
                        Days: result.PatriotDays,
                        Message: result.DonorMessage));
                    break;
                default:
                    Log.Warn($"[TriggerNotifications] Unhandled {nameof(DonorAidType)}: {result.AidType}");
                    break;
            }
        }

        // ============================================================================
        // EXPIRATION HANDLERS
        // ============================================================================

        private void OnSanctionsExpired()
        {
            m_State.ClearSanctions();

            // A2 FIX 2c: Write singleton (replaces L1↔L1 EventBus state sync)
            WriteSanctionsSingleton(false, 0f);

            EventBus?.SafePublish(new DonorEvent(DonorEventType.SanctionsExpired), "DonorConferenceSystem");
            Log.Info("International sanctions expired");
        }

        /// <summary>
        /// A2 FIX 2c: Write sanctions state to ECS singleton.
        /// Replaces DonorEvent(SanctionsApplied/Expired) for CrisisEconomicsSystem.
        /// </summary>
        /// <summary>
        /// FIX H59: Write via EntityQuery + EntityManager instead of SystemAPI.TryGetSingletonRW.
        /// SystemAPI triggers CompleteDependencyBeforeRW (sync point) — unsafe from event handlers.
        /// EntityManager.SetComponentData is a direct write, no dependency tracking.
        /// Pattern: same as ScandalSystem.UpdateSingleton().
        /// </summary>
        private void WriteSanctionsSingleton(bool active, float tradePenalty)
        {
            var sanctionsEntity = ResolveDonorSanctionsEntity();
            m_DonorSanctionsLookup.Update(this);
            if (sanctionsEntity == Entity.Null || !m_DonorSanctionsLookup.HasComponent(sanctionsEntity))
                return;

            m_DonorSanctionsLookup[sanctionsEntity] = new DonorSanctionsSingleton
            {
                SanctionsActive = active,
                TradePenalty = Math.Min(Math.Max(tradePenalty, 0f), 1f)
            };
        }

        private void RestoreDonorSanctionsEntity()
        {
            if (m_DonorSanctionsQuery == default)
                m_DonorSanctionsQuery = GetEntityQuery(ComponentType.ReadWrite<DonorSanctionsSingleton>());
            if (!m_DonorSanctions.IsCreated)
                m_DonorSanctions = CreateSingletonHandle<DonorSanctionsSingleton>(m_DonorSanctionsQuery);

            EnsureSingleton(
                ref m_DonorSanctions,
                EntityManager,
                default(DonorSanctionsSingleton));
        }

        private void RestoreDonorSanctionsEntity(EntityManager entityManager)
        {
            if (m_DonorSanctionsQuery == default)
                m_DonorSanctionsQuery = GetEntityQuery(ComponentType.ReadWrite<DonorSanctionsSingleton>());
            if (!m_DonorSanctions.IsCreated)
                m_DonorSanctions = CreateSingletonHandle<DonorSanctionsSingleton>(m_DonorSanctionsQuery);

            EnsureSingleton(
                ref m_DonorSanctions,
                entityManager,
                default(DonorSanctionsSingleton));
        }

        private Entity ResolveDonorSanctionsEntity()
        {
            return ResolveSingletonReadOnly(ref m_DonorSanctions);
        }


        // ============================================================================
        // AID MATRIX INTEGRATION (Shock × Trust)
        // ============================================================================

        /// <summary>
        /// Get filtered aid based on BOTH World Shock AND Trust.
        /// This is the combined Attention Economy result.
        /// </summary>
        public FilteredAidPackage GetFilteredAid()
        {
            var shockTier = GetShockTier();
            if (!TryGetTrustInputs(out var corruption, out var scandalPenalty, out _))
                return new FilteredAidPackage { ShockTier = shockTier, TrustLevel = TrustLevel.Refused };

            var trust = ResolveTrustLevel(corruption, scandalPenalty);
            var package = AidMatrixCalculator.Calculate(shockTier, trust);

            float shockLevel = GetShockLevel();

            if (Log.IsDebugEnabled) Log.Debug($"[DonorConference] Shock: {shockLevel:F1}% ({shockTier}), " +
                $"Corruption: {corruption:F1}%, Scandal: {scandalPenalty:F1} ({package.TrustLevel})");

            if (package.HasBlockedItems)
            {
                if (Log.IsDebugEnabled) Log.Debug($"[DonorConference] Blocked: reason={package.BlockedReason}");
            }

            return package;
        }

        /// <summary>
        /// Static accessor for filtered aid package.
        /// </summary>
        public static FilteredAidPackage GetCurrentFilteredAid()
        {
            if (Instance != null) return Instance.GetFilteredAid();
            WarnNull();
            return new FilteredAidPackage();
        }

        /// <summary>
        /// Check if Patriot is available (GlobalShock + Full Trust).
        /// </summary>
        public static bool IsPatriotAvailable()
        {
            if (Instance == null) { WarnNull(); return false; }
            if (!Instance.TryGetTrustInputs(out var corruption, out var scandalPenalty, out _))
                return false;
            return ResolveTrustLevel(corruption, scandalPenalty) == TrustLevel.Full
                && Instance.GetShockTier() == AidTier.GlobalShock;
        }

        /// <summary>
        /// Check if Patriot is offered but blocked by trust.
        /// </summary>
        public static bool IsPatriotBlocked()
        {
            if (Instance == null) { WarnNull(); return false; }
            if (!Instance.TryGetTrustInputs(out var corruption, out var scandalPenalty, out _))
                return true;
            return Instance.GetShockTier() == AidTier.GlobalShock
                && ResolveTrustLevel(corruption, scandalPenalty) != TrustLevel.Full;
        }

        /// <summary>
        /// Get current world shock tier.
        /// </summary>
        public static AidTier GetCurrentShockTier()
        {
            if (Instance != null) return Instance.GetShockTier();
            WarnNull();
            return AidTier.DeepConcern;
        }

        /// <summary>
        /// Get current shock level (0-100).
        /// </summary>
        public static float GetCurrentShockLevel()
        {
            if (Instance != null) return Instance.GetShockLevel();
            WarnNull();
            return 0f;
        }

        /// <summary>
        /// Get current scandal penalty (from ScandalSystem).
        /// </summary>
        public static float GetScandalPenalty()
        {
            if (Instance != null) return Instance.GetScandalPenaltyInternal();
            WarnNull();
            return 0f;
        }

        // ============================================================================
        // SINGLETON ACCESSORS (ECS-Pure)
        // ============================================================================

        /// <summary>
        /// Get shock level from ECS singleton.
        /// </summary>
        private float GetShockLevel()
        {
            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock))
                return shock.ShockLevel;
            return 0f;
        }

        /// <summary>
        /// Get aid tier from ECS singleton.
        /// </summary>
        private AidTier GetShockTier()
        {
            if (m_ShockQuery.TryGetSingleton<ShockStateSingleton>(out var shock))
                return shock.CurrentTier;
            return AidTier.DeepConcern;
        }

        /// <summary>
        /// Get crisis level from ECS singleton.
        /// </summary>
        private float GetCrisisLevel()
        {
            if (m_CrisisQuery.TryGetSingleton<CrisisStateSingleton>(out var crisis))
                return crisis.CrisisLevel;
            return 0f;
        }

        /// <summary>
        /// Get net trust modifier (scandal penalty minus Greta bonus).
        /// Greta "Lightning Rod" effect: Western partners love eco-activism optics.
        /// </summary>
        private float GetScandalPenaltyInternal()
        {
            float penalty = 0f;

            // Scandal penalty (positive = bad for trust)
            if (m_ScandalQuery.TryGetSingleton<ScandalStateSingleton>(out var scandal))
                penalty = scandal.ScandalPenalty;

            // FIX M-100: Add accumulated import discovery trust penalty
            penalty += m_ImportTrustPenalty;

            // Greta bonus (negative penalty = good for trust)
            // "Lightning Rod" satire: doing nothing useful but looking progressive
            if (m_HeroStateQuery.TryGetSingleton<HeroDeploymentState>(out var heroState))
            {
                if (heroState.HeroStatus == HeroStatus.Deployed || heroState.HeroStatus == HeroStatus.Lecturing)
                {
                    // Western partners love eco-activism, even when it accomplishes nothing
                    // S2-02 FIX: Greta is a shield against scandals, not a washing machine for baseline corruption.
                    penalty = Math.Max(0, penalty - BalanceConfig.Current.Diplomacy.GretaTrustBonus);
                }
            }

            return penalty;
        }

        /// <summary>
        /// Read corruption/scandal trust inputs from CountermeasuresCoreFsm singleton.
        /// Returns false if Countermeasures lifecycle has not produced the singleton yet
        /// (transient boot timing) or in any failure mode. Beta-gating is handled
        /// upstream by DiplomacyDomain.Dependencies = ["Countermeasures"] — when
        /// Countermeasures is dep-skipped, this whole system is not registered.
        /// </summary>
        public bool TryGetTrustInputs(out float corruption, out float scandalPenalty, out ReasonId reasonId)
        {
            corruption = 0f;
            scandalPenalty = 0f;
            reasonId = ReasonId.None;

            if (!m_CountermeasuresQuery.TryGetSingleton<CountermeasuresCoreFsm>(out var core))
            {
                reasonId = ReasonIds.DonorTrustSourceUnavailable;
                return false;
            }

            corruption = core.CorruptionScore;
            scandalPenalty = GetScandalPenaltyInternal();
            return true;
        }

        public static TrustLevel ResolveTrustLevel(float corruption, float scandalPenalty)
            => DonorAidCalculator.GetTrustLevel(corruption, scandalPenalty);
    }
}
