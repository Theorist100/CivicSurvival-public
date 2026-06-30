using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Colossal;
using Colossal.Core;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Unity.Entities;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
// 'Mod' alone resolves to our CivicSurvival.Mod (the IMod class) from this namespace, not the Paradox
// mod descriptor — alias the platform type to disambiguate.
using PdxMod = Colossal.PSI.Common.Mod;
// Unity.Entities also defines Hash128; the asset GUIDs are Colossal.Hash128.
using Hash128 = Colossal.Hash128;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Loads the mod's late-delivered .cok by triggering CS2's OWN native mod-load, then registers their
    /// virtual texturing — the path that both registers the prefabs and textures them correctly.
    ///
    /// Why it is needed: on a fresh subscription (or any version update) the large .cok arrive via .cpatch
    /// AFTER CS2's single playset scan, so the native load registers only whatever was on disk in time (the
    /// small Gepard) and misses the big ones. They are on disk by post-load, but that one-shot scan is over.
    ///
    /// CS2's native load is <c>GameManager.OnEntryIsInActivePlaysetChanged</c> → <c>m_PrefabSystem.AddPrefab</c>
    /// (GameManager.cs:1586/1611), raised by <c>ParadoxModsDataSource.InvokeOnEntryIsInActivePlaysetChanged</c>.
    /// We replicate vanilla's enable-path (ParadoxModsDataSource.OnModStatusChanged / OnActivePlaysetChanged):
    ///   1. <c>PopulateFromDirectory(modPath)</c>  — re-scan the folder so the late .cok enter the source
    ///      (protected → reflection).
    ///   2. <c>GetEntries()</c>                     — collect the GUIDs under our mod path
    ///      (protected → reflection).
    ///   3. <c>InvokeOnEntryIsInActivePlaysetChanged(guids, true)</c> — public; fires GameManager's loader.
    /// then scopes the VT registration to our own surfaces (see <see cref="CompleteVirtualTexturing"/>).
    ///
    /// Runs POST-LOAD (in-game, not loading) so the city VT cache already exists when the native AddPrefab
    /// builds materials — the timing that textures correctly. After the native load finishes,
    /// <see cref="LoadAttemptSettled"/> latches so the owner can finalize a genuine miss without waiting
    /// for its coarse hard-cap.
    /// </summary>
    internal sealed class ParadoxNativeLoader
    {
        private static readonly LogContext Log = new("ModLoad.D");

        /// <summary>
        /// True once the native load attempt has finished (the AddPrefab batches completed and the VT step
        /// ran). The owner (<c>CivicPrefabInitSystem</c>) reads this to decide a genuine prefab miss the
        /// moment our load is done rather than after a coarse wall-clock cap — if our core .cok still are
        /// not in PrefabSystem once this is true, they genuinely failed to load. False until then (and on
        /// the paths where the native load never starts), so the owner's hard-cap stays the backstop.
        /// </summary>
        public bool LoadAttemptSettled { get; private set; }

        /// <summary>
        /// Furthest stage this native load reached, as a short tag. Read by the owner
        /// (<c>CivicPrefabInitSystem.FinalizeMissing</c>) and folded into its single telemetry Error line so a
        /// prod miss self-identifies WHERE the native chain stalled — the in-flight Info/Warn breadcrumbs here
        /// never reach the server (only LogType.Error is forwarded). A <c>hard-cap</c> finalize means this
        /// never reached <c>settled</c>; this tag says why (parked in the readiness gate vs aborted vs invoked
        /// but the VT continuation never ran). Set only at existing milestones on the main thread; the last
        /// write wins, so the value is the furthest point reached.
        /// </summary>
        public string NativePhase { get; private set; } = "not-started";

        private bool m_Done;
        private readonly World m_World;

        // The GUIDs of our mod's assets (collected in FinishPopulateAndFire). Used to scope the VT registration to ONLY
        // our surfaces instead of the global RefreshVT, which would re-load every ParadoxMods surface
        // (other asset mods' included).
        private HashSet<Hash128>? m_OurGuids;

        // Drain-wait state: after RefreshVT we wait for the VT init queue to empty before re-binding
        // materials, so the re-bind cannot race the first render of an already-loaded prefab.
        private TextureStreamingSystem? m_PendingRebindTss;
        private int m_RebindWaitFrames;

        // Folder re-scan (PopulateFromDirectory) state. The enumeration runs on a background thread; we poll
        // its Task to completion from the dispatcher instead of blocking the main thread on .Wait() — a
        // slow/busy disk would otherwise freeze the frame on CreateFileW inside the scan (ANR). Held across
        // frames between WhenReadyPostLoad's two phases, so they cannot be locals.
        private Task? m_PopulateTask;
        private ParadoxModsDataSource? m_PendingDs;
        private string? m_PendingModPath;

        // Case-(a) recovery state: when ResolveModPath can't find our path because our mod isn't in the
        // source's m_ActiveMods yet (boot's single playset scan didn't add it in time, and there is no
        // re-scan), we drive vanilla's own playset re-evaluation ONCE (ds.Populate → OnActivePlaysetChanged,
        // which adds us to m_ActiveMods and loads the on-disk .cok), then retry the resolve. Polled like the
        // folder scan so the heavy Populate can't block the frame. Attempted at most once — a still-unresolved
        // path after it means the mod genuinely isn't in the platform's active playset.
        private Task? m_PopulateActiveModsTask;
        private bool m_PopulateActiveModsAttempted;

        // Frame budget for the VT drain wait before forcing the re-bind anyway (≈10s at 60 FPS) — a guard
        // so a surface that never finishes loading cannot wedge the re-bind forever.
        private const int MaxDrainWaitFrames = 600;

        public ParadoxNativeLoader(World world)
        {
            m_World = world;
        }

        public void Activate() => MainThreadDispatcher.RegisterUpdater(WhenReadyPostLoad);

        // Polled by MainThreadDispatcher on the main thread, all phases driven by this one updater so nothing
        // registers a nested updater mid-pump:
        //   1. Readiness: wait until the mod manager is initialized, the city is loaded (VT cache built),
        //      and the .cok are on disk; then START the resolve + background folder re-scan (no blocking).
        //   1b. Playset re-populate (case-a recovery): if the resolve found no mod path because our mod isn't
        //      in m_ActiveMods yet, ResolveAndScan kicks ds.Populate() to add it via vanilla's playset path;
        //      this phase polls that Task to completion (non-blocking) and re-enters ResolveAndScan.
        //   2. Drain: poll the re-scan Task to completion without blocking the frame, then fire the native
        //      load. Blocking the main thread on .Wait() here would freeze the frame on CreateFileW inside
        //      the scan when the disk is slow/busy (observed as an ANR/CTD on a player's machine).
        private bool WhenReadyPostLoad()
        {
            if (m_Done)
                return true;

            // Phase 2: a folder re-scan is in flight — wait (non-blocking) for it, then fire the load once.
            if (m_PopulateTask != null)
            {
                if (!m_PopulateTask.IsCompleted)
                    return false; // scan still running — keep polling, frame stays responsive
                FinishPopulateAndFire();
                m_Done = true;
                return true;
            }

            // Phase 1b (case-a recovery): a playset re-populate (ds.Populate) is in flight because our path
            // didn't resolve — our mod wasn't in m_ActiveMods. Wait (non-blocking); once it completes, retry
            // the resolve + start the folder scan. Populate itself adds us to m_ActiveMods and loads the
            // on-disk .cok via vanilla's path; the scan + InvokeOnEntry that follow are idempotent
            // (AddPrefab skips already-registered prefabs) and still run so the VT registration
            // (CompleteVirtualTexturing) happens.
            if (m_PopulateActiveModsTask != null)
            {
                if (!m_PopulateActiveModsTask.IsCompleted)
                    return false; // Populate still running — keep polling, frame stays responsive
                Task populateDone = m_PopulateActiveModsTask;
                m_PopulateActiveModsTask = null;
                ParadoxModsDataSource? ds = m_PendingDs;
                if (populateDone.IsFaulted || ds == null)
                {
                    NativePhase = "abort-activemods-faulted";
                    Log.Error($"[ParadoxNative] playset re-populate (ds.Populate) failed — cannot add our mod to the active set: {populateDone.Exception}");
                    m_Done = true;
                    return true;
                }
                try
                {
                    if (!ResolveAndScan(ds))
                        m_Done = true;
                }
                catch (Exception e)
                {
                    NativePhase = "abort-exception";
                    Log.Error($"[ParadoxNative] resolve/scan after playset re-populate failed: {e}");
                    m_Done = true;
                }
                return m_Done;
            }

            // Phase 1: readiness gate. Each block records which condition is holding the loader so a
            // never-started native load reports its blocker (e.g. awaiting-disk = the .cok never arrived).
            var gm = GameManager.instance;
            if (gm == null || gm.modManager == null || !gm.modManager.isInitialized)
            {
                NativePhase = "awaiting-modmanager";
                return false;
            }
            if (!gm.gameMode.IsGame() || gm.isGameLoading)
            {
                NativePhase = "awaiting-ingame"; // wait for the city to finish loading so the VT cache exists
                return false;
            }
            if (CivicCokSelfLoader.MissingCoreCokOnDisk().Length > 0)
            {
                NativePhase = "awaiting-disk"; // .cok not delivered to disk yet
                return false;
            }

            // Ready: kick off the background re-scan. If it can't start (no DS / no mod path / reflection
            // miss), we're done; otherwise keep polling — phase 2 above picks it up next frame.
            if (!StartPopulate())
                m_Done = true;
            return m_Done;
        }

        // Phase 1 tail: resolve the data source, then resolve our mod path + start the folder re-scan via
        // ResolveAndScan. Returns true if an async task is now in flight (keep polling); false if there is
        // nothing to do (abort → done).
        private bool StartPopulate()
        {
            try
            {
                var ds = AssetDatabase<ParadoxMods>.instance?.dataSource as ParadoxModsDataSource;
                if (ds == null)
                {
                    NativePhase = "abort-no-ds";
                    Log.Error("[ParadoxNative] ParadoxModsDataSource unavailable — cannot trigger native load.");
                    return false;
                }

                return ResolveAndScan(ds);
            }
            catch (Exception e)
            {
                NativePhase = "abort-exception";
                Log.Error($"[ParadoxNative] failed to start native load: {e}");
                return false;
            }
        }

        // Resolve our active mod path and start the background folder re-scan. If the path can't resolve, it
        // is because our mod isn't in the source's m_ActiveMods yet — ResolveModPath reads ONLY
        // m_ActiveMods.Keys (TryGetMod / GetActiveMods), the same dictionary ContainsActiveMod keys (the gate
        // vanilla's AddPrefab runs via ModRequirement). Boot's single playset scan may not have added us in
        // time (folder/LocalData not ready when it ran). Drive vanilla's own playset re-evaluation ONCE
        // (ds.Populate → OnActivePlaysetChanged) to add us to m_ActiveMods and load the on-disk .cok, then
        // retry the resolve from the poll loop. Returns true if an async task is in flight (folder scan OR
        // the playset re-populate), false on a genuine miss.
        private bool ResolveAndScan(ParadoxModsDataSource ds)
        {
            string? modPath = ResolveModPath(ds);
            if (modPath is not null && modPath.Length > 0)
                return StartDirectoryScan(ds, modPath);

            // Path unresolved → our mod isn't in m_ActiveMods. For a Paradox-delivered install, drive the
            // playset re-populate once to add it (vanilla's OnActivePlaysetChanged). For a local/dev install
            // the mod isn't in ParadoxMods at all (it lives in the 'global' source), so Populate would never
            // add it — skip straight to the abort (Warn), exactly as before.
            if (IsParadoxDelivered() && !m_PopulateActiveModsAttempted)
            {
                m_PopulateActiveModsAttempted = true;
                m_PendingDs = ds;
                m_PopulateActiveModsTask = ds.Populate();
                NativePhase = "populating-activemods";
                Log.Info("[ParadoxNative] active mod path unresolved — our mod is not in m_ActiveMods; driving ds.Populate() to add it via vanilla's playset path, then retrying the resolve.");
                return true; // poll loop waits for Populate, then re-enters ResolveAndScan
            }

            // Already re-populated the playset and the path still doesn't resolve → genuine miss.
            return AbortNoModPath();
        }

        // Start vanilla's PopulateFromDirectory re-scan on its background thread (so the late .cok enter the
        // source). Returns true if a scan is now in flight (m_PopulateTask set); false on a reflection/Task
        // miss (abort).
        private bool StartDirectoryScan(ParadoxModsDataSource ds, string modPath)
        {
            MethodInfo? populate = ResolvePopulateFromDirectoryMethod();
            if (populate == null)
            {
                NativePhase = "abort-reflect";
                return false; // reflection miss already logged
            }

            // Kick off the background folder enumeration (returns a Task). Do NOT .Wait() it here — the
            // caller polls it to completion via the dispatcher so a slow disk can't freeze the frame.
            if (populate.Invoke(ds, new object?[] { modPath, false, CancellationToken.None, null }) is not Task populateTask)
            {
                NativePhase = "abort-no-task";
                Log.Error("[ParadoxNative] PopulateFromDirectory returned no Task — cannot await re-scan.");
                return false;
            }

            m_PendingDs = ds;
            m_PendingModPath = modPath;
            m_PopulateTask = populateTask;
            NativePhase = "populating";
            return true;
        }

        // Final "our mod path can't be resolved" verdict, emitted only after the playset re-populate above
        // also failed to add us to m_ActiveMods. The native re-load never fires, the core .cok stay
        // unregistered, and the owner finalizes them "prefab(s) absent" later. Error for a Paradox-delivered
        // install (where the path MUST resolve, and prod telemetry forwards ONLY LogType.Error so this is the
        // one line that reaches Grafana); Warn for a local/dev deploy, where there legitimately is no Paradox
        // mod path and the .cok load via vanilla's normal scan (same pdx_mods discriminator as ResolveModPath).
        // Returns false.
        private bool AbortNoModPath()
        {
            NativePhase = "abort-no-modpath";
            if (IsParadoxDelivered())
                Log.Error("[ParadoxNative] Paradox-delivered mod but active mod path unresolved even after playset re-populate — native .cok re-load skipped; core prefabs will hard-cap as absent.");
            else
                Log.Warn("[ParadoxNative] could not resolve our active mod path — skipping native VT re-populate (expected in local/dev installs not delivered via Paradox Mods).");
            return false;
        }

        // Phase 2 tail: the background re-scan finished — collect our GUIDs and fire CS2's native load on
        // the main thread (the dispatcher pumps here, so the first AddPrefab is main-thread as before, and
        // its internal WaitXFrames keeps the rest main-thread).
        private void FinishPopulateAndFire()
        {
            ParadoxModsDataSource? ds = m_PendingDs;
            string? modPath = m_PendingModPath;
            Task? task = m_PopulateTask;
            m_PopulateTask = null;
            m_PendingDs = null;
            m_PendingModPath = null;

            if (task != null && task.IsFaulted)
            {
                NativePhase = "abort-populate-faulted";
                Log.Error($"[ParadoxNative] folder re-scan failed: {task.Exception}");
                return;
            }
            if (ds == null || modPath == null || modPath.Length == 0)
                return;

            try
            {
                IReadOnlyCollection<Hash128> guids = ReflectCollectGuids(ds, modPath);
                NativePhase = $"guids:{guids.Count}";
                Log.Info($"[ParadoxNative] {guids.Count} entries under '{ModPaths.SanitizePathTail(modPath)}' after re-populate.");
                if (guids.Count == 0)
                {
                    NativePhase = "abort-0-entries";
                    // The folder re-scan resolved our mod path but found zero entries under it → the native
                    // re-load fires nothing, LoadAttemptSettled never latches, and the owner hard-caps the core
                    // .cok as absent. Error (not Warn) so this dead-end rides the telemetry Error channel
                    // instead of staying invisible in the player's local log.
                    Log.Error("[ParadoxNative] no entries under mod path after re-populate — native .cok re-load fired nothing; core prefabs will hard-cap as absent.");
                    return;
                }
                m_OurGuids = guids as HashSet<Hash128> ?? new HashSet<Hash128>(guids);

                // Start the native load on the main thread (handler's first AddPrefab runs main-thread,
                // the rest stays main-thread via its WaitXFrames). When ALL AddPrefab batches are done, run
                // the post-load VT step (CompleteVirtualTexturing) that registers our surfaces with the live
                // atlas — without it the surfaces never enter the VT atlas → chrome.
                Task invokeTask = ds.InvokeOnEntryIsInActivePlaysetChanged(guids, isActive: true);
                NativePhase = $"invoke-fired:{guids.Count}";
                Log.Info($"[ParadoxNative] InvokeOnEntryIsInActivePlaysetChanged fired for {guids.Count} entries — CS2 native-loads them via GameManager.AddPrefab.");

                // When ALL AddPrefab batches are done, marshal the VT completion back to the main thread.
                _ = invokeTask.ContinueWith(
                    _ => MainThreadDispatcher.RunOnMainThread(CompleteVirtualTexturing),
                    TaskScheduler.Default);
            }
            catch (Exception e)
            {
                Log.Error($"[ParadoxNative] native load dispatch failed: {e}");
            }
        }

        // Post-load VT step, on the main thread after the native AddPrefab batches finished: UpdateAvailable,
        // then register our surfaces with the live VT atlas — scoped to our own models
        // (TryRegisterOwnSurfacesScoped), with the global MarkVTAssetsDirty + RefreshVT only as a fallback —
        // then a drain-gated BatchManagerSystem.VirtualTexturingUpdated(). Latches LoadAttemptSettled first:
        // by here the prefab AddPrefab batches are done, so the owner can decide a genuine miss now.
        private void CompleteVirtualTexturing()
        {
            // The native AddPrefab batches completed before this continuation ran, so prefab presence is now
            // final — let the owner finalize a genuine miss without waiting for its hard-cap. Set before the
            // VT work (and outside the try) so a VT exception cannot swallow the prefab-done signal.
            LoadAttemptSettled = true;
            NativePhase = "vt-running";
            try
            {
                var prefabSystem = m_World.GetOrCreateSystemManaged<PrefabSystem>();
                prefabSystem.UpdateAvailable();

                var tss = m_World.GetOrCreateSystemManaged<TextureStreamingSystem>();
                var pdxDb = AssetDatabase<ParadoxMods>.instance;

                // Register ONLY our surfaces into the live VT atlas. The vanilla way (MarkVTAssetsDirty +
                // RefreshVT) is correct but GLOBAL — it ClearVTAtlassingInfos + re-loads EVERY ParadoxMods
                // surface, so a big subscribed asset mod (e.g. a 1000-surface building pack) gets re-streamed
                // on every city-load. The scoped path touches only our models; it falls back to the global
                // RefreshVT if the vanilla-internal field it needs is unavailable.
                if (!TryRegisterOwnSurfacesScoped(tss, pdxDb))
                {
                    Log.Warn("[ParadoxNative] scoped VT register unavailable — falling back to global RefreshVT(ParadoxMods).");
                    tss.MarkVTAssetsDirty();
                    tss.RefreshVT(pdxDb);
                }

                // Wait for the init queue to DRAIN before re-binding, so the re-bind cannot race the first
                // render of an already-loaded prefab (the residual chrome cause). Polled per frame.
                m_PendingRebindTss = tss;
                m_RebindWaitFrames = 0;
                MainThreadDispatcher.RegisterUpdater(WhenVTLoadedRebind);
            }
            catch (Exception e)
            {
                Log.Error($"[ParadoxNative] VT completion failed: {e}");
            }
        }

        // Scoped equivalent of RefreshVT: registers and loads ONLY our mod's surfaces into the VT atlas,
        // leaving every other subscribed asset mod's surfaces untouched. Replays the same internal sequence
        // RefreshVT uses (AddMidMipCache → PreRegisterToVT → SetPreReservedIndex → LoadVTAsync) but filtered
        // to our GUIDs. Needs the one private field m_AtlasMaterialsDatabase via reflection; returns false
        // (so the caller falls back to the global RefreshVT) if that field, our GUIDs, or a usable surface
        // is unavailable.
        private bool TryRegisterOwnSurfacesScoped(TextureStreamingSystem tss, IAssetDatabase pdxDb)
        {
            if (m_OurGuids == null || m_OurGuids.Count == 0)
                return false;

            AtlasMaterialsDatabase? atlasDb = ReflectAtlasMaterialsDatabase(tss);
            if (atlasDb == null)
                return false;

            int tileSize = tss.tileSize;
            int midMips = tss.midMipLevelsCount;
            string cacheTag = AtlasMaterialsGrouper.GetAssetName(tileSize, midMips);

            // 1. Register every TS512_MB.. cache (AddMidMipCache is idempotent — IsCacheAlreadyAdded skips
            //    ones already registered at city init, e.g. another asset mod's), then PreRegisterToVT.
            //    This is cheap (header-only) and is what makes IsHandlingMaterial(ourGuid) become true and
            //    reserves atlas slots for our pre-processed mid-mips.
            foreach (MidMipCacheAsset cache in pdxDb.GetAssets(SearchFilter<MidMipCacheAsset>.ByCondition(
                         (MidMipCacheAsset x) => x.tags.Count == 1 && x.tags[0] == cacheTag)))
            {
                atlasDb.AddMidMipCache(tileSize, midMips, cache);
            }
            atlasDb.PreRegisterToVT(tss);

            // 2. Load ONLY our surfaces, passing our known pre-processed mid-mip count (== the file's 3)
            //    directly — no global scan, no re-load of other mods' surfaces.
            int loaded = 0;
            foreach (SurfaceAsset surface in pdxDb.GetAssets(default(SearchFilter<SurfaceAsset>)))
            {
                if (!m_OurGuids.Contains(surface.id) || !surface.isVTMaterial)
                    continue;
                surface.ClearVTAtlassingInfos();
                if (!atlasDb.SetPreReservedIndex(surface))
                    continue; // material not registered after step 1 — skip rather than mis-load at count 0
                surface.LoadHeader();
                int mipBiasOverride = surface.GetMipBiasOverride();
                surface.LoadVTAsync(tss, mipBiasOverride >= 0 ? mipBiasOverride : tss.mipBias, tileSize, midMips, duplicate: false);
                loaded++;
            }

            if (loaded == 0)
                return false; // no surface matched our GUIDs — let the caller fall back

            Log.Warn($"[ParadoxNative] scoped VT register: {loaded} surface(s) (ours only) loaded into the live atlas — other asset mods' surfaces untouched.");
            return true;
        }

        // Reflects TextureStreamingSystem.m_AtlasMaterialsDatabase (no public accessor). Null-guarded; a
        // null return makes the caller fall back to the global RefreshVT.
        private static AtlasMaterialsDatabase? ReflectAtlasMaterialsDatabase(TextureStreamingSystem tss)
        {
#pragma warning disable S3011 // intentional vanilla-internal access: the atlas materials DB has no public accessor; null-guarded with a global-RefreshVT fallback if the field moves
            FieldInfo? fi = typeof(TextureStreamingSystem).GetField(
                "m_AtlasMaterialsDatabase", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
            return fi?.GetValue(tss) as AtlasMaterialsDatabase;
        }

        // Polled per frame after RefreshVT. Holds off the batch re-bind until the VT init queue is empty
        // (all our surfaces fully loaded with valid VT — no mid-mip mismatch), then flags the batch system
        // to re-bind existing materials to the now-valid VT. Deterministic: the re-bind no longer races the
        // first render. A frame budget guards against waiting forever if a surface never finishes.
        private bool WhenVTLoadedRebind()
        {
            var tss = m_PendingRebindTss;
            if (tss == null)
                return true;

            int left = tss.VTMaterialsLeftToLoadCount;
            int dup = tss.VTMaterialsDuplicatesToProcessCount;
            m_RebindWaitFrames++;

            if (left + dup > 0)
            {
                if (m_RebindWaitFrames < MaxDrainWaitFrames)
                    return false; // keep waiting
                Log.Warn($"[ParadoxNative] VT drain wait exceeded {m_RebindWaitFrames} frames (left={left}, dup={dup}) — re-binding anyway.");
            }

            m_World.GetOrCreateSystemManaged<BatchManagerSystem>().VirtualTexturingUpdated();
            m_PendingRebindTss = null;
            NativePhase = "settled";
            return true;
        }

        // True when our mod was delivered through Paradox Mods (install path under pdx_mods) rather than a
        // local/dev drop into Mods\. Discriminates the playset re-populate + abort severity: a dev mod isn't
        // in ParadoxMods at all, so Populate can't add it and the abort is an expected Warn.
        private static bool IsParadoxDelivered() => ModPaths.ModInstallDirectory
            .Replace('\\', '/').Contains("pdx_mods", StringComparison.OrdinalIgnoreCase);

        // Find our active mod's canonical path (the prefix the source's entries use). TryGetMod returns the
        // active mod whose .path is a prefix of the passed path; fall back to scanning active mods and
        // matching by our pdx_mods folder name.
        private static string? ResolveModPath(ParadoxModsDataSource ds)
        {
            string installDir = ModPaths.ModInstallDirectory;

            if (ds.TryGetMod(installDir, out PdxMod mod) && !string.IsNullOrEmpty(mod.path))
                return mod.path;

            string folder = System.IO.Path.GetFileName(installDir.TrimEnd('\\', '/'));
            foreach (PdxMod m in ds.GetActiveMods())
            {
                Log.Info($"[ParadoxNative] active mod id='{m.id}' path='{ModPaths.SanitizePathTail(m.path)}'");
                if (!string.IsNullOrEmpty(m.path) && m.path.Replace('\\', '/').Contains(folder, StringComparison.OrdinalIgnoreCase))
                    return m.path;
            }
            return null;
        }

        // Resolves protected FileSystemDataSource.PopulateFromDirectory(string root, bool priorityData,
        // CancellationToken ct, List<(Type, Identifier)> newData) — the only way to re-scan the mod folder so
        // late .cok enter the source. Returns the MethodInfo; the caller invokes it (off-thread enumeration,
        // returns a Task) and polls that Task instead of blocking the main thread on .Wait(). Null on a
        // reflection miss (logged) if CS2's layout changed.
        private static MethodInfo? ResolvePopulateFromDirectoryMethod()
        {
#pragma warning disable S3011 // intentional vanilla-internal access: the only way to re-scan the mod folder; null-guarded if the method moves
            MethodInfo? mi = typeof(FileSystemDataSource).GetMethod(
                "PopulateFromDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
            if (mi == null)
                Log.Error("[ParadoxNative] PopulateFromDirectory not found via reflection — CS2 layout changed.");
            return mi;
        }

        // protected FileSystemDataSource.GetEntries() → IReadOnlyDictionary<Hash128, EntryInfo>. Collect the
        // GUIDs whose entry path is under our mod folder. Iterated as IEnumerable of KeyValuePair to avoid
        // referencing the (possibly nested) EntryInfo type — Key/Value/path are read reflectively.
        private static IReadOnlyCollection<Hash128> ReflectCollectGuids(ParadoxModsDataSource ds, string modPath)
        {
            var result = new HashSet<Hash128>();

#pragma warning disable S3011 // intentional vanilla-internal access: the only way to read the source's entry table; null-guarded if the method moves
            MethodInfo? mi = typeof(FileSystemDataSource).GetMethod(
                "GetEntries", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
            if (mi == null)
            {
                Log.Error("[ParadoxNative] GetEntries not found via reflection — CS2 layout changed.");
                return result;
            }

            if (mi.Invoke(ds, null) is not IEnumerable entries)
                return result;

            string prefix = modPath.Replace('\\', '/') + "/";
            foreach (object kv in entries)
            {
                Type t = kv.GetType();
                object? keyObj = t.GetProperty("Key")?.GetValue(kv);
                object? valObj = t.GetProperty("Value")?.GetValue(kv);
                if (keyObj is not Hash128 key || valObj == null)
                    continue;

                string? path = valObj.GetType().GetProperty("path")?.GetValue(valObj) as string;
                if (path != null && path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(key);
            }
            return result;
        }
    }
}
