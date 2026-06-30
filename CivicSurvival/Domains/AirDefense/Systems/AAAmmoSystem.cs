using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using CivicSurvival.Domains.AirDefense.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Core.Systems.Scheduling;

namespace CivicSurvival.Domains.AirDefense.Systems
{
    /// <summary>
    /// AA Ammo Management System — handles resupply and stats for custom AA installations.
    ///
    /// Responsibilities:
    /// - Ammo resupply operations (automatic on wave end, emergency, manual)
    /// - Patriot active check (based on AAType.Patriot props)
    /// - AA statistics aggregation
    ///
    /// Works with AirDefenseInstallation component (custom AA props only).
    /// No parasitic AA on Fire/Police/Hospital.
    /// </summary>
    [ActIndependent]
    public partial class AAAmmoSystem : EventDrivenSystemBase, IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        private static readonly LogContext Log = new("AAAmmoSystem");

        // ECB for creating resupply requests (Single Writer pattern)
        private GameSimulationEndBarrier m_Barrier = null!;
        private EntityQuery m_AAQuery;
        private EntityQuery m_PendingResupplyBatchQuery;
        private EntityQuery m_WaveStateQuery;
        private ComponentLookup<AirDefenseInstallation> m_AAInstallationLookup;
        // Per-save AA auto-resupply rule (left-panel toggle), read fail-open: null owner = enabled.
        private IAutoResupplyReader? m_AutoResupplyReader;

        // Debug cheat flag — set from event handler, processed in OnEventDrivenUpdate.
        private bool m_PendingDebugResupply;
        private long m_NextBatchId = 1;

        // Graduated trickle refill state — transient (re-derived from calm phase +
        // live deficit after load, so neither field is serialized).
        // m_RefillAccum: fraction-of-magazine accumulator; one delivery per
        // TRICKLE_DELIVER_FRACTION of refill duration. m_TrickleCycleActive: true while
        // a refill cycle is in progress, so the terminal Full event fires exactly once.
        [System.NonSerialized] private float m_RefillAccum;
        [System.NonSerialized] private bool m_TrickleCycleActive;
        // Latch so the "AA underfunded" alert fires once per low-funds episode (= one calm cycle),
        // not on every ~36s delivery tick. Re-armed when funds recover, all magazines fill, or the
        // calm cycle ends (a new calm after a wave is a new episode). Transient.
        [System.NonSerialized] private bool m_TrickleUnderfundedNotified;

        // 5% of each magazine per delivery → ~20 deliveries to refill a full gun over
        // AmmoRefillDurationSeconds. Small enough to look smooth, large enough that the
        // smallest magazine (Patriot=16) still gets >=1 round per delivery.
        private const float TRICKLE_DELIVER_FRACTION = 0.05f;

        // PERF M2.5-M2.6: Cached lists for CollectAAData methods (avoids allocation per call)
        // NOTE: Entity stored as Index+Version to avoid vanilla orphan detection (homeless spike bug)
        // AAType rides along so the trickle refill can skip Patriot (auto refill is guns-only);
        // the full/partial resupply consumers read only current/max and ignore the type.
        private readonly List<(int entityIndex, int entityVersion, int current, int max, AAType type)> m_AADataWithEntitiesCache = new();

        // Result of the public scenario/debug full-resupply path (StartResupplyWithCost).
        // The normal gameplay refill is the graduated TrickleRefillTick, which does not
        // use this struct — so only the delivered rounds/cost/fullness are consumed.
        private readonly struct ResupplyStartResult
        {
            public readonly int Rounds;
            public readonly long Cost;
            public readonly bool IsFullResupply;

            public ResupplyStartResult(int rounds, long cost, bool isFullResupply)
            {
                Rounds = rounds;
                Cost = cost;
                IsFullResupply = isFullResupply;
            }
        }

        // ============================================================================
        // LIFECYCLE
        // ============================================================================

