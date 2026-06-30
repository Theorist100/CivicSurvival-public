using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Agents;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.Refugees;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Features.Population;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Interfaces.Domain.Population;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.Attention.Data;
using CivicSurvival.Core.Components.Lifecycle;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Domains.Attention.Systems
{
    /// <summary>
    /// Manages population exodus based on World Shock level × Cognitive Integrity.
    /// Publishes state via ExodusStateSingleton.
    ///
    /// Mechanism: Adds MovingAway component to random households.
    /// Vanilla HouseholdMoveAwaySystem handles actual emigration, statistics, triggers.
    ///
    /// Logic:
    /// - Headlines tier (30-60%): 0.5% population leaves per day
    /// - GlobalShock tier (60%+): 2-4% population leaves per day
    /// - Even with Patriot, if shock stays high, city empties
    ///
    /// Integrity Multiplier ("Hero City Coefficient"):
    /// - >= 80% (Loyal):      0.5× — "Kyiv 2022", people stay despite attacks
    /// - 50-80% (Anxious):    1.0× — Normal evacuation behavior
    /// - 30-50% (Rebellious): 1.5× — Faster packing, more fear
    /// - 10-30% (Brainwashed):2.0× — Panic spreading
    /// - &lt; 10% (Zombie):       3.0× — Stampede at train station
    ///
    /// The dark truth: Best defense can't save an empty city.
    ///
    /// Uses DayChangedEvent from TimeSystem for reliable daily processing
    /// regardless of game speed.
    ///
    /// S14b-8 ACCEPTED: DayChanged handler bypasses Enabled — harmless because handler checks
    /// shock level (0 during peace) and calculates exodus rate; no exodus when no crisis.
    ///
    /// Constants from: BalanceConstants.cs -> Balance.Attention
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(ExodusStateSingleton))]
    [SingletonOwner(typeof(ExodusRateRegistry))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnStartRunning | SingletonLifecyclePhase.ReconcileAfterLoad,
        DisposePhase = SingletonLifecyclePhase.OnDestroy)]
    public partial class ExodusSystem : CivicSystemBase, IResettable, IPostLoadValidation
#if DEBUG
        , IExodusDebugMutator
