using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Components.Domain.AirDefense;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Constants;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Systems.Effects;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Init for all mod .cok prefabs.
    ///
    /// Mod-wide bootstrap: walks PrefabSystem.m_Prefabs, name-matches our assets against a
    /// name-to-handler dispatch table built in OnCreate, installs derived components
    /// (AirDefensePrefabData marker, UpdateFrameData, OGD.MinLod fix), caches PrefabBase +
    /// Entity for runtime consumers.
    ///
    /// Lifecycle: IInitializable.OnInitialize runs in PostLoadValidationSystem's
    /// post-load pass — after validators / singleton restore / building-ref rebind,
    /// before MarkGameplayReady. Vanilla's own LoadPrefabs(AssetDatabase.global) is done by
    /// then, but mod .cok register on the asynchronous ParadoxMods path
    /// (GameManager.OnEntryIsInActivePlaysetChanged, batched 128 with WaitXFrames between
    /// AddPrefab batches) and can land AFTER this pass. On a fast local load they are all
    /// present by post-load and resolve in a single OnInitialize scan; on a slow first-boot
    /// or server-delivered load the tail arrives over the following seconds, so OnUpdateImpl
    /// keeps scanning the newly-appended prefabs until the core models resolve.
    ///
    /// The OnUpdateImpl scan is INCREMENTAL: PrefabSystem.AddPrefab appends, so only the
    /// tail [m_LastScannedIndex..Count) is examined each pass — never a full-registry copy.
    /// (A prior revision did prefabs.ToArray() + full walk every frame the list grew; on a
    /// multi-second server load that copied the entire 8000+ registry every frame and froze
    /// the game to ~5 fps. Local loads finish in ~150 ms so it never showed up there.) Once
    /// all core models are resolved and the exhaust VFX is bound the system disables its own
    /// tick (Enabled = false); OnInitialize re-arms it on the next load. A genuinely-absent
    /// asset (mod .cok failed to load) is finalized at GameManager.onGameLoadingComplete —
    /// the real "population done" signal — so the system stops ticking instead of polling
    /// for the rest of the session. The anchor is the load-complete event, not a guessed
    /// frame budget, so a slow server load is never cut short.
    ///
    /// Adding a new .cok: write a Setup method, register it in OnCreate via
    /// m_Setup.Add. No new files, no new registrations elsewhere.
    ///
    /// This class knows mod asset names and AA balance values; that makes a Core file
    /// aware of gameplay assets. Accepted as a bootstrap compromise — domain-side
    /// ownership would require a registry abstraction, overkill for a handful of assets.
    ///
    /// Also owns the deferred exhaust-VFX bind: OnUpdateImpl polls until both the
    /// Rocket prefab and EffectCacheSystem are ready, then adds a one-element
    /// Game.Prefabs.Effect buffer (FireMovingMediumVFX) to the Rocket prefab entity.
    /// </summary>
    [ActIndependent]
    public partial class CivicPrefabInitSystem : CivicSystemBase, IInitializable
    {
        private static readonly LogContext Log = new("CivicPrefabInitSystem");

        private PrefabSystem m_PrefabSystem = null!;
        private EffectCacheSystem m_EffectCache = null!;

        // Triggers CS2's own native mod-load for our late-delivered .cok post-load (the path that
        // textures them correctly). See ParadoxNativeLoader.
        private ParadoxNativeLoader? m_ParadoxNativeLoader;

        // Name → setup handler dispatch table. Built once in OnCreate via collection
        // initializer, walked once per OnInitialize. Avoids a string switch (CIVIC135)
        // and a discard-arm enum switch (CIVIC019); each asset binds directly to its
        // handler. Stored as IReadOnlyDictionary so the analyzer sees the field as
        // immutable post-init (no Add/Remove/Clear callsites).
        private IReadOnlyDictionary<string, Action<PrefabBase>> m_Setup = null!;

        // Consumer-visible cache. The structural prefab setup (component marking) runs ONLY in
        // OnInitialize and this system's OnUpdateImpl, never inside a consumer's update or a
        // synchronous event dispatch — so a consumer read can never trigger a structural change.
        // The entity getters are lazy and self-healing (ResolveCachedEntity): they memoize the
        // entity and re-fetch it from the stable PrefabBase ref when the cache is cleared (every
        // load) or went stale — a pure read (dictionary lookup + EntityManager.Exists), not
        // structural setup. Mod .cok land asynchronously and frame-batched on the vanilla
        // ParadoxMods path, so a single post-load scan can run before they arrive — OnUpdateImpl
        // re-scans the appended tail as m_Prefabs grows, and a null read just makes the consumer
        // retry next wave/frame. PrefabBase managed ref is stable per PrefabSystem decompile
        // (m_Prefabs/m_Entities are built once in OnCreate and survive every Deserialize).
        private PrefabBase? m_AttackDronePrefab;
        private Entity m_AttackDroneEntity = Entity.Null;
        private PrefabBase? m_RocketPrefab;
        private Entity m_RocketEntity = Entity.Null;
        private PrefabBase? m_InterceptorPrefab;
        private Entity m_InterceptorEntity = Entity.Null;

        public PrefabBase? AttackDronePrefab => m_AttackDronePrefab;
        public Entity AttackDroneEntity => ResolveCachedEntity(ref m_AttackDroneEntity, m_AttackDronePrefab, "AttackDrone");
        public PrefabBase? RocketPrefab => m_RocketPrefab;
        public Entity RocketEntity => ResolveCachedEntity(ref m_RocketEntity, m_RocketPrefab, "Rocket");
        public PrefabBase? InterceptorPrefab => m_InterceptorPrefab;
        public Entity InterceptorEntity => ResolveCachedEntity(ref m_InterceptorEntity, m_InterceptorPrefab, "Interceptor");

        // Lazy, self-healing entity resolve. The managed PrefabBase ref is the source of truth, not
        // the cached Entity: PrefabSystem.m_Prefabs/m_Entities are built once in OnCreate and never
        // cleared (decompile — Deserialize touches only obsolete-ID maps; ClearSystem excludes
        // entities with PrefabData), so a prefab and its entity survive every in-game reload. A
        // cleared cache field (OnInitialize zeroes the Entity each load) or an entity recreated by an
        // asset-editor hot-reload (m_Entities updated to the fresh one) is recovered here on read
        // instead of latching Null for the rest of the session — the regression where a wave mid-
        // session reported the AttackDrone "missing" while the .cok was present in m_Prefabs. Returns
        // Null only when the prefab was never resolved (genuinely absent .cok). No structural change:
        // a managed dictionary lookup + an EntityManager.Exists read, safe from a consumer's getter.
        private Entity ResolveCachedEntity(ref Entity cached, PrefabBase? prefab, string name)
        {
            if (cached != Entity.Null && EntityManager.Exists(cached))
                return cached;

            // The cache is empty (cleared on load) or went stale (hot-reload recreated the entity).
            bool wasStale = cached != Entity.Null;
            cached = prefab != null && m_PrefabSystem.TryGetEntity(prefab, out var entity) ? entity : Entity.Null;

            // Prod diagnostic (Info, fires once per recovery — the next read is a cache hit and is
            // silent). The normal post-load scan already fills the cache, so this never logs on a
            // healthy load; it only speaks when the self-heal actually mattered — a cleared cache
            // after an in-game reload, or a stale entity after an asset hot-reload — i.e. exactly
            // the "prefab reported missing mid-session" regression class. A player's
            // CivicSurvival.log then shows the recovery instead of a silent skipped wave.
            if (cached != Entity.Null)
                Log.Info($"{name} entity self-healed on read ({(wasStale ? "stale entity refetched after hot-reload" : "cache empty after load")}) → entity={cached.Index}:{cached.Version}");

            return cached;
        }

        // Consumer gate (intro strike / wave scheduling): true once the core threat prefabs are
        // resolved OR load-complete has finalized them genuinely absent. A consumer holds the
        // first wave until this is true so it never fires into an unresolved prefab during the
        // async ParadoxMods drain, yet is never blocked forever — FinalizeMissing settles
        // m_ResolvePending at onGameLoadingComplete regardless. When this is true the consumer
        // reads AttackDroneEntity to distinguish ready (non-null) from genuinely-absent (null).
        public bool CoreThreatPrefabsSettled => !m_ResolvePending;

        // Stronger than Settled: the AttackDrone prefab is actually resolved (entity present), so a
        // wave can spawn renderable threats. Settled-but-not-Ready = the model is genuinely absent
        // this session (FinalizeMissing decided it at load-complete). The intro gate fires on Ready
        // and stops waiting on Settled: ready → strike; settled-but-not-ready → skip the empty
        // strike and let the ModLoadFailure modal speak.
        public bool CoreThreatPrefabsReady => AttackDroneEntity != Entity.Null;
        // AA marker is consumed via HasComponent<AirDefensePrefabData> on the prefab
        // entity (AAInstallationDetectorSystem / AAPlacementCommandSystem). No cached
        // accessor field for AA prefabs here.

        // Set in handlers, checked after the walk to emit a single missing-asset
        // error per asset.
        // AA prefab types resolved this load. Placement-capable types only (Bofors/Gepard/Patriot —
        // each a .cok prefab); HeritageBofors is a placement MODE over the Bofors prefab, never set
        // here. Reset in OnInitialize.
        private readonly HashSet<AAType> m_AaFound = new();

        // Placement-capable AA: prefab .cok name → type. Drives the setup dispatch and the
        // missing-asset warnings. HeritageBofors is intentionally absent (no own prefab).
        private static readonly (AAType Type, string CokName)[] s_PlaceableAA =
        {
            (AAType.Bofors40mm, "AA_40mm_Bofors"),
            (AAType.Gepard, "Gepard"),
            (AAType.PatriotSAM, "MIM104_SAM"),
        };

        // Cached live reference to PrefabSystem.m_Prefabs. The List instance is allocated
        // once in PrefabSystem.OnCreate and reused for the session, so reflecting it every
        // scan is pure waste — resolve once, then read .Count / indexer off the cached ref.
        // Re-resolved (set null) in OnInitialize because a hot-reload can repopulate the
        // list with fresh PrefabBase instances.
        private System.Collections.Generic.List<PrefabBase>? m_PrefabsRef;

        // Wall-clock baseline (Restart in OnInitialize) for the one-shot "resolved in Xms
        // after post-load" diagnostic — tester logs show the real server-load timing
        // without subtracting line timestamps by hand. Real-time (incl. any pause) is the
        // intended measure: it is exactly how long the load took.
        private readonly Stopwatch m_InitClock = new();

        // One-shot latch for the exhaust-VFX bind. Reset in OnInitialize (the only
        // point where RocketEntity is (re-)resolved, incl. hot-reload), set once the
        // buffer is in place OR abandoned (load complete, effect never resolved) — so
        // OnUpdateImpl needs no per-frame Exists/HasBuffer polling on the prefab entity.
#pragma warning disable CIVIC150 // Transient by design: reset + re-derived in OnInitialize on every load; persisting it would wrongly skip the bind on a fresh prefab entity
        private bool m_RocketExhaustBound;
        private bool m_InterceptorExhaustBound;
        // SPIKE (Phase 3.0b §B.0): one-shot latch for the hidden-factory upgrade prefab
        // registration. Reset in OnInitialize so a fresh load re-registers against the
        // newly-recreated vanilla building prefabs. Set once registered OR once we know we
        // cannot register this session (no eligible vanilla building prefab present).
        private bool m_HiddenFactoryUpgradeSettled;
#pragma warning restore CIVIC150

        // Demand-driven resolve state. m_ResolvePending stays true until all core prefabs
        // are found (or finalized absent at load-complete); m_LastScannedIndex is the tail
        // cursor for the incremental scan (skip the prefabs already examined). m_LoadComplete
        // latches the GameManager.onGameLoadingComplete backstop. All transient — reset in
        // OnInitialize on every load.
#pragma warning disable CIVIC150 // Transient by design: reset in OnInitialize on every load
        private bool m_ResolvePending;
#pragma warning disable CIVIC241 // False positive: per-load tail cursor, intentionally reset to 0 in OnInitialize for a full re-scan (the prefab cache is cleared and PrefabBase refs are fresh on hot-reload). The Setup* handlers it gates are idempotent (HasComponent/null-guarded early-return), so a re-scan never double-applies. Serializing it would WRONGLY skip re-resolution on load.
        private int m_LastScannedIndex;
#pragma warning restore CIVIC241
        private bool m_LoadComplete;
#pragma warning restore CIVIC150

        // Coarse last-resort cap (real seconds, measured by m_InitClock from OnInitialize). Backstop for
        // when the native loader never reports settled — e.g. ParadoxNativeLoader aborted because this is
        // not a Paradox-delivered mod (a local dev deploy), so m_ParadoxNativeLoader.LoadAttemptSettled
        // never latches, yet the core .cok are genuinely absent. The normal genuine-miss path is decided
        // the moment the native load settles (OnUpdateImpl below); this only fires if that signal never
        // arrives. Generous on purpose — it must sit well above any real load.
        private const double FINALIZE_HARD_CAP_SECONDS = 45.0;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_EffectCache = World.GetOrCreateSystemManaged<EffectCacheSystem>();

            // Event-driven re-validation. Vanilla raises onContentAvailabilityChanged whenever
            // content/DLC/mod availability flips (PrefabSystem.UpdateAvailable / IsAvailable,
            // decompile PrefabSystem.cs:465-518). A core .cok that AddPrefab rejected earlier via
            // the IsAvailable gate — it was momentarily marked unavailable during the async
            // ParadoxMods drain — becomes addable on this signal, but the resolve tick has already
            // disabled itself after the first pass and would never pick it up. Subscribing lets a
            // late availability change re-arm the tick instead of stranding the prefab for the
            // session. Unsubscribed in OnDestroy.
            m_PrefabSystem.onContentAvailabilityChanged += OnContentAvailabilityChanged;

            m_Setup = new Dictionary<string, Action<PrefabBase>>
            {
                ["AA_40mm_Bofors"] = p => SetupAA(p, AAType.Bofors40mm),
                ["Gepard"] = p => SetupAA(p, AAType.Gepard),
                ["MIM104_SAM"] = p => SetupAA(p, AAType.PatriotSAM),
                ["AttackDrone"] = SetupAttackDrone,
                ["Rocket"] = SetupRocket,
                ["AIM120"] = SetupInterceptor,
            };

            // Late-delivered .cok are loaded by triggering CS2's own native mod-load post-load — the path
            // that registers their VT and textures them. ParadoxNativeLoader registers its post-load poll
            // here; ResolvePrefabs picks the prefabs up once the native trigger lands them.
            m_ParadoxNativeLoader = new ParadoxNativeLoader(World);
            m_ParadoxNativeLoader.Activate();

            Log.Info("Created (native post-load .cok delivery)");
        }

        // Backstop anchor: the real "playset/prefab population done" moment. GameSystemBase
        // already subscribes GameManager.onGameLoadingComplete and dispatches to this virtual
        // (in a try/catch), so no manual wiring is needed — just override. Only the in-city
        // load carries our .cok; editor/menu transitions don't. Sets a latch only; the
        // finalize/disable is owned by OnUpdateImpl. Fires regardless of Enabled, which is
        // fine: a disabled-after-init system just re-latches harmlessly.
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game)
                m_LoadComplete = true;
        }

        protected override void OnDestroy()
        {
            // m_PrefabSystem outlives this system (created in OnCreate, owned by the World), so the
            // event would keep a dangling handler to a destroyed system without this unsubscribe.
            if (m_PrefabSystem != null)
                m_PrefabSystem.onContentAvailabilityChanged -= OnContentAvailabilityChanged;
            base.OnDestroy();
        }

        // Fired by vanilla when content/DLC/mod availability flips. The expensive work stays in
        // OnUpdateImpl — this callback only flips flags and re-arms the tick. It must NOT touch
        // EntityManager: vanilla can raise it from any main-thread context (UpdateAvailable /
        // IsAvailable), not an owner-controlled structural point, so doing a structural Add here
        // would be an out-of-phase change. Re-resolution itself is O(our set) point lookups in
        // ResolvePrefabs, not a registry walk, so re-arming on this signal carries no scan cost.
        private void OnContentAvailabilityChanged()
        {
            // Re-open the core gate only if a core model is still unresolved. FinalizeMissing may
            // have settled m_ResolvePending=false as a "genuine miss", but a real availability
            // change is exactly the signal that the miss might no longer hold (the IsAvailable gate
            // that rejected our AddPrefab just flipped). AA stay optional — the unconditional re-arm
            // below already lets a late AA batch be picked up via EnsureResolved.
            if (m_AttackDronePrefab == null || m_RocketPrefab == null)
                m_ResolvePending = true;

            // Wake the tick (a prior load disabled it once init settled). OnUpdateImpl re-resolves
            // cheaply and disables itself again once everything is settled, so a spurious event with
            // nothing to fix costs a single O(tail-no-op) pass, never a registry walk.
            Enabled = true;
        }

        public void OnInitialize()
        {
            // Keep the managed PrefabBase refs across loads: PrefabSystem.m_Prefabs is built once
            // in OnCreate and never cleared (decompile — Deserialize touches only obsolete-ID maps),
            // so our prefab instances are the SAME for the whole process. Once resolved, a ref stays
            // valid every reload; clearing it here is what stranded a present prefab as "missing"
            // when a later scan failed to re-find it. Only the cached Entity is dropped — the lazy
            // getter (ResolveCachedEntity) re-fetches it from the stable ref on next read, so a
            // reload always recovers a live entity rather than latching Null for the session.
            m_AttackDroneEntity = Entity.Null;
            m_RocketEntity = Entity.Null;
            m_InterceptorEntity = Entity.Null;
            m_RocketExhaustBound = false;
            m_InterceptorExhaustBound = false;
            m_HiddenFactoryUpgradeSettled = false;
            m_AaFound.Clear();
            m_ResolvePending = true;
            // Do NOT reset m_LoadComplete here. OnGameLoadingComplete fires synchronously at
            // GameManager:1104 (frame N), but this OnInitialize runs from PostLoadValidationSystem's
            // post-load pass which is deferred (m_SkipFrames=1) to a LATER frame (N+2) — so the
            // load-complete latch for THIS load has ALREADY been set true before we get here.
            // Clearing it would clobber that signal with nothing to re-set it, and FinalizeMissing
            // (hence the ModLoadFailure modal) would never run on a genuine-missing asset.
            // Re-reflect the current list (hot-reload may swap it) and rescan from the
            // head so the cleared cache is re-populated by the first ResolvePrefabs pass.
            m_PrefabsRef = null;
            m_LastScannedIndex = 0;
            m_InitClock.Restart();
            // Re-arm the tick: a previous load disabled the system once init finished.
            Enabled = true;

            // Prod diagnostic (Info, once per load): managed PrefabBase refs surviving this load.
            // On an in-game reload these should stay non-null — PrefabSystem keeps m_Prefabs/
            // m_Entities across Deserialize, so a ref resolved on a prior load is still valid. If a
            // player's CivicSurvival.log shows one reset to False on a reload, it was wrongly
            // cleared and threats will mis-resolve — the exact failure this re-arm path guards.
            Log.Info($"Prefab init re-armed (loadComplete={m_LoadComplete}) — surviving refs: AttackDrone={m_AttackDronePrefab != null}, Rocket={m_RocketPrefab != null}, Interceptor={m_InterceptorPrefab != null}.");

            // Resolve now if our .cok are already in m_Prefabs (the common case). If a slow
            // ParadoxMods / server-delivered load deferred them past this post-load pass,
            // they are NOT permanently missing: OnUpdateImpl re-scans the appended tail as
            // m_Prefabs grows, and the load-complete backstop finalizes anything truly
            // absent. The "truly missing" verdict is therefore tied to the actual
            // population-done signal, not asserted here at a fixed instant that races load.
            ResolvePrefabs();

            if (m_ResolvePending)
                Log.Info("Mod prefabs not all present at post-load scan — asset load still draining, resolving on demand.");
        }

        // Re-scan the newly-appended tail of PrefabSystem.m_Prefabs for any mod .cok not yet
        // cached. Called only from this system's OnUpdateImpl (owner-controlled point for the
        // structural setup), so a late-registered asset is picked up within a frame of landing.
        // Keeps scanning while the core gate is still pending OR an optional AA is still missing:
        // the AA .cok can land in a LATER ParadoxMods batch than the core models, so the scan must
        // not stop on core alone (that stranded a late AA without its AirDefensePrefabData marker
        // for the session). The Setup* handlers are idempotent (cached / m_AaFound-guarded
        // early-return) and the tail scan is a cheap int-compare no-op on a stable list, so the
        // extra passes re-apply nothing. Gated on m_PrefabsRef != null so a reflection failure
        // (handled once in ResolvePrefabs) does not re-log every frame.
        private void EnsureResolved()
        {
            if (m_ResolvePending || (!AllAaResolved && m_PrefabsRef != null))
                ResolvePrefabs();
        }

        private void ResolvePrefabs()
        {
            // Resolve the live m_Prefabs reference once — same List instance all session.
            if (m_PrefabsRef == null)
            {
                if (!VanillaReflectionRegistry.TryGetPrefabSystemPrefabs(m_PrefabSystem, out var prefabs))
                {
                    Log.Error("PrefabSystem.m_Prefabs reflection unavailable — mod prefabs cannot be initialized");
                    m_ResolvePending = false; // unrecoverable via re-scan
                    return;
                }
                m_PrefabsRef = prefabs;
            }

            int count = m_PrefabsRef.Count;

            // Removal (playset deactivation) shrank the list — tail indices shifted, so the
            // cursor is no longer valid; rescan from the head. Rare (load-time is append-only).
            if (count < m_LastScannedIndex)
                m_LastScannedIndex = 0;

            // PERF-LOCK: incremental tail scan — only [m_LastScannedIndex..count) is new (AddPrefab
            // appends), and a stable count is a cheap int-compare no-op. Never restore a
            // prefabs.ToArray()/full-registry walk per frame: a prior revision did and copied the
            // whole 8000+ registry every frame the list grew on a multi-second server load → ~5 fps.
            // This getter is polled every frame the tick is alive (now also while late AA drain), so
            // the per-pass cost MUST stay O(new tail), not O(registry).
            if (count == m_LastScannedIndex)
                return;

            for (int i = m_LastScannedIndex; i < count; i++)
            {
                var prefab = m_PrefabsRef[i];
                if (prefab?.name == null)
                    continue;

                if (m_Setup.TryGetValue(prefab.name, out var setup))
                    setup(prefab); // Setup* early-return once their target is cached
            }
            m_LastScannedIndex = count;

            // Core gameplay gate: m_ResolvePending clears as soon as the CORE threat models
            // (drones + ballistics) are present — that is what unblocks intro/wave scheduling.
            // AA models are deliberately NOT part of this gate: they are optional (a genuinely
            // absent AA .cok — Patriot has shipped missing before — must not keep the core gate
            // pending for the session), and they can register in a LATER ParadoxMods batch than
            // core. The tail keeps being scanned for late AA via EnsureResolved until the tick
            // disables; the final AA roster is logged once at that point (LogAaResolution), so a
            // still-draining AA is never prematurely reported "missing" here.
            if (m_ResolvePending && m_AttackDronePrefab != null && m_RocketPrefab != null)
            {
                m_ResolvePending = false;
                Log.Info($"Core mod prefabs resolved in {m_InitClock.ElapsedMilliseconds}ms after post-load — AttackDrone=true, Rocket=true");
            }
        }