        protected override void OnCreate()
        {
            base.OnCreate();

            // ECB for creating resupply requests
            m_Barrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();
            // S002b: only count AA that can actually receive ammo — Simulate=alive, no Deleted/Destroyed.
            m_AAQuery = GetEntityQuery(
                ComponentType.ReadOnly<AirDefenseInstallation>(),
                ComponentType.ReadOnly<Simulate>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Destroyed>()
            );
            m_PendingResupplyBatchQuery = GetEntityQuery(ComponentType.ReadOnly<AAResupplyBatchIntent>());
            // Read-only calm-phase gate for the graduated refill (Core singleton — not Domain→Domain).
            m_WaveStateQuery = GetEntityQuery(ComponentType.ReadOnly<WaveStateSingleton>());
            m_AAInstallationLookup = GetComponentLookup<AirDefenseInstallation>(true);

            // CIVIC243 FIX: Wire ResupplyAll to debug command
            SubscribeRequired<DebugResupplyAllCommand>(OnDebugResupplyAll);

            Log.Info("Created (Single Writer: requests → AirDefenseOrchestrator)");
        }

        protected override void OnDestroy()
        {
            UnsubscribeSafe<DebugResupplyAllCommand>(OnDebugResupplyAll);
            base.OnDestroy();
        }

        public void ResetState()
        {
            m_PendingDebugResupply = false;
            m_NextBatchId = 1;
            m_RefillAccum = 0f;
            m_TrickleCycleActive = false;
            m_TrickleUnderfundedNotified = false;
            m_AutoResupplyReader = null;
            m_AADataWithEntitiesCache.Clear();
        }

        public void ResetToBootDefaults(ResetReason reason) => ResetState();

