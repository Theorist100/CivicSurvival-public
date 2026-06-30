using System.Collections.Generic;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Components.Threats;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces;
using CivicSurvival.Core.Interfaces.Domain.GridWarfare;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Scheduling;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Components.Requests;
using CivicSurvival.Core.Config;
using CivicSurvival.Domains.GridWarfare.Events;
using Game.Simulation;
using Unity.Entities;

namespace CivicSurvival.Domains.GridWarfare.Systems
{
    /// <summary>
    /// Physical counter-strike commit owner (ModificationEnd, pause-safe). Two phase-owned jobs:
    ///
    /// 1. <b>Launch</b> — drains the operations the player committed via
    ///    <c>PlayerAttackSystem.ExecuteOperation</c>. For each, it spends one arsenal munition and
    ///    appends an outbound <c>ThreatSpawnIntent</c> through <see cref="IOutboundStrikeService"/>
    ///    (a synchronous main-thread buffer append — NOT a structural change, so it is safe and
    ///    pause-safe here; the render-archetype CreateEntity happens render-safe in
    ///    <c>ThreatSpawnApplySystem</c>, Modification4). The Shadow Cash is confirmed and the slot
    ///    cleared on a successful launch (<c>CompleteOperationExecution</c>); a failed launch
    ///    (prefabs unresolved / no munition) rolls the slot back to Ready and spends nothing. The
    ///    axis effect is <i>deferred</i> to arrival — no axis is touched at launch.
    ///
    /// 2. <b>Arrival</b> — drains <see cref="OutboundArrivalSignal"/> elements the ThreatDamage
    ///    arrival reader queued when an outbound projectile reached the frontier. For each, it rolls
    ///    the enemy's <c>InterceptChance</c> (intercepted → 0 damage) and otherwise lowers the
    ///    targeted enemy axis (<c>ReduceAxis</c>). This is where the former instant
    ///    <c>Pressure -= damage</c> moved to.
    ///
    /// The exactly-once commit protocol (<c>Claim</c>/<c>Complete</c>/<c>Rollback</c>) still guards
    /// the launch: a launch is applied at most once and rolled back if the act gate closes between
    /// Execute and commit.
    /// </summary>
    [ActIndependent]
    public partial class EnemyOperationEffectSystem : CivicSystemBase, ICivicSingletonOwner<OutboundArrivalSignalHost>
    {
        private static readonly LogContext Log = new("EnemyOperationEffectSystem");

        private readonly List<OperationExecutedEvent> m_PendingEffects = new(4);

        private PlayerAttackSystem m_PlayerSystem = null!;
        private IOutboundStrikeService m_Strike = NullOutboundStrikeService.Instance;
        private ICounterAttackArsenalService m_Arsenal = NullCounterAttackArsenalService.Instance;

        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_CurrentActQuery;
        private EntityQuery m_ArrivalSignalQuery;
        private ComponentLookup<EnemyState> m_EnemyStateLookup;

        // Loot for an enemy-beachhead collapse is paid as Shadow Cash income. It rides the same
        // GameSimulation-end barrier ShadowWalletSystem drains, so the credit lands in the regular
        // income pipeline; the durable per-collapse OperationKey makes the wallet apply it once.
        private GameSimulationEndBarrier m_GameSimulationEndBarrier = null!;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EnemyStateQuery = GetEntityQuery(ComponentType.ReadWrite<EnemyState>());
            m_CurrentActQuery = GetEntityQuery(ComponentType.ReadOnly<CurrentActSingleton>());
            m_ArrivalSignalQuery = GetEntityQuery(ComponentType.ReadWrite<OutboundArrivalSignal>());
            m_EnemyStateLookup = GetComponentLookup<EnemyState>(false);
            m_GameSimulationEndBarrier = World.GetOrCreateSystemManaged<GameSimulationEndBarrier>();

            // The consumer owns the arrival-signal host: create it here and re-create on every
            // start/load (OnCreate doesn't re-run on a fresh-world load, and the non-serialized
            // host is stripped on load).
            OutboundArrivalSignalHost.EnsureExists(EntityManager);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_PlayerSystem ??= FeatureRegistry.Instance.Require<PlayerAttackSystem>();
#pragma warning disable CIVIC114 // Wired in OnStartRunning only; consumer is single-threaded on the main thread
            m_Strike = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullOutboundStrikeService.Instance);
            m_Arsenal = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullCounterAttackArsenalService.Instance);
