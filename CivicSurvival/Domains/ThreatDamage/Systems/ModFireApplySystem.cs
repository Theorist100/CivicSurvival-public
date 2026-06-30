using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.ThreatDamage;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Unity.Entities;

namespace CivicSurvival.Domains.ThreatDamage.Systems
{
    /// <summary>
    /// Consumer half of the mod fire producer/consumer split (mirror of vanilla
    /// <c>IgniteSystem</c>). Runs in ModificationEnd: reads <see cref="ModFireIntent"/>
    /// entities created by fire producers in GameSimulation and performs the
    /// <c>OnFire</c>+<c>BatchesUpdated</c> archetype migration on the vanilla building from
    /// THIS phase — where vanilla's render batch pipeline (<c>RequiredBatchesSystem</c>,
    /// <c>PreCullingSystem</c>, <c>BatchInstanceSystem</c>, all later in MainLoop) expects
    /// it.
    ///
    /// Why the producer cannot do this itself (see <see cref="ModFireIntent"/>): the
    /// structural add from GameSimulation (LateUpdate, end of frame) desyncs with the render
    /// pass and can crash a vanilla Burst batch job on a null chunk pointer; and a
    /// GameSimulation system cannot even create a <c>ModificationEndBarrier</c> buffer —
    /// the barrier's usage gate is closed outside ModificationEnd ("not allowed").
    /// </summary>
    [ActIndependent]
    public partial class ModFireApplySystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ModFireApplySystem");

        private EntityQuery m_IntentQuery;
        private ModificationEndBarrier m_ModificationEndBarrier = null!;
        private EntityStorageInfoLookup m_StorageInfoLookup;
        private ComponentLookup<OnFire> m_OnFireLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private ComponentLookup<Destroyed> m_DestroyedLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private BufferLookup<InstalledUpgrade> m_InstalledUpgradeLookup;

        // Event-backed fire (escalation/spread): the vanilla fire-event-prefab is the
        // EventData + FireData carrier. For an event-backed intent we create an event instance
        // from its EventData.m_Archetype whose PrefabRef points at the prefab, so vanilla
        // FireSimulationSystem drives escalation/spread off OnFire.m_Event. Two prefabs are
        // resolved by m_RandomTargetType: Building (mod building fires) and WildTree (debris
        // forest fires — the tree prefab carries the forest spread params). Cached within-session;
        // re-resolved after load via the StorageInfo guard (the prefab Entity differs across a
        // load) — no separate re-seed pass needed.
        [System.NonSerialized] private Entity m_FirePrefab = Entity.Null;
        [System.NonSerialized] private EntityArchetype m_FireEventArchetype;
        [System.NonSerialized] private bool m_FirePrefabResolved;
        [System.NonSerialized] private Entity m_TreeFirePrefab = Entity.Null;
        [System.NonSerialized] private EntityArchetype m_TreeFireEventArchetype;
        [System.NonSerialized] private bool m_TreeFirePrefabResolved;

        // Diagnostic "burning" counter throttle ([FIRE-SPREAD] burning ...), Debug only.
        private const float BurningLogIntervalSeconds = 5f;
        [System.NonSerialized] private float m_LastBurningLogTime = -100f;