#pragma warning disable CIVIC006 // One-time derived prefab marker during bootstrap (single shared prefab entity, HasComponent-guarded idempotent)
        // Mark a placement-capable AA prefab (Bofors / Gepard / Patriot) with its derived
        // AirDefensePrefabData. All per-type values come from the single AAParams.ForType view, so
        // adding a type is one map entry above, not a new copy here. IsHeritage ("can use heritage
        // credits") is a placement property of the Bofors prefab only — set directly, it is not a
        // balance parameter.
        private void SetupAA(PrefabBase prefab, AAType type)
        {
            if (m_AaFound.Contains(type))
                return; // already resolved this load

            if (!m_PrefabSystem.TryGetEntity(prefab, out var entity))
            {
                Log.Warn($"PrefabSystem.TryGetEntity failed for {prefab.name}");
                return;
            }

            m_AaFound.Add(type);

            if (EntityManager.HasComponent<AirDefensePrefabData>(entity))
                return; // idempotent: marker already present

            var p = AAParams.ForType(BalanceConfig.Current, type);
            EntityManager.AddComponentData(entity, new AirDefensePrefabData
            {
                Type = p.Type,
                Range = p.Range,
                InterceptChanceShahed = p.InterceptChanceShahed,
                InterceptChanceBallistic = p.InterceptChanceBallistic,
                MaxAmmo = p.MaxAmmo,
                CooldownDuration = p.CooldownDuration,
                CrewRequired = p.CrewRequired,
                Price = p.Price,
                IsHeritage = type == AAType.Bofors40mm
            });
            Log.Info($"Marked prefab: {prefab.name} as {type}");
        }

        private void SetupAttackDrone(PrefabBase prefab)
        {
            if (m_AttackDronePrefab != null)
                return; // already resolved this load

            if (!m_PrefabSystem.TryGetEntity(prefab, out var entity))
            {
                Log.Warn("PrefabSystem.TryGetEntity failed for AttackDrone");
                return;
            }

            m_AttackDronePrefab = prefab;
            m_AttackDroneEntity = entity;

            // UpdateFrameData pins TMS sub-frame on prefab so instances inherit it
            // (vanilla AircraftPrefab pattern). Without this, UpdateGroupSystem load-
            // balances and scatters UF across drone instances.
            if (!EntityManager.HasComponent<UpdateFrameData>(entity))
                EntityManager.AddComponentData(entity, new UpdateFrameData(ThreatConstants.TMS_SUB_FRAME));

            // OGD.MinLod=0 so small drones (~2.4x3.5m) pass LOD at any camera distance
            // and participate in Bezier interpolation. Without this, vanilla
            // CalculateLodLimit gives m_MinLod≈125 → CullingInfo.m_MinLod stays high →
            // InterpolatedTransform skipped → jitter at 2x/3x speed.
            if (EntityManager.HasComponent<ObjectGeometryData>(entity))
            {
                var ogd = EntityManager.GetComponentData<ObjectGeometryData>(entity);
                if (ogd.m_MinLod != 0)
                {
                    int oldMinLod = ogd.m_MinLod;
                    ogd.m_MinLod = 0;
                    EntityManager.SetComponentData(entity, ogd);
                    Log.Info($"AttackDrone OGD fixed: m_MinLod {oldMinLod} → 0");
                }
            }

            Log.Info($"AttackDrone prefab resolved: entity={entity.Index}:{entity.Version}");
        }

        private void SetupRocket(PrefabBase prefab)
        {
            if (m_RocketPrefab != null)
                return; // already resolved this load

            if (!m_PrefabSystem.TryGetEntity(prefab, out var entity))
            {
                Log.Warn("PrefabSystem.TryGetEntity failed for Rocket");
                return;
            }

            m_RocketPrefab = prefab;
            m_RocketEntity = entity;

            // Same derived setup as AttackDrone: ballistic instances spawn with this
            // prefab via PrefabRef and tick in the TMS sub-frame.
            if (!EntityManager.HasComponent<UpdateFrameData>(entity))
                EntityManager.AddComponentData(entity, new UpdateFrameData(ThreatConstants.TMS_SUB_FRAME));

            // MinLod=0: the 7.3m missile must pass LOD at any camera distance to stay
            // in Bezier interpolation (same jitter fix as AttackDrone).
            if (EntityManager.HasComponent<ObjectGeometryData>(entity))
            {
                var ogd = EntityManager.GetComponentData<ObjectGeometryData>(entity);
                if (ogd.m_MinLod != 0)
                {
                    ogd.m_MinLod = 0;
                    EntityManager.SetComponentData(entity, ogd);
                    Log.Info("Rocket OGD fixed: m_MinLod → 0");
                }
            }

            Log.Info($"Rocket prefab resolved: entity={entity.Index}:{entity.Version}");
        }

        // AIM-120 interceptor missile — visible Patriot SAM round. Same derived setup as Rocket:
        // ticks in the TMS sub-frame and passes LOD at any camera distance (Bezier interpolation).
        // Absent .cok (not yet imported) just leaves InterceptorEntity Null → InterceptorSpawnSystem
        // skips the missile gracefully; tracers and the intercept formula are unaffected.
        private void SetupInterceptor(PrefabBase prefab)
        {
            if (m_InterceptorPrefab != null)
                return; // already resolved this load

            if (!m_PrefabSystem.TryGetEntity(prefab, out var entity))
            {
                Log.Warn("PrefabSystem.TryGetEntity failed for AIM120");
                return;
            }

            m_InterceptorPrefab = prefab;
            m_InterceptorEntity = entity;

            if (!EntityManager.HasComponent<UpdateFrameData>(entity))
                EntityManager.AddComponentData(entity, new UpdateFrameData(ThreatConstants.TMS_SUB_FRAME));

            if (EntityManager.HasComponent<ObjectGeometryData>(entity))
            {
                var ogd = EntityManager.GetComponentData<ObjectGeometryData>(entity);
                if (ogd.m_MinLod != 0)
                {
                    ogd.m_MinLod = 0;
                    EntityManager.SetComponentData(entity, ogd);
                    Log.Info("AIM120 OGD fixed: m_MinLod → 0");
                }
            }

            Log.Info($"Interceptor (AIM120) prefab resolved: entity={entity.Index}:{entity.Version}");
        }