        public void SetDefaults(Context context) => ResetState();

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "nextBatch", m_NextBatchId);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(AAAmmoSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }

            try
            {
                m_PendingDebugResupply = false;
                m_NextBatchId = 1;
                m_RefillAccum = 0f;
                m_TrickleCycleActive = false;
                m_TrickleUnderfundedNotified = false;

                int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int i = 0; i < fc; i++)
                {
                    var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "nextBatch":
                            m_NextBatchId = KeyedSerializer.ReadBoundedLong(reader, tag, "nextBatch", 1, long.MaxValue);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, tag);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        protected override void OnEventDrivenUpdate()
        {
            // Debug cheat: free full resupply on command. Wait for any in-flight batch first.
            if (m_PendingDebugResupply)
            {
                if (!m_PendingResupplyBatchQuery.IsEmpty)
                    return;
                m_PendingDebugResupply = false;
                ResupplyAll();
                return;
            }

            // Graduated trickle refill during the calm phase — replaces the old instant
            // full resupply on WaveSettledEvent. Pause-safe: this system ticks in
            // GameSimulation, which is frozen while the game is paused (Axiom 14), so the
            // accumulator does not advance and ammo does not refill through a pause.
            TrickleRefillTick();
        }

        private void TrickleRefillTick()
        {
            // Player opt-out: when auto-resupply is off, AA is refilled only via the manual
            // emergency button. Resolved lazily (null → treat as enabled). Pause-safe: this runs
            // in GameSimulation (frozen on pause), so a mid-pause toggle takes effect on resume.
            m_AutoResupplyReader ??= ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullAutoResupplyReader.Instance);
            if (m_AutoResupplyReader != null && !m_AutoResupplyReader.AutoResupplyEnabled)
            {
                // Per-save AA rule (left Defense-panel toggle). Symmetric with the calm gate below:
                // leaving auto-resupply resets the cadence so re-enabling starts a fresh interval,
                // not a near-instant top-up. Null reader (owner not yet up) → treated as enabled.
                m_RefillAccum = 0f;
                return;
            }

            // Refill only in calm — recovery is the "lick wounds" window and active waves
            // spend ammo. Outside calm, hold the accumulator at zero so the next calm
            // starts a fresh delivery cadence.
            if (!IsCalmPhase())
            {
                m_RefillAccum = 0f;
                // A new calm after the wave is a new underfunded episode — re-arm the alert so a
                // still-broke city is warned again. The funds-recovered / magazines-full re-arms
                // never fire in a death spiral, which is exactly where the warning matters most.
                m_TrickleUnderfundedNotified = false;
                return;
            }

            // One in-flight batch at a time: wait for the pipeline to apply the previous
            // trickle portion before queuing the next (no entity churn, no double budget).
            if (!m_PendingResupplyBatchQuery.IsEmpty)
                return;

            var cfg = BalanceConfig.Current;
            // Guard the divisor locally (config already clamps >= 1 on load/hot-reload).
            float refillDuration = Math.Max(1f, cfg.AirDefense.AmmoRefillDurationSeconds);

            // PERF-LOCK: the fraction throttle gates the CollectAADataWithEntities sync
            // point (SystemAPI.Query → CompleteDependencyBeforeRO) to delivery ticks only
            // (~once per TRICKLE_DELIVER_FRACTION of the refill duration), never per-frame.
            // A per-frame AA query here is a sync-point regression — see the history note
            // on CollectAADataWithEntities. Do not hoist the query above this gate.
            m_RefillAccum += SystemAPI.Time.DeltaTime / refillDuration;
            if (m_RefillAccum < TRICKLE_DELIVER_FRACTION)
                return;
            m_RefillAccum -= TRICKLE_DELIVER_FRACTION;

            var aaWithEntities = CollectAADataWithEntities();
            if (aaWithEntities.Count == 0)
                return;

            int totalDeficit = 0;
            int totalRounds = 0;
            foreach (var aa in aaWithEntities)
            {
                // Patriot is never auto-refilled: its dear interceptor missiles are restocked only
                // through the deliberate per-type emergency button (flat cost + cooldown), so the
                // calm-phase trickle would otherwise underprice them at the global per-round rate.
                if (aa.type == AAType.PatriotSAM) continue;
                int deficit = Math.Max(0, aa.max - aa.current);
                if (deficit == 0) continue;
                totalDeficit += deficit;
                totalRounds += TricklePortion(aa.max, deficit);
            }

            if (totalDeficit == 0)
            {
                // City-wide magazines are full — emit the single terminal Full for the cycle.
                if (m_TrickleCycleActive)
                {
                    m_TrickleCycleActive = false;
                    EventBus?.SafePublish(new AAResupplyEvent(AAResupplyResult.Full), "AAAmmoSystem");
                }
                m_TrickleUnderfundedNotified = false; // episode over — re-arm the low-funds alert
                m_RefillAccum = 0f;
                return;
            }

            int costPerRound = cfg.Economy.AmmoCostPerRound;

            // Affordability clamp: the budget deduct is all-or-nothing per batch, so a batch
            // priced above the city balance is dropped WHOLE — zero ammo, silently — which starves
            // AA in a death spiral (empty AA → heavier waves → less money → still can't afford).
            // Instead deliver only what the city can pay this tick, mirroring the Partial path in
            // AAAmmoLogic.EvaluateResupply. cityBalance - PendingDeductions is the conservative
            // figure the pipeline's deduct will see, so the clamped batch is payable, not dropped.
            int deliverRounds = totalRounds;
            if (costPerRound > 0)
            {
                if (!CityBudgetService.TryGetBalance(World, out long cityBalance))
                {
                    // Budget subsystem not ready (boot/transition window) — defer rather than build a
                    // full-cost batch the pipeline would drop WHOLE (the very death spiral this clamp
                    // fixes). Retry next calm tick, mirroring StartResupplyWithCost's defer.
                    m_RefillAccum = 0f;
                    return;
                }
                long availableMoney = cityBalance - CityBudgetService.PendingDeductions;
                int affordable = AAAmmoLogic.CalculateAffordableRounds(availableMoney, costPerRound);
                if (affordable < totalRounds)
                {
                    deliverRounds = Math.Max(0, affordable);
                    // Notify once per underfunded episode — without this the player's AA empties
                    // with no in-game signal (Partial = delivered some, Failed = couldn't afford any).
                    // Rounds/Needed are read by the toast ("Only {Rounds}/{Needed} missiles") and by
                    // telemetry; leaving them default-0 prints "Only 0/0". deliverRounds == the rounds
                    // DistributeRounds hands out here (capacity >= deliverRounds), so it is exact.
                    if (!m_TrickleUnderfundedNotified)
                    {
                        m_TrickleUnderfundedNotified = true;
                        EventBus?.SafePublish(new AAResupplyEvent(
                            deliverRounds > 0 ? AAResupplyResult.Partial : AAResupplyResult.Failed,
                            Rounds: deliverRounds,
                            Needed: totalDeficit,
                            Cost: AAAmmoLogic.CalculateResupplyCost(totalRounds - deliverRounds, costPerRound)
                        ), "AAAmmoSystem");
                    }
                }
                else
                {
                    m_TrickleUnderfundedNotified = false; // funds recovered — re-arm the alert
                }
            }

            if (deliverRounds <= 0)
            {
                // Can't afford a single round — nothing to deliver (already alerted). Reset the
                // accumulator so the next calm tick retries cleanly once funds recover.
                m_RefillAccum = 0f;
                return;
            }

            // Distribute the affordable rounds across guns, capping each at its trickle portion
            // (DistributeRounds fills toward each unit's max in index order until rounds run out).
            // When fully affordable, deliverRounds == totalRounds and every gun gets its portion —
            // identical to the pre-clamp behavior.
            var lineUnits = new (int current, int max)[aaWithEntities.Count];
            for (int i = 0; i < aaWithEntities.Count; i++)
            {
                var aa = aaWithEntities[i];
                if (aa.type == AAType.PatriotSAM) { lineUnits[i] = (aa.current, aa.current); continue; }
                int deficit = Math.Max(0, aa.max - aa.current);
                int portion = deficit == 0 ? 0 : TricklePortion(aa.max, deficit);
                lineUnits[i] = (aa.current, aa.current + portion);
            }
            int[] perGun = AAAmmoLogic.DistributeRounds(lineUnits, deliverRounds);

            int actualRounds = 0;
            for (int i = 0; i < perGun.Length; i++) actualRounds += perGun[i];
            if (actualRounds <= 0)
            {
                m_RefillAccum = 0f;
                return;
            }
            long actualCost = AAAmmoLogic.CalculateResupplyCost(actualRounds, costPerRound);

            var ecb = m_Barrier.CreateCommandBuffer();
            long batchId = AllocateBatchId();
            CreateResupplyBatch(
                ecb,
                batchId,
                actualCost,
                requiresBudget: actualCost > 0,
                label: "Trickle",
                isFullResupply: false,
                requestedRounds: actualRounds,
                neededRounds: totalDeficit,
                trickle: true);

            for (int i = 0; i < aaWithEntities.Count; i++)
            {
                int give = perGun[i];
                if (give <= 0) continue;
                var aa = aaWithEntities[i];
                CreateResupplyLineIntent(
                    ecb, batchId, aa.entityIndex, aa.entityVersion,
                    aa.current + give, give, costPerRound);
            }

            m_TrickleCycleActive = true;
        }

        // Per-installation delivery: a fixed fraction of THIS gun's magazine (each gun is
        // its own depot, refilled over the same duration regardless of capacity), at least
        // one round so the smallest magazine still progresses, capped at the deficit.
        private static int TricklePortion(int maxAmmo, int deficit)
            => Math.Min(deficit, Math.Max(1, (int)Math.Round(maxAmmo * TRICKLE_DELIVER_FRACTION)));

        private bool IsCalmPhase()
            => m_WaveStateQuery.TryGetSingleton<WaveStateSingleton>(out var waveState)
               && waveState.CurrentPhase == GamePhase.Calm;

        // ============================================================================
        // EVENT HANDLERS
        // ============================================================================

        /// <summary>
        /// CIVIC243 FIX: Handle debug resupply command (free resupply all AA).
        /// </summary>
        private void OnDebugResupplyAll(DebugResupplyAllCommand cmd)
        {
            if (!m_PendingResupplyBatchQuery.IsEmpty)
            {
                Log.Warn("Debug resupply deferred: AA resupply batch is still pending");
            }

            m_PendingDebugResupply = true;
        }

        // ============================================================================
        // RESUPPLY OPERATIONS
        // ============================================================================

        /// <summary>
        /// Resupply all AA installations to max ammo (no cost).
        /// Creates ResupplyAARequest for AirDefenseOrchestrator (single writer).
        /// Used by cheat commands and scenario events.
        /// </summary>
