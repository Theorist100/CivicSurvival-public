using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using Colossal.Core;
using Colossal.IO.AssetDatabase;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Service.Mods.Interfaces;
using PDX.SDK.Contracts.Service.Mods.Models;
// 'Mod' unqualified resolves to CivicSurvival.Mod (the IMod) from the enclosing namespace, not the
// platform descriptor — alias the platform type to disambiguate (same as ParadoxNativeLoader).
using PdxMod = Colossal.PSI.Common.Mod;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Resolves the mod's CURRENT install directory from the live active playset instead of the
    /// boot-pinned <see cref="ModPaths.ModInstallDirectory"/>.
    ///
    /// Why: a Paradox subscription lives under <c>.../pdx_mods/&lt;modId&gt;_&lt;instance&gt;</c>
    /// (e.g. <c>147665_18</c>). The <c>_&lt;instance&gt;</c> suffix changes on every version update /
    /// re-sync while the numeric <c>modId</c> does not. <see cref="ModPaths.ModInstallDirectory"/> is
    /// captured once at <c>Mod.OnLoad</c> from the loaded DLL's <c>ExecutableAsset.path</c>, so once
    /// Paradox re-materialises the mod into a new instance folder the pinned path points at a folder
    /// that is being torn down: its <c>.cok</c> vanish (disk-check → MISSING) and a folder-name match
    /// against the live playset misses (<c>ResolveModPath</c> → <c>abort-no-modpath</c>).
    ///
    /// The live active set (<c>ParadoxModsDataSource.GetActiveMods</c>, repopulated by vanilla's
    /// <c>OnActivePlaysetChanged</c> / <c>ds.Populate()</c>) is the only source that follows the
    /// instance move. <c>Colossal.PSI.Common.Mod</c> equality is by <c>id</c> (decompile-verified), so
    /// matching our stable <c>modId</c> against it returns the current instance path regardless of the
    /// folder counter — no hard-coded ModId needed (it is derived from our own boot folder name).
    /// </summary>
    internal static class ModInstallResolver
    {
        private static readonly LogContext Log = new("ModLoad.D");

        /// <summary>
        /// Our stable platform ModId, derived from the boot install folder name
        /// (<c>147665</c> from <c>.../pdx_mods/147665_18</c>). Empty for a non-Paradox (local dev)
        /// install, whose folder is just <c>CivicSurvival</c> with no <c>_&lt;instance&gt;</c> suffix.
        /// </summary>
        public static string StableModId()
        {
            string folder = Path.GetFileName(ModPaths.ModInstallDirectory.TrimEnd('\\', '/'));
            int underscore = folder.IndexOf('_', System.StringComparison.Ordinal);
            return underscore > 0 ? folder.Substring(0, underscore) : string.Empty;
        }

        /// <summary>
        /// Our mod's CURRENT path in the live active playset, matched by stable ModId, or null when
        /// our mod isn't in the active set (not populated yet, local dev, or genuinely removed). When
        /// <paramref name="log"/> is true, each scanned active mod is logged (Info) — the diagnostic
        /// the native loader wants; the per-frame disk-check passes false to stay quiet.
        /// </summary>
        public static string? MatchLivePath(ParadoxModsDataSource ds, bool log)
        {
            string modId = StableModId();
            string? live = null;
            foreach (PdxMod m in ds.GetActiveMods())
            {
                if (log)
                    Log.Info($"[ParadoxNative] active mod id='{m.id}' path='{ModPaths.SanitizePathTail(m.path)}'");
                if (live == null && modId.Length != 0 && m.id == modId && !string.IsNullOrEmpty(m.path))
                    live = m.path;
            }
            return live;
        }

        /// <summary>
        /// The live PDX SDK context behind the <c>PdxSdk</c> platform, or false when there is none to
        /// read: a non-Paradox launch (no PSI registered) or a platform whose SDK context creation
        /// failed — <c>isInitialized</c> false leaves <c>m_SDKContext</c> null, and every getter that
        /// walks it would dereference that null.
        ///
        /// The SINGLE reflection site for that private field. Every probe needing the context goes
        /// through here instead of re-reflecting it: a second copy would have to repeat the
        /// null/initialized guard verbatim, and the copies would drift apart the day the field moves.
        /// Pass <paramref name="platform"/> when the caller already holds the instance (the native
        /// loader hands its healthy platform to <see cref="StartInstalledSetProbe"/>); omit it to
        /// resolve from <c>PlatformManager</c>. Read-only and silent — callers fold any failure into
        /// their own probe string, because these run inside the log pipeline (see
        /// <see cref="DescribeActiveSetSafe"/>).
        /// </summary>
        private static bool TryGetSdkContext([NotNullWhen(true)] out IContext? ctx,
                                             Colossal.PSI.PdxSdk.PdxSdkPlatform? platform = null)
        {
            ctx = null;
            var pdx = platform
                ?? Colossal.PSI.Common.PlatformManager.instance?.GetPSI<Colossal.PSI.PdxSdk.PdxSdkPlatform>("PdxSdk");
            if (pdx == null || !pdx.isInitialized)
                return false;

#pragma warning disable S3011 // intentional vanilla-internal access: the SDK context has no public accessor on the platform; null-guarded, read-only
            FieldInfo? ctxField = typeof(Colossal.PSI.PdxSdk.PdxSdkPlatform)
                .GetField("m_SDKContext", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
            ctx = ctxField?.GetValue(pdx) as IContext;
            return ctx != null;
        }

        /// <summary>
        /// Skyve-style authoritative install-path resolve: our mod's CURRENT folder from the SDK's
        /// raw per-mod local record (<c>IModsService.GetLocalModData</c>) — the same source of truth
        /// the Skyve mod manager reads (Skyve.Systems.CS2 <c>ModsUtil</c>). This BYPASSES the transient
        /// <c>ParadoxModsDataSource.GetActiveMods()</c> / <c>PdxSdkPlatform.GetModsInActivePlayset</c>
        /// LocalData-filter (<c>:1616</c>) that strands a healthy files-present install for the whole
        /// session (folder present, SDK row enabled, yet dropped from the runtime active set — the
        /// files-present α bucket). Synchronous and READ-ONLY — writes nothing to any SDK/playset
        /// state. Returns null on a dev/local install (no stable ModId), a non-Paradox/uninitialised
        /// platform, an unavailable SDK context, or when the SDK holds no local record / an empty path
        /// (genuinely not installed) — every null makes the caller abort exactly as before, so this
        /// only ever converts a dead-end into an attempt.
        /// </summary>
        public static string? MatchSdkLocalPath()
        {
            try
            {
                string modId = StableModId();
                if (modId.Length == 0)
                    return null; // dev/local install — no Paradox ModId, no SDK record to read

                if (!TryGetSdkContext(out IContext? ctx))
                    return null;

                // The same synchronous per-mod read Skyve uses (Context.Mods.GetLocalModData(ModBase)):
                // returns the raw local record straight from the SDK's on-disk DB, unfiltered by the
                // playset-enabled / LocalData gates that strand us from GetActiveMods.
                string? path = ctx.Mods
                    .GetLocalModData(new ModBase(SourceType.PdxMods, modId))?.LocalData?.FolderAbsolutePath;
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (System.Exception e)
            {
                Log.Warn($"[ParadoxNative] SDK local-path resolve threw ({e.GetType().Name}) — falling through to abort.");
                return null;
            }
        }

        /// <summary>
        /// Classifies WHY <see cref="MatchLivePath"/> returned null, for the abort-no-modpath
        /// telemetry Error (prod forwards ONLY LogType.Error — the per-mod Info lines above never
        /// arrive): how many mods the live active set holds, whether our stable id is among them,
        /// and whether it was present but carried an empty install path. Splits the two roots the
        /// 2026-07-15 prod session could not: <c>activeMods=0</c> = the platform returned nothing
        /// (offline / SDK context failure — <c>GetModsInActivePlayset</c> null makes
        /// <c>OnActivePlaysetChanged</c> bail without touching the set); <c>idSeen=False</c> with a
        /// populated set = PdxSdkPlatform silently dropped us (LocalData-less mods are filtered out
        /// of the set) or the playset genuinely lacks the mod.
        /// </summary>
        public static string DescribeActiveSet(ParadoxModsDataSource ds)
        {
            ReadActiveSetFacts(ds, out int count, out bool idSeen, out bool pathEmpty);
            return $"activeMods={count}, idSeen={idSeen}, pathEmpty={pathEmpty}";
        }

        /// <summary>
        /// The same three facts <see cref="DescribeActiveSet"/> renders, as values rather than text —
        /// for callers that must COMPARE two samples instead of printing one.
        ///
        /// Comparing the rendered strings is not equivalent: any difference reads as a change, so a
        /// harmless one (the player enabling someone else's mod, the set growing) is indistinguishable
        /// from the transition worth reporting (our row dropping out, or the set shrinking). A probe
        /// that spends its single per-process report on the harmless case is blind to the real one.
        ///
        /// Main thread only, same as <see cref="DescribeActiveSet"/>: this enumerates vanilla's live
        /// key collection (<c>GetActiveMods()</c> returns <c>m_ActiveMods.Keys</c>, not a copy), and
        /// vanilla mutates that dictionary from the main thread after its own frame waits.
        /// </summary>
        public static void ReadActiveSetFacts(ParadoxModsDataSource ds,
                                              out int activeMods, out bool idSeen, out bool pathEmpty)
        {
            string modId = StableModId();
            activeMods = 0;
            idSeen = false;
            pathEmpty = false;
            foreach (PdxMod m in ds.GetActiveMods())
            {
                activeMods++;
                if (modId.Length != 0 && m.id == modId)
                {
                    idSeen = true;
                    pathEmpty = string.IsNullOrEmpty(m.path);
                }
            }
        }

        /// <summary>
        /// Crash-proof wrapper around <see cref="DescribeActiveSet"/> that resolves the data source
        /// itself. For the FinalizeMissing Error line (the only line prod reliably receives): the
        /// α/β/γ discriminator of WHY the active-set resolve failed must ride that line, because the
        /// loader's own abort Error races the diagnostics log-hook attach and can be lost (live case
        /// 2026-07-16, Helios: finalize arrived, abort with the probe never did). Exceptions are
        /// swallowed INTO the return value — a throwing probe inside a string interpolation would
        /// silently eat the whole Error it rides on.
        /// </summary>
        public static string DescribeActiveSetSafe()
        {
            try
            {
                if (AssetDatabase<ParadoxMods>.instance?.dataSource is ParadoxModsDataSource ds)
                    return DescribeActiveSet(ds);
                return "no-ds";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — it is folded into the
            // returned string, which rides the FinalizeMissing Error line. This probe runs while a
            // log line is being composed inside the log pipeline; calling the logger from here
            // would recurse into the very hook being serviced.
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// The SDK's OWN activation row for our mod, read SYNCHRONOUSLY at verdict time:
        /// <c>IModsManagementService.IsIncludedInActivePlayset</c> (<c>ModsService.cs:767</c>) →
        /// <c>ModsManagementService.cs:616</c> → <c>ModsCache.IsModInActivePlayset</c>
        /// (<c>ModsCache.cs:357</c>). No disk, no backend call, no state written — the only assignment
        /// on the path is the out-parameter (<c>ModsCache.cs:362,365</c>). The <c>ModBase</c> argument
        /// costs nothing to build, the same way <see cref="MatchSdkLocalPath"/> already builds it.
        ///
        /// Cost, corrected 2026-07-26 against the decompile (an earlier version of this comment claimed
        /// "two dictionary reads" and was wrong): ONE dictionary read for the playset
        /// (<c>ModsAndPlaysetsCache.cs:155</c>) plus a LINEAR string-compare scan of every mod row in it
        /// (<c>PlaysetData.cs:148-155</c>), taken under two monitors — <c>LockObject</c>
        /// (<c>ModsAndPlaysetsCache.cs:153</c>) and <c>lock (this)</c> on the playset
        /// (<c>PlaysetData.cs:146</c>). They are taken in sequence, not nested, and no I/O or await sits
        /// under either, so this cannot deadlock against the SDK's own writers and cannot stall the
        /// caller on disk; it is microseconds on a 150-mod playset, but it is O(n), not O(1).
        ///
        /// Splits what <c>activeSet</c> alone cannot: <c>included=True</c> sitting next to
        /// <c>idSeen=False</c> means the SDK still holds our row and it is vanilla's runtime active set
        /// that lost us (the files-present α bucket); <c>included=False</c> means the activation row
        /// is not visible right now — the state the sync path actually decides removals from.
        ///
        /// READ IT AS "not visible at this instant", never as "the row was deleted". The sync rebuilds
        /// the live playset object through TWO separate critical sections — <c>ClearModsFromSource</c>
        /// (<c>PlaysetData.cs:238</c>) then <c>AddMod</c> (<c>:223</c>) — so a read landing between them
        /// sees zero PdxMods rows for a playset that has them. That window is exactly when this probe
        /// fires at a deletion verdict, which is why the watchdog samples this field TWICE (once at
        /// detection, once in its follow-up line): False→True across the pair means the first read fell
        /// into the rebuild, not that the row ever left. The converse is just as weak — <c>included=True</c>
        /// with the folder gone also happens during an ordinary update's re-extraction
        /// (<c>ModsDownloaderService.cs:205,210</c>), so it is not by itself proof of the files-present α
        /// bucket. Print <c>syncOngoing</c> next to it or the field cannot be read at all.
        ///
        /// NOT a duplicate of the installedSet probe's <c>activeRow</c>: that one rides the async
        /// <c>Mods.List()</c> and frequently has not landed by the verdict, while this reads the same
        /// local store vanilla itself consumes, in the frame the Error line is composed. Exceptions fold
        /// into the return value (same contract as <see cref="DescribeActiveSetSafe"/> — this runs
        /// inside the log pipeline, so the logger must not be called from here).
        /// </summary>
        public static string DescribeSdkRowSafe()
        {
            try
            {
                string modId = StableModId();
                if (modId.Length == 0)
                    return "dev-install"; // no Paradox ModId — there is no SDK playset row to read

                if (!TryGetSdkContext(out IContext? ctx))
                    return "no-context";

                bool included = ctx.Mods.Management.IsIncludedInActivePlayset(
                    new ModBase(SourceType.PdxMods, modId), out bool enabled);
                return $"included={included}, enabled={enabled}";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // returned string riding the FinalizeMissing Error line; logging from inside the log
            // pipeline would recurse into the hook being serviced (same as DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// Is a Paradox mod-sync (download / patch / integrity pass) in flight AT THIS INSTANT, read
        /// straight off the platform. The companion field <c>sdkRow</c> cannot be interpreted without
        /// it: the sync rebuilds the live playset object through two separate critical sections, so
        /// <c>included=False</c> observed while a sync runs may be the rebuild window rather than a
        /// missing row (see <see cref="DescribeSdkRowSafe"/>).
        ///
        /// A SECOND source of this fact next to <c>ParadoxNativeLoader.ModSyncOngoing</c>, on purpose
        /// and not a duplicate: that one is an event-driven latch owned by the prefab loader and
        /// readable only by its owner, while callers outside that ownership (the folder watchdog) need
        /// the value synchronously at verdict time. Both read the same platform property — the loader
        /// seeds itself from it too (ParadoxNativeLoader.SubscribeModSync).
        ///
        /// "not-initialized" is a distinct answer rather than an assumed false: on an uninitialised
        /// platform the <c>isSyncOnGoing</c> getter walks a null SDK context, so it must not be read at
        /// all there. Exceptions fold into the return value (same contract as
        /// <see cref="DescribeActiveSetSafe"/> — this runs inside the log pipeline).
        /// </summary>
        public static string DescribeSyncOngoingSafe()
        {
            try
            {
                var pdx = Colossal.PSI.Common.PlatformManager.instance
                    ?.GetPSI<Colossal.PSI.PdxSdk.PdxSdkPlatform>("PdxSdk");
                if (pdx == null)
                    return "no-platform";
                if (!pdx.isInitialized)
                    return "not-initialized";
                return pdx.isSyncOnGoing ? "True" : "False";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // returned string riding the deletion Error line; logging from inside the log pipeline
            // would recurse into the hook being serviced (same as DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// Is our UIModuleAsset (CivicSurvival.mjs) registered in the global AssetDatabase — i.e. did
        /// vanilla's <c>ModManager.InitializeUIModules</c>/<c>AddUIModule</c> get the chance to mount
        /// our folder as a <c>ui-mods</c> host location? The React bundle rides the same
        /// activation-driven asset registration as the <c>.cok</c> prefabs, so form α can starve it
        /// the same way — with the bundle unmounted, every React surface (including the
        /// ModLoadFailure modal that carries the player-facing fix instructions) physically cannot
        /// render, while the C# assembly keeps running from memory. This probe makes that state a
        /// per-report fact for the files-present bucket (dir-absent already implies the .mjs file
        /// itself is gone). Read-only: enumerates the already-built registry with the same
        /// SearchFilter API vanilla itself uses — loads nothing, writes nothing. Exceptions fold
        /// into the return value (same contract as <see cref="DescribeActiveSetSafe"/>).
        /// </summary>
        public static string DescribeUiModuleSafe()
        {
            try
            {
                // Our bundle's on-disk name is fixed by the build (webpack output). Matching by
                // file name covers both delivery shapes: pdx_mods/<id>_<v>/CivicSurvival.mjs and a
                // local dev install's Mods/CivicSurvival/CivicSurvival.mjs.
                const string ourBundle = "CivicSurvival.mjs";
                int total = 0;
                string? oursPath = null;
                foreach (UIModuleAsset asset in AssetDatabase.global.GetAssets(default(SearchFilter<UIModuleAsset>)))
                {
                    total++;
                    string? path = asset?.path;
                    if (oursPath == null && path != null
                        && string.Equals(Path.GetFileName(path), ourBundle, System.StringComparison.OrdinalIgnoreCase))
                        oursPath = path;
                }

                return oursPath != null
                    ? $"registered=True, path={ModPaths.SanitizePathTail(oursPath)}, modules={total}"
                    : $"registered=False, modules={total}";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // returned string riding the FinalizeMissing Error line; logging from inside the log
            // pipeline would recurse into the hook being serviced (same as DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        // ---- boot active-set probe ---------------------------------------------------------
        // Samples the SDK's active-mod set as early in the process as possible — BEFORE the first
        // in-session playset sync can rewrite it. The 2026-07-21 α sessions proved the
        // finalize-time activeSet (idSeen=False) cannot say WHEN the activation row died: the DLL
        // and the UI bundle had loaded normally at boot (activation was alive for the boot scan),
        // and the set read without our id only after the first sync ~2 min in deleted the instance
        // folder. This latch turns "was the row alive at boot" into a per-report fact:
        // FinalizeMissing prints it as activeSetBoot= next to the at-finalize activeSet=.
        // (Replaces the dllDir probe: Assembly.Location is ALWAYS empty for CS2 mod assemblies —
        // they are byte-loaded — so that field could never expose the version skew it was built
        // for; all three first live reports showed dllDir=empty.)
        //
        // Registered as a MainThreadDispatcher updater from Mod.OnLoad: GameManager.Update pumps
        // the dispatcher unconditionally every frame, menu included (decompile
        // GameManager.cs:713), so the poll starts frames after the DLL loads. Latches the first
        // NON-EMPTY answer (the boot playset scan has populated m_ActiveMods) and unregisters; an
        // answer still empty at the cap latches as empty-thru (offline / γ-like boot).
        private static volatile string? s_BootActiveSet;
        private static int s_BootProbeStarted;
        private const double BOOT_PROBE_CAP_SECONDS = 600.0;
        private const int BOOT_PROBE_TICK_STRIDE = 30;

        /// <summary>Boot-time active-set answer for the FinalizeMissing Error line — never throws.</summary>
        public static string BootActiveSetSafe()
            => s_BootActiveSet ?? (s_BootProbeStarted == 0 ? "not-started" : "pending");

        /// <summary>
        /// Arm the one-shot boot active-set sampler. Called from <c>Mod.OnLoad</c> (Paradox installs
        /// only — a dev install has no playset row to sample); self-guarded against double starts.
        /// </summary>
        public static void StartBootActiveSetProbe()
        {
            if (Interlocked.Exchange(ref s_BootProbeStarted, 1) != 0)
                return;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            int tick = 0;
            MainThreadDispatcher.RegisterUpdater(() =>
            {
                // Gentle poll: one enumeration attempt per BOOT_PROBE_TICK_STRIDE frames.
                if (++tick % BOOT_PROBE_TICK_STRIDE != 0)
                    return false;
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
                // probe string riding the FinalizeMissing Error line (same as DescribeActiveSetSafe).
                try
                {
                    if (clock.Elapsed.TotalSeconds > BOOT_PROBE_CAP_SECONDS)
                    {
                        s_BootActiveSet ??= $"empty-thru:{(long)clock.Elapsed.TotalSeconds}s";
                        return true; // cap reached — unregister
                    }

                    if (AssetDatabase<ParadoxMods>.instance?.dataSource is not ParadoxModsDataSource ds)
                        return false; // SDK data source not up yet — keep polling

                    string answer = DescribeActiveSet(ds);
                    if (answer.StartsWith("activeMods=0", System.StringComparison.Ordinal))
                        return false; // boot playset scan hasn't populated the set yet

                    s_BootActiveSet = $"{answer}, atS={(long)clock.Elapsed.TotalSeconds}";
                    return true; // latched — unregister
                }
                catch (System.Exception e)
                {
                    s_BootActiveSet = $"probe-failed:{e.GetType().Name}";
                    return true;
                }
#pragma warning restore CIVIC052
            });
        }

        /// <summary>
        /// Directory to look for our own on-disk files (<c>.cok</c>, icons, audio): the live active
        /// instance path when resolvable, else the boot-pinned <see cref="ModPaths.ModInstallDirectory"/>.
        /// Fails safe to the boot path so early-load (active set not yet populated) and local dev keep
        /// working exactly as before.
        /// </summary>
        public static string LiveInstallDirectoryOrBoot()
        {
            if (AssetDatabase<ParadoxMods>.instance?.dataSource is ParadoxModsDataSource ds
                && MatchLivePath(ds, log: false) is string live)
                return live;
            return ModPaths.ModInstallDirectory;
        }

        // ---- installedSet probe ------------------------------------------------------------
        // "Is our mod INSTALLED" answered by the SDK itself, not by our own disk heuristics:
        // Context.Mods.List() merges the backend subscribed list with the SDK's local install
        // cache, INDEPENDENT of playset activation — exactly the question activeSet cannot
        // answer. activeSet's idSeen=False (form α: dropped from the ACTIVE set) splits into
        // "still installed per the SDK, activation desynced" vs "not installed at all".
        // Read-only: List() issues one backend GET (GetAllSubscribed) + a local cache read; it
        // never queues downloads, never touches sync state. Async by design: the probe starts
        // once at loader activation and lands its answer in a volatile string; FinalizeMissing
        // reads whatever has arrived — the frame is never blocked. "pending" = finalize beat
        // the answer (offline backend can stall the GET); "not-started" = non-Paradox launch
        // or SDK context creation failed (the probe is only armed on a healthy platform).
        private static volatile string? s_InstalledSet;
        private static int s_InstalledProbeStarted;

        /// <summary>Probe answer for the FinalizeMissing Error line — never throws, never blocks.</summary>
        public static string InstalledSetSafe()
            => s_InstalledSet ?? (s_InstalledProbeStarted == 0 ? "not-started" : "pending");

        /// <summary>
        /// Fire the one-shot installed-set probe. Called from ParadoxNativeLoader's subscribe
        /// path once the PdxSdk platform is known healthy; self-guarded against double starts.
        /// The only reflected step is the private <c>m_SDKContext</c> field, and it happens in the
        /// shared <see cref="TryGetSdkContext"/> (the caller's already-resolved platform is handed
        /// straight to it) — everything after is typed against the shipped PDX.SDK contracts.
        /// </summary>
        public static void StartInstalledSetProbe(Colossal.PSI.PdxSdk.PdxSdkPlatform pdx)
        {
            if (Interlocked.Exchange(ref s_InstalledProbeStarted, 1) != 0)
                return;
#pragma warning disable CIVIC052 // False positive: every exception IS reported — folded into the
            // probe string that rides the FinalizeMissing Error line (same pattern as
            // DescribeActiveSetSafe). Logging from the thread-pool continuation would race the
            // log hook this diagnostic exists to feed.
            try
            {
                if (!TryGetSdkContext(out IContext? resolved, pdx))
                {
                    s_InstalledSet = "no-context";
                    return;
                }
                // Non-nullable alias: the continuation below captures it, and nullable flow state does
                // not carry into a lambda body for a captured local.
                IContext ctx = resolved;

                string modId = StableModId();
                if (modId.Length == 0)
                {
                    s_InstalledSet = "dev-install";
                    return;
                }

                _ = ctx.Mods.List().ContinueWith(t =>
                {
                    try
                    {
                        if (t.IsFaulted || !t.IsCompleted || t.Result?.Mods == null)
                        {
                            s_InstalledSet =
                                $"probe-failed:{t.Exception?.GetBaseException().GetType().Name ?? "no-result"}";
                            return;
                        }

                        int total = t.Result.Mods.Count;
                        IMod? ours = null;
                        int entries = 0;
                        foreach (IMod m in t.Result.Mods)
                        {
                            if (m?.Id != modId)
                                continue;
                            entries++;
                            // Prefer the entry the SDK considers materialised on disk.
                            if (ours == null || (string.IsNullOrEmpty(ours.LocalData?.FolderAbsolutePath)
                                                 && !string.IsNullOrEmpty(m.LocalData?.FolderAbsolutePath)))
                                ours = m;
                        }

                        if (ours == null)
                        {
                            s_InstalledSet = $"idListed=False, mods={total}";
                            return;
                        }

                        string path = string.IsNullOrEmpty(ours.LocalData?.FolderAbsolutePath)
                            ? "empty"
                            : ModPaths.SanitizePathTail(ours.LocalData!.FolderAbsolutePath);
                        string tail = entries > 1 ? $", entries={entries}" : string.Empty;
                        string activeRow = DescribeActivePlaysetRow(ctx, ours);
                        s_InstalledSet = $"idListed=True, ver={ours.Version ?? "null"}, path={path}, activeRow=[{activeRow}]{tail}";
                    }
                    catch (System.Exception e)
                    {
                        s_InstalledSet = $"probe-failed:{e.GetType().Name}";
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
            }
            catch (System.Exception e)
            {
                s_InstalledSet = $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        // ---- raw activation-row probe --------------------------------------------------------
        // The α discriminator idSeen=False CANNOT tell "the activation row is gone" from "the row
        // exists but is unreadable": the value vanilla reads is filtered twice before we see it —
        // the SDK returns only enabled rows (PlaysetsService.GetActivePlaysetEnabledMods:
        // where m.IsEnabled), and PdxSdkPlatform.GetModsInActivePlayset then drops rows whose
        // LocalData is null/pathless. Both exits look identical to the reader. The Mods.List()
        // rows this probe reads bypass BOTH filters: ModsCache.FillModLocalData copies every
        // playset row (enabled or not) into IMod.Playsets and stamps the active-playset flags,
        // so this is the raw activation row as stored in the SDK's local DB.
        //   absent                → no row for our id in the active playset (row genuinely lost)
        //   enabled=…, prefVer=…  → the row exists; prefVer against installedSet's ver names the
        //                           version skew, and path=empty next to it marks the
        //                           LocalData-filter bucket (row present, vanilla still drops us)
        //   no-active-playset     → the SDK reports no active playset at all
        private static string DescribeActivePlaysetRow(IContext ctx, IMod ours)
        {
            try
            {
                string activeId = ctx.Mods.Playsets.GetActivePlaysetId();
                if (string.IsNullOrEmpty(activeId))
                    return "no-active-playset";

                PlaysetInMod? row = null;
                int rows = 0;
                if (ours.Playsets != null)
                {
                    foreach (PlaysetInMod p in ours.Playsets)
                    {
                        rows++;
                        if (row == null && p?.PlaysetId == activeId)
                            row = p;
                    }
                }

                // Named for what they ACTUALLY hold, corrected 2026-07-26 against the decompile. The SDK
                // property is called IsSubscribedInActivePlayset, and reading that name as "the player's
                // subscription is alive" is wrong: on this path FillModLocalData first clears the flag
                // (ModsCache.cs:264-265) and then sets it from a LOCAL playset-cache row matched on source
                // and id (:273, PlaysetData.cs:144-159) — the server's own value for the same field
                // (ModMapper.cs:181) is discarded by List() before we ever see it. So this says "a row for
                // us exists in the locally cached copy of the active playset", which can lag the server by
                // as long as the local cache goes unrefreshed. Whether the SUBSCRIPTION is alive is the
                // playset-listing probe's question, not this one.
                string flags = $"rowInActive={ours.IsSubscribedInActivePlayset}, rowEnabled={ours.IsEnabledInActivePlayset}";
                return row == null
                    ? $"absent, rowsElsewhere={rows}, {flags}"
                    : $"enabled={row.ModIsEnabled}, prefVer={row.PreferredVersion ?? "null"}, rows={rows}, {flags}";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // activeRow segment of the installedSet probe string riding the FinalizeMissing Error
            // line (same pattern as the enclosing probe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }

        // ---- active-playset listing probe ----------------------------------------------------
        // "Does the SERVER still list us in the active playset" — the one input that decides the
        // folder's fate and that no probe has ever measured. The sync path removes an install when the
        // BACKEND playset listing disagrees with the local playset cache (PlaysetAndModsSyncService
        // fills remoteMods from ListAllModsInPlayset, then IsModInAnyPlaysetOnDisk version-matches
        // against the local cache); vanilla's GetActiveMods — the source of our activeSet/idSeen — is
        // not consulted anywhere in that chain. So idSeen=False can never say whether the server
        // dropped us or only the runtime cache did.
        //
        // Three constraints, all decompile-derived, all mandatory:
        //   * limit MUST be int.MaxValue. ModsInformationService defaults limit=10, page=1
        //     (:166-167), so the convenient call would report "not listed" on ANY playset larger than
        //     ten mods. The SDK's own sync path passes int.MaxValue for exactly this reason.
        //   * this is NOT one network call. Besides the backend GET the method runs FillModLocalData
        //     over every returned mod, and GetModLocalData walks ALL mod roots with PathExists plus a
        //     thumbnail check — hundreds of syscalls on a 150-mod playset. Hence the Interlocked
        //     one-shot latch: a failing session produces three-four verdicts (one per city load) and
        //     each would repeat the whole sweep.
        //   * never awaited from the main thread. The answer lands in a volatile string and the verdict
        //     prints whatever arrived; "pending" is a legal answer, and so is an offline backend.
        // Armed at the failure verdicts (CivicPrefabInitSystem.FinalizeMissing /
        // LogRevokedCoreEntities) and, through the second slot below, at the folder-deletion verdict —
        // so a healthy session pays nothing at all. The first verdict of a
        // process therefore prints "pending" — the request and its PathExists sweep cannot outrun the
        // string being composed — and the later verdicts of the same session carry the answer.
        //
        // TWO SLOTS, not one, and they are not interchangeable. The verdict slot may already have been
        // spent minutes BEFORE a folder deletion (a prefab failure fires first in some sessions), and an
        // answer captured before the event describes the state that preceded it. The deletion slot is
        // armed by ModInstanceWatchSystem the moment the folder goes missing, so its answer is the one
        // that can be attributed to the removal. Each slot has its own latch: a shared latch would let
        // whichever verdict came first silence the other permanently.
        private static volatile string? s_PlaysetListing;
        private static int s_PlaysetProbeStarted;
        private static volatile string? s_PlaysetListingAfterDeletion;
        private static int s_PlaysetAfterDeletionProbeStarted;

        // The deletion slot's source= verdict as a VALUE, published alongside the rendered string.
        // The classifier needs the attribution ("did the server list us"), and re-deriving it by
        // parsing the report line would tie a decision to a text format that exists for humans — one
        // reworded field and the classification silently changes meaning.
        private static volatile string? s_AfterDeletionSource;

        /// <summary>
        /// Attribution of the post-deletion listing: "backend", "backend-or-local", "absent",
        /// "local-unknown", or "pending" when no answer has landed. See the source= note in the probe.
        /// </summary>
        public static string PlaysetListingAfterDeletionSource() => s_AfterDeletionSource ?? "pending";

        /// <summary>Backend playset-listing answer for the failure Error lines — never throws, never blocks.</summary>
        public static string PlaysetListingSafe()
            => s_PlaysetListing ?? (s_PlaysetProbeStarted == 0 ? "not-started" : "pending");

        /// <summary>
        /// Backend playset-listing answer sampled AFTER the install folder was deleted, for the
        /// watchdog's follow-up line — never throws, never blocks. Separate from
        /// <see cref="PlaysetListingSafe"/> on purpose: see the two-slot note above.
        /// </summary>
        public static string PlaysetListingAfterDeletionSafe()
            => s_PlaysetListingAfterDeletion
               ?? (s_PlaysetAfterDeletionProbeStarted == 0 ? "not-started" : "pending");

        /// <summary>
        /// Fire the one-shot backend playset-listing probe. Called from the prefab-failure verdicts,
        /// which are the only points where the answer is worth its cost; self-guarded against double
        /// starts, so the three-four verdicts of a failing session issue exactly one request between
        /// them. Read-only: <c>ListModsInPlayset</c> issues a GET and fills local data, it never
        /// enables/disables anything and never queues a download.
        /// </summary>
        public static void StartPlaysetListingProbe()
        {
            if (Interlocked.Exchange(ref s_PlaysetProbeStarted, 1) != 0)
                return;
            RunPlaysetListingProbe(afterDeletion: false);
        }

        /// <summary>
        /// Fire the same probe into the deletion slot, once per process, from the folder-deletion
        /// verdict (<c>ModInstanceWatchSystem</c>). This is the ONE question that separates the two
        /// roots of a deletion with no replacement: does the SERVER still list us in the active playset?
        /// If it does, nobody removed the mod on purpose and the sync took an install it had no listing
        /// reason to take; if it does not, the removal followed the listing it is supposed to follow.
        ///
        /// Deliberately NOT a second <c>Mods.List()</c> (weighed and rejected 2026-07-26): that call
        /// rebuilds the SDK's "latest version on disk" index (<c>ModsCache.cs:200</c>), which is an input
        /// to <c>IsModInAnyPlaysetOnDisk</c> — the very predicate deciding the fate of removals already
        /// queued (<c>ModsDownloadManagerService.cs:270-277</c>) — and firing it while that queue is
        /// draining is the last place a read-only probe should be. It also costs roughly twice the
        /// filesystem traffic (it sweeps every mod root; this listing fills local data only for returned
        /// rows, <c>ModsInformationService.cs:186-189</c>) and its row flags come from the local cache,
        /// while this one carries the server's own <c>IsEnabled</c>/<c>PreferredVersion</c>.
        /// </summary>
        public static void StartPlaysetListingProbeAfterDeletion()
        {
            if (Interlocked.Exchange(ref s_PlaysetAfterDeletionProbeStarted, 1) != 0)
                return;
            RunPlaysetListingProbe(afterDeletion: true);
        }

        /// <summary>Publishes a probe result into the slot the caller armed.</summary>
        private static void PublishPlaysetListing(bool afterDeletion, string value)
        {
            if (afterDeletion)
                s_PlaysetListingAfterDeletion = value;
            else
                s_PlaysetListing = value;
        }

        /// <summary>
        /// The probe body shared by both slots — one implementation, because the two callers differ
        /// only in WHEN they fire and WHERE the answer lands. Latching is the caller's job (each slot
        /// guards its own), so this is only ever entered once per slot per process.
        /// </summary>
        private static void RunPlaysetListingProbe(bool afterDeletion)
        {
#pragma warning disable CIVIC052 // False positive: every exception IS reported — folded into the
            // probe string that rides the failure Error line (same pattern as StartInstalledSetProbe).
            // Logging from the thread-pool continuation would race the log hook this diagnostic feeds.
            try
            {
                string modId = StableModId();
                if (modId.Length == 0)
                {
                    PublishPlaysetListing(afterDeletion, "dev-install");
                    return;
                }

                if (!TryGetSdkContext(out IContext? ctx))
                {
                    PublishPlaysetListing(afterDeletion, "no-context");
                    return;
                }

                string playsetId = ctx.Mods.Playsets.GetActivePlaysetId();
                if (string.IsNullOrEmpty(playsetId))
                {
                    PublishPlaysetListing(afterDeletion, "no-active-playset");
                    return;
                }

                var info = ctx.Mods.Information;

                // TWO calls, and the answer is their difference — a single merged listing cannot be
                // read on its own. With the backend unreachable the SDK still returns our row from the
                // local playset cache: the offline half contributes every source, ours included, once
                // the online call failed (ModsInformationService.cs:212), TotalCount adds those local
                // rows to the online count (:224), and Success is hardcoded true on that path (:221).
                // So "listed" alone cannot separate "the server confirms us" from "we never reached the
                // server" — the first version of this probe claimed it could, by reading total=, and
                // that rule was wrong. The local-only call establishes the baseline; a row present in
                // the online call but absent from it is the backend's own.
                _ = info.ListModsInPlayset(playsetId, limit: int.MaxValue, page: 1, includeOnline: false)
                    .ContinueWith(localTask => _ = info
                        .ListModsInPlayset(playsetId, limit: int.MaxValue, page: 1, includeOnline: true)
                        .ContinueWith(onlineTask =>
                        {
                            try
                            {
                                // Both tasks are already completed inside their own continuation, so
                                // reading Result here is not a synchronous wait — it cannot block and
                                // cannot deadlock (VSTHRD002 cannot see that from the lambda).
#pragma warning disable VSTHRD002
                                var online = onlineTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                                    ? onlineTask.Result
                                    : null;
                                var local = localTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
                                    ? localTask.Result
                                    : null;
#pragma warning restore VSTHRD002
                                if (online?.Mods == null)
                                {
                                    PublishPlaysetListing(afterDeletion,
                                        $"probe-failed:{onlineTask.Exception?.GetBaseException().GetType().Name ?? "no-result"}");
                                    return;
                                }

                                ILocalPlaysetMod? ours = FindOurRow(online.Mods, modId);
                                var localRows = local?.Mods;
                                bool inLocal = localRows != null && FindOurRow(localRows, modId) != null;

                                // source= is the honest part of this field:
                                //   backend           — present online, absent locally: the server listed us;
                                //   backend-or-local  — present in both: indistinguishable BY DESIGN, because
                                //                       an offline answer looks exactly like a confirmation;
                                //   absent            — the server does not list us AND the local cache does
                                //                       not either, which is the removal decision's own input;
                                //   local-unknown     — the baseline call failed, so nothing can be attributed.
                                string source;
                                if (ours == null)
                                    source = "absent";
                                else if (localRows == null)
                                    source = "local-unknown";
                                else if (inLocal)
                                    source = "backend-or-local";
                                else
                                    source = "backend";

                                if (afterDeletion)
                                    s_AfterDeletionSource = source;

                                string tail = $"source={source}, localRows={(localRows?.Count ?? -1)}, "
                                              + $"onlineRows={online.Mods.Count}, total={online.TotalCount}, ok={online.Success}";

                                PublishPlaysetListing(afterDeletion, ours == null
                                    ? $"listed=False, {tail}"
                                    : $"listed=True, enabled={ours.IsEnabled}, ver={ours.Version ?? "null"}, "
                                      + $"prefVer={ours.PreferredVersion ?? "null"}, {tail}");
                            }
                            catch (System.Exception e)
                            {
                                PublishPlaysetListing(afterDeletion, $"probe-failed:{e.GetType().Name}");
                            }
                        }, System.Threading.Tasks.TaskScheduler.Default),
                        System.Threading.Tasks.TaskScheduler.Default);
            }
            catch (System.Exception e)
            {
                PublishPlaysetListing(afterDeletion, $"probe-failed:{e.GetType().Name}");
            }
#pragma warning restore CIVIC052
        }

        /// <summary>
        /// Our own row in a playset listing, or null. Matched on source AND id: the offline half of
        /// the merge contributes rows from other sources, whose ids are folder names rather than our
        /// numeric ModId, so an id-only match can hit a stranger.
        /// </summary>
        private static ILocalPlaysetMod? FindOurRow(
            System.Collections.Generic.IEnumerable<ILocalPlaysetMod>? rows, string modId)
        {
            if (rows == null)
                return null;
            foreach (ILocalPlaysetMod m in rows)
            {
                if (m != null && m.Source == SourceType.PdxMods && m.Id == modId)
                    return m;
            }
            return null;
        }
    }
}