#pragma warning restore CIVIC006

        // Every placement-capable AA prefab (Bofors / Gepard / Patriot) has been marked this load.
        // Holds the tick (InitializationComplete) so a late AA batch is still picked up instead of
        // the system disabling on the core models alone.
        private bool AllAaResolved => m_AaFound.Count == s_PlaceableAA.Length;

        // True once all async init is done: core models resolved (or finalized absent) and
        // the exhaust bind is settled (bound, abandoned, or no Rocket to bind to).
        private bool InitializationComplete =>
            !m_ResolvePending
            && (m_RocketExhaustBound || m_RocketEntity == Entity.Null)
            && (m_InterceptorExhaustBound || m_InterceptorEntity == Entity.Null)
            // SPIKE (Phase 3.0b §B.0): hold the tick until the hidden-factory upgrade prefab
            // is registered (or known unregisterable this session), so a mid-drain first tick
            // that hasn't seen a vanilla building prefab yet does not disable the system before
            // it can register. Settles within a tick once one building prefab is present.
            && m_HiddenFactoryUpgradeSettled
            // Hold the tick for a late AA batch: optional AA .cok can register after the core
            // models (separate ParadoxMods batch). Keep scanning the tail until every AA is found
            // OR the game signals load-complete (population done → a still-absent AA is genuinely
            // missing, not late). Never blocks forever — m_LoadComplete always latches at
            // onGameLoadingComplete; without this the tick disabled on core alone and a late AA
            // never got its marker for the session.
            && (AllAaResolved || m_LoadComplete);

        // Per-frame while enabled: pick up late .cok, bind exhaust, then disable the tick
        // once everything is settled. EnsureResolved is a cheap int-compare no-op once the
        // tail cursor catches the list; TryBindRocketExhaust early-returns once latched.
        protected override void OnUpdateImpl()
        {
            // Pick up mod .cok that registered after the post-load scan (slow ParadoxMods /
            // server-delivered load). Cheap once resolved — tail-cursor no-op.
            EnsureResolved();
            TryBindRocketExhaust();
            TryBindInterceptorExhaust();
            TryRegisterHiddenFactoryUpgrade();

            // Backstop: once the game reports load-complete AND our core models are still unresolved,
            // decide a genuine miss — but only once the LOAD that delivers them is actually finished.
            // The .cok come from ParadoxNativeLoader's native load, which runs POST-load and async over
            // frames; EnsureResolved above picks each one up the moment it lands. So a still-missing core
            // .cok is a real miss only when:
            //   - reflection into m_Prefabs failed (unrecoverable — finalize now, fail-loud), or
            //   - the native loader reports its attempt settled (AddPrefab batches done) and the prefabs
            //     are STILL absent — the precise "load finished, models genuinely failed" signal, or
            //   - the coarse hard-cap fired (backstop for when the native loader never settles, e.g. it
            //     aborted because this is a local dev deploy, not a Paradox mod).
            if (m_LoadComplete && !InitializationComplete)
            {
                bool nativeSettled = m_ParadoxNativeLoader?.LoadAttemptSettled == true;
                bool capExceeded = m_InitClock.Elapsed.TotalSeconds >= FINALIZE_HARD_CAP_SECONDS;
                if (m_PrefabsRef == null || nativeSettled || capExceeded)
                    FinalizeMissing();
            }

            if (InitializationComplete)
            {
                LogAaResolution();
                Enabled = false;
                Log.Info($"Mod prefab init settled in {m_InitClock.ElapsedMilliseconds}ms after post-load — tick disabled");
            }
        }

        // One-shot AA roster log at tick-disable. By here the tail scan is done for this session
        // (every AA found, or load-complete proved the rest genuinely absent), so a per-AA Warn is
        // a real verdict, not a mid-drain false positive. Warn (not Error): an absent AA is a
        // handled degradation (that placement type is disabled this session), never a Backtrace
        // per-session crash snapshot. Runs once — the tick disables right after.
        private void LogAaResolution()
        {
            foreach (var (type, cok) in s_PlaceableAA)
            {
                if (!m_AaFound.Contains(type))
                    Log.Warn($"AA prefab missing from PrefabSystem.m_Prefabs: {cok} (expected Assets/Models/{cok}.cok). {type} placement disabled this session.");
            }
            Log.Info($"AA prefabs resolved — Bofors={m_AaFound.Contains(AAType.Bofors40mm)}, Gepard={m_AaFound.Contains(AAType.Gepard)}, Patriot={m_AaFound.Contains(AAType.PatriotSAM)}");
        }

        // Called once the native load has settled (ParadoxNativeLoader.LoadAttemptSettled, or the hard-cap
        // backstop fired, or m_Prefabs reflection failed) with core models or the exhaust effect still
        // unresolved: the assets genuinely failed to load this session. Mark them done (with a single Warn
        // each) so InitializationComplete latches and the system disables its tick.
        private void FinalizeMissing()
        {
            if (m_ResolvePending)
            {
                m_ResolvePending = false;

                // Miss decided FOR NOW — not necessarily for the session: OnContentAvailabilityChanged
                // re-opens the gate and re-arms the tick if availability later flips, so a .cok the
                // IsAvailable gate rejected can still resolve after this fires. Disk-check the core
                // threat models to tell an incomplete / corrupt Paradox Mods download (file absent)
                // apart from a load failure (file on disk, never registered) — vanilla never reports
                // this (it checks its in-memory AssetDatabase, not the folder), so we do it ourselves.
                // ModInstallDirectory resolves from the loaded DLL, valid for both dev and PDX subscribers.
                string missingOnDisk = CivicCokSelfLoader.MissingCoreCokOnDisk();
                bool filesMissing = missingOnDisk.Length > 0;

                // The resolve-state context is appended INTO this Error string on purpose, NOT
                // emitted as a neighbouring Log.Info. Prod telemetry forwards ONLY LogType.Error/
                // Exception (TelemetryCrashDetector.OnUnityLogEntry) — an Info line never leaves the
                // player's local CivicSurvival.log, so the self-heal / surviving-refs / re-armed Info
                // diagnostics added earlier are invisible on the server. The disk-check above is the
                // single piece of state that ever reached Grafana, precisely because it lives in this
                // Error text; the rest of the resolve state must ride the same channel or prod stays
                // blind. This one line classifies the three failure modes without a follow-up build:
                //   registryCount near 0 / far below the vanilla ~8k → the player's AssetDatabase
                //     never finished populating (not our scan timing);
                //   registry full + refs all false + disk-check "files present" → the .cok loaded but
                //     under a name our dispatch table doesn't match (the 0.1.0-beta.1 MIM104 class);
                //   disk-check "MISSING" → genuinely-absent / corrupt Paradox Mods download.
                // Moving any field below back to Log.Info re-blinds prod diagnosis — keep it inline.
                // finalizeReason tells the finalize paths apart for prod: "native-load-settled" = the native
                // load finished (ParadoxNativeLoader.LoadAttemptSettled) and the core .cok genuinely never
                // registered — the normal verdict; "hard-cap" = the native load never reported settled
                // within FINALIZE_HARD_CAP (e.g. it aborted on a local dev deploy) and we gave up;
                // "reflect-failed" = PrefabSystem.m_Prefabs reflection was unavailable.
                string finalizeReason;
                if (m_PrefabsRef == null)
                    finalizeReason = "reflect-failed";
                else if (m_ParadoxNativeLoader?.LoadAttemptSettled == true)
                    finalizeReason = "native-load-settled";
                else
                    finalizeReason = "hard-cap";

                // Three extra fields ride the same Error channel to name the culprit a follow-up build would
                // otherwise be needed to find (prod forwards ONLY LogType.Error — the native loader's own
                // Info/Warn breadcrumbs never arrive):
                //   nativePhase  — the furthest stage the native re-load (ParadoxNativeLoader) reached. A
                //     "hard-cap" finalize means it never settled; this says WHY — parked in the readiness gate
                //     (awaiting-disk = .cok never arrived), aborted (abort-*), or invoke-fired but the prefabs
                //     still never landed (AddPrefab / IsAvailable gate). Without it, "hard-cap" is silent.
                //   nameVariant  — files-present registration-fail only: did a core .cok register under a NAME
                //     our exact-match dispatch missed (e.g. an install-source GUID suffix "AttackDrone_<guid>")?
                //     ResolvePrefabs matches on prefab.name, so a variant would strand a present prefab.
                //   installDir   — the pdx_mods tail the disk-check actually looked in (user-home prefix
                //     stripped). For a MISSING verdict it separates a real delivery miss from a wrong asset.path
                //     resolve (looked in the wrong folder).
                string nativePhase = m_ParadoxNativeLoader?.NativePhase ?? "no-loader";
                string nameVariant = filesMissing ? "skip" : ProbeCoreNameVariant();
                string installDirTail = SanitizedInstallDirTail();
                Log.Error($"Core mod prefab(s) absent — AttackDrone/Tracer/Rocket unavailable this session. " +
                          $"On-disk check: {(filesMissing ? $"MISSING {missingOnDisk}" : "files present (asset load/registration failure)")}. " +
                          $"Resolve state: registryCount={(m_PrefabsRef?.Count.ToString() ?? "reflect-failed")}, scannedTo={m_LastScannedIndex}, " +
                          $"refsResolved[drone={m_AttackDronePrefab != null}, rocket={m_RocketPrefab != null}, interceptor={m_InterceptorPrefab != null}], " +
                          $"aaFound={m_AaFound.Count}/{s_PlaceableAA.Length}, loadComplete={m_LoadComplete}, finalizeReason={finalizeReason}, " +
                          $"nativePhase={nativePhase}, nameVariant={nameVariant}, installDir={installDirTail}, elapsedMs={m_InitClock.ElapsedMilliseconds}.");

                // Tell the player instead of leaving a silent dead war, and offer a one-click
                // diagnostic report. Reaching here means the mod's core gameplay cannot run.
                ShowModLoadFailureModal(filesMissing, missingOnDisk);
            }

            if (m_RocketEntity != Entity.Null && !m_RocketExhaustBound)
            {
                // Rocket present but EffectCacheSystem never produced FireMovingMediumVFX — give
                // up the bind so the tick can stop (the latch means "bind settled", not "bound").
                m_RocketExhaustBound = true;
                Log.Warn("FireMovingMediumVFX unresolved at game-loading-complete — ballistic exhaust skipped this session.");
            }

            if (m_InterceptorEntity != Entity.Null && !m_InterceptorExhaustBound)
            {
                m_InterceptorExhaustBound = true;
                Log.Warn("FireMovingMediumVFX unresolved at game-loading-complete — interceptor exhaust skipped this session.");
            }

            // SPIKE (Phase 3.0b §B.0): if no vanilla building prefab was ever found to bind the
            // hidden-factory upgrade to by load-complete, settle the latch so the tick can stop
            // (this should never happen in a real city load — building prefabs are part of the
            // vanilla AssetDatabase loaded before post-load — but the backstop keeps the system
            // from polling forever if it does).
            if (!m_HiddenFactoryUpgradeSettled)
            {
                m_HiddenFactoryUpgradeSettled = true;
                Log.Warn("[HiddenFactory] no eligible vanilla building prefab found at game-loading-complete — upgrade prefab not registered this session.");
            }
        }

        // Raise the player-facing failure modal with the disk-check verdict as payload. The UI
        // reads Cause to pick wording (re-download vs restart/report) and lists the missing files.
        // Filenames are plain ASCII (no JSON-special chars), so no escaping is needed.
        private static void ShowModLoadFailureModal(bool filesMissingOnDisk, string missingCok)
        {
#pragma warning disable CIVIC050 // One-shot at the load-failure verdict (fires once when a miss is finalized) — not a per-frame allocation
            var sb = new StringBuilder(128);
            sb.Append('{');
            sb.Append("\"Cause\":\"").Append(filesMissingOnDisk ? "FilesMissingOnDisk" : "AssetsFailedToLoad").Append("\",");
            sb.Append("\"MissingCok\":\"").Append(missingCok).Append('"');
            sb.Append('}');
#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
#pragma warning disable CIVIC239 // Best-effort surface on a dead-mod notice; show-or-queue result is irrelevant
            ModalCoordinator.Instance.TryShow("ModLoadFailure", sb.ToString());
#pragma warning restore CIVIC239
#pragma warning restore CIVIC098
        }