#pragma warning disable CIVIC231 // Called by cheat/scenario — caller checks act
        public void ResupplyAll()
        {
#pragma warning restore CIVIC231
            int count = 0;
#pragma warning disable CIVIC327 // Sync point acceptable — cheat/scenario path, not called during normal gameplay
            var aaEntities = m_AAQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
#pragma warning restore CIVIC327
            if (aaEntities.Length > 0)
            {
                m_AAInstallationLookup.Update(this);
                var ecb = m_Barrier.CreateCommandBuffer();
                foreach (var aaEntity in aaEntities)
                {
                    var aa = m_AAInstallationLookup[aaEntity];
                    var requestEntity = ecb.CreateEntity();
                    ecb.AddComponent(requestEntity, new ResupplyAARequest
                    {
                        AAEntityIndex = aaEntity.Index,
                        AAEntityVersion = aaEntity.Version,
                        NewAmmo = aa.MaxAmmo,
                        RoundsAdded = Math.Max(0, aa.MaxAmmo - aa.CurrentAmmo),
                        CostPerRound = 0 // Free resupply (cheat/scenario) — no refund needed
                    });
                    RequestMetaWriter.AddInternal(ecb, requestEntity, nameof(ResupplyAARequest), aaEntity.Index.ToString());
                    count++;
                }
            }
            if (count > 0)
                Log.Info($"All AA resupply requested (free): {count} installations");
            aaEntities.Dispose();
        }

        /// <summary>
        /// Resupply AA installations with budget deduction.
        /// Attempts full resupply, falls back to partial if insufficient funds.
        /// Returns (rounds resupplied, cost, isFullResupply).
        /// </summary>
