using System;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Corruption;
using CivicSurvival.Core.Interfaces.Domain.Debug;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.Toast;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Attributes;
using Game.Simulation;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Draft-Exemption corruption scheme: the corrupt military registration office
    /// sells draft deferrals and fake disability exemptions for Shadow Cash at the
    /// cost of the mobilization manpower pool.
    ///
    /// Two layers (state in DraftExemptionSingleton, deduction in ManpowerLogic):
    /// - Deferral rate (0/15/30/50%, CorruptionSchemeRequest like Fuel Siphoning):
    ///   daily Shadow-Cash wage, reversible proportional bite out of the pool,
    ///   stationary corruption contribution while held (CorruptionCalculator).
    /// - Disability toast offers (chance/day, scaled by shadow reputation): one-time
    ///   large payout, PERMANENT head-count removed from the pool, one-time
    ///   corruption exposure jump (CorruptionGainEvent).
    ///
    /// Single-writer pattern: wallet writes go through ShadowIncomeRequest.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(DraftExemptionSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class DraftExemptionSystem : ThrottledSystemBase, IDraftExemptionSettingsWriter, IDraftExemptionDebugMutator, ICivicSingletonOwner<DraftExemptionSingleton>
    {
        private static readonly LogContext Log = new("DraftExemption");

        private const string DAILY_OPERATION_PREFIX = "DraftExemption:";
        private const string OFFER_OPERATION_PREFIX = "DraftExemptionOffer:";
        private const double PREV_GAME_HOURS_INIT_EPSILON = 0.001;
        private const uint OFFER_SEED_FRAME_MULTIPLIER = 747796405u;

        // Flavor names for the buy-out toast — same inline-copy style as procurement vendors.
        private static readonly string[] OfferFamilyNames =
        {
            "Kovalenko", "Bondar", "Melnyk", "Shevchenko", "Tkachenko", "Kravets"
        };

        // ============================================================================
        // ECS STATE
        // ============================================================================

        private EntityQuery m_SingletonQuery;
        private ComponentLookup<DraftExemptionSingleton> m_SingletonLookup;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private SimulationSystem m_SimulationSystem = null!;
        private IShadowWalletService m_WalletService = null!;
        private IShadowReputationService m_ReputationService = null!;
        private ToastService? m_ToastService;
        // Monotonic-tick dedup keyed by GLOBAL HOUR (day*24+hour) — bribes pay hourly
        // slices. The struct tracks "highest handled counter" only, so the day-named
        // type fits an hour counter; the field name carries the unit.
        private DayChangedDedup m_HourDedup = default;
        // Carries sub-dollar slices between hours so a small rate still pays out.
        // double by contract: GameRate.AccumulateWithRemainder takes `ref double` and
        // owns the precision handling — this holds its fractional carry, never a
        // balance (CIVIC167). Not persisted: at most $1 of carry, and a load re-seeds
        // it at zero rather than paying a stale fraction (CIVIC267).
#pragma warning disable CIVIC167 // False positive: not a monetary balance but the fractional carry of GameRate.AccumulateWithRemainder, whose API is `ref double` and which owns the precision handling. Whole dollars leave here as long; drift is exactly what the carry prevents.
        [System.NonSerialized] private double m_IncomeRemainder;
#pragma warning restore CIVIC167

        // Ceiling for the fractional carry: it should never exceed a dollar or two
        // (one failed queue returns at most a single hourly slice). A cap keeps the
        // give-back path provably bounded (CIVIC226) — a runaway carry would be a
        // bug, not a balance.
        private const double MAX_INCOME_CARRY = 1_000_000.0;

        [NonSerialized] private bool m_SingletonMissingWarned;
        [NonSerialized] private DraftExemptionSingleton m_LiveDraftExemption;

        // Toast-offer runtime state. Intentionally NOT persisted: the toast itself is
        // transient, so a save/load mid-offer simply lapses the offer with no orphan
        // durable state (a fresh one rolls later).
        [NonSerialized] private int m_LastOfferToastId = -1;
        [NonSerialized] private long m_OfferCash;
        [NonSerialized] private int m_PendingAcceptToastId = -1;
        [NonSerialized] private bool m_ToastEventsSubscribed;
        [NonSerialized] private double m_PrevGameHours;

        protected override int UpdateInterval => Engine.Timing.UPDATE_INTERVAL_1_SECOND;

        // ============================================================================
        // SINGLETON ACCESS
        // ============================================================================

        private DraftExemptionSingleton ReadSingleton()
        {
            if (m_HasRestoredDraftExemption)
                return m_LiveDraftExemption;

            if (!m_SingletonQuery.TryGetSingleton<DraftExemptionSingleton>(out var singleton))
            {
                if (!m_SingletonMissingWarned)
                {
                    m_SingletonMissingWarned = true;
                    Log.Warn("DraftExemptionSingleton missing — returning Default. Singleton entity may have been destroyed.");
                }
                return m_LiveDraftExemption;
            }
            m_LiveDraftExemption = singleton;
            return singleton;
        }

        public bool TrySetDraftDeferralPercent(int percent)
        {
            DraftExemptionSingleton.EnsureExists(EntityManager);
            if (!m_SingletonQuery.TryGetSingletonEntity<DraftExemptionSingleton>(out var entity))
                return false;

            var current = ReadSingleton();
            m_LiveDraftExemption = new DraftExemptionSingleton
            {
                RatePercent = percent,
                DisabilityHeadcount = current.DisabilityHeadcount
            };
            EntityManager.SetComponentData(entity, m_LiveDraftExemption);
            Log.Info($"[DraftExemption] Deferral rate set to {percent}%");
            return true;
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            DraftExemptionSingleton.EnsureExists(EntityManager);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IDraftExemptionSettingsWriter>(this);
#if DEBUG
                ServiceRegistry.Instance.Register<IDraftExemptionDebugMutator>(this);
#endif
            }

            m_SingletonQuery = GetEntityQuery(ComponentType.ReadWrite<DraftExemptionSingleton>());
            m_SingletonLookup = GetComponentLookup<DraftExemptionSingleton>(false);
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            RefreshLiveSnapshotFromEntities();
            SubscribeRequired<HourChangedEvent>(OnHourChanged);
            SubscribeRequired<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);

            Log.Info($"{nameof(DraftExemptionSystem)} created (single-writer pattern)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            DraftExemptionSingleton.EnsureExists(EntityManager);
            if (!m_HasRestoredDraftExemption)
                RefreshLiveSnapshotFromEntities();
            ResolveFeatureServices();
            ResolveToastService();
        }

        protected override void OnThrottledUpdate()
        {
            ResolveFeatureServices();
            ResolveToastService();

            ProcessPendingAccept();
            TryGenerateOffer();
        }

        protected override void OnDestroy()
        {
            if (m_ToastService != null)
            {
                m_ToastService.OnToastExpired -= OnToastExpired;
                m_ToastService.OnToastInteracted -= OnToastInteracted;
                m_ToastEventsSubscribed = false;
            }

            UnsubscribeSafe<HourChangedEvent>(OnHourChanged);
            UnsubscribeSafe<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IDraftExemptionSettingsWriter>(this);
#if DEBUG
                ServiceRegistry.Instance.Unregister<IDraftExemptionDebugMutator>(this);
#endif
            }

            Log.Info($"{nameof(DraftExemptionSystem)} destroyed");
            base.OnDestroy();
        }

        private void RefreshLiveSnapshotFromEntities()
        {
            if (m_HasRestoredDraftExemption)
                return;

            m_LiveDraftExemption = m_SingletonQuery.TryGetSingleton<DraftExemptionSingleton>(out var singleton)
                ? singleton
                : DraftExemptionSingleton.Default;
        }

        private void ResolveFeatureServices()
        {
            m_WalletService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);
            m_ReputationService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowReputationService.Instance);
        }

        // ============================================================================
        // LAYER 1 — DEFERRAL RATE: DAILY INCOME
        // ============================================================================

        /// <summary>
        /// Pay one hourly slice of the day's exemption bribes. DailyIncome is a fixed
        /// per-day amount, so the slice is a plain 24th and the daily total is
        /// unchanged — cadence only. Sub-dollar slices carry in the remainder.
        /// </summary>
        private void OnHourChanged(HourChangedEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("DraftExemption.OnHourChanged");
            if (!Enabled) return;

            int globalHour = HourTick.GlobalHour(evt);
            if (m_HourDedup.IsProcessed(globalHour)) return;

            var singleton = ReadSingleton();
            if (singleton.RatePercent <= 0)
            {
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            ResolveFeatureServices();

            // Skip income while the wallet system is disabled (PreWar) or frozen —
            // requests created in these states are orphaned/blocked by design.
            if (!m_WalletService.IsOperational)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Income skipped - wallet not operational");
                return;
            }

            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Income skipped - assets frozen");
                return;
            }

            long roundedIncome = GameRate.AccumulateWithRemainder(HourTick.SliceOfDaily(singleton.DailyIncome), 1.0, ref m_IncomeRemainder);
            if (roundedIncome <= 0)
            {
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, roundedIncome, "DraftExemption", $"{DAILY_OPERATION_PREFIX}{globalHour}"))
            {
                // Queue failed (wallet unavailable this frame): mark the hour like the
                // other terminal early-returns — HourChangedEvent never replays. Return
                // the slice to the remainder so it is not lost.
                m_IncomeRemainder = System.Math.Min(m_IncomeRemainder + roundedIncome, MAX_INCOME_CARRY);
                Log.Warn($"Day {evt.DayNumber} hour {evt.Hour}: draft-exemption income queue failed (wallet unavailable), skipping");
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: queued +${roundedIncome:N0} deferral wages to offshore");
        }

        private void OnShadowIncomeApplied(ShadowIncomeAppliedEvent evt)
        {
            // NOTE: the one-time offer payout uses OFFER_OPERATION_PREFIX and must not
            // mark an hour processed — the ":"-terminated prefixes cannot collide.
            if (!evt.OperationKey.StartsWith(DAILY_OPERATION_PREFIX, StringComparison.Ordinal))
                return;
            if (evt.Amount <= 0)
                return;
            // Key carries the global hour (day*24+hour) since the payout went hourly.
            if (int.TryParse(evt.OperationKey.Substring(DAILY_OPERATION_PREFIX.Length), out int globalHour))
                m_HourDedup.MarkProcessed(globalHour);
            if (Log.IsDebugEnabled) Log.Debug($"Hour {globalHour}: applied +${evt.Amount:N0} deferral wages to offshore");
        }

        // ============================================================================
        // LAYER 2 — DISABILITY BUY-OUT: TOAST OFFERS
        // ============================================================================

        private void TryGenerateOffer()
        {
            if (m_ToastService == null)
                return;

            var timeProvider = GameTimeSystem.Instance;
            if (timeProvider == null)
                return;
            double currentGameHours = timeProvider.Current.TotalGameHours;

            // A live offer toast pauses generation; keep the time anchor current so
            // the delta does not spike when the toast clears.
            if (m_LastOfferToastId > 0)
            {
                m_PrevGameHours = currentGameHours;
                return;
            }

            if (m_PrevGameHours < PREV_GAME_HOURS_INIT_EPSILON)
            {
                m_PrevGameHours = currentGameHours;
                return; // Skip first update after load — prevents huge delta spike
            }
            float deltaGameHours = (float)Math.Max(0.0, currentGameHours - m_PrevGameHours);
            m_PrevGameHours = currentGameHours;

            // Frozen-out players get no offers; wallet must be able to receive the payout.
            if (m_ReputationService.IsFrozenOut || !m_WalletService.IsOperational || m_WalletService.IsFrozen)
                return;

            var cfg = BalanceConfig.Current.DraftExemption;
            float hourlyChance = cfg.OfferChancePerDay / GameRate.HOURS_PER_DAY;
            float adjustedChance = math.saturate(hourlyChance * m_ReputationService.GetFrequencyMultiplier() * deltaGameHours);
            if (adjustedChance <= 0f)
                return;

            uint seed = unchecked((m_SimulationSystem.frameIndex + 1u) * OFFER_SEED_FRAME_MULTIPLIER);
            if (seed == 0u) seed = 1u;
            var random = new Unity.Mathematics.Random(seed);
            if (random.NextFloat() > adjustedChance)
                return;

            QueueOfferToast(random.NextUInt());
        }

        private bool QueueOfferToast(uint seed)
        {
            if (m_ToastService == null)
                return false;

            if (seed == 0u) seed = 1u;
            var random = new Unity.Mathematics.Random(seed);
            var cfg = BalanceConfig.Current.DraftExemption;

            int cashMin = Math.Max(0, Math.Min(cfg.OfferCashMin, cfg.OfferCashMax));
            int cashMax = Math.Max(cfg.OfferCashMin, cfg.OfferCashMax);
            long cash = random.NextInt(cashMin, cashMax + 1);
            int heads = Math.Max(1, cfg.OfferDisabilityHeads);
            string familyName = OfferFamilyNames.Length > 0
                ? OfferFamilyNames[random.NextInt(0, OfferFamilyNames.Length)]
                : "Anonymous";

            int queuedToastId = m_ToastService.QueueToastId(
                ToastType.DraftExemptionOffer,
                "Draft Exemption Deal",
                $"The {familyName} family offers ${cash:N0} for signed disability papers — {heads} men written off the draft rolls for good. The money lands offshore.",
                "Take the Deal",
                "Refuse",
                ToastPriority.Normal);

            if (queuedToastId <= 0)
            {
                Log.Debug(" Offer toast rejected (cooldown or queue full)");
                return false;
            }

            m_LastOfferToastId = queuedToastId;
            m_OfferCash = cash;
            if (Log.IsDebugEnabled) Log.Debug($"Generated disability buy-out offer: ${cash:N0} for {heads} heads (toast #{queuedToastId})");
            return true;
        }

        private void ProcessPendingAccept()
        {
            if (m_PendingAcceptToastId < 0)
                return;

            int toastId = m_PendingAcceptToastId;
            long cash = m_OfferCash;
            m_PendingAcceptToastId = -1;
            ClearOfferTracking();

            // The payout must be receivable — otherwise the deal falls through
            // (no heads are taken without the money landing).
            if (!m_WalletService.IsOperational || m_WalletService.IsFrozen)
            {
                Log.Warn($"Toast #{toastId}: disability deal fell through — wallet unavailable or frozen");
                return;
            }

            // Owner lifecycle (OnCreate/OnLoadRestore) guarantees the singleton; a missing
            // entity here is abnormal — drop the deal instead of recreating in an update path.
            if (!m_SingletonQuery.TryGetSingletonEntity<DraftExemptionSingleton>(out var entity))
            {
                Log.Warn($"Toast #{toastId}: disability deal fell through — singleton entity missing");
                return;
            }

            var cfg = BalanceConfig.Current.DraftExemption;
            int heads = Math.Max(1, cfg.OfferDisabilityHeads);

            var current = ReadSingleton();
            m_LiveDraftExemption = new DraftExemptionSingleton
            {
                RatePercent = current.RatePercent,
                DisabilityHeadcount = current.DisabilityHeadcount + heads
            };
            m_SingletonLookup.Update(this);
            m_SingletonLookup[entity] = m_LiveDraftExemption;

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (ShadowEconomyEmitter.TryQueueIncome(World, ecb, cash, "DraftExemptionOffer", $"{OFFER_OPERATION_PREFIX}{toastId}"))
            {
                m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            }
            else
            {
                Log.Warn($"Toast #{toastId}: payout queue failed — heads already committed, income lost");
            }

            EventBus?.SafePublish(new CorruptionGainEvent(cfg.OfferCorruptionJump, "DraftExemptionOffer"), nameof(DraftExemptionSystem));
            Log.Info($"Toast #{toastId}: accepted disability deal — +${cash:N0} offshore, {heads} men written off (total {m_LiveDraftExemption.DisabilityHeadcount})");
        }

        // ============================================================================
        // TOAST EVENT PLUMBING (fires on UIUpdate phase — defer to GameSimulation)
        // ============================================================================

        private void SubscribeToastEvents()
        {
            if (m_ToastEventsSubscribed || m_ToastService == null) return;

            m_ToastService.OnToastExpired -= OnToastExpired;
            m_ToastService.OnToastExpired += OnToastExpired;
            m_ToastService.OnToastInteracted -= OnToastInteracted;
            m_ToastService.OnToastInteracted += OnToastInteracted;
            m_ToastEventsSubscribed = true;
        }

        private void ResolveToastService()
        {
            var currentToast = ServiceRegistry.TryGet<ToastService>();
            if (currentToast == m_ToastService)
            {
                SubscribeToastEvents();
                return;
            }

            if (m_ToastService != null)
            {
                m_ToastService.OnToastExpired -= OnToastExpired;
                m_ToastService.OnToastInteracted -= OnToastInteracted;
                m_ToastEventsSubscribed = false;
            }

            m_ToastService = currentToast;
            SubscribeToastEvents();
        }

        private void OnToastExpired(int toastId)
        {
            if (toastId == m_LastOfferToastId)
                ClearOfferTracking();
        }

        private void OnToastInteracted(int toastId, bool wasAccepted)
        {
            if (toastId != m_LastOfferToastId)
                return;

            if (wasAccepted)
            {
                // Defer the apply to the next GameSimulation tick (UIUpdate-phase callback).
                m_PendingAcceptToastId = toastId;
            }
            else
            {
                ClearOfferTracking();
            }
        }

        private void ClearOfferTracking()
        {
            m_LastOfferToastId = -1;
        }

        // ============================================================================
        // DEBUG API
        // ============================================================================

        /// <summary>
        /// DEBUG: force a disability buy-out offer toast immediately, bypassing the
        /// daily chance roll and the reputation/wallet gates. The accept path still
        /// runs the normal wallet checks.
        /// </summary>
#pragma warning disable CIVIC243 // False positive: called via IDraftExemptionDebugMutator (registered at OnStartRunning, dispatched from ScenarioDebugActionSystem.OnDebugForceDraftOffer) — the analyzer does not credit interface-dispatched calls to the implementing method.
        public void DebugForceOffer(string source)
        {
            ResolveFeatureServices();
            ResolveToastService();

            if (m_LastOfferToastId > 0)
            {
                Log.Info($"[DEBUG] Offer already live (toast #{m_LastOfferToastId}), skipping force ({source})");
                return;
            }

            uint seed = unchecked((m_SimulationSystem.frameIndex + 1u) * OFFER_SEED_FRAME_MULTIPLIER);
            if (QueueOfferToast(seed))
                Log.Info($"[DEBUG] Forced draft-exemption offer ({source})");
            else
                Log.Warn($"[DEBUG] Forced draft-exemption offer failed — toast rejected or service missing ({source})");
        }
#pragma warning restore CIVIC243
    }
}