#pragma warning restore CIVIC050

        // One-shot probe for the files-present registration-fail: is a core .cok registered under a NAME our
        // exact-match dispatch (m_Setup keyed on prefab.name) missed — e.g. an install-source GUID suffix
        // "AttackDrone_<guid>" (PrefabID.m_Hash varies by Paradox vs local install source, but prefab.name
        // should not — this catches the case where it does)? Returns the variant name, "none" if every prefab
        // name is exact, or "n/a" if the registry ref is unavailable. Scans the already-cached m_PrefabsRef
        // (no reflection) once at finalize — never per-frame, so the PERF-LOCK tail-scan invariant is untouched.
        private string ProbeCoreNameVariant()
        {
            if (m_PrefabsRef == null)
                return "n/a";

            for (int i = 0; i < m_PrefabsRef.Count; i++)
            {
                string? name = m_PrefabsRef[i]?.name;
                if (name == null)
                    continue;
                if (name != "AttackDrone" && name.Contains("AttackDrone", StringComparison.Ordinal))
                    return name;
                if (name != "Rocket" && name.Contains("Rocket", StringComparison.Ordinal))
                    return name;
            }
            return "none";
        }

        // The install directory tail from "pdx_mods" onward (the user-home prefix is stripped — PII). Tells
        // WHERE the disk-check looked, so a MISSING verdict caused by a wrong asset.path resolve (looked in the
        // wrong folder) is distinguishable from a real delivery miss. A non-Paradox (local dev) install has no
        // pdx_mods segment → report the leaf folder name (no PII: it is just "CivicSurvival").
        private static string SanitizedInstallDirTail() => ModPaths.SanitizePathTail(ModPaths.ModInstallDirectory);

        // Nozzle sits at the tail of the centered 7.3m mesh: local -Z half-length.
        private const float ROCKET_NOZZLE_LOCAL_Z = -3.65f;