#endif
    {
        private static readonly LogContext Log = new("ExodusSystem");
        private WorldShockSystem m_WorldShockSystem = null!;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        [System.NonSerialized] private CivicDependencyWire m_DependencyWire = null!;
        [System.NonSerialized] private IResidentHouseholdView m_ResidentHouseholdView = null!;
        [System.NonSerialized] private IResidentPopulationReader m_ResidentPopulationReader = null!;

        // Queries (cached in OnCreate)
        private EntityQuery m_CognitiveStateQuery;
        private EntityQuery m_ScenarioQuery;
        private EntityQuery m_CurrentActQuery;
        private BufferLookup<CognitiveIntegrityBuffer> m_IntegrityBufferLookup;

        // Random for household selection
        private Unity.Mathematics.Random m_Random;

        // Stats
        private int m_TotalExodusThisSession;
        private float m_BaseExodusRatePercentPerDay;
        private float m_EffectiveExodusRatePercentPerDay;

        // Die-Hards damper: peak population for exodus slowdown ratio
        // "Everyone who could flee already fled — the rest are staying"
        private int m_PeakPopulation;

        // A2 FIX 2a: Removed m_RateOverride — read ScenarioSingleton.ExodusRateOverrideFraction directly

        // FIX S5-03: Track act to skip exodus on transition day (prevents compound spike)
        private Act m_LastProcessedAct;

        // Dedup guard for DayChangedEvent (prevents double-processing same day)
        private DayChangedDedup m_DayDedup = default;

        // Pending-day ownership lives in the resident-population model (its
        // m_PendingDayChanges count, published as ResidentHouseholdSnapshot.PendingDayChanges
        // and acked via AckPendingDays). Exodus is a consumer: it derives both the trigger
        // ("there is pending work") and the day window from the published count plus its own
        // dedup cursor, so a readiness gap cannot desync a second Exodus-side counter from
        // the model's. There is intentionally no m_PendingDayChanged/m_PendingDayNumber here.
        [System.NonSerialized] private int m_ResidentHouseholdObserverVersion;
        [System.NonSerialized] private int m_ResidentPopulationObserverVersion;

        // ECS singleton — liveness-validated handle (Inv 2; CIVIC427)
        [System.NonSerialized] private CivicSingletonHandle<ExodusStateSingleton> m_Singleton;
        private ComponentLookup<ExodusStateSingleton> m_SingletonLookup;

        // Sheltered households (refugees at park/border, vanilla homeless) must not be
        // exodus targets: the population model's eligible set includes them, and a
        // MovingAway mark evicts them from the shelter's Renter buffer via vanilla
        // HouseholdMoveAwaySystem — draining the refugee pipeline as fast as it fills.
        private ComponentLookup<HomelessHousehold> m_HomelessLookup;

        /// <summary>Total exodus this session.</summary>
        public int TotalExodus => m_TotalExodusThisSession;

        /// <summary>Current exodus rate.</summary>
        public float CurrentRatePercentPerDay => m_EffectiveExodusRatePercentPerDay;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_DependencyWire = new CivicDependencyWire(nameof(ExodusSystem));

            m_Random = new Unity.Mathematics.Random((uint)World.GetHashCode() | 1u); // seed must be non-zero

            // Query for cognitive integrity (for exodus multiplier)
            m_CognitiveStateQuery = GetEntityQuery(
                ComponentType.ReadOnly<CognitiveState>()
            );
            m_ScenarioQuery = GetEntityQuery(ComponentType.ReadOnly<ScenarioSingleton>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_IntegrityBufferLookup = GetBufferLookup<CognitiveIntegrityBuffer>(true);

            // Create ECS singletons
            m_Singleton = CreateSingletonHandle<ExodusStateSingleton>();
            EnsureSingleton(ref m_Singleton, ExodusStateSingleton.Default);
            m_SingletonLookup = GetComponentLookup<ExodusStateSingleton>(false);
            m_HomelessLookup = GetComponentLookup<HomelessHousehold>(true);

            ExodusRateRegistry.EnsureExists(EntityManager);
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Register<IExodusDebugMutator>(this);
#endif

            // A2 FIX 2a: ExodusRateOverrideEvent removed — read ScenarioSingleton.ExodusRateOverrideFraction.
            // Day-change pending state is owned by the resident-population model; Exodus reads
            // its published ResidentHouseholdSnapshot.PendingDayChanges instead of subscribing
            // to DayChangedEvent itself (single pending-day owner).

            Log.Info("Created — population exodus tracking active (MovingAway mechanism)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            EnsureSingleton(ref m_Singleton, ExodusStateSingleton.Default);
            ExodusRateRegistry.EnsureExists(EntityManager);
            m_WorldShockSystem ??= m_DependencyWire.RequireWired(() => FeatureRegistry.Instance.Require<WorldShockSystem>());
            m_ResidentHouseholdView ??= ServiceRegistry.Instance.Require<IResidentHouseholdView>();
            m_ResidentPopulationReader ??= ServiceRegistry.Instance.Require<IResidentPopulationReader>();
        }

        protected override void OnDestroy()
        {
#if DEBUG
            if (ServiceRegistry.IsInitialized)
                ServiceRegistry.Instance.Unregister<IExodusDebugMutator>(this);
#endif

            // Clean up ECS singleton
            if (m_Singleton.Entity != Entity.Null && EntityManager.Exists(m_Singleton.Entity))
            {
                EntityManager.DestroyEntity(m_Singleton.Entity);
            }

            base.OnDestroy();
        }

        private void UpdateSingleton()
        {
            var state = new ExodusStateSingleton
            {
                BaseRatePercentPerDay = m_BaseExodusRatePercentPerDay,
                EffectiveRatePercentPerDay = m_EffectiveExodusRatePercentPerDay,
                ExodusRatePercentPerDay = m_EffectiveExodusRatePercentPerDay,
                TotalExodus = m_TotalExodusThisSession,
                IsExodusActive = m_EffectiveExodusRatePercentPerDay > 0f
            };

            if (TryGetExodusSingletonEntity(out var singleton))
#pragma warning disable CIVIC035 // Write access — TryGetExodusSingletonEntity verifies HasComponent above
                m_SingletonLookup[singleton] = state;
#pragma warning restore CIVIC035
        }

        public void ValidateAfterLoad()
        {
            EnsureSingleton(ref m_Singleton, ExodusStateSingleton.Default);
            ExodusRateRegistry.EnsureExists(EntityManager);
            UpdateSingleton();
        }

        private bool TryGetExodusSingletonEntity(out Entity entity)
        {
            entity = m_Singleton.Entity;
            if (entity != Entity.Null && m_SingletonLookup.HasComponent(entity))
            {
                return true;
            }

            return m_Singleton.Query.TryGetSingletonEntity<ExodusStateSingleton>(out entity);
        }

        protected override void OnUpdateImpl()
        {
            // Single pending-day owner: the resident-population model publishes the
            // unacknowledged day count as ResidentHouseholdSnapshot.PendingDayChanges.
            // Exodus drives off that count (the trigger and the catch-up window both come
            // from the model) instead of a separate Exodus-side flag/day-number that could
            // desync across a readiness gap. Processing stays here in OnUpdateImpl, which
            // runs after WorldShockSystem (RegisterAfter), so shock state is current-frame.
            m_ResidentPopulationReader ??= ServiceRegistry.Instance.Require<IResidentPopulationReader>();

            var initialObserved = m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion);
            var initialSnapshot = initialObserved.Value;
            int pendingDays = initialSnapshot.PendingDayChanges;
            if (pendingDays <= 0)
                return;

            m_SingletonLookup.Update(this);
            m_IntegrityBufferLookup.Update(this);

            // Catch-up window anchors on the dedup cursor (last successfully processed day),
            // so the days are [LastProcessedDay+1 .. LastProcessedDay+pendingDays]. Each
            // processed day advances the cursor by one and acks one pending day in the model,
            // keeping cursor and model count in lockstep.
            int firstDay = m_DayDedup.LastProcessedDay + 1;
            int catchupCitizensAlreadyMarked = 0;
            var alreadyMarked = new NativeParallelHashSet<Entity>(
                math.max(1, initialSnapshot.EligibleHouseholds.Length),
                Allocator.Temp);
            try
            {
                for (int i = 0; i < pendingDays; i++)
                {
                    int currentDay = firstDay + i;
                    if (m_DayDedup.IsProcessed(currentDay))
                    {
                        // Already processed but still counted by the model: drain the stale
                        // pending day so the model count converges to the cursor.
                        LogPopReady(currentDay, "ack-stale");
                        if (m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion).Value.PendingDayChanges > 0)
                            m_ResidentHouseholdView.AckPendingDays(1);
                        continue;
                    }

                    var observed = m_ResidentHouseholdView.Observe(ref m_ResidentHouseholdObserverVersion);
                    var populationObserved = m_ResidentPopulationReader.Observe(ref m_ResidentPopulationObserverVersion);
                    var populationSnapshot = WithCatchupPopulation(populationObserved.Value, catchupCitizensAlreadyMarked);

                    if (!ProcessDailyExodus(currentDay, observed.Value, populationSnapshot, alreadyMarked, out int citizensMarked))
                    {
                        // Gate route (e.g. selection not ready or WorldShock missing): stop
                        // before marking/acking. The model keeps the pending count, so the
                        // next frame retries this day — no day is consumed and none is lost.
                        LogPopReady(currentDay, "gate-skip");
                        break;
                    }

                    m_DayDedup.MarkProcessed(currentDay);
                    catchupCitizensAlreadyMarked += citizensMarked;
                    LogPopReady(currentDay, "mark");

                    if (observed.Value.PendingDayChanges > 0)
                    {
                        m_ResidentHouseholdView.AckPendingDays(1);
                        LogPopReady(currentDay, "ack");
                    }
                }
            }
            finally
            {
                if (alreadyMarked.IsCreated) alreadyMarked.Dispose();
            }
        }

        // [POP-READY] pending-day ledger (Verification table). Gated by the same diagnostic
        // log switch as the rest of the contour so it is provable by Grep without metadata.
        // Reads through a throwaway version cursor so logging never mutates the real observer
        // version used by the catch-up loop.
        private void LogPopReady(int day, string action)
        {
            if (!Log.IsDebugEnabled)
                return;

            int pending = -1;
            if (m_ResidentHouseholdView != null)
            {
                int logCursor = 0;
                pending = m_ResidentHouseholdView.Observe(ref logCursor).Value.PendingDayChanges;
            }
            ResidentPopulationReadiness readiness = m_ResidentPopulationReader?.Readiness ?? ResidentPopulationReadiness.NotReady;
            Log.Info($"[POP-READY] Exodus day={day} readiness={readiness} pending={pending} action={action}");
        }

        /// <summary>
        /// Process daily exodus using current-frame shock state.
        /// Called from OnUpdateImpl (after WorldShockSystem has updated).
        /// </summary>
        private static ResidentPopulationSnapshot WithCatchupPopulation(ResidentPopulationSnapshot snapshot, int citizensAlreadyMarked)
        {
            if (citizensAlreadyMarked <= 0)
                return snapshot;

            return new ResidentPopulationSnapshot(
                snapshot.Version,
                snapshot.EligibleHouseholdCount,
                math.max(0, snapshot.AliveResidentCitizens - citizensAlreadyMarked),
                snapshot.HomelessHouseholdCount,
                snapshot.MovedInHouseholdCount);
        }

        private bool ProcessDailyExodus(
            int dayNumber,
            ResidentHouseholdSnapshot householdSnapshot,
            ResidentPopulationSnapshot populationSnapshot,
            NativeParallelHashSet<Entity> alreadyMarked,
            out int citizensMarked)
        {
            citizensMarked = 0;
            using var _ = PerformanceProfiler.Measure("Exodus.ProcessDailyExodus");
#pragma warning disable CIVIC256 // Resolved in OnCreate — null only if WorldShockSystem missing
            if (m_WorldShockSystem == null) return false;
#pragma warning restore CIVIC256

            // Readiness gate (A7). Exodus needs both the population scalar and the
            // household selection to decide and apply an exodus, so it gates on the
            // strongest level (SelectionReady, which implies ScalarReady). The gate is
            // placed here — before MarkProcessed / AckPendingDays / cursor advance in the
            // caller — and returns false via the same break route as the WorldShock check.
            // The model keeps the pending count, so the day is retried next frame: not
            // consumed, not lost. An early return at the top of OnUpdateImpl is forbidden
            // (it is the desync-prone path).
            if (!m_ResidentHouseholdView.IsSelectionReady)
                return false;

            int population = populationSnapshot.AliveResidentCitizens;
            if (population > m_PeakPopulation)
                m_PeakPopulation = population;
            float citySizeMultiplier = GetCitySizeMultiplier(population);
            float integrityMultiplier = GetIntegrityMultiplier();

            // A2 FIX 2a: Read exodus rate override from singleton (replaces EventBus m_RateOverride)
            float exodusRateOverrideFraction = (m_ScenarioQuery.TryGetSingleton<ScenarioSingleton>(out var sc)
                    ? sc : ScenarioSingleton.Default)
                .ExodusRateOverrideFraction;
#pragma warning disable CIVIC070 // ScenarioSingleton changes at act transitions only; 1-frame lag invisible
            var currentAct = (m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var ca)
                    ? ca : CurrentActSingleton.Default)
                .CurrentAct;