#pragma warning restore CIVIC114
            OutboundArrivalSignalHost.EnsureExists(EntityManager);
        }

        public void OnLoadRestore(EntityManager entityManager)
        {
            OutboundArrivalSignalHost.EnsureExists(entityManager);
        }

        protected override void OnUpdateImpl()
        {
            // Arrival effects first: an outbound strike that arrived this frame lowers the enemy
            // axis regardless of new launches. Then process launches.
            ApplyArrivalEffects();
            ProcessLaunches();
        }

        // ----------------------------------------------------------------------------
        // Launch phase — exactly-once commit of player operations into outbound projectiles
        // ----------------------------------------------------------------------------
        private void ProcessLaunches()
        {
            m_PendingEffects.Clear();
            m_PlayerSystem.ClaimPendingOperationEffects(m_PendingEffects);
            if (m_PendingEffects.Count == 0)
                return;

            // R3-D-5 invariant: queued effects still respect the current act when the ECS owner
            // commits them. This matches the old subscriber gate.
            if (!m_CurrentActQuery.TryGetSingleton<CurrentActSingleton>(out var actSingleton)
                || actSingleton.CurrentAct < Act.Adaptation)
            {
                RollbackPendingEffects("act gate closed before operation launch commit");
                return;
            }

            for (int i = 0; i < m_PendingEffects.Count; i++)
            {
                var evt = m_PendingEffects[i];
                var kind = ArsenalKindMap.ForCategory(evt.Category);

                // Spend the munition atomically with the launch: if the arsenal is empty (or the
                // launch producer cannot fire yet), roll the slot back to Ready and leave the
                // Shadow lock intact — nothing is committed. Spending here (not at Execute) makes a
                // rolled-back commit leak-free: no projectile, no munition lost.
                if (!m_Arsenal.TrySpend(kind, 1))
                {
                    m_PlayerSystem.RollbackOperationExecution(evt.ExecutionId, evt.OperationId);
                    Log.Warn($"Launch rolled back for {evt.AttackType} (execution {evt.ExecutionId}): no {kind} in arsenal");
                    continue;
                }

                // Freeze the intercept-roll seed HERE, at launch — a deterministic function of the
                // operation's stable identity (ExecutionId/OperationId) and the current game time
                // (stable across save/load), NOT a runtime Random. The seed rides the projectile to
                // arrival, so the intercept verdict is reproducible after a mid-flight save/load and
                // recomputable by a server fed the same launch seed (Wave3Arena Phase-40).
                uint strikeSeed = FreezeStrikeSeed(evt);

                if (!m_Strike.Launch(kind, evt.Category, evt.ActualDamage, strikeSeed))
                {
                    // Launch could not be recorded (prefabs unresolved / Waves closed): refund the
                    // munition and roll back so the player can retry.
                    m_Arsenal.Replenish(kind, 1);
                    m_PlayerSystem.RollbackOperationExecution(evt.ExecutionId, evt.OperationId);
                    Log.Warn($"Launch rolled back for {evt.AttackType} (execution {evt.ExecutionId}): outbound strike producer unavailable");
                    continue;
                }

                // Confirm the Shadow Cash deduction and clear the slot. The axis effect is deferred
                // to arrival (OutboundArrivalSignal) — no axis is touched here.
                if (!m_PlayerSystem.CompleteOperationExecution(evt.ExecutionId, evt.OperationId))
                {
                    // The slot/wallet was no longer in the expected state (stale execution intent):
                    // the projectile is already in flight, but we could not confirm the wallet. The
                    // arsenal stays spent (the launch happened). Log and move on.
                    Log.Warn($"Launched {evt.AttackType} but could not complete execution intent {evt.ExecutionId} ({evt.OperationId}); wallet not confirmed");
                    continue;
                }

                EventBus?.SafePublish(evt, nameof(EnemyOperationEffectSystem));
                Log.Info($"Launched {evt.AttackType} ({kind}, {evt.Category}) → outbound strike, {evt.ActualDamage:F1}% pending at arrival");
            }
        }

        private void RollbackPendingEffects(string reason)
        {
            for (int i = 0; i < m_PendingEffects.Count; i++)
            {
                var evt = m_PendingEffects[i];
                m_PlayerSystem.RollbackOperationExecution(evt.ExecutionId, evt.OperationId);
            }

            Log.Warn($"Rolled back {m_PendingEffects.Count} operation launch intent(s): {reason}");
        }

        /// <summary>
        /// Freeze the intercept-roll seed at launch from the operation's stable identity plus the
        /// current game time — a DETERMINISTIC mix, never a runtime <c>Random</c>. The same launched
        /// operation always produces the same seed, so the arrival roll is reproducible after a
        /// mid-flight save/load and recomputable by a server (Wave3Arena Phase-40).
        ///
        /// Inputs are the per-execution counter (<c>ExecutionId</c>), the operation id string
        /// (hashed with FNV-1a — a fixed algorithm, unlike the runtime-salted <c>string.GetHashCode</c>
        /// which differs per process and would break server parity), and the integer game-hour
        /// (stable across save/load — <c>SystemAPI.Time.ElapsedTime</c> resets on load, so it is NOT
        /// used). The mix forces a set bit so the seed is never 0 (a 0 state never advances the RNG).
        /// </summary>
        private static uint FreezeStrikeSeed(in OperationExecutedEvent evt)
        {
            // FNV-1a over the operation id (deterministic, process-independent).
            const uint FnvOffsetBasis = 2166136261u;
            const uint FnvPrime = 16777619u;
            uint hash = FnvOffsetBasis;
            string id = evt.OperationId ?? string.Empty;
            for (int i = 0; i < id.Length; i++)
            {
                hash ^= id[i];
                hash *= FnvPrime;
            }

            // Integer game-hour: stable across save/load (unlike ElapsedTime). Falls back to 0 before
            // GameTimeSystem activates — harmless here (launch happens well after city load).
            uint gameHourBits = GameTimeSystem.TryGetGameHours(out float gameHours)
                ? (uint)(int)gameHours
                : 0u;

            // Mix execution counter + game-hour into the id hash (FNV-1a step per 32-bit word).
            hash = (hash ^ unchecked((uint)evt.ExecutionId)) * FnvPrime;
            hash = (hash ^ gameHourBits) * FnvPrime;

            return hash | 1u; // never 0 — Unity.Mathematics.Random rejects a 0 seed
        }

        // ----------------------------------------------------------------------------
        // Arrival phase — deferred axis effect when an outbound strike reaches the frontier
        // ----------------------------------------------------------------------------
        private void ApplyArrivalEffects()
        {
            if (!m_ArrivalSignalQuery.TryGetSingletonBuffer<OutboundArrivalSignal>(out var signals, isReadOnly: false)
                || signals.Length == 0)
                return;

            if (!m_EnemyStateQuery.TryGetSingletonEntity<EnemyState>(out var stateEntity))
            {
                // No enemy state to apply to — drop the signals (the projectile already terminalized).
                signals.Clear();
                return;
            }

            m_EnemyStateLookup.Update(this);
            var enemyState = m_EnemyStateLookup[stateEntity];
            float axisFloor = EnemySimulationSystem.AxisFloor;
            var gw = BalanceConfig.Current.GridWarfare;
            float respiteWindowHours = gw.RespiteWindowHours;
            // Game-hour stamp for respite windows: absolute game time (stable across save/load).
            // 0 before GameTime activates — harmless, the window opens at "now" either way.
            float nowHours = GameTimeSystem.TryGetGameHours(out var gh) ? gh : 0f;

            for (int i = 0; i < signals.Length; i++)
            {
                var signal = signals[i];

                // Pure, seeded resolution (Core/Logic): the intercept verdict is a deterministic
                // function of the launch-frozen seed carried on the signal — no session RNG, so a
                // strike caught by a save replays the SAME verdict on load and a server recomputes
                // the identical one. The resolver only computes; the axis write stays here (domain).
                var outcome = StrikeResolver.Resolve(
                    signal.Axis,
                    signal.Damage,
                    enemyState.GetAxis(signal.Axis),
                    axisFloor,
                    enemyState.InterceptChance,
                    signal.Seed);

                if (outcome.Intercepted)
                {
                    Log.Info($"Outbound {signal.Axis} strike intercepted by enemy defence — 0% axis damage");
                    continue;
                }

                // Apply via ReduceAxis so the Category→axis mapping and the in-struct write keep their
                // existing home (the resolver's NewAxis is byte-identical to this clamp).
                float oldAxis = outcome.OldAxis;
                float newAxis = enemyState.ReduceAxis(signal.Axis, signal.Damage, axisFloor);

                // Suppression → regroup: a strike that pins the axis to its floor opens that axis's
                // respite window — waves of this category weaken until it expires (regen then lifts
                // the axis). Only the floor touch arms it; a partial reduction does not.
                if (newAxis <= axisFloor)
                {
                    enemyState.BeginRespite(signal.Axis, nowHours, respiteWindowHours);
                    Log.Info($"{signal.Axis} axis floored — enemy regroups for {respiteWindowHours:F1}h (waves of this type weakened)");
                }

                EventBus?.SafePublish(new EnemyAxisChangedEvent(
                    OldValue: oldAxis,
                    NewValue: newAxis,
                    Axis: signal.Axis,
                    Cause: "outbound strike arrival"
                ), nameof(EnemyOperationEffectSystem));
                Log.Info($"{signal.Axis} axis: {oldAxis:F1}% -> {newAxis:F1}% (-{(oldAxis - newAxis):F1}% from arrived outbound strike)");
            }

            // Act-objective: when this batch of arrivals leaves ALL three axes suppressed under the
            // objective threshold, the enemy beachhead is broken — pay the Shadow Cash loot ONCE per
            // collapse (terminal latch + per-collapse idempotency key), then regen rebuilds the enemy
            // for the next push. This is the PvE precursor of a PvP raid loot.
            TryRewardObjective(ref enemyState, gw);

            m_EnemyStateLookup[stateEntity] = enemyState;
            signals.Clear();
        }

        /// <summary>
        /// Pay the enemy-beachhead-collapse loot exactly once per collapse. Fires only when all three
        /// axes are at or below <c>ObjectiveAxisThreshold</c> and the loot for this collapse is not yet
        /// claimed; the latch (<see cref="EnemyState.ObjectiveClaimed"/>) blocks per-tick re-payment and
        /// is reset by regen (<c>EnemySimulationSystem</c>) once any axis recovers above the threshold,
        /// so a fresh collapse pays again. The income request carries a durable per-collapse
        /// <c>OperationKey</c>, so the wallet de-dupes the credit even across a mid-frame save/load.
        /// </summary>
        private void TryRewardObjective(ref EnemyState enemyState, GridWarfareConfig gw)
        {
            float threshold = gw.ObjectiveAxisThreshold;
            if (enemyState.ObjectiveClaimed || !enemyState.AllAxesBelow(threshold))
                return;

            long loot = gw.ObjectiveLootShadowCash;
            int collapseId = enemyState.ObjectiveCollapseCount + 1;

            // A configured loot of 0 still counts as a collapse: latch it (no income to queue) so we
            // do not re-evaluate every arrival while the axes stay suppressed.
            if (loot > 0)
            {
                string operationKey = $"GwObjective:{collapseId}";
                var ecb = m_GameSimulationEndBarrier.CreateCommandBuffer();
                if (!ShadowEconomyEmitter.TryQueueIncome(World, ecb, loot, "GridWarfare beachhead suppressed", operationKey))
                {
                    // Wallet not operational yet (boot/pre-act). Leave the latch open and the counter
                    // untouched so the loot is retried on the next arrival while the axes stay suppressed.
                    return;
                }
                m_GameSimulationEndBarrier.AddJobHandleForProducer(Dependency);
            }

            // Claim atomically with the queued credit: bump the collapse counter (so a later collapse
            // gets a fresh key) and latch until regen lifts an axis. The income idempotency key is the
            // durable backstop if this claim is lost to a crash before the credit drains.
            enemyState.ObjectiveCollapseCount = collapseId;
            enemyState.ObjectiveClaimed = true;

            Log.Info($"Enemy beachhead suppressed (collapse #{collapseId}) — looted {loot:N0} Shadow Cash");
        }
    }
}
