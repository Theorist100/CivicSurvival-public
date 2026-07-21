using System.Diagnostics;
using System.IO;
using System.Text;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Services;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Watches the mod's own install folder for deletion UNDER the live process.
    ///
    /// Why this can happen at all: the Paradox SDK's playset sync replaces the instance
    /// folder on a version update — download the new <c>&lt;modId&gt;_&lt;N+1&gt;</c>, then
    /// <c>ModsCache.DeleteModFromDisk</c> recursively deletes <c>&lt;modId&gt;_&lt;N&gt;</c> with no
    /// check for a running game (reproduced live 2026-07-19 on the private test listing;
    /// PdxSdk.log names the chain QueueRemoval → ProcessPendingRemovals → DeleteModFromDisk).
    /// The running session survives on in-memory state, but the boot-pinned paths inside
    /// already-registered vanilla assets keep pointing at the deleted folder: icons 404, and
    /// reloading a save rebuilds render batches whose SurfaceAsset.LoadProperties reads the
    /// .Surface files from disk on every rebuild by design (MeshSystem.GetMaterialIndex
    /// load→use→unload) — a poisoned load then NREs ManagedBatchSystem.CreateBatch and takes
    /// the game down.
    ///
    /// Response is deliberately non-invasive (decision 2026-07-19): no file restore, no SDK
    /// patch. One Error line with the facts for telemetry (attribution channel for the
    /// prod "prefab-absent at boot" reports — did a live-session deletion precede them?),
    /// and the ModUpdatedRestart modal telling the player to save and restart before
    /// loading anything else. The residual risk — a player who ignores the modal and
    /// reloads a save still crashes — is accepted; every stronger option (folder copy,
    /// Harmony gate on the delete, AssetDatabase re-pointing) was rejected as invasive.
    ///
    /// Cost envelope: one Directory.Exists per CHECK_INTERVAL_SECONDS (30s), main thread,
    /// Paradox installs only (a dev install's folder is rebuilt by every build — watching
    /// it would false-alarm constantly). Latches off after the first detection.
    /// </summary>
    [ActIndependent]
    public partial class ModInstanceWatchSystem : CivicSystemBase
    {
        private static readonly LogContext Log = new("ModInstanceWatch");

        private const double CHECK_INTERVAL_SECONDS = 30.0;

        // Wall-clock throttle (game-speed independent; must keep counting while paused —
        // the SDK deletes folders regardless of simulation state).
        private readonly Stopwatch m_Clock = Stopwatch.StartNew();
        private double m_NextCheckSeconds = CHECK_INTERVAL_SECONDS;
#pragma warning disable CIVIC150 // Deliberately process-lifetime, NOT save state: the folder
        // deletion is a fact about this game PROCESS (its boot-pinned paths are dead), true for
        // every city loaded until the player restarts. CS2 reuses system instances across loads,
        // so the unserialized latch persisting through save/load is exactly the wanted behavior —
        // detect once, warn once per process.
        private bool m_Latched;
#pragma warning restore CIVIC150
        private bool m_IsParadoxInstall;

        protected override void OnCreate()
        {
            base.OnCreate();
            // StableModId is derived from the boot folder name ("147665" from
            // ".../pdx_mods/147665_30"); empty means a local dev install — nothing to watch.
            m_IsParadoxInstall = ModInstallResolver.StableModId().Length != 0;
        }

        protected override void OnUpdateImpl()
        {
            if (m_Latched || !m_IsParadoxInstall)
                return;

            double now = m_Clock.Elapsed.TotalSeconds;
            if (now < m_NextCheckSeconds)
                return;
            m_NextCheckSeconds = now + CHECK_INTERVAL_SECONDS;

            // PERF-LOCK: one Directory.Exists per 30s is this system's entire budget — do not
            // add per-frame work above the throttle or wider disk scans below it (the sibling
            // listing runs once, only after a detection).
            if (Directory.Exists(ModPaths.ModInstallDirectory))
                return;

            m_Latched = true;
            Enabled = false;

            // The facts prod needs for attribution, on the one line telemetry forwards (Error):
            // when the folder vanished (session-relative) and whether a replacement instance
            // is already materialised next to it — "sibling with readable core .cok present"
            // separates a normal update-swap from a deletion with nothing left behind.
            Log.Error("Mod install folder deleted under the live process — " +
                      $"installDir={ModPaths.SanitizePathTail(ModPaths.ModInstallDirectory)}, " +
                      $"elapsedSinceBootSeconds={(long)now}, siblings=[{DescribeSiblingInstances()}]. " +
                      "Session keeps running from memory; save reload in this process would read " +
                      "surface assets from the deleted path. ModUpdatedRestart modal requested.");

#pragma warning disable CIVIC098 // ModalCoordinator.Instance is static readonly = new(), never null
#pragma warning disable CIVIC239 // Best-effort surface: a busier slot queues the request inside the
            // coordinator (lower-priority requests pend until the slot frees), and the latch above
            // guarantees this is the only show attempt of the process — nothing to retry on false.
            ModalCoordinator.Instance.TryShow("ModUpdatedRestart");
#pragma warning restore CIVIC239
#pragma warning restore CIVIC098
        }

        /// <summary>
        /// Lists sibling instances of our mod (<c>&lt;modId&gt;_*</c>) in the pdx_mods parent with
        /// a per-instance flag for readable core .cok — the "переезд vs снос" discriminator.
        /// Exceptions fold into the return value: this string rides the Error line above and a
        /// throwing probe must not eat the line it decorates.
        /// </summary>
        private static string DescribeSiblingInstances()
        {
            try
            {
                string? parent = Path.GetDirectoryName(ModPaths.ModInstallDirectory.TrimEnd('\\', '/'));
                if (parent == null || !Directory.Exists(parent))
                    return "parent-absent";

                string modId = ModInstallResolver.StableModId();
#pragma warning disable CIVIC050 // One-shot at the deletion verdict (runs once per process, after
                // the latch) — not a per-frame allocation.
                var sb = new StringBuilder(128);
#pragma warning restore CIVIC050
                foreach (string dir in Directory.GetDirectories(parent, modId + "_*"))
                {
                    if (sb.Length > 0)
                        sb.Append(", ");
                    bool cokPresent = CivicCokSelfLoader.CoreCokPresentIn(dir);
                    sb.Append(Path.GetFileName(dir)).Append(cokPresent ? "(cok=ok)" : "(cok=missing)");
                }
                return sb.Length > 0 ? sb.ToString() : "none";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — folded into the
            // returned string, which rides the deletion Error line above (same pattern as
            // ModInstallResolver.DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"probe-failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }
    }
}