#pragma warning restore CIVIC070

            // FIX S5-03: Skip exodus on act transition day when leaving Crisis.
            // Prevents compound spike: act transition clears override to 0, DayChanged fires
            // on same frame → shock-based fallback at 100% shock → 18%/day phantom spike.
            // Next day: shock decays naturally → no spike.
            if (currentAct != m_LastProcessedAct)
            {
                Act previousAct = m_LastProcessedAct;
                m_LastProcessedAct = currentAct;

                if (previousAct == Act.Crisis)
                {
                    Log.Info($"Act transition from Crisis to {currentAct} — skipping exodus for transition day");
                    m_BaseExodusRatePercentPerDay = 0f;
                    m_EffectiveExodusRatePercentPerDay = 0f;
                    UpdateSingleton();
                    return true;
                }
            }

            // Write modifiers to registry — single source of truth for all exodus rate factors
            // Registry guaranteed by EnsureExists in OnCreate + OnStartRunning + ValidateAfterLoad.
#pragma warning disable CIVIC070, CIVIC055 // Registry guaranteed by EnsureExists in lifecycle restore paths
            ref var reg = ref SystemAPI.GetSingletonRW<ExodusRateRegistry>().ValueRW;
#pragma warning restore CIVIC070, CIVIC055
            reg.Rate.Set(ExodusRateRegistry.Source.CitySize, citySizeMultiplier);
            reg.Rate.Set(ExodusRateRegistry.Source.Integrity, integrityMultiplier);

            if (exodusRateOverrideFraction > 0f)
            {
                reg.Rate.SetOverride(exodusRateOverrideFraction * 100f);
            }
            else
            {
                reg.Rate.ClearOverride();
            }

            var shockState = m_WorldShockSystem.GetCurrentState();
            float baseRatePercentPerDay = CalculateExodusRatePercentPerDay(shockState);
            m_BaseExodusRatePercentPerDay = reg.Rate.Resolve(baseRatePercentPerDay);

            // Only exodus if rate > 0
            if (m_BaseExodusRatePercentPerDay <= 0f)
            {
                m_EffectiveExodusRatePercentPerDay = 0f;
                UpdateSingleton();
                return true;
            }

            if (Log.IsDebugEnabled)
                Log.Debug($"Day {dayNumber}: pop={population}, cityMult={citySizeMultiplier:F1}x, integrityMult={integrityMultiplier:F1}x, exodus={m_BaseExodusRatePercentPerDay:F1}%/day");
            m_EffectiveExodusRatePercentPerDay = ProcessExodus(m_BaseExodusRatePercentPerDay, householdSnapshot, populationSnapshot, alreadyMarked, out citizensMarked);

            // Update singleton after daily processing
            UpdateSingleton();
            return true;
        }

        /// <summary>
        /// Cognitive integrity exodus multiplier ("Hero City Coefficient").
        ///
        /// Logic: Mental resilience determines exodus speed during crises.
        /// - High integrity (80%+): "Kyiv 2022" - people STAY despite attacks
        /// - Low integrity (&lt;10%): Panic, stampede at train station
        ///
        /// This multiplier only affects exodus rate when there IS a crisis (WorldShock).
        /// Low integrity alone doesn't cause exodus - but amplifies it when attacked.
        ///
        /// Aligned with CrisisEconomicsSystem thresholds: 80/50/30/10%
        /// </summary>
        private float GetIntegrityMultiplier()
        {
            if (!m_CognitiveStateQuery.TryGetSingletonEntity<CognitiveState>(out var stateEntity))
                return 1.0f; // No cognitive data = normal rate

            // Calculate average integrity from district buffer (Cognitive data read stays
            // in this domain). The threshold → multiplier RULE lives in Core so it is not a
            // cross-domain decision baked into Attention (Axiom 5).
            float integrity = CalculateAvgIntegrity(stateEntity);
            return StabilityMath.IntegrityExodusMultiplier(integrity);
        }

        /// <summary>
        /// Calculate average integrity from CognitiveIntegrityBuffer.
        /// </summary>
        private float CalculateAvgIntegrity(Entity stateEntity)
        {
            if (!m_IntegrityBufferLookup.HasBuffer(stateEntity))
                return 1.0f; // No buffer = assume full integrity

            var buffer = m_IntegrityBufferLookup[stateEntity];
            if (buffer.Length == 0)
                return 1.0f;

            float total = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                total += buffer[i].Integrity;
            }

            return total / buffer.Length;
        }

        /// <summary>
        /// City size exodus multiplier ("Autonomy Coefficient").
        ///
        /// Logic: Infrastructure dependency determines exodus speed.
        /// - Villages are autonomous (wells, wood stoves, cellars) - people STAY
        /// - Cities are "concrete traps" (no power = no water pumps, no heating, no elevators) - people FLEE
        ///
        /// This inverts the intuitive "big city has more inertia" logic because
        /// during infrastructure warfare, high-rises become death traps without electricity.
        /// </summary>
        private float GetCitySizeMultiplier(int population)
        {
            // Population bucketing + per-tier exodus multiplier (with negative-clamp) live in
            // Core so the Refugees spawn-rate path keys off the same tier thresholds.
            return StabilityMath.CitySizeExodusMultiplier(population);
        }

        /// <summary>
        /// Calculate exodus rate based on shock level.
        /// </summary>
        private float CalculateExodusRatePercentPerDay(WorldShockState shockState)
        {
            var attn = BalanceConfig.Current.Attention;
            switch (shockState.CurrentTier)
            {
                case AidTier.DeepConcern:
                    // Below Headlines - minimal exodus
                    if (shockState.ShockLevel >= attn.TierDeepConcern)
                    {
                        return attn.ExodusDeepConcern;
                    }
                    return 0f;

                case AidTier.Headlines:
                    // 30-60% shock = 0.5%/day exodus
                    return attn.ExodusHeadlines;

                case AidTier.GlobalShock:
                    // 60-100% shock = 2-4%/day exodus (scales with shock)
                    // FIX BUG-ATT-001: Guard against division by zero if TierGlobalShock == 100
                    float divisor = 100f - attn.TierGlobalShock;
                    float shockScale = divisor > 0f
                        ? (shockState.ShockLevel - attn.TierGlobalShock) / divisor
                        : 1f;
                    return math.lerp(
                        attn.ExodusGlobalMin,
                        attn.ExodusGlobalMax,
                        math.saturate(shockScale)
                    );

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Process daily exodus.
        /// </summary>
        private float ProcessExodus(
            float ratePercent,
            ResidentHouseholdSnapshot householdSnapshot,
            ResidentPopulationSnapshot populationSnapshot,
            NativeParallelHashSet<Entity> alreadyMarked,
            out int actualLeaving)
        {
            actualLeaving = 0;
            int totalPopulation = populationSnapshot.AliveResidentCitizens;
            if (totalPopulation == 0) return 0f;

            // Die-Hards damper: "Everyone who could flee already fled. The rest are staying."
            // Below 50% of peak → exodus slows asymptotically. Near 10% → nearly zero.
            if (m_PeakPopulation > 0)
            {
                float popRatio = (float)totalPopulation / m_PeakPopulation;
                float damper = math.smoothstep(0.1f, 0.5f, popRatio);
                ratePercent *= damper;
            }

            // Calculate number leaving (use double for precision, ceiling to ensure minimum 1 if rate > 0)
            double exactLeaving = totalPopulation * (ratePercent / 100.0);
            int leaving = exactLeaving > 0 ? (int)math.ceil(exactLeaving) : 0;
            if (leaving == 0) return 0f;

            // Apply exodus effects — returns actual citizen count in marked households
            actualLeaving = ApplyExodusEffects(householdSnapshot, populationSnapshot.AliveResidentCitizens, leaving, alreadyMarked);
            if (actualLeaving == 0) return 0f;

            int prevTotal = m_TotalExodusThisSession;
            m_TotalExodusThisSession += actualLeaving;

            Log.Warn($"EXODUS: {actualLeaving} citizens leaving ({ratePercent:F1}%/day, target {leaving}). " +
                $"Population: {totalPopulation} -> {totalPopulation - actualLeaving}. " +
                $"Total exodus this session: {m_TotalExodusThisSession}");

            // Social feed notification
            PostExodusNotification(actualLeaving, ratePercent, prevTotal);

            // CIVIC243 FIX: Notify ScenarioStatisticsSystem for CitizensLeft counter
            EventBus?.SafePublish(new CitizensLeftEvent(actualLeaving), "ExodusSystem");
            return ratePercent;
        }

        /// <summary>
        /// Apply exodus effects by marking households with MovingAway component.
        /// Uses vanilla CS2 mechanism — HouseholdMoveAwaySystem will process them.
        ///
        /// NOTE: We target HOUSEHOLDS, not individual citizens.
        /// Each household has ~2-4 citizens on average.
        /// </summary>
        private int ApplyExodusEffects(
            ResidentHouseholdSnapshot snapshot,
            int aliveResidentCitizens,
            int targetCitizenCount,
            NativeParallelHashSet<Entity> alreadyMarked)
        {
            int householdCount = snapshot.EligibleHouseholds.Length;
            if (householdCount == 0)
            {
                Log.Debug("No eligible households for exodus");
                return 0;
            }

            // Estimate households needed from eligible resident households, not all citizens.
            int avgCitizensPerHousehold = math.max(1, aliveResidentCitizens / math.max(1, householdCount));
            int targetHouseholds = math.max(1, targetCitizenCount / avgCitizensPerHousehold);
            targetHouseholds = math.min(targetHouseholds, householdCount);

            var indices = new NativeArray<int>(householdCount, Allocator.Temp);
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;

            // Fisher-Yates shuffle first N elements for random selection
            for (int i = 0; i < targetHouseholds && i < indices.Length - 1; i++)
            {
                int j = m_Random.NextInt(i, indices.Length);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            // Create ECB and mark households for exodus
            EntityCommandBuffer ecb = default;
            bool hasEcb = false;
            int actualCitizens = 0;
            m_HomelessLookup.Update(this);

            int markedHouseholds = 0;
            for (int i = 0; i < indices.Length && markedHouseholds < targetHouseholds; i++)
            {
                int snapshotIndex = indices[i];
                Entity household = snapshot.EligibleHouseholds[snapshotIndex];

                // Sheltered (homeless/refugee) households are not exodus candidates —
                // marking them MovingAway would evict them from the park shelter.
                if (m_HomelessLookup.HasComponent(household))
                    continue;

                if (!alreadyMarked.Add(household))
                    continue;

                if (!hasEcb) { ecb = m_GameSimulationEndBarrier.CreateCommandBuffer(); hasEcb = true; }
                markedHouseholds++;
                actualCitizens += snapshot.LiveCitizensPerHousehold[snapshotIndex];

                // Mark for exodus via vanilla mechanism
                ecb.AddComponent(household, new MovingAway
                {
                    m_Reason = MoveAwayReason.NotHappy
                });
            }

            if (indices.IsCreated) indices.Dispose();

            if (Log.IsDebugEnabled) Log.Debug($"Marked {markedHouseholds} households (~{actualCitizens} citizens) for exodus via MovingAway");

            return actualCitizens;
        }

        /// <summary>
        /// Post social feed notification about exodus.
        /// </summary>
        private void PostExodusNotification(int leaving, float rate, int prevTotal)
        {
            if (EventBus == null)
            {
                Log.Warn("EventBus not ready - exodus notification suppressed");
                return;
            }

            var shockState = m_WorldShockSystem.GetCurrentState();

#pragma warning disable CIVIC102 // Only GlobalShock/Concern trigger exodus narratives
            if (shockState.CurrentTier == AidTier.GlobalShock)
#pragma warning restore CIVIC102
            {
                EventBus.SafePublish(new NarrativeTriggerEvent(
                    NarrativeTrigger.ExodusMass.ToKey(),
#pragma warning disable CIVIC050 // returned/stored in event, not per-frame
                    new Dictionary<string, string>
#pragma warning restore CIVIC050
                    {
                        ["arg0"] = leaving.ToString(),
                        ["arg1"] = $"{rate:F1}"
                    }
                ));
            }
            else if (shockState.CurrentTier == AidTier.Headlines)
            {
                EventBus.SafePublish(new NarrativeTriggerEvent(
                    NarrativeTrigger.ExodusModerate.ToKey(),
#pragma warning disable CIVIC050 // returned/stored in event, not per-frame
                    new Dictionary<string, string>
#pragma warning restore CIVIC050
                    {
                        ["arg0"] = leaving.ToString(),
                        ["arg1"] = $"{rate:F1}"
                    }
                ));
            }

            // Occasional IT brain drain message
            var attnCfg = BalanceConfig.Current.Attention;
            if (attnCfg.ExodusMilestoneInterval > 0 &&
                m_TotalExodusThisSession > attnCfg.ExodusMilestoneMinThreshold &&
                prevTotal / attnCfg.ExodusMilestoneInterval != m_TotalExodusThisSession / attnCfg.ExodusMilestoneInterval)
            {
#pragma warning disable CIVIC242 // Multi-publisher by design — each system publishes distinct NarrativeTrigger keys
                EventBus.SafePublish(new NarrativeTriggerEvent(NarrativeTrigger.ExodusBraindrain.ToKey()));
#pragma warning restore CIVIC242
            }
        }

        /// <summary>
        /// Reset all serializable state to defaults (IResettable implementation).
        /// </summary>
        public void ResetState()
        {
            m_TotalExodusThisSession = 0;
            m_BaseExodusRatePercentPerDay = 0f;
            m_EffectiveExodusRatePercentPerDay = 0f;
            // A2 FIX 2a: m_RateOverride removed — ScenarioSingleton resets on new game
            m_PeakPopulation = 0;
            m_LastProcessedAct = Act.PreWar; // FIX S5-03
            m_DayDedup.Reset();
            m_ResidentHouseholdObserverVersion = 0;
            m_ResidentPopulationObserverVersion = 0;
            m_Random = new Unity.Mathematics.Random((uint)World.GetHashCode() | 1u); // seed must be non-zero
            m_Singleton.Invalidate();
            UpdateSingleton();
        }

#if DEBUG
        public bool DebugIsExodusActive => m_EffectiveExodusRatePercentPerDay > 0f;

        public void DebugSetExodusActive(bool active, string source)
        {
            if (active)
            {
                float rate = math.max(m_EffectiveExodusRatePercentPerDay, math.max(m_BaseExodusRatePercentPerDay, 1f));
                m_BaseExodusRatePercentPerDay = rate;
                m_EffectiveExodusRatePercentPerDay = rate;
            }
            else
            {
                m_BaseExodusRatePercentPerDay = 0f;
                m_EffectiveExodusRatePercentPerDay = 0f;
            }
            UpdateSingleton();
            Log.Info($"[DEBUG] {source}: exodus active = {active}");
        }

        public void DebugResetExodus(string source)
        {
            ResetState();
            Log.Info($"[DEBUG] {source}: exodus reset");
        }
#endif
    }
}