#pragma warning disable CIVIC006 // One-shot structural add on a single prefab entity, HasBuffer-guarded idempotent (bootstrap bind, mirrors Setup* markers above)
        private void TryBindRocketExhaust()
        {
            if (m_RocketExhaustBound || m_RocketEntity == Entity.Null)
                return;

            // RocketEntity is resolved (demand-driven) before this binds, so the prefab
            // entity is alive — no per-frame Exists check needed (the exhaust latch is
            // reset in OnInitialize alongside the entity cache).
            if (EntityManager.HasBuffer<Game.Prefabs.Effect>(m_RocketEntity))
            {
                m_RocketExhaustBound = true; // asset ships its own effects — done
                return;
            }

            if (!m_EffectCache.TryGetEffect(EffectNames.FIRE_MOVING_MEDIUM_VFX, out Entity vfx))
                return; // effect cache not ready yet — retry next frame

            // FireMovingMediumVFX: vanilla authors a separate "moving" medium-fire for
            // burning objects in motion (vs FireMedium for stationary buildings) — closer
            // fit for a flying missile's exhaust than the stationary FireSmallVFX, and reads
            // brighter. Same required=OnFire constraint —
            // vanilla will NEVER enable this element declaratively (Game.Events.OnFire
            // on the missile would drag it into the fire simulation: damage, fire
            // engines, spread — FireSimulationSystem). The record is injected manually
            // by VanillaVfxSystem.TryAttachEffect (driven from ThreatSpawnSystem's
            // attach controller); this prefab element still matters twice: it gives
            // EffectTransformSystem the nozzle pose offsets for the DynamicTransform
            // record, and its m_Effect matching the record's m_Prefab prevents a
            // WrongPrefab removal when EffectControlSystem re-evaluates the entity.
            // (FireTinyVFX was the previous declarative choice — visually negligible,
            // ~4 particles; RocketExhaustVFX was rejected earlier: its graph ignores
            // InstanceData scale/intensity, authored solely for the huge SpaceRocket01 —
            // the vanilla-authored baseline values are recorded in
            // Docs/Plans/THREAT_EXHAUST_VFX_PLAN.md.)
            //
            // All fields are load-bearing (vanilla sets them via EffectSource.LateInitialize;
            // defaults here mean invisible VFX or a read of a missing BoneHistory):
            // m_BoneIndex = (-1,-1) routes EffectTransformSystem into the boneless
            // fallback (direct m_Position/m_Rotation via LocalToWorld) — the prefab is
            // already initialized (Created stripped), EffectInitializeSystem won't rematch.
            var buffer = EntityManager.AddBuffer<Game.Prefabs.Effect>(m_RocketEntity);
            buffer.Add(new Game.Prefabs.Effect
            {
                m_Effect = vfx,
                m_Position = new float3(0f, 0f, ROCKET_NOZZLE_LOCAL_Z),
                m_Rotation = quaternion.identity,
                m_Scale = new float3(1f, 1f, 1f),
                m_Intensity = 1f,
                m_BoneIndex = new int2(-1, -1),
                m_ParentMesh = 0,
                m_AnimationIndex = -1,
                m_Procedural = false
            });

            m_RocketExhaustBound = true;
            Log.Info($"[BallisticVFX] Effect buffer bound: {EffectNames.FIRE_MOVING_MEDIUM_VFX} → Rocket prefab (nozzle local z={ROCKET_NOZZLE_LOCAL_Z})");
        }

        // Nozzle at the tail of the centered 3.66m AIM-120 mesh: local -Z half-length.
        private const float INTERCEPTOR_NOZZLE_LOCAL_Z = -1.83f;

        // Mirror of TryBindRocketExhaust for the interceptor prefab. Same owner-attached
        // FireMovingMediumVFX path — the missile flies, so the exhaust trails the nozzle; the prefab
        // Effect element supplies the pose offset and its m_Effect matching the injected record's
        // m_Prefab prevents a WrongPrefab removal on re-evaluation. Absent .cok leaves
        // InterceptorEntity Null → this latches done and never binds (graceful fallback).
        private void TryBindInterceptorExhaust()
        {
            if (m_InterceptorExhaustBound || m_InterceptorEntity == Entity.Null)
                return;

            if (EntityManager.HasBuffer<Game.Prefabs.Effect>(m_InterceptorEntity))
            {
                m_InterceptorExhaustBound = true; // asset ships its own effects — done
                return;
            }

            if (!m_EffectCache.TryGetEffect(EffectNames.FIRE_MOVING_MEDIUM_VFX, out Entity vfx))
                return; // effect cache not ready yet — retry next frame

            var buffer = EntityManager.AddBuffer<Game.Prefabs.Effect>(m_InterceptorEntity);
            buffer.Add(new Game.Prefabs.Effect
            {
                m_Effect = vfx,
                m_Position = new float3(0f, 0f, INTERCEPTOR_NOZZLE_LOCAL_Z),
                m_Rotation = quaternion.identity,
                m_Scale = new float3(1f, 1f, 1f),
                m_Intensity = 1f,
                m_BoneIndex = new int2(-1, -1),
                m_ParentMesh = 0,
                m_AnimationIndex = -1,
                m_Procedural = false
            });

            m_InterceptorExhaustBound = true;
            Log.Info($"[InterceptorVFX] Effect buffer bound: {EffectNames.FIRE_MOVING_MEDIUM_VFX} → AIM120 prefab (nozzle local z={INTERCEPTOR_NOZZLE_LOCAL_Z})");
        }
