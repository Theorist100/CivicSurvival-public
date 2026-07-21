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
    ///    arrival reader queued when an outbound projectile reached the frontier. Each strike is
    ///    resolved against a concrete mirror-city target
    ///    (<c>MirrorCitySystem.ApplyStrikeToTarget</c>: positional AA intercept roll, per-tier
    ///    death rules, derived-axis recompute). This is where the former instant
    ///    <c>Pressure -= damage</c> moved to.
    ///
    /// The exactly-once commit protocol (<c>Claim</c>/<c>Complete</c>/<c>Rollback</c>) still guards
    /// the launch: a launch is applied at most once and rolled back if the act gate closes between
    /// Execute and commit. On top of it, every committed launch is recorded in the PERSISTED
    /// pending-launch ledger (<see cref="m_PendingLaunches"/>): the spawn intent the launch writes
    /// is transient by design (<c>ThreatSpawnIntentHost</c>), so a save taken in the one-frame
    /// producer→consumer gap would otherwise lose a strike whose munition and Shadow Cash are
    /// already spent. The ledger closes that hole — an entry is dropped when its strike arrives,
    /// and after a load any entry with no matching in-flight projectile is re-launched (resources
    /// NOT re-spent; the original launch already paid).
    /// </summary>
    [ActIndependent]
    public partial class EnemyOperationEffectSystem : CivicSystemBase, ICivicSingletonOwner<OutboundArrivalSignalHost>
    {
        private static readonly LogContext Log = new("EnemyOperationEffectSystem");

        private readonly List<OperationExecutedEvent> m_PendingEffects = new(4);

        private PlayerAttackSystem m_PlayerSystem = null!;
        // Mirror-city model (same domain). Resolved softly: null only while the feature registry is
        // still populating — arrivals then wait in the buffer for the next tick.
        private MirrorCitySystem? m_MirrorCity;
        private IOutboundStrikeService m_Strike = NullOutboundStrikeService.Instance;
        private ICounterAttackArsenalService m_Arsenal = NullCounterAttackArsenalService.Instance;

        private EntityQuery m_EnemyStateQuery;
        private EntityQuery m_CurrentActQuery;
        private EntityQuery m_ArrivalSignalQuery;
        private ComponentLookup<EnemyState> m_EnemyStateLookup;

        /// <summary>
        /// One committed launch whose projectile may not have materialised yet — see the class
        /// summary. Blittable record of everything a re-launch needs.
        /// </summary>
        internal struct PendingOutboundLaunch
        {
            public ArsenalKind Kind;
            public AttackCategory Axis;
            public float Damage;
            public uint Seed;
            public ushort TargetId;
            /// <summary>
            /// Game-hour the launch was committed (0 = unknown, e.g. a pre-TTL save). Bounds the
            /// entry's life across loads: retirement normally happens at arrival, but if the
            /// projectile ever disappears WITHOUT an arrival signal (a future deletion path), the
            /// orphaned entry would otherwise re-launch a strike that was already removed in flight
            /// on every subsequent load. The reconcile pass retires entries older than
            /// <see cref="PendingLaunchTtlHours"/> instead.
            /// </summary>
            public float LaunchedAtHours;
        }

        /// <summary>
        /// Post-load re-launch window (game hours). Generous versus any real flight time — a strike
        /// still legitimately in flight is identified by its persisted payload, so the TTL only ever
        /// retires entries whose projectile is gone and whose arrival never fired.
        /// </summary>
        private const float PendingLaunchTtlHours = 24f;

        // Persisted pending-launch ledger (small: one entry per strike still in the launch→spawn
        // gap or in flight; dropped at arrival). Serialization in the sibling partial.
        private readonly List<PendingOutboundLaunch> m_PendingLaunches = new(4);

        // Pre-allocated scratch for the post-load reconcile (CIVIC050: no per-call allocation).
        [System.NonSerialized] private readonly HashSet<uint> m_InFlightSeeds = new();

        // Set by Deserialize: on the first tick after a load, re-launch any ledger entry whose
        // projectile does not exist (it was lost to the transient spawn-intent gap).
        [System.NonSerialized] private bool m_ReconcilePendingLaunches;

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
            m_MirrorCity ??= FeatureRegistry.Instance.Query<MirrorCitySystem>();
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
            // Post-load re-drive first (one shot): a pending launch whose projectile was lost to
            // the transient spawn-intent gap is re-issued before anything else this session.
            if (m_ReconcilePendingLaunches)
                ReconcilePendingLaunches();

            // Arrival effects first: an outbound strike that arrived this frame lowers the enemy
            // axis regardless of new launches. Then process launches.
            ApplyArrivalEffects();
            ProcessLaunches();
        }

        /// <summary>
        /// Re-issue every persisted pending launch whose projectile does not exist (see the class
        /// summary). Resources are NOT re-spent — the original launch already paid; only the spawn
        /// intent is re-recorded with the SAME launch-frozen seed and target, so the eventual
        /// intercept verdict is the one the original launch would have had.
        /// </summary>
        private void ReconcilePendingLaunches()
        {
            m_ReconcilePendingLaunches = false;
            if (m_PendingLaunches.Count == 0)
                return;

            // Seeds of the outbound projectiles that DID survive the save (their payload persists).
            // One-shot post-load pass; SystemAPI.Query keeps it in the system's own update context.
            var inFlight = m_InFlightSeeds;
            inFlight.Clear();
            foreach (var payload in SystemAPI.Query<RefRO<OutboundStrikePayload>>())
            {
                if (payload.ValueRO.IsOutbound)
                    inFlight.Add(payload.ValueRO.Seed);
            }

            bool hasNow = GameTimeSystem.TryGetGameHours(out float nowGameHours);

            for (int i = m_PendingLaunches.Count - 1; i >= 0; i--)
            {
                var pending = m_PendingLaunches[i];
                if (inFlight.Contains(pending.Seed))
                    continue; // projectile made it into the save — the ledger entry retires at arrival

                // TTL guard: an entry far older than any real flight time is an orphan — its
                // projectile disappeared without ever emitting an arrival signal (a deletion path
                // outside the arrival contract). Re-launching it would deliver a strike that was
                // already removed in flight, and would keep re-delivering on every load. Retire it.
                // LaunchedAtHours == 0 marks a pre-TTL save (no stamp) — those keep the old re-drive.
                if (hasNow && pending.LaunchedAtHours > 0f
                    && nowGameHours - pending.LaunchedAtHours > PendingLaunchTtlHours)
                {
                    m_PendingLaunches.RemoveAt(i);
                    Log.Warn($"Pending outbound {pending.Kind} ({pending.Axis}, seed {pending.Seed}) exceeded the {PendingLaunchTtlHours}h re-launch window — retiring as lost in flight");
                    continue;
                }

                if (m_Strike.Launch(pending.Kind, pending.Axis, pending.Damage, pending.Seed, pending.TargetId))
                {
                    Log.Info($"Re-launched outbound {pending.Kind} ({pending.Axis}) lost to the save-time spawn gap (seed {pending.Seed})");
                }
                else if (!m_Strike.CanLaunch)
                {
                    // Producer not ready yet (prefabs still resolving right after load) — keep the
                    // entry and retry next tick until the producer comes up.
                    m_ReconcilePendingLaunches = true;
                }
                else
                {
                    // Producer is up but the launch is impossible (e.g. the drone launcher was
                    // demolished before the save). The paid strike cannot be delivered — retire the
                    // entry honestly instead of retrying forever.
                    m_PendingLaunches.RemoveAt(i);
                    Log.Warn($"Pending outbound {pending.Kind} ({pending.Axis}, seed {pending.Seed}) could not be re-launched after load — strike lost");
                }
            }
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
                || actSingleton.CurrentAct < Act.Crisis)
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

                if (!m_Strike.Launch(kind, evt.Category, evt.ActualDamage, strikeSeed, evt.TargetId))
                {
                    // Launch could not be recorded (prefabs unresolved / Waves closed): refund the
                    // munition and roll back so the player can retry.
                    m_Arsenal.Replenish(kind, 1);
                    m_PlayerSystem.RollbackOperationExecution(evt.ExecutionId, evt.OperationId);
                    Log.Warn($"Launch rolled back for {evt.AttackType} (execution {evt.ExecutionId}): outbound strike producer unavailable");
                    continue;
                }

                // The spawn intent the launch just wrote is transient — record the committed launch
                // in the persisted ledger so a save in the producer→consumer gap can re-drive it
                // (the entry retires when the strike arrives).
                m_PendingLaunches.Add(new PendingOutboundLaunch
                {
                    Kind = kind,
                    Axis = evt.Category,
                    Damage = evt.ActualDamage,
                    Seed = strikeSeed,
                    TargetId = evt.TargetId,
                    LaunchedAtHours = GameTimeSystem.TryGetGameHours(out var launchHours) ? launchHours : 0f
                });

                // Confirm the Shadow Cash deduction and clear the slot. The axis effect is deferred
                // to arrival (OutboundArrivalSignal) — no axis is touched here.
                if (!m_PlayerSystem.CompleteOperationExecution(evt.ExecutionId, evt.OperationId))
                {
                    // The slot/wallet was no longer in the expected state (stale execution intent):
                    // the projectile is already in flight, so the slot cannot go back to Ready —
                    // close it terminally (confirming the deduction if its lock still exists) so it
                    // does not sit in Executing+Claimed forever, unusable and unclearable.
                    m_PlayerSystem.TerminalClearClaimedExecution(evt.ExecutionId, evt.OperationId);
                    Log.Warn($"Launched {evt.AttackType} but could not complete execution intent {evt.ExecutionId} ({evt.OperationId}); slot closed terminally");
                    continue;
                }

                EventBus?.SafePublish(evt, nameof(EnemyOperationEffectSystem));
                // seed= is the launch↔arrival correlator for log-based validation: ExecutionId dies
                // here (the payload carries only the seed), so arrival/intercept lines print the same
                // seed and exactly-once per launch is verifiable by grep.
                Log.Info($"Launched {evt.AttackType} ({kind}, {evt.Category}) → outbound strike, {evt.ActualDamage:F1}% pending at arrival (execution {evt.ExecutionId}, seed {strikeSeed})");
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

            m_MirrorCity ??= FeatureRegistry.Instance.Query<MirrorCitySystem>();
            if (m_MirrorCity == null)
                return; // feature registry still populating — the arrivals wait in the buffer

            if (!m_EnemyStateQuery.TryGetSingletonEntity<EnemyState>(out var stateEntity))
            {
                // No enemy state to apply to — drop the signals (the projectile already terminalized).
                RetirePendingLaunches(signals);
                signals.Clear();
                return;
            }

            var gw = BalanceConfig.Current.GridWarfare;
            float respiteWindowHours = gw.RespiteWindowHours;
            // Game-hour stamp for respite windows: absolute game time (stable across save/load).
            // 0 before GameTime activates — harmless, the window opens at "now" either way.
            float nowHours = GameTimeSystem.TryGetGameHours(out var gh) ? gh : 0f;

            ResolveArrivalsByTargets(signals, stateEntity, gw, respiteWindowHours, nowHours);
        }

        /// <summary>Retire every pending-launch ledger entry whose strike is in this arrival batch.</summary>
        private void RetirePendingLaunches(DynamicBuffer<OutboundArrivalSignal> signals)
        {
            if (m_PendingLaunches.Count == 0)
                return;
            for (int i = 0; i < signals.Length; i++)
            {
                uint seed = signals[i].Seed;
                m_PendingLaunches.RemoveAll(p => p.Seed == seed);
            }
        }

        /// <summary>
        /// Arrival resolution: each strike is resolved against a concrete mirror-city target via
        /// <c>MirrorCitySystem.ApplyStrikeToTarget</c>, which converts axis damage to hp, rolls the
        /// positional AA intercept, applies the per-tier death rules, and recomputes the derived
        /// axes. The payload's <c>TargetId</c> (the player's War Room pick or the Execute-time
        /// auto-selection) is forwarded, so an explicit pick actually lands where the UI promised;
        /// the shared-selection fallback still covers a target that died mid-flight. The respite
        /// window arms against <c>MirrorStrikeResult.AxisFloor</c> — the axis's REAL bottom as the
        /// model computed it, not a second, separately-maintained floor constant.
        /// </summary>
        private void ResolveArrivalsByTargets(
            DynamicBuffer<OutboundArrivalSignal> signals,
            Entity stateEntity,
            GridWarfareConfig gw,
            float respiteWindowHours,
            float nowHours)
        {
            // Refreshed once here; the indexer reads live component data each access, so it sees the
            // EnemyState axes ApplyStrikeToTarget commits between iterations.
            m_EnemyStateLookup.Update(this);

            RetirePendingLaunches(signals);

            for (int i = 0; i < signals.Length; i++)
            {
                var signal = signals[i];

                var res = m_MirrorCity!.ApplyStrikeToTarget(signal.TargetId, signal.Axis, signal.Damage, signal.Seed);
                if (!res.Applied)
                {
                    // City not generated / no target to resolve (should not happen in war — the city
                    // generates before the first launch can fly). Drop the signal honestly rather
                    // than touch the axis.
                    Log.Warn($"Outbound {signal.Axis} strike had no mirror-city target to resolve (seed {signal.Seed})");
                    continue;
                }

                if (res.Intercepted)
                {
                    EventBus?.SafePublish(new OutboundStrikeResolvedEvent(
                        Axis: signal.Axis,
                        Intercepted: true,
                        OldValue: res.OldAxis,
                        NewValue: res.OldAxis,
                        Seed: signal.Seed,
                        TargetId: res.ResolvedTargetId
                    ), nameof(EnemyOperationEffectSystem));
                    Log.Info($"Outbound {signal.Axis} strike intercepted over target {res.ResolvedTargetId} — 0% axis damage (seed {signal.Seed})");
                    continue;
                }

                if (res.NoEffect)
                {
                    // Landed on the axis's invulnerable reserve — the axis is stripped bare. Report
                    // the landing honestly AS a no-effect outcome (the UI shows a neutral "no
                    // effective target" note, not a success toast), publish no axis change and arm
                    // no respite: nothing was newly suppressed.
                    EventBus?.SafePublish(new OutboundStrikeResolvedEvent(
                        Axis: signal.Axis,
                        Intercepted: false,
                        OldValue: res.OldAxis,
                        NewValue: res.NewAxis,
                        Seed: signal.Seed,
                        NoEffect: true,
                        TargetId: res.ResolvedTargetId
                    ), nameof(EnemyOperationEffectSystem));
                    Log.Info($"Outbound {signal.Axis} strike landed on the reserve — axis stripped to its floor, no effective target (seed {signal.Seed})");
                    continue;
                }

                // Suppression → regroup: a strike that pins the axis to its REAL bottom (the model's
                // reserve floor combined with the balance clamp, carried on the result) opens that
                // axis's respite window. ApplyStrikeToTarget already committed the axes (EnemyState
                // written via its owner), so the freshly-updated lookup read here sees the
                // post-strike value; touch only the respite fields.
                if (res.NewAxis <= res.AxisFloor)
                {
                    if (m_EnemyStateLookup.HasComponent(stateEntity))
                    {
                        var es = m_EnemyStateLookup[stateEntity];
                        es.BeginRespite(signal.Axis, nowHours, respiteWindowHours);
                        m_EnemyStateLookup[stateEntity] = es;
                    }
                    Log.Info($"{signal.Axis} axis floored — enemy regroups for {respiteWindowHours:F1}h (waves of this type weakened)");
                }

                EventBus?.SafePublish(new EnemyAxisChangedEvent(
                    OldValue: res.OldAxis,
                    NewValue: res.NewAxis,
                    Axis: signal.Axis,
                    Cause: "outbound strike arrival"
                ), nameof(EnemyOperationEffectSystem));
                EventBus?.SafePublish(new OutboundStrikeResolvedEvent(
                    Axis: signal.Axis,
                    Intercepted: false,
                    OldValue: res.OldAxis,
                    NewValue: res.NewAxis,
                    Seed: signal.Seed,
                    TargetId: res.ResolvedTargetId
                ), nameof(EnemyOperationEffectSystem));
                string killNote;
                if (res.KeyPermanentKill)
                    killNote = " (KEY target destroyed — axis cap cut)";
                else if (res.TargetDestroyed)
                    killNote = " (target destroyed)";
                else
                    killNote = string.Empty;
                Log.Info($"{signal.Axis} axis: {res.OldAxis:F1}% -> {res.NewAxis:F1}% (-{(res.OldAxis - res.NewAxis):F1}% via target {res.ResolvedTargetId}{killNote}, seed {signal.Seed})");
            }

            // Act-objective: when this batch of arrivals leaves ALL three axes suppressed under the
            // objective threshold, the enemy beachhead is broken — pay the Shadow Cash loot ONCE per
            // collapse (terminal latch + per-collapse idempotency key). Re-read the enemy state (its
            // axes were updated in-place by ApplyStrikeToTarget) so the collapse check sees the
            // post-strike values.
            if (m_EnemyStateLookup.HasComponent(stateEntity))
            {
                var enemyState = m_EnemyStateLookup[stateEntity];
                TryRewardObjective(ref enemyState, gw);
                m_EnemyStateLookup[stateEntity] = enemyState;
            }
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
                // ECB is filled synchronously on the main thread (no producer job), so the barrier needs
                // no AddJobHandleForProducer registration.
            }

            // Claim atomically with the queued credit: bump the collapse counter (so a later collapse
            // gets a fresh key) and latch until regen lifts an axis. The income idempotency key is the
            // durable backstop if this claim is lost to a crash before the credit drains.
            enemyState.ObjectiveCollapseCount = collapseId;
            enemyState.ObjectiveClaimed = true;

            EventBus?.SafePublish(new BeachheadCollapsedEvent(collapseId, loot), nameof(EnemyOperationEffectSystem));

            Log.Info($"Enemy beachhead suppressed (collapse #{collapseId}) — looted {loot:N0} Shadow Cash");
        }
    }
}
