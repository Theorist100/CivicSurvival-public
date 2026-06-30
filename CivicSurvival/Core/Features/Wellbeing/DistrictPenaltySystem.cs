using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Components.Domain.Mobilization;
using CivicSurvival.Core.Components.Domain.Engineering;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Features.Wellbeing
{
    // NOTE: ApplyPenaltiesJob REMOVED - wellbeing now handled by WellbeingResolverSystem
    // This system only tracks penalties; WellbeingResolverSystem applies them.

    /// <summary>
    /// Penalty configuration per source.
    /// </summary>
    public static class PenaltyConfig
    {
        // NOTE: Blackout/NeighborEnvy happiness penalties REMOVED - now via PsyPressure → WellbeingResolverSystem
        // Only commerce penalties remain here for those sources.
#pragma warning disable CIVIC148 // Immutable hardcoded config — no runtime accumulation
        private static readonly Dictionary<PenaltySource, (float Happiness, float Commerce)> s_Penalties = new()
#pragma warning restore CIVIC148
        {
            { PenaltySource.InternetDisabled, (0.15f, 0.20f) },  // -15% happiness, -20% commerce
            { PenaltySource.NeighborEnvy,     (0.00f, 0.00f) },  // REMOVED: happiness via PsyPressure.Envy
            { PenaltySource.WinterCold,       (0.05f, 0.00f) },  // -5% happiness
            { PenaltySource.VIPVisible,       (0.15f, 0.00f) },  // -15% happiness (Phase 5)
            { PenaltySource.FoodAidProvided,  (-0.15f, 0.00f) }, // +15% happiness (negative = bonus!)
            { PenaltySource.Blackout,         (0.00f, 0.20f) },  // REMOVED happiness: via PsyPressure.Blackout; commerce only
            { PenaltySource.ScheduledBlackout,(0.00f, 0.15f) },  // REMOVED happiness: via PsyPressure.Blackout; commerce only
            { PenaltySource.AutoDispatch,    (0.00f, 0.24f) },  // Auto load shedding: 1.2x manual blackout commerce penalty
            { PenaltySource.CognitiveCompromised, (0.10f, 0.15f) }, // -10% happiness, -15% commerce (propaganda effect)
            // Social tradeoff only. Global crisis/internet commerce effects are owned
            // by CrisisEconomicsSystem; keeping commerce at 0 prevents UI/gameplay
            // double-accounting when the hero is deployed during Crisis.
            { PenaltySource.GretaDeployed, (0.15f, 0.00f) },
            // FIX S1-01: Registered as flags by CognitiveStateSystem, but commerce impact
            // handled via CrisisEconomicsSystem.CommerceMultiplier. Zero here to prevent double-count.
            { PenaltySource.FirewallActive, (0.00f, 0.00f) },    // Commerce via CrisisEconomicsSystem
            { PenaltySource.InternetBlackout, (0.00f, 0.00f) },  // Commerce via CrisisEconomicsSystem
            { PenaltySource.ShadowExport,     (0.20f, 0.00f) },  // -20% happiness: selling power while district is dark (> VIPVisible's 15%), happiness-only
        };

        public static IReadOnlyDictionary<PenaltySource, (float Happiness, float Commerce)> Penalties => s_Penalties;

        public static void OverridePenalty(PenaltySource source, float happiness, float commerce)
        {
            s_Penalties[source] = (happiness, commerce);
        }

        public static float MAX_HAPPINESS_PENALTY => BalanceConfig.Current.Penalties.MaxHappinessPenalty;
        public static float MAX_COMMERCE_PENALTY => BalanceConfig.Current.Penalties.MaxCommercePenalty;
    }

    /// <summary>
    /// Aggregated penalties for a district.
    /// </summary>
    public struct DistrictPenalties
    {
        public PenaltySource ActiveSources;
        public float TotalHappinessPenalty;
        public float TotalCommercePenalty;
    }

    /// <summary>
    /// Central system for managing district-wide penalties.
    /// Cross-domain system used by: HumanitarianAid, Engineering, Blackout.
    ///
    /// Thread-safe: All state is delegated to ThreadSafeDistrictState.
    /// Static API methods are thin wrappers for backward compatibility.
    ///
    /// Benefits:
    /// - Single point of truth for all penalties
    /// - Thread-safe via ThreadSafeDistrictState
    /// - Easy to balance and debug
    /// - Extensible for new penalty types
    /// </summary>
    [ActIndependent]
    public partial class DistrictPenaltySystem : ThrottledSystemBase
    {
        private const float GretaDeployedHappinessPenalty = 0.15f;
        private const float GretaDeployedCommercePenalty = 0.00f;

        private static readonly LogContext Log = new("DistrictPenaltySystem");
        // Thread-safe state container (resolved in OnCreate, not lazy)
        private IDistrictStateReader? m_StateReader;
        private IDistrictStateWriter? m_StateWriter;

        // FIX: Static empty dictionary to avoid allocation when State is null
        private static readonly IReadOnlyDictionary<int, DistrictPenalties> s_EmptyPenalties =
            new Dictionary<int, DistrictPenalties>();

        // Update throttling
        // R3-C-7: PenaltyRequest buffer processed every 500ms (throttled). Event handlers
        // (OnPenaltyRegistered, OnPenaltyRemoved) apply changes immediately — buffer is only
        // for Data-Driven Commands from systems that can't use events. 500ms staleness acceptable.
        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_500_MS;

        // Public stats
        public int AffectedDistricts => EnsureState() ? m_StateReader.AffectedDistrictsCount : 0;

        /// <summary>
        /// Maximum happiness penalty across all districts (including global effects).
        /// </summary>
        public float MaxHappinessPenalty
        {
            get
            {
                float max = 0f;
                // Global penalty sum (computed once, shared across all districts)
                float globalPenalty = 0f;
                if (IsWinterActive) globalPenalty += WinterHappinessPenalty;
                if (IsInfraCollapsed) globalPenalty += INFRA_COLLAPSE_HAPPINESS_PENALTY;
                if (IsConscriptionActive) globalPenalty += CONSCRIPTION_HAPPINESS_PENALTY;
                if (IsMourningActive) globalPenalty += m_MourningHappinessPenalty;
                if (IsPreWarTensionActive) globalPenalty += m_PreWarHappinessPenalty;
                if (globalPenalty >= PenaltyConfig.MAX_HAPPINESS_PENALTY)
                    return PenaltyConfig.MAX_HAPPINESS_PENALTY;

                if (!EnsureState()) return Unity.Mathematics.math.min(globalPenalty, PenaltyConfig.MAX_HAPPINESS_PENALTY);
                var snapshot = m_StateReader.TakeSnapshot();
                if (snapshot.DistrictPenalties != null)
                {
                    foreach (var kvp in snapshot.DistrictPenalties)
                    {
                        float penalty = kvp.Value.TotalHappinessPenalty + globalPenalty;
                        if (penalty > max) max = penalty;
                    }
                }
                // If no districts have penalties, still account for global effects
                if (max <= 0f && globalPenalty > 0f)
                {
                    max = Unity.Mathematics.math.min(globalPenalty, PenaltyConfig.MAX_HAPPINESS_PENALTY);
                }
                return Unity.Mathematics.math.min(max, PenaltyConfig.MAX_HAPPINESS_PENALTY);
            }
        }

        /// <summary>
        /// Sum of all global happiness penalties (Winter, InfraCollapse, Conscription, Mourning, PreWarTension).
        /// Used by WellbeingResolverSystem to include global effects in citizen wellbeing calculation.
        /// </summary>
        public float GlobalHappinessPenaltyTotal
        {
            get
            {
                float total = 0f;
                if (IsWinterActive) total += WinterHappinessPenalty;
                if (IsInfraCollapsed) total += INFRA_COLLAPSE_HAPPINESS_PENALTY;
                if (IsConscriptionActive) total += CONSCRIPTION_HAPPINESS_PENALTY;
                if (IsMourningActive) total += m_MourningHappinessPenalty;
                if (IsPreWarTensionActive) total += m_PreWarHappinessPenalty;
                return math.min(total, PenaltyConfig.MAX_HAPPINESS_PENALTY);
            }
        }

        // Global penalties (city-wide, not district-specific)
        public bool IsWinterActive { get; private set; }
        private static float WinterHappinessPenalty => BalanceConfig.Current.Penalties.WinterHappinessPenalty;

        // Infrastructure collapse (refugees overwhelm water/sewage)
        public bool IsInfraCollapsed { get; private set; }
        private float m_InfraCollapseHoursRemaining;
        private const float INFRA_COLLAPSE_DURATION_HOURS = 24f;
        private const float INFRA_COLLAPSE_HAPPINESS_PENALTY = 0.10f; // -10%

        // Conscription (forced mobilization — global happiness penalty)
        public bool IsConscriptionActive { get; private set; }
        private const float CONSCRIPTION_HAPPINESS_PENALTY = 0.10f; // -10%

        // Mourning (post-wave casualties — temporary global happiness penalty, S14a-9)
        public bool IsMourningActive { get; private set; }
        private float m_MourningHoursRemaining;
        private float m_MourningHappinessPenalty;
        private float m_LastMourningCheckHour = -1f;
        private const int MOURNING_CASUALTY_THRESHOLD = 10;
        private const float MOURNING_MINOR_PENALTY = 0.05f;        // -5% (1-10 casualties)
        private const float MOURNING_MINOR_DURATION_HOURS = 24f;
        private const float MOURNING_MAJOR_PENALTY = 0.10f;        // -10% (>10 casualties)
        private const float MOURNING_MAJOR_DURATION_HOURS = 48f;

        // Pre-war tension (Village scenario — S18b-2 FIX: was dead write in OminousSignsSystem)
        public bool IsPreWarTensionActive { get; private set; }
        private float m_PreWarHappinessPenalty;

        // NOTE: m_CurrentDistrictLookup REMOVED - was only used by ApplyPenaltiesJob

        // Query for PenaltyRequest buffer (Data-Driven Commands pattern)
        private EntityQuery m_PenaltyRequestQuery;
        private BufferLookup<PenaltyRequest> m_PenaltyRequestBufferLookup;
        private bool m_IsProcessingPenaltyRequests;
        private const int MAX_EXPECTED_PENDING_PENALTY_REQUESTS = 512;
        [System.NonSerialized] private bool m_LoggedMissingStateServices;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Domain-Driven Initialization
            PenaltyRequestSingleton.EnsureExists(EntityManager);

            // Sync FoodAid happiness bonus from config (dictionary has static default)
            SyncConfigDerivedPenalties();

            // Query for PenaltyRequest buffer singleton
            m_PenaltyRequestQuery = GetEntityQuery(ComponentType.ReadOnly<PenaltyRequestSingleton>());
            m_PenaltyRequestBufferLookup = GetBufferLookup<PenaltyRequest>(false);

            // m_StateReader/Writer are resolved lazily in EnsureState() at
            // point-of-use, NOT here. Eager resolve in OnCreate/OnStartRunning
            // races against cross-system entry points (event handlers fired
            // before our OnStartRunning, public API called from another
            // system's OnStartRunning, e.g. MobilizationSystem.OnStartRunning
            // -> GetAllPenalties). Lazy TryGet closes the window without
            // depending on lifecycle order.

            // A2 FIX 2b: WinterActiveChangedEvent removed — read WinterStateSingleton in OnThrottledUpdate
            SubscribeRequired<PenaltyRegisteredEvent>(OnPenaltyRegistered);
            SubscribeRequired<PenaltyRemovedEvent>(OnPenaltyRemoved);
            SubscribeRequired<InfrastructureCollapseEvent>(OnInfrastructureCollapse);
            // A2 FIX 2d: ConscriptionActivatedEvent/DeactivatedEvent removed — read singleton in OnThrottledUpdate
            SubscribeRequired<WaveEndedEvent>(OnWaveEnded);
            SubscribeRequired<PreWarTensionEvent>(OnPreWarTension);

            // No self-registration: accessed via World.GetExistingSystemManaged<DistrictPenaltySystem>()

            Log.Info("Created (ThreadSafeDistrictState backend)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            PenaltyRequestSingleton.EnsureExists(EntityManager);
        }

        protected override void OnDestroy()
        {
            if (ServiceRegistry.IsInitialized)
            {
                // A2 FIX 2b: WinterActiveChangedEvent unsubscribe removed — singleton read
                UnsubscribeSafe<PenaltyRegisteredEvent>(OnPenaltyRegistered);
                UnsubscribeSafe<PenaltyRemovedEvent>(OnPenaltyRemoved);
                UnsubscribeSafe<InfrastructureCollapseEvent>(OnInfrastructureCollapse);
                // A2 FIX 2d: ConscriptionActivatedEvent/DeactivatedEvent unsubscribe removed — singleton read
                UnsubscribeSafe<WaveEndedEvent>(OnWaveEnded);
                UnsubscribeSafe<PreWarTensionEvent>(OnPreWarTension);
            }

            base.OnDestroy();
        }

        // Never skip - always check for pending requests
        protected override bool ShouldSkipUpdate() => false;

        protected override void OnThrottledUpdate()
        {
            m_PenaltyRequestBufferLookup.Update(this);

            // A2 FIX 2b: Read winter state from singleton (replaces WinterActiveChangedEvent handler)
            SyncWinterFromSingleton();
            // A2 FIX 2d: Read conscription state from singleton (replaces ConscriptionActivatedEvent handler)
            SyncConscriptionFromSingleton();
            SyncConfigDerivedPenalties();

            // Process pending PenaltyRequests (Data-Driven Commands)
            ProcessPenaltyRequests();

            // Update timed global penalty timers
            UpdateInfraCollapseTimer();
            UpdateMourningTimer();

            // NOTE: Happiness penalties now applied by WellbeingResolverSystem
            // This system only tracks district penalty state
        }

        /// <summary>
        /// Process PenaltyRequest buffer and clear it.
        /// Data-Driven Commands pattern: producers add requests, consumer processes.
        /// </summary>
        private void ProcessPenaltyRequests()
        {
            // TOCTOU FIX: Use TryGetSingletonEntity for atomic check-and-get
            if (!m_PenaltyRequestQuery.TryGetSingletonEntity<PenaltyRequestSingleton>(out var singletonEntity))
                return;
            if (!m_PenaltyRequestBufferLookup.TryGetBuffer(singletonEntity, out var buffer)) return;
            if (buffer.Length == 0)
                return;

            if (buffer.Length > MAX_EXPECTED_PENDING_PENALTY_REQUESTS)
                Log.Warn($"PenaltyRequest buffer high water mark: {buffer.Length} pending requests");

            int processed = 0;
            m_IsProcessingPenaltyRequests = true;
            try
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var request = buffer[i];
                    if (request.IsRemoval)
                    {
                        ApplyRemovePenalty(request.DistrictIndex, request.Source);
                    }
                    else
                    {
                        ApplyRegisterPenalty(request.DistrictIndex, request.Source);
                    }
                    processed++;
                }
            }
            finally
            {
                m_IsProcessingPenaltyRequests = false;
            }

            buffer.Clear();

            if (processed > 0)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Processed {processed} penalty requests");
            }
        }

        // Track last game hour for delta calculation
        private float m_LastInfraCheckHour = -1f;

        private void UpdateInfraCollapseTimer()
        {
            if (!IsInfraCollapsed) return;

            // FIX: Use actual game time instead of real time
            var gameTime = GameTimeSystem.Instance;
            if (gameTime == null) return;

            float currentHour = gameTime.Current.TotalGameHours;

            // Initialize on first call
            if (m_LastInfraCheckHour < 0f)
            {
                m_LastInfraCheckHour = currentHour;
                return;
            }

            float gameHoursPassed = currentHour - m_LastInfraCheckHour;
            if (gameHoursPassed < 0f) gameHoursPassed = 0f;
            // Cap delta to prevent instant timer expiry from serialization edge cases
            gameHoursPassed = math.min(gameHoursPassed, m_InfraCollapseHoursRemaining + 1f);

            m_LastInfraCheckHour = currentHour;
            m_InfraCollapseHoursRemaining -= gameHoursPassed;

            if (m_InfraCollapseHoursRemaining <= 0)
            {
                IsInfraCollapsed = false;
                m_InfraCollapseHoursRemaining = 0;
                m_LastInfraCheckHour = -1f;  // Reset for next collapse
                Log.Info("Infrastructure collapse penalty ended");
            }
        }

        private void UpdateMourningTimer()
        {
            if (!IsMourningActive) return;

            var gameTime = GameTimeSystem.Instance;
            if (gameTime == null) return;

            float currentHour = gameTime.Current.TotalGameHours;

            if (m_LastMourningCheckHour < 0f)
            {
                m_LastMourningCheckHour = currentHour;
                return;
            }

            float gameHoursPassed = currentHour - m_LastMourningCheckHour;
            if (gameHoursPassed < 0f) gameHoursPassed = 0f;
            gameHoursPassed = math.min(gameHoursPassed, m_MourningHoursRemaining + 1f);

            m_LastMourningCheckHour = currentHour;
            m_MourningHoursRemaining -= gameHoursPassed;

            if (m_MourningHoursRemaining <= 0)
            {
                IsMourningActive = false;
                m_MourningHoursRemaining = 0;
                m_MourningHappinessPenalty = 0;
                m_LastMourningCheckHour = -1f;
                Log.Info("Mourning period ended");
            }
        }

        // NOTE: ApplyHappinessPenalties() REMOVED - now handled by WellbeingResolverSystem

        // ============================================================================
        // PUBLIC INSTANCE API
        // ============================================================================

        /// <summary>
        /// Register a penalty for a district.
        /// Thread-safe via ThreadSafeDistrictState.
        /// </summary>
        public void RegisterPenalty(int districtIndex, PenaltySource source)
        {
            DrainPendingPenaltyRequestsBeforeDirectMutation();
            ApplyRegisterPenalty(districtIndex, source);
        }

        /// <summary>
        /// Remove a penalty from a district.
        /// Thread-safe via ThreadSafeDistrictState.
        /// </summary>
        public void RemovePenalty(int districtIndex, PenaltySource source)
        {
            DrainPendingPenaltyRequestsBeforeDirectMutation();
            ApplyRemovePenalty(districtIndex, source);
        }

        /// <summary>
        /// Check if a penalty is active for a district.
        /// Thread-safe via ThreadSafeDistrictState.
        /// </summary>
        public bool HasPenalty(int districtIndex, PenaltySource source)
        {
            if (!EnsureState()) return false;
            return m_StateReader.HasPenalty(districtIndex, source);
        }

        /// <summary>
        /// Get all active penalties for a district.
        /// Thread-safe via ThreadSafeDistrictState.
        /// </summary>
        public DistrictPenalties GetPenalties(int districtIndex)
        {
            if (!EnsureState()) return default;
            return m_StateReader.GetPenalties(districtIndex);
        }

        /// <summary>
        /// Get total happiness penalty for citywide manpower morale. Negative
        /// district bonuses (e.g. food aid) remain available to wellbeing/UI through
        /// the raw per-district penalty snapshot (GetPenalties/GetAllPenalties), but
        /// are excluded here so local aid cannot boost global manpower.
        /// </summary>
        public float GetTotalPositiveHappinessPenalty(int districtIndex)
        {
            float penalty = EnsureState() ? m_StateReader.GetPositiveHappinessPenalty(districtIndex) : 0f;
            penalty += GetPositiveGlobalHappinessPenalty();
            return math.clamp(penalty, 0f, PenaltyConfig.MAX_HAPPINESS_PENALTY);
        }

        /// <summary>
        /// Get summary of all district penalties (for UI/debug).
        /// Returns snapshot - safe to iterate.
        /// </summary>
        public IReadOnlyDictionary<int, DistrictPenalties> GetAllPenalties()
        {
            if (!EnsureState()) return s_EmptyPenalties;
            return m_StateReader.TakeSnapshot().DistrictPenalties ?? s_EmptyPenalties;
        }

        /// <summary>
        /// Clear all penalties (for game reset).
        /// Thread-safe via ThreadSafeDistrictState.
        /// </summary>
        public void ClearAll()
        {
            if (!EnsureState()) return;
            m_StateWriter.ClearPenalties();
            IsWinterActive = false;
            IsInfraCollapsed = false;
            m_InfraCollapseHoursRemaining = 0;
            m_LastInfraCheckHour = -1f; // FIX S23_RAG2:119: Reset to prevent stale delta on next collapse
            IsConscriptionActive = false;
            IsMourningActive = false;
            m_MourningHoursRemaining = 0;
            m_MourningHappinessPenalty = 0;
            m_LastMourningCheckHour = -1f;
            IsPreWarTensionActive = false;
            m_PreWarHappinessPenalty = 0;
            Log.Info("Cleared all penalties");
        }

        private void DrainPendingPenaltyRequestsBeforeDirectMutation()
        {
            if (m_IsProcessingPenaltyRequests)
                return;

            m_PenaltyRequestBufferLookup.Update(this);
            ProcessPenaltyRequests();
        }

        private void ApplyRegisterPenalty(int districtIndex, PenaltySource source)
        {
            if (!EnsureState()) return;
            m_StateWriter.RegisterPenalty(districtIndex, source);
        }

        private void ApplyRemovePenalty(int districtIndex, PenaltySource source)
        {
            if (!EnsureState()) return;
            m_StateWriter.RemovePenalty(districtIndex, source);
        }

        [MemberNotNullWhen(true, nameof(m_StateReader), nameof(m_StateWriter))]
        private bool EnsureState()
        {
            if (m_StateReader != null && m_StateWriter != null)
                return true;

            // Lazy resolve at point-of-use: IDistrictStateReader/Writer are
            // [InfrastructureService] (registered in Mod.OnLoad before any
            // system OnCreate runs), but cross-system OnStartRunning ordering
            // still races — e.g. MobilizationSystem.OnStartRunning ->
            // UpdateBreakdown -> GetAverageHappinessPenalty -> GetAllPenalties
            // -> EnsureState can fire before DistrictPenaltySystem.OnStartRunning
            // ran. Event handlers subscribed in OnCreate (PenaltyRegistered,
            // PenaltyRemoved) have the same hazard. Resolving here closes the
            // window without depending on lifecycle order. TryGet is null-safe
            // across world reload too.
            m_StateReader ??= ServiceRegistry.TryGet<IDistrictStateReader>();
            m_StateWriter ??= ServiceRegistry.TryGet<IDistrictStateWriter>();

            if (m_StateReader != null && m_StateWriter != null)
                return true;

            // Real bug: registry is up but the infrastructure service is
            // missing (Mod.OnLoad incomplete, registration order broken, world
            // disposed). Warn ONCE per session, then skip silently — otherwise
            // this fires on every penalty operation until the world tears down.
            if (!m_LoggedMissingStateServices && ServiceRegistry.IsInitialized && Enabled)
            {
                m_LoggedMissingStateServices = true;
                Log.Warn("District state services unavailable; penalty operation skipped");
            }
            return false;
        }

        private float GetPositiveGlobalHappinessPenalty()
        {
            float total = 0f;
            if (IsWinterActive) total += math.max(0f, WinterHappinessPenalty);
            if (IsInfraCollapsed) total += INFRA_COLLAPSE_HAPPINESS_PENALTY;
            if (IsConscriptionActive) total += CONSCRIPTION_HAPPINESS_PENALTY;
            if (IsMourningActive) total += math.max(0f, m_MourningHappinessPenalty);
            if (IsPreWarTensionActive) total += math.max(0f, m_PreWarHappinessPenalty);
            return math.min(total, PenaltyConfig.MAX_HAPPINESS_PENALTY);
        }

        private static void SyncConfigDerivedPenalties()
        {
            PenaltyConfig.OverridePenalty(PenaltySource.FoodAidProvided, -BalanceConfig.Current.HumanitarianAid.HappinessBonus, 0.00f);
            PenaltyConfig.OverridePenalty(PenaltySource.GretaDeployed, GretaDeployedHappinessPenalty, GretaDeployedCommercePenalty);
        }

        /// <summary>
        /// Set winter active state (global penalty).
        /// </summary>
        public void SetWinterActive(bool active)
        {
            if (IsWinterActive == active)
                return;

            IsWinterActive = active;
            Log.Info($"Winter {(active ? "ACTIVE" : "ended")} - global: {(active ? $"-{WinterHappinessPenalty:P0}" : "0%")}");
        }

        private void OnPenaltyRegistered(PenaltyRegisteredEvent evt)
        {
            ForceNextUpdate();
            RegisterPenalty(evt.DistrictIndex, evt.Source);
        }

        private void OnPenaltyRemoved(PenaltyRemovedEvent evt)
        {
            RemovePenalty(evt.DistrictIndex, evt.Source);
        }

        private void OnInfrastructureCollapse(InfrastructureCollapseEvent evt)
        {
            IsInfraCollapsed = true;
            m_InfraCollapseHoursRemaining = INFRA_COLLAPSE_DURATION_HOURS;
            var gt = GameTimeSystem.Instance;
            m_LastInfraCheckHour = gt != null ? gt.Current.TotalGameHours : -1f;
            Log.Info($"Infrastructure collapse! {evt.RefugeeCount} refugees ({evt.PopulationRatio:F1}x original pop). Penalty -{INFRA_COLLAPSE_HAPPINESS_PENALTY:P0} for {INFRA_COLLAPSE_DURATION_HOURS}h");
        }

        /// <summary>
        /// A2 FIX 2b: Sync winter state from WinterStateSingleton.
        /// Replaces WinterActiveChangedEvent handler.
        /// </summary>
        private void SyncWinterFromSingleton()
        {
            if (!SystemAPI.TryGetSingleton<WinterStateSingleton>(out var winter))
                return;

            SetWinterActive(winter.IsWinterActive);
        }

        /// <summary>
        /// A2 FIX 2d: Sync conscription state from MobilizationStateSingleton.
        /// Replaces ConscriptionActivatedEvent/ConscriptionDeactivatedEvent handlers.
        /// Single source of truth — no desync possible.
        /// </summary>
        private void SyncConscriptionFromSingleton()
        {
            if (!SystemAPI.TryGetSingleton<MobilizationStateSingleton>(out var mobState))
                return;

            bool newState = mobState.IsConscriptionActive;
            if (newState == IsConscriptionActive)
                return;

            IsConscriptionActive = newState;
            if (newState)
                Log.Info($"Conscription ACTIVE (singleton) - global happiness penalty -{CONSCRIPTION_HAPPINESS_PENALTY:P0}");
            else
                Log.Info("Conscription ended (singleton) - happiness penalty removed");
        }

        private void OnWaveEnded(WaveEndedEvent evt)
        {
            if (evt.Casualties <= 0) return;

            bool isMajor = evt.Casualties > MOURNING_CASUALTY_THRESHOLD;
            float newPenalty = isMajor ? MOURNING_MAJOR_PENALTY : MOURNING_MINOR_PENALTY;
            float newDuration = isMajor ? MOURNING_MAJOR_DURATION_HOURS : MOURNING_MINOR_DURATION_HOURS;

            m_MourningHappinessPenalty = IsMourningActive
                ? math.max(m_MourningHappinessPenalty, newPenalty)
                : newPenalty;
            m_MourningHoursRemaining = IsMourningActive
                ? math.max(m_MourningHoursRemaining, newDuration)
                : newDuration;
            var gt = GameTimeSystem.Instance;
            m_LastMourningCheckHour = gt != null ? gt.Current.TotalGameHours : -1f;
            IsMourningActive = true;

            Log.Info($"Mourning started: {evt.Casualties} casualties (wave #{evt.WaveNumber}) " +
                $"→ -{m_MourningHappinessPenalty:P0} for {m_MourningHoursRemaining}h");
        }

        // S18b-2 FIX: Pre-war tension handler (was dead write in OminousSignsSystem)
        private void OnPreWarTension(PreWarTensionEvent evt)
        {
            if (evt.Effect == PreWarEffect.WarStarted)
            {
                // War started — clear pre-war tension penalty
                if (!IsPreWarTensionActive) return;
                IsPreWarTensionActive = false;
                m_PreWarHappinessPenalty = 0f;
                Log.Info("Pre-war tension ended (war started)");
                return;
            }

            if (evt.Effect == PreWarEffect.HappinessPenalty)
            {
                IsPreWarTensionActive = true;
                m_PreWarHappinessPenalty = evt.Value;
                Log.Info($"Pre-war tension: happiness penalty -{evt.Value:P0}");
            }
        }

    }
}