        private readonly System.Collections.Generic.HashSet<long> m_ProcessedTargets = new();
        [System.NonSerialized] private int m_ProcessedTargetsFrame = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_IntentQuery = GetEntityQuery(ComponentType.ReadWrite<ModFireIntent>());
            m_ModificationEndBarrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            m_StorageInfoLookup = GetEntityStorageInfoLookup();
            m_OnFireLookup = GetComponentLookup<OnFire>(true);
            m_DeletedLookup = GetComponentLookup<Deleted>(true);
            m_DestroyedLookup = GetComponentLookup<Destroyed>(true);
            m_BuildingLookup = GetComponentLookup<Building>(true);
            m_InstalledUpgradeLookup = GetBufferLookup<InstalledUpgrade>(true);
            RequireForUpdate(m_IntentQuery);
            Log.Info("Created");
        }

        protected override void OnUpdateImpl()
        {
            if (m_IntentQuery.IsEmpty)
                return;

            m_StorageInfoLookup.Update(this);
            m_OnFireLookup.Update(this);
            m_DeletedLookup.Update(this);
            m_DestroyedLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_InstalledUpgradeLookup.Update(this);

            // Resolve the vanilla fire-event-prefabs once (top-level, not nested in the intent
            // loop). StorageInfo guard re-resolves after a load, where the prefab Entity differs.
            if (!m_FirePrefabResolved || !m_StorageInfoLookup.Exists(m_FirePrefab)
                || !m_TreeFirePrefabResolved || !m_StorageInfoLookup.Exists(m_TreeFirePrefab))
                ResolveFirePrefab();

            int frame = UnityEngine.Time.frameCount;
            if (frame != m_ProcessedTargetsFrame)
            {
                m_ProcessedTargets.Clear();
                m_ProcessedTargetsFrame = frame;
            }

            EntityCommandBuffer ecb = default;
            bool ecbCreated = false;

            foreach (var (intentRef, entity) in
                SystemAPI.Query<RefRW<ModFireIntent>>()
                .WithEntityAccess())
            {
                ref var intent = ref intentRef.ValueRW;
                if (intent.Applied)
                    continue;

                if (!ecbCreated)
                {
                    ecb = m_ModificationEndBarrier.CreateCommandBuffer();
                    ecbCreated = true;
                }

                var target = intent.Target.ToEntity();

                // Cross-frame guard: target may have been destroyed/deleted between the
                // producer (frame N) and this apply (frame N+1).
                if (!m_StorageInfoLookup.Exists(target)
                    || m_DeletedLookup.HasComponent(target)
                    || m_DestroyedLookup.HasComponent(target))
                {
                    intent.Applied = true;
                    ecb.DestroyEntity(entity);
                    continue;
                }

                // Select the vanilla fire prefab/archetype for this target's kind.
                bool isTree = intent.Kind == FireTargetKind.WildTree;
                Entity firePrefab = isTree ? m_TreeFirePrefab : m_FirePrefab;
                EntityArchetype fireArchetype = isTree ? m_TreeFireEventArchetype : m_FireEventArchetype;
                bool prefabResolved = isTree ? m_TreeFirePrefabResolved : m_FirePrefabResolved;

                // Retry guard: if the vanilla fire prefab is not yet resolved, leave the
                // intent unapplied (do NOT set Applied, do NOT destroy) so it is reprocessed
                // next update once the prefab loads — rather than silently dropping the fire.
                if (!prefabResolved || firePrefab == Entity.Null)
                    continue;

                // Same-pass dedup: two intents on one target this frame migrate the
                // archetype only once. The deferred OnFire add is not yet visible via the
                // lookup, so guard here instead of relying on the OnFire check below.
                if (!m_ProcessedTargets.Add(BuildingIdentityKey.Pack(target.Index, target.Version)))
                {
                    intent.Applied = true;
                    ecb.DestroyEntity(entity);
                    continue;
                }

                ApplyFire(ecb, target, firePrefab, fireArchetype, intent.Kind, intent.Intensity, intent.AllowExistingFire);
                intent.Applied = true;
                ecb.DestroyEntity(entity);
            }

            if (ecbCreated)
                m_ModificationEndBarrier.AddJobHandleForProducer(Dependency);

            LogBurningDiagnostics();
        }

        /// <summary>
        /// OnFire + BatchesUpdated application, moved from the old
        /// <c>BuildingDamageHelper</c> structural branch. Runs in ModificationEnd so the
        /// archetype migration is in phase with the render pass. Mirrors
        /// <c>IgniteSystem.IgniteFireJob</c> (intensity merge + installed-upgrade batch
        /// propagation), minus the journal data the mod has no prefab to back. Every mod fire
        /// is event-backed: the caller only reaches here once <c>m_FirePrefab</c> is resolved
        /// (the unresolved case is retried by the OnUpdate loop, not dropped to a dead fire).
        /// </summary>
        private void ApplyFire(EntityCommandBuffer ecb, Entity target, Entity firePrefab,
            EntityArchetype fireArchetype, FireTargetKind kind, float intensity, bool allowExistingFire)
        {
            if (m_OnFireLookup.HasComponent(target))
            {
                if (!allowExistingFire)
                    return;
                var existing = m_OnFireLookup[target];
                if (intensity <= existing.m_Intensity)
                    return;
                // Raise intensity only; preserve existing m_Event + m_RescueRequest so a
                // vanilla fire's real event is not stomped to Null. SetComponent does not
                // change the archetype — phase-safe.
                var merged = existing;
                merged.m_Intensity = intensity;
                ecb.SetComponent(target, merged);
                return;
            }

            // Structural add — archetype migration. Legal here because this system runs in
            // ModificationEnd, where ModificationEndBarrier's usage gate is open and the
            // render batch pipeline consumes the tag later in the same MainLoop.
            //
            // Event-backed mod fire: create a real event instance (Event + PrefabRef +
            // Fire + TargetElement, from EventData.m_Archetype) whose PrefabRef points at
            // the vanilla fire-prefab. Vanilla FireSimulationSystem reads OnFire.m_Event →
            // PrefabRef → FireData and drives escalation/spread (and structural damage). For
            // a WildTree-backed event this is the forest-fire path (spread tree↔tree/building).
            // m_RequestFrame stays 0u — vanilla InitializeRequestFrame fills it for fire-rescue.
            var ev = ecb.CreateEntity(fireArchetype);
            ecb.SetComponent(ev, new Game.Prefabs.PrefabRef(firePrefab));
            ecb.AppendToBuffer<TargetElement>(ev, new TargetElement(target));
            ecb.AddComponent(target, new OnFire(ev, intensity, 0u));
            if (Log.IsDebugEnabled)
                Log.Debug($"[FIRE-SPREAD] ignite {kind} target={target.Index} intensity={intensity:F2} event-backed");

            ecb.AddComponent<BatchesUpdated>(target);
            // Installed-upgrade batch walk is building-only; wild trees have no upgrades.
            if (kind == FireTargetKind.Building)
                AddBatchesUpdatedToNonBuildingUpgrades(ecb, target);
        }

        /// <summary>
        /// Resolves the vanilla fire-event-prefabs (the <c>EventData</c>+<c>FireData</c> carriers
        /// with <c>m_RandomTargetType == Building</c> and <c>== WildTree</c>) and caches their
        /// event-instance archetypes (<c>EventData.m_Archetype</c>: Event + PrefabRef + Fire +
        /// TargetElement + Created + Updated). Iterates via <c>SystemAPI.Query</c> (no sync-point
        /// array copy). Called from OnUpdate before the intent loop, gated so it runs once until a
        /// load invalidates the cached prefab Entity. If a prefab is not yet loaded, leaves it
        /// unresolved and retries next update.
        /// </summary>
        private void ResolveFirePrefab()
        {
            bool building = false;
            bool tree = false;
            foreach (var (fireData, eventData, entity) in
                SystemAPI.Query<RefRO<Game.Prefabs.FireData>, RefRO<Game.Prefabs.EventData>>()
                .WithEntityAccess())
            {
                switch (fireData.ValueRO.m_RandomTargetType)
                {
                    case Game.Prefabs.EventTargetType.Building when !building:
                        m_FirePrefab = entity;
                        m_FireEventArchetype = eventData.ValueRO.m_Archetype;
                        building = true;
                        break;
                    case Game.Prefabs.EventTargetType.WildTree when !tree:
                        m_TreeFirePrefab = entity;
                        m_TreeFireEventArchetype = eventData.ValueRO.m_Archetype;
                        tree = true;
                        break;
                    default:
                        // Other vanilla fire target types (Road/Citizen/…) and already-resolved
                        // kinds — the mod ignites only buildings and wild trees.
                        break;
                }
                if (building && tree)
                    break;
            }

            m_FirePrefabResolved = building;
            m_TreeFirePrefabResolved = tree;

            // Either may be unavailable very early in load — leave it unresolved so the
            // top-level guard retries on the next update rather than dropping the fire.
            if (building || tree)
                Log.Info($"[FIRE-SPREAD] resolved vanilla fire prefabs building={building} tree={tree}");
        }

        /// <summary>
        /// Diagnostic counter (<c>[FIRE-SPREAD] burning ...</c>): how many buildings are on
        /// fire with a non-null <c>m_Event</c> (mod event-backed + vanilla) and their average
        /// intensity. Throttled to ~once per <see cref="BurningLogIntervalSeconds"/>, Debug-only.
        /// This is the only way to observe vanilla escalation/spread from mod code (vanilla does
        /// not log it): rising count with no new ignite lines = spread working; rising
        /// avgIntensity = escalation working.
        /// </summary>
        private void LogBurningDiagnostics()
        {
            if (!Log.IsDebugEnabled)
                return;

            float now = UnityEngine.Time.time;
            if (now - m_LastBurningLogTime < BurningLogIntervalSeconds)
                return;
            m_LastBurningLogTime = now;

            int n = 0;
            float sum = 0f;
            foreach (var fire in SystemAPI.Query<RefRO<OnFire>>())
            {
                if (fire.ValueRO.m_Event == Entity.Null)
                    continue;
                n++;
                sum += fire.ValueRO.m_Intensity;
            }

            float avg = n > 0 ? sum / n : 0f;
            Log.Debug($"[FIRE-SPREAD] burning event-backed={n} avgIntensity={avg:F1}");
        }

        private void AddBatchesUpdatedToNonBuildingUpgrades(EntityCommandBuffer ecb, Entity building)
        {
            if (!m_InstalledUpgradeLookup.TryGetBuffer(building, out var upgrades))
                return;
            for (int i = 0; i < upgrades.Length; i++)
            {
                var upgrade = upgrades[i].m_Upgrade;
                if (upgrade == Entity.Null)
                    continue;
                // Sub-buildings are independent batch sources; only genuine sub-upgrades
                // need the rebuild flag (mirror of IgniteSystem upgrade walk).
                if (m_BuildingLookup.HasComponent(upgrade))
                    continue;
                ecb.AddComponent<BatchesUpdated>(upgrade);
            }
        }
    }
}