#pragma warning disable CIVIC243 // Public scenario/debug API; normal wave path uses StartResupplyWithCost for deferred status.
        public (int rounds, long cost, bool isFullResupply) ResupplyWithCost()
#pragma warning restore CIVIC243
        {
            var result = StartResupplyWithCost();
            return (result.Rounds, result.Cost, result.IsFullResupply);
        }

        private ResupplyStartResult StartResupplyWithCost()
        {
            if (!m_PendingResupplyBatchQuery.IsEmpty)
            {
                if (Log.IsDebugEnabled) Log.Debug("AA resupply deferred: previous resupply batch is still pending");
                return new ResupplyStartResult(0, 0, true);
            }

            var aaWithEntities = CollectAADataWithEntities();
            if (aaWithEntities.Count == 0)
            {
                Log.Info("No AA to resupply");
                return new ResupplyStartResult(0, 0, true);
            }

            var aaData = new (int current, int max)[aaWithEntities.Count];
            for (int i = 0; i < aaWithEntities.Count; i++)
                aaData[i] = (aaWithEntities[i].current, aaWithEntities[i].max);

            int totalRoundsNeeded = AAAmmoLogic.CalculateTotalRoundsNeeded(aaData);

            // Budget subsystem not ready (load window / no city) is NOT "no money":
            // defer so the pending wave-settled count is not consumed (see OnUpdateImpl
            // gate) and retry next frame instead of emitting a false FailedNoBudget.
            if (!CityBudgetService.TryGetBalance(World, out long cityBalance))
                return new ResupplyStartResult(0, 0, true);

            long availableMoney = cityBalance - CityBudgetService.PendingDeductions;

            // CIVIC347: cache once — BalanceConfig.Current is hot-reloadable.
            int ammoCostPerRound = BalanceConfig.Current.Economy.AmmoCostPerRound;
            var result = AAAmmoLogic.EvaluateResupply(
                totalRoundsNeeded,
                availableMoney,
                ammoCostPerRound
            );

            switch (result.Status)
            {
                case ResupplyStatus.NotNeeded:
                    Log.Info("No resupply needed");
                    return new ResupplyStartResult(0, 0, true);

                case ResupplyStatus.Full:
                {
                    // S003b: one budget decision gates a frozen batch of AA line items.
                    var fullEcb = m_Barrier.CreateCommandBuffer();
                    long batchId = AllocateBatchId();
                    CreateResupplyBatch(
                        fullEcb,
                        batchId,
                        result.Cost,
                        requiresBudget: result.Cost > 0,
                        label: "Full",
                        isFullResupply: true,
                        requestedRounds: result.RoundsToResupply,
                        neededRounds: totalRoundsNeeded);
                    QueueFullResupplyLines(fullEcb, batchId, aaWithEntities, ammoCostPerRound);
                    Log.Info($"Resupply requested: {result.RoundsToResupply} rounds for ${result.Cost:N0} (pending budget result)");
                    return new ResupplyStartResult(result.RoundsToResupply, result.Cost, true);
                }

                case ResupplyStatus.Partial:
                    return ApplyPartialResupply(result.RoundsToResupply, totalRoundsNeeded, aaWithEntities);

                default:
                    Log.Error($"NO RESUPPLY - city is broke! Need ${result.RequiredCost:N0}");
                    EventBus?.SafePublish(new AAResupplyEvent(
                        AAResupplyResult.Failed,
                        Cost: result.RequiredCost
                    ), "AAAmmoSystem");
                    return new ResupplyStartResult(0, 0, false);
            }
        }

        // ============================================================================
        // PRIVATE HELPERS
        // ============================================================================

        /// <summary>
        /// Collect AA ammo data from all installations with their entities.
        /// Returns array of (entityIndex, entityVersion, current ammo, max ammo) tuples.
        /// Using entity Index+Version ensures distribution matches same iteration order.
        /// NOTE: Entity stored as Index+Version to avoid vanilla orphan detection.
        /// PERF M2.5: Uses cached list and returns array copy.
        ///
        /// R9-M12: SystemAPI.Query triggers CompleteDependencyBeforeRO sync point.
        /// Acceptable here — this path runs only while draining wave-settled resupply work.
        /// DO NOT move to a per-frame system without converting to ComponentLookup pattern.
        /// History: a "harmless" sync point in ThreatArrivalSystem once cost 5 FPS
        /// (see PERF_COMPARISON_TAS_OPTIMIZATION.md).
        /// </summary>
        private List<(int entityIndex, int entityVersion, int current, int max, AAType type)> CollectAADataWithEntities()
        {
            m_AADataWithEntitiesCache.Clear();
            // S002b: skip Deleted/destroyed AA — paying for ammo on dead installations leaks budget.
            foreach (var (aa, entity) in
                SystemAPI.Query<RefRO<AirDefenseInstallation>>()
                .WithAll<Simulate>()
                .WithNone<Deleted, Destroyed>()
                .WithEntityAccess())
            {
                m_AADataWithEntitiesCache.Add((entity.Index, entity.Version, aa.ValueRO.CurrentAmmo, aa.ValueRO.MaxAmmo, aa.ValueRO.Type));
            }
            return m_AADataWithEntitiesCache;
        }


        /// <summary>
        /// Apply partial resupply when budget is insufficient for full resupply.
        /// Distributes available ammo via greedy/priority (first-AA-first, ECS query order).
        /// Creates ResupplyAARequest for AirDefenseOrchestrator (single writer).
        /// </summary>
        private ResupplyStartResult ApplyPartialResupply(
            int affordableRounds, int totalRoundsNeeded,
            List<(int entityIndex, int entityVersion, int current, int max, AAType type)> aaWithEntities)
        {
            int ammoCostPerRound = BalanceConfig.Current.Economy.AmmoCostPerRound;
            long actualCost = AAAmmoLogic.CalculateResupplyCost(affordableRounds, ammoCostPerRound);

            // S003b: pipeline gates ammo on confirmed BudgetDeductResult.
            var partialEcb = m_Barrier.CreateCommandBuffer();
            long batchId = AllocateBatchId();
            CreateResupplyBatch(
                partialEcb,
                batchId,
                actualCost,
                requiresBudget: actualCost > 0,
                label: "Partial",
                isFullResupply: false,
                requestedRounds: affordableRounds,
                neededRounds: totalRoundsNeeded);

            var singlePassData = new (int current, int max)[aaWithEntities.Count];
            for (int j = 0; j < aaWithEntities.Count; j++)
                singlePassData[j] = (aaWithEntities[j].current, aaWithEntities[j].max);
            var distribution = AAAmmoLogic.DistributeRounds(singlePassData, affordableRounds);

            for (int i = 0; i < distribution.Length && i < aaWithEntities.Count; i++)
            {
                if (distribution[i] == 0) continue;
                int newAmmo = aaWithEntities[i].current + distribution[i];
                int rounds = distribution[i];
                CreateResupplyLineIntent(partialEcb, batchId,
                    aaWithEntities[i].entityIndex, aaWithEntities[i].entityVersion,
                    newAmmo, rounds, ammoCostPerRound);
            }

            Log.Warn($"PARTIAL resupply: {affordableRounds}/{totalRoundsNeeded} rounds (pending budget result)");
            return new ResupplyStartResult(affordableRounds, actualCost, false);
        }

        private long AllocateBatchId()
        {
            long maxExisting = 0;
            foreach (var batch in SystemAPI.Query<RefRO<AAResupplyBatchIntent>>())
                maxExisting = Math.Max(maxExisting, batch.ValueRO.BatchId);

            if (m_NextBatchId <= maxExisting)
                m_NextBatchId = maxExisting + 1;

            if (m_NextBatchId <= 0)
                m_NextBatchId = 1;

            return m_NextBatchId++;
        }

        private void QueueFullResupplyLines(
            EntityCommandBuffer ecb,
            long batchId,
            List<(int entityIndex, int entityVersion, int current, int max, AAType type)> aaWithEntities,
            int costPerRound)
        {
            foreach (var aa in aaWithEntities)
            {
                int rounds = Math.Max(0, aa.max - aa.current);
                if (rounds == 0) continue;
                CreateResupplyLineIntent(ecb, batchId, aa.entityIndex, aa.entityVersion, aa.max, rounds, costPerRound);
            }
        }

        private void CreateResupplyBatch(
            EntityCommandBuffer ecb,
            long batchId,
            long totalCost,
            bool requiresBudget,
            string label,
            bool isFullResupply,
            int requestedRounds,
            int neededRounds,
            bool trickle = false)
        {
            var batchEntity = ecb.CreateEntity();
            ecb.AddComponent(batchEntity, new AAResupplyBatchIntent
            {
                BatchId = batchId,
                TotalCost = totalCost,
                RequiresBudget = requiresBudget,
                BudgetResolved = !requiresBudget,
                BudgetSucceeded = !requiresBudget,
                IsFullResupply = isFullResupply,
                RequestedRounds = requestedRounds,
                NeededRounds = neededRounds,
                Trickle = trickle
            });

            if (!requiresBudget)
                return;

            if (!AirDefenseEligibility.CanPayAirDefenseBudget(totalCost, World, out _))
            {
                ecb.SetComponent(batchEntity, new AAResupplyBatchIntent
                {
                    BatchId = batchId,
                    TotalCost = totalCost,
                    RequiresBudget = true,
                    BudgetResolved = true,
                    BudgetSucceeded = false,
                    IsFullResupply = isFullResupply,
                    RequestedRounds = requestedRounds,
                    NeededRounds = neededRounds,
                    Trickle = trickle
                });
                Log.Warn($"AA resupply budget request failed for batch {batchId}: insufficient funds (${totalCost:N0})");
                return;
            }

            bool budgetQueued;
#pragma warning disable CIVIC022 // Source string built once per resupply batch, not per frame
            budgetQueued = BudgetEmitter.TryQueueDeduct(
                World,
                ecb,
                totalCost,
                BudgetCategory.AirDefense,
                BudgetPriority.Operational,
                $"AAResupplyBatch:{batchId}:{label}",
                out var budgetEntity,
                BudgetResultMode.RetainResult);
#pragma warning restore CIVIC022

            if (!budgetQueued)
            {
                ecb.SetComponent(batchEntity, new AAResupplyBatchIntent
                {
                    BatchId = batchId,
                    TotalCost = totalCost,
                    RequiresBudget = true,
                    BudgetResolved = true,
                    BudgetSucceeded = false,
                    IsFullResupply = isFullResupply,
                    RequestedRounds = requestedRounds,
                    NeededRounds = neededRounds,
                    Trickle = trickle
                });
                Log.Warn($"AA resupply budget request failed for batch {batchId}: insufficient funds (${totalCost:N0})");
                return;
            }

            ecb.AddComponent(budgetEntity, new AAResupplyBudgetLink
            {
                BatchId = batchId
            });
        }

        private void CreateResupplyLineIntent(
            EntityCommandBuffer ecb,
            long batchId,
            int aaIndex, int aaVersion,
            int newAmmo, int rounds, int costPerRound)
        {
            var lineEntity = ecb.CreateEntity();
            ecb.AddComponent(lineEntity, new AAResupplyLineIntent
            {
                BatchId = batchId,
                AAEntityIndex = aaIndex,
                AAEntityVersion = aaVersion,
                NewAmmo = newAmmo,
                RoundsAdded = rounds,
                CostPerRound = costPerRound
            });
        }

    }
}
