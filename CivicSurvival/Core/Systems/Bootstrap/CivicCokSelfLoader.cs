using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Disk-presence check for the mod's core threat models (<c>AttackDrone.cok</c> /
    /// <c>Rocket.cok</c>), used to distinguish a broken/incomplete download (file absent) from a
    /// load failure (file on disk but never registered into <c>PrefabSystem</c>). Resolved against
    /// <see cref="ModInstallResolver.LiveInstallDirectoryOrBoot"/> — the live active-playset instance
    /// path (boot path fallback) — so it follows a Paradox instance-folder swap and works for both a
    /// dev deploy and a Paradox Mods subscriber.
    /// </summary>
    internal static class CivicCokSelfLoader
    {
        private static readonly LogContext Log = new("ModLoad.DiskProbe");

        // Core threat models whose absence kills the mod's gameplay (drones + ballistics). The .cok
        // filename is the prefab name + ".cok" (build copies them to the mod root).
        private static readonly string[] s_CoreCokFiles = { "AttackDrone.cok", "Rocket.cok" };

        // Returns the comma-joined core .cok NOT on disk (empty string = all present).
        // ParadoxNativeLoader gates on this (wait until the files arrive); FinalizeMissing uses it to
        // classify a genuine miss (file absent = bad download vs file present = load/registration failure).
        public static string MissingCoreCokOnDisk()
        {
            // Resolve the mod's CURRENT install directory from the live active playset, not the
            // boot-pinned ModPaths.ModInstallDirectory: a Paradox instance-folder swap
            // (147665_18 → _19) leaves the pinned path pointing at a torn-down folder whose .cok
            // have moved, which would false-flag them MISSING. Falls back to the boot path when the
            // active set has no match (early load / local dev).
            string dir = ModInstallResolver.LiveInstallDirectoryOrBoot();
            var missing = new List<string>(s_CoreCokFiles.Length);
            foreach (var cok in s_CoreCokFiles)
            {
                if (!File.Exists(Path.Combine(dir, cok)))
                    missing.Add(cok);
            }
            return string.Join(", ", missing);
        }

        // True when every core .cok exists in the given directory. Existence-only by intent: the
        // caller (ModInstanceWatchSystem's sibling listing) classifies OTHER instance folders it
        // will never load from, so the readability probe above would be overkill there.
        public static bool CoreCokPresentIn(string dir)
        {
            foreach (var cok in s_CoreCokFiles)
            {
                if (!File.Exists(Path.Combine(dir, cok)))
                    return false;
            }
            return true;
        }

        // True when every core .cok on disk can actually be opened for read. Existence alone is not
        // arrival: a file appearing via a non-atomic writer (antivirus un-quarantine, cloud-sync
        // restore, manual copy — Paradox's own install is atomic and never hits this) is visible to
        // File.Exists while still half-written, and firing the vanilla asset scan at that moment
        // throws a sharing-violation IOException inside PopulateFromDirectory and skips the file
        // with no retry (live repro 2026-07-15). The probe opens with the most permissive share
        // flags so it never blocks the writer or a concurrent Paradox move/delete; only a genuinely
        // exclusive hold (write in progress / external lock) fails it. A missing file fails it too —
        // the caller's existence gate runs first, this is just belt-and-braces against a delete race.
        public static bool AllCoreCokReadable()
        {
            string dir = ModInstallResolver.LiveInstallDirectoryOrBoot();
            foreach (var cok in s_CoreCokFiles)
            {
                try
                {
                    using var probe = new FileStream(
                        Path.Combine(dir, cok), FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Debug-gated: this fires on every throttle tick while a writer holds the
                    // file — at Info a long external lock would spam the log; the caller already
                    // logs once on the awaiting-readable phase transition.
                    if (Log.IsDebugEnabled)
                        Log.Debug($"Core .cok present but not yet readable ({cok}): {ex.GetType().Name}");
                    return false;
                }
            }
            return true;
        }

        // Paradox local patch-state probe for the MISSING bucket — forensics of HOW the install got
        // gutted, not a repair selector. Decompile-verified: mod "installedness" for the Paradox
        // sync is the mere existence of the <id>_<version> folder; .cpatch/<app>/version is only
        // ever read INSIDE an already-queued download flow, which never starts while the folder
        // exists. So EVERY state below is terminal (no self-heal, no re-delivery) and the repair is
        // the same for all of them — remove the whole instance folder. The probe still matters as
        // origin forensics:
        //   cpatch=absent           — patch-state lost along with the .cok (first live reports
        //                             2026-07-15 were 2/2 this signature).
        //   cpatch=present,ver=<v>  — .cok lost while Paradox's own state survived (population B).
        //   cpatch=present,ver=absent / cpatch=empty — partial state, origin differs again.
        // Rides the FinalizeMissing Error string (prod forwards only LogType.Error), same channel as
        // the disk-check itself. Paths are PDX.SDK-decompile-verified: .cpatch/<app>/version under
        // the mod install folder (LocalRepoManagerFactory / LocalRepoManager.GetInstalledVersion).
        public static string CorePatchState()
        {
            string cpatch = Path.Combine(ModInstallResolver.LiveInstallDirectoryOrBoot(), ".cpatch");
            if (!Directory.Exists(cpatch))
                return "cpatch=absent";

            try
            {
                // <app> is the single subfolder Paradox writes under .cpatch (LocalRepoManagerFactory
                // takes directories[0]); take the first rather than hard-code the app name.
                string? appDir = Directory.EnumerateDirectories(cpatch).FirstOrDefault();
                if (appDir == null)
                    return "cpatch=empty";

                string versionFile = Path.Combine(appDir, "version");
                if (!File.Exists(versionFile))
                    return "cpatch=present,ver=absent";

                // The version file is a short single-line version string; read just that line via a
                // StreamReader (bounded read) instead of loading the whole file (CIVIC028 OOM guard).
                using var reader = new StreamReader(versionFile);
                string version = reader.ReadLine()?.Trim() ?? string.Empty;
                return $"cpatch=present,ver={version}";
            }
            catch (Exception ex)
            {
                Log.Warn($"CorePatchState probe of {ModPaths.SanitizePathTail(cpatch)} failed: {ex}");
                return $"cpatch=probe-failed({ex.GetType().Name})";
            }
        }

        // Skyve-presence probe for the MISSING bucket. Skyve is an external manager app plus a
        // PERMANENT background service (Skyve.Service.exe), both carrying their OWN copy of
        // PDX.SDK pointed at the same .cache/Mods root as the game (decompile-verified) — a
        // second, uncoordinated writer over the install folders whose sync can download,
        // upgrade and recursively DELETE mod folders while the game holds their files loaded.
        // A Skyve-side delete/upgrade dying on a locked file mid-recursion leaves exactly the
        // partial-folder signature this bucket reports (.cpatch dies first — alphabetical).
        // This field lets every prod report vote on that hypothesis:
        //   mod+svc / mod / svc — the Skyve bridge mod is loaded in-session / its background
        //   service is running right now / both;  none — neither signal present.
        public static string SkyvePresence()
        {
            try
            {
                bool modLoaded = false;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string? name = asm.GetName().Name;
                    if (name != null && name.StartsWith("Skyve", StringComparison.OrdinalIgnoreCase))
                    {
                        modLoaded = true;
                        break;
                    }
                }

                bool serviceRunning = false;
                var procs = System.Diagnostics.Process.GetProcessesByName("Skyve.Service");
                serviceRunning = procs.Length > 0;
                foreach (var p in procs)
                    p.Dispose();

                if (modLoaded && serviceRunning) return "mod+svc";
                if (modLoaded) return "mod";
                if (serviceRunning) return "svc";
                return "none";
            }
            catch (Exception ex)
            {
                Log.Warn($"SkyvePresence probe failed: {ex}");
                return $"probe-failed({ex.GetType().Name})";
            }
        }

        // Listing caps: the install dir normally holds ~a dozen top-level entries; the caps only
        // guard against a pathological folder (player dropped files in) flooding the Error string.
        private const int LISTING_MAX_ENTRIES = 30;
        private const int LISTING_MAX_CHARS = 700;

        // Compact top-level listing of the live install directory for the MISSING bucket: entry
        // names + file sizes, sorted ordinal, capped. The two-file disk-check says WHICH core .cok
        // are gone; this says WHAT SURVIVED — the deletion fingerprint that separates loss
        // mechanisms the check cannot: «only DLL + .metadata left» (partial install/uninstall) vs
        // «everything but .cok» (selective external removal, e.g. antivirus) vs «near-empty
        // folder». PII-safe: entry names relative to the install dir only (our own
        // shipped files + Paradox service entries), no user-home prefix. Rides the FinalizeMissing
        // Error string — prod forwards only LogType.Error, same channel as the disk-check itself.
        public static string InstanceDirListing()
        {
            string dir = ModInstallResolver.LiveInstallDirectoryOrBoot();
            try
            {
                if (!Directory.Exists(dir))
                    return "dir-absent";

                // Directories first (trailing '/'), then files with sizes; each group sorted
                // ordinal so reports are comparable across players/sessions.
                var entries = new List<string>();
                foreach (string sub in Directory.EnumerateDirectories(dir).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                    entries.Add(Path.GetFileName(sub) + "/");
                foreach (string file in Directory.EnumerateFiles(dir).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                    entries.Add(Path.GetFileName(file) + ":" + FormatSize(new FileInfo(file).Length));

                if (entries.Count == 0)
                    return "empty";

                var sb = new System.Text.StringBuilder(256);
                int included = 0;
                foreach (string entry in entries)
                {
                    // +1 for the comma; stop before the string outgrows the cap.
                    if (included == LISTING_MAX_ENTRIES || sb.Length + entry.Length + 1 > LISTING_MAX_CHARS)
                        break;
                    if (included > 0)
                        sb.Append(',');
                    sb.Append(entry);
                    included++;
                }
                if (included < entries.Count)
                    sb.Append(",…+").Append(entries.Count - included);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log.Warn($"InstanceDirListing probe of {ModPaths.SanitizePathTail(dir)} failed: {ex}");
                return $"probe-failed({ex.GetType().Name})";
            }
        }

        private const long BYTES_PER_KB = 1024;
        private const long BYTES_PER_MB = BYTES_PER_KB * BYTES_PER_KB;

        // Human-compact size for the listing: bytes are exact where small sizes matter most
        // (0-byte / truncated files), KB/MB rounded — enough to tell a stub from a real asset.
        private static string FormatSize(long bytes)
        {
            if (bytes < BYTES_PER_KB)
                return bytes + "B";
            if (bytes < BYTES_PER_MB)
                return (bytes / BYTES_PER_KB) + "KB";
            return ((double)bytes / BYTES_PER_MB).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "MB";
        }
    }
}