#pragma warning restore CIVIC006

        // SPIKE (Phase 3.0b §B.0, GO/NO-GO): assemble an in-place ServiceUpgrade prefab in
        // code and register it via PrefabSystem.AddPrefab, bound to one vanilla building
        // prefab. Proves the unproven step of the hybrid hidden-factory entry — the mod has
        // never built a prefab programmatically (all assets are .cok from the Asset Editor).
        //
        // DEBUG-ONLY: the actual registration is wrapped in #if DEBUG. This spike is a dev
        // GO/NO-GO experiment, not shippable until the detector / sidecar / production / reveal
        // (B.1–B.6) exist. Shipping it would inject a functionally-empty upgrade into a random
        // vanilla building's panel for every player — a visible artefact of an unfinished spike.
        // In a Release build the method settle-skips immediately (no registration). Once
        // B.1–B.6 are implemented and this becomes real content, drop the #if DEBUG.
        //
        // This is the WHOLE spike; the detector / sidecar / production / reveal are phases
        // B.1-B.6 and are NOT done here.
        //
        // WHY HERE (lifecycle): m_PrefabsRef is resolved by ResolvePrefabs (called from
        // OnInitialize and EnsureResolved above), and vanilla building prefabs are all in
        // m_Prefabs by the post-load pass. AddPrefab only creates the prefab entity + Created
        // tag; vanilla PrefabInitializeSystem (ticks every frame on a Created+PrefabData query)
        // runs Initialize → LateInitialize on the next tick, so ServiceUpgrade.LateInitialize
        // → BuildingPrefab.AddUpgrade injects BuildingUpgradeElement into the bound building's
        // prefab archetype one frame later. The binding therefore lands asynchronously — the
        // log line below confirms registration, the in-game building panel confirms the bind.
        //
        // WHY FULLY-QUALIFIED (no using): Core must not `using CivicSurvival.Domains.*`
        // (CIVIC179 / Axiom 5). The analyzer flags using-directives only; a global:: FQN
        // reference is the sanctioned bootstrap pattern already used in SystemRegistrar /
        // BurstDiagnosticsLogger / PerfReportSections for Core→Domain type references at a
        // registration site. The prefab-assembly logic itself lives in the GridWarfare domain.
        private void TryRegisterHiddenFactoryUpgrade()
        {
            if (m_HiddenFactoryUpgradeSettled)
                return;

#if DEBUG
            // GATE: the hidden-factory upgrade is wave-3 GridWarfare content. This Core bootstrap is
            // NOT a feature module — it ticks even when GridWarfare is wave-gated closed (FeatureGates
            // sentinel 99 on a shipped build), unlike the GridWarfare systems which FeatureRegistry
            // skips entirely. Without this guard the upgrade would be injected into a vanilla
            // building's panel for every player. Register only once GridWarfare is open; otherwise
            // settle-skip for this session (the gate cannot open mid-session).
            if (!FeatureRegistry.IsInitialized || !FeatureRegistry.Instance.IsRegistrationComplete)
                return; // feature registration not settled yet — retry next tick, do NOT settle
            if (!FeatureRegistry.Instance.IsOpen("GridWarfare"))
            {
                m_HiddenFactoryUpgradeSettled = true;
                if (Log.IsDebugEnabled) Log.Debug("[HiddenFactory] GridWarfare gate closed — upgrade registration skipped");
                return;
            }

            // Needs the resolved live m_Prefabs list (set by ResolvePrefabs). If a slow load
            // has not populated it yet, retry next tick — do NOT settle.
            if (m_PrefabsRef == null)
                return;

            // Find one vanilla building prefab to bind to. First BuildingPrefab that resolves
            // to a prefab entity (the mod ships no BuildingPrefab .cok — its assets are drone /
            // rocket / AA object prefabs — so any BuildingPrefab in the registry is vanilla).
            // Its name is logged so the tester knows which building's panel to open in-game.
            BuildingPrefab? target = null;
            for (int i = 0; i < m_PrefabsRef.Count; i++)
            {
                if (m_PrefabsRef[i] is BuildingPrefab bp && m_PrefabSystem.TryGetEntity(bp, out _))
                {
                    target = bp;
                    break;
                }
            }

            if (target == null)
            {
                // No eligible vanilla building prefab present yet — building prefabs are part
                // of the vanilla AssetDatabase load done by post-load, so absence here means
                // we are mid-drain. Retry next tick; settle is reached once one is found.
                return;
            }

            var upgradePrefab = global::CivicSurvival.Domains.GridWarfare.Prefabs.HiddenFactoryUpgradePrefab.Build(
                new BuildingPrefab[] { target });

            // Hoist the FQN read to a local: global:: works as a normal expression
            // (as in the Build call above) but the C# version's interpolated-string
            // parser rejects global:: inside $"{ }".
            var prefabName = global::CivicSurvival.Domains.GridWarfare.Prefabs.HiddenFactoryUpgradePrefab.PrefabName;
            bool added = m_PrefabSystem.AddPrefab(upgradePrefab);
            m_HiddenFactoryUpgradeSettled = true;

            if (added)
                Log.Info($"[HiddenFactory] registered upgrade prefab '{prefabName}' bound to vanilla building '{target.name}' — vanilla PrefabInitializeSystem will inject BuildingUpgradeElement next tick; open that building's panel to verify the upgrade appears.");
            else
                Log.Warn($"[HiddenFactory] PrefabSystem.AddPrefab returned false for '{prefabName}' (dependency unavailable or duplicate) — upgrade not registered this session.");
#else
            // RELEASE: the spike's runtime artefacts (detector / sidecar / production, B.1–B.6) do
            // not exist yet, so registering the upgrade would only put an empty, non-functional
            // entry in a random vanilla building's panel. Settle-skip without touching any prefab.
            m_HiddenFactoryUpgradeSettled = true;
#endif
        }
    }
}
