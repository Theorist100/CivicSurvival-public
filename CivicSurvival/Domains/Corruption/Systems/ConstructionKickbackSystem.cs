using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Simulation;
using Unity.Entities;
using Unity.Jobs;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Interfaces.Domain.Economy;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Domains.Corruption.Systems
{
    /// <summary>
    /// Construction Kickback: skim a percentage of road/net construction spend
    /// into the shadow wallet via inflated invoices.
    ///
    /// Mechanics:
    /// - Player sets kickback percentage via CorruptionSchemeRequest (0%, 5%, 10%, 20%)
    /// - Runs in ModificationEnd (pause-safe, Axiom 14): vanilla ApplyNetSystem stamps
    ///   Recent{m_ModificationFrame, m_ModificationCost} on built net edges in the
    ///   ApplyTool phase of the same frame; the transient Created/Updated tags are
    ///   cleared only later in the Cleanup phase, so this system sees exactly the
    ///   entities built this frame — including while paused
    /// - Accrued skim lands in ConstructionKickbackSingleton.PendingPayout (persisted)
    /// - Once per day the accrued amount is queued to the shadow wallet; on the wallet
    ///   ack the same amount is deducted from the city budget (inflated invoice) and
    ///   corruption exposure is published
    ///
    /// Known approximation: on re-modification of a net edge still inside the vanilla
    /// refund window, Recent.m_ModificationCost is the merged refundable investment
    /// (old cost + refund credit + this frame's charge), not the pure per-frame charge —
    /// rapid build/upgrade churn skims slightly more than the raw spend. Accepted:
    /// exposure scales with the payout, so the cheese self-punishes.
    ///
    /// Single-writer pattern: Uses ShadowIncomeRequest for wallet writes.
    /// </summary>
    [ActIndependent]
    [SingletonOwner(typeof(ConstructionKickbackSingleton))]
    [OwnedSingletonLifecycle(
        Persisted = true,
        EnsurePhase = SingletonLifecyclePhase.OnCreate | SingletonLifecyclePhase.OnLoadRestore,
        DisposePhase = SingletonLifecyclePhase.None)]
    public partial class ConstructionKickbackSystem : CivicSystemBase, IConstructionKickbackSettingsWriter, ICivicSingletonOwner<ConstructionKickbackSingleton>
    {
        private static readonly LogContext Log = new("ConstructionKickback");

        private const string OPERATION_PREFIX = "ConstructionKickback:";
        private const double EXPOSURE_AMOUNT_UNIT = 100_000.0;

        private EntityQuery m_SingletonQuery;
        private ComponentLookup<ConstructionKickbackSingleton> m_SingletonLookup;
        private EntityQuery m_BuiltNetQuery;
        private SimulationSystem m_SimulationSystem = null!;
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;
        private IShadowWalletService m_WalletService = null!;
        // Monotonic-tick dedup keyed by GLOBAL HOUR (day*24+hour) — the payout gate
        // is checked hourly. The struct tracks "highest handled counter" only, so the
        // day-named type fits an hour counter; the field name carries the unit.
        private DayChangedDedup m_HourDedup = default;
        [System.NonSerialized] private bool m_SingletonMissingWarned;
        [System.NonSerialized] private ConstructionKickbackSingleton m_LiveKickback;

        // Same-frame dedup: while paused the simulation frame does not advance, so a
        // neighbor edge re-tagged Updated later in the same pause window would re-pass
        // the freshness check with its old Recent. Entries only ever belong to the
        // current simulation frame; the set resets as soon as the frame advances.
        [System.NonSerialized] private readonly HashSet<Entity> m_AccruedThisFrame = new();
        [System.NonSerialized] private uint m_AccruedFrame;

        private ConstructionKickbackSingleton ReadSingleton()
        {
            if (m_HasRestoredKickback)
                return m_LiveKickback;

            if (!m_SingletonQuery.TryGetSingleton<ConstructionKickbackSingleton>(out var singleton))
            {
                if (!m_SingletonMissingWarned)
                {
                    m_SingletonMissingWarned = true;
                    Log.Warn("ConstructionKickbackSingleton missing — returning Default. Singleton entity may have been destroyed.");
                }
                return m_LiveKickback;
            }
            m_LiveKickback = singleton;
            return singleton;
        }

        /// <summary>Kickback percentage (0, 5, 10, 20). Read-only - use CorruptionSchemeRequest to change.</summary>
        public int KickbackPercent => ReadSingleton().KickbackPercent;

        /// <summary>Accrued skim awaiting the daily payout.</summary>
        public long PendingPayout => ReadSingleton().PendingPayout;

        public bool TrySetConstructionKickbackPercent(int percent)
        {
            ConstructionKickbackSingleton.EnsureExists(EntityManager);
            if (!m_SingletonQuery.TryGetSingletonEntity<ConstructionKickbackSingleton>(out var entity))
                return false;

            var current = ReadSingleton();
            m_LiveKickback = new ConstructionKickbackSingleton
            {
                KickbackPercent = percent,
                PendingPayout = current.PendingPayout
            };
            EntityManager.SetComponentData(entity, m_LiveKickback);
            Log.Info($"[ConstructionKickback] Set to {percent}%");
            return true;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            ConstructionKickbackSingleton.EnsureExists(EntityManager);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Register<IConstructionKickbackSettingsWriter>(this);
            }

            m_SingletonQuery = GetEntityQuery(
                ComponentType.ReadWrite<ConstructionKickbackSingleton>()
            );
            m_SingletonLookup = GetComponentLookup<ConstructionKickbackSingleton>(false);

            // Net edges whose construction/modification applied this frame: vanilla
            // stamps Recent in ApplyTool and clears Created/Updated in Cleanup, so in
            // ModificationEnd this matches exactly the newly-built (or just-modified)
            // road segments. Temp is excluded so tool previews never accrue skim.
            m_BuiltNetQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Edge>(),
                    ComponentType.ReadOnly<Game.Tools.Recent>()
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<Deleted>()
                }
            });
            RequireForUpdate(m_BuiltNetQuery);

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            RefreshLiveSnapshotFromEntities();
            SubscribeRequired<HourChangedEvent>(OnHourChanged);
            SubscribeRequired<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);

            Log.Info($"{nameof(ConstructionKickbackSystem)} created (single-writer pattern)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            ConstructionKickbackSingleton.EnsureExists(EntityManager);
            if (!m_HasRestoredKickback)
                RefreshLiveSnapshotFromEntities();
        }

        protected override void OnUpdateImpl()
        {
            var singleton = ReadSingleton();
            int percent = singleton.KickbackPercent;
            if (percent <= 0)
                return;

            uint frame = m_SimulationSystem.frameIndex;
            if (frame != m_AccruedFrame)
            {
                m_AccruedThisFrame.Clear();
                m_AccruedFrame = frame;
            }

            long accrued = 0;
            foreach (var (recent, entity) in SystemAPI.Query<RefRO<Game.Tools.Recent>>()
                         .WithAll<Edge>()
                         .WithAny<Created, Updated>()
                         .WithNone<Game.Tools.Temp, Deleted>()
                         .WithEntityAccess())
            {
                // Older Recent on a merely re-tagged edge = no new spend this frame.
                if (recent.ValueRO.m_ModificationFrame != frame)
                    continue;
                int cost = recent.ValueRO.m_ModificationCost;
                if (cost <= 0)
                    continue;
                if (!m_AccruedThisFrame.Add(entity))
                    continue;

                accrued += (long)cost * percent / 100;
            }

            if (accrued <= 0)
                return;

            if (!m_SingletonQuery.TryGetSingletonEntity<ConstructionKickbackSingleton>(out var singletonEntity))
                return;

            m_LiveKickback = new ConstructionKickbackSingleton
            {
                KickbackPercent = percent,
                PendingPayout = singleton.PendingPayout + accrued
            };
            m_SingletonLookup.Update(this);
            m_SingletonLookup[singletonEntity] = m_LiveKickback;
            if (Log.IsDebugEnabled)
                Log.Debug($"[ConstructionKickback] Accrued +${accrued:N0} (pending ${m_LiveKickback.PendingPayout:N0})");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<HourChangedEvent>(OnHourChanged);
            UnsubscribeSafe<ShadowIncomeAppliedEvent>(OnShadowIncomeApplied);
            if (ServiceRegistry.IsInitialized)
            {
                ServiceRegistry.Instance.Unregister<IConstructionKickbackSettingsWriter>(this);
            }

            Log.Info($"{nameof(ConstructionKickbackSystem)} destroyed");
            base.OnDestroy();
        }

        private void RefreshLiveSnapshotFromEntities()
        {
            if (m_HasRestoredKickback)
                return;

            m_LiveKickback = m_SingletonQuery.TryGetSingleton<ConstructionKickbackSingleton>(out var singleton)
                ? singleton
                : ConstructionKickbackSingleton.Default;
        }

        /// <summary>
        /// Pay out accrued kickback once it clears the threshold, checked hourly
        /// rather than at midnight only. Nothing is sliced here: the skim accrues
        /// from construction as it happens and this is the payout gate — checking
        /// it 24x per day only shortens the wait between "skim accrued" and "money
        /// visible", the amounts and the threshold are untouched.
        /// </summary>
        private void OnHourChanged(HourChangedEvent evt)
        {
            using var _ = PerformanceProfiler.Measure("ConstructionKickback.OnHourChanged");
            if (!Enabled) return;

            int globalHour = HourTick.GlobalHour(evt);
            if (m_HourDedup.IsProcessed(globalHour)) return;

            var singleton = ReadSingleton();
            long pending = singleton.PendingPayout;
            long threshold = BalanceConfig.Current.ConstructionKickback.MinPayoutThreshold;
            if (pending <= 0 || pending < threshold)
            {
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            m_WalletService ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullShadowWalletService.Instance);

            if (!m_WalletService.IsOperational)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Payout skipped - wallet not operational");
                return;
            }

            if (m_WalletService.IsFrozen)
            {
                if (Log.IsDebugEnabled) Log.Debug($"Day {evt.DayNumber} hour {evt.Hour}: Payout skipped - assets frozen");
                return;
            }

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, pending, "ConstructionKickback", $"{OPERATION_PREFIX}{globalHour}"))
            {
                // No ack will mark this day; mark here so the payout retries tomorrow
                // with the still-accrued pending instead of silently stalling.
                Log.Warn($"Day {evt.DayNumber} hour {evt.Hour}: construction-kickback payout queue failed (wallet unavailable), deferring to next hour");
                m_HourDedup.MarkProcessed(globalHour);
                return;
            }

            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));
            Log.Info($"Day {evt.DayNumber} hour {evt.Hour}: queued +${pending:N0} construction kickback to offshore");
        }

        /// <summary>Eligibility gate for billing the city the settled kickback amount.</summary>
        private static bool CanSettleInflatedInvoice(long amount) => amount > 0;

        private void OnShadowIncomeApplied(ShadowIncomeAppliedEvent evt)
        {
            if (!evt.OperationKey.StartsWith(OPERATION_PREFIX, System.StringComparison.Ordinal))
                return;
            if (!CanSettleInflatedInvoice(evt.Amount))
                return;
            // Key carries the global hour (day*24+hour) since the gate went hourly.
            if (int.TryParse(evt.OperationKey.Substring(OPERATION_PREFIX.Length), out int globalHour))
                m_HourDedup.MarkProcessed(globalHour);

            // The wallet actually received the skim: settle the inflated invoice — take
            // the paid amount out of pending and bill the city budget for it.
            var singleton = ReadSingleton();
            long remaining = System.Math.Max(0, singleton.PendingPayout - evt.Amount);
            m_LiveKickback = new ConstructionKickbackSingleton
            {
                KickbackPercent = singleton.KickbackPercent,
                PendingPayout = remaining
            };
            if (m_SingletonQuery.TryGetSingletonEntity<ConstructionKickbackSingleton>(out var entity))
                EntityManager.SetComponentData(entity, m_LiveKickback);

            var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
            if (!BudgetEmitter.TryQueueDeduct(
                    World,
                    ecb,
                    evt.Amount,
                    BudgetCategory.Procurement,
                    BudgetPriority.DailyCost,
                    "ConstructionKickback.InflatedInvoice"))
            {
                // The skim already landed offshore; a dropped invoice only means the city
                // budget dodged the bill this time — log it, nothing to roll back.
                Log.Warn($"Hour {globalHour}: inflated-invoice billing (${evt.Amount:N0}) failed to queue");
            }
            m_GameSimulationEndBarrier.AddJobHandleForProducer(default(JobHandle));

            float exposure = (float)(evt.Amount / EXPOSURE_AMOUNT_UNIT) * BalanceConfig.Current.ConstructionKickback.ExposurePer100k;
            if (exposure > 0f)
            {
#pragma warning disable CIVIC062 // One-shot event per applied daily payout, not per-frame.
                EventBus?.SafePublish(
                    new CorruptionGainEvent(exposure, "ConstructionKickback"),
                    nameof(ConstructionKickbackSystem));
#pragma warning restore CIVIC062
            }

            Log.Info($"Hour {globalHour}: applied +${evt.Amount:N0} construction kickback to offshore (pending ${remaining:N0})");
        }
    }
}
