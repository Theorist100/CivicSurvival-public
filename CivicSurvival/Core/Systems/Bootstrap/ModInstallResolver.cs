using System.IO;
using System.Reflection;
using System.Threading;
using Colossal.IO.AssetDatabase;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Service.Mods.Interfaces;
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
            string modId = StableModId();
            int count = 0;
            bool idSeen = false;
            bool pathEmpty = false;
            foreach (PdxMod m in ds.GetActiveMods())
            {
                count++;
                if (modId.Length != 0 && m.id == modId)
                {
                    idSeen = true;
                    pathEmpty = string.IsNullOrEmpty(m.path);
                }
            }
            return $"activeMods={count}, idSeen={idSeen}, pathEmpty={pathEmpty}";
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

        /// <summary>
        /// The instance folder our C# assembly is actually executing from
        /// (<c>Assembly.Location</c> tail). Compared against <c>installDir</c>/<c>liveDir</c> in the
        /// FinalizeMissing line it exposes a version skew — DLL loaded from <c>_29</c> while the disk
        /// serves <c>_30</c> — which is evidence for the "α is born in the mod-update window"
        /// hypothesis (every α report so far landed within days of a release train). Read-only.
        /// </summary>
        public static string DllDirTailSafe()
        {
            try
            {
                string location = typeof(ModInstallResolver).Assembly.Location;
                if (string.IsNullOrEmpty(location))
                    return "empty";
                return ModPaths.SanitizePathTail(Path.GetDirectoryName(location) ?? location);
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // returned string riding the FinalizeMissing Error line (same as DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
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
        /// The only reflected step is the private <c>m_SDKContext</c> field — everything after
        /// is typed against the shipped PDX.SDK contracts.
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
#pragma warning disable S3011 // intentional vanilla-internal access: the SDK context has no public accessor on the platform; null-guarded if the field moves
                FieldInfo? ctxField = typeof(Colossal.PSI.PdxSdk.PdxSdkPlatform)
                    .GetField("m_SDKContext", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011
                if (ctxField?.GetValue(pdx) is not IContext ctx)
                {
                    s_InstalledSet = "no-context";
                    return;
                }

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
                        s_InstalledSet = $"idListed=True, ver={ours.Version ?? "null"}, path={path}{tail}";
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
    }
}
