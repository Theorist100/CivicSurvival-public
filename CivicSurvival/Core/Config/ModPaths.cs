using System.IO;
using System.Threading;

namespace CivicSurvival.Core.Config
{
    /// <summary>
    /// Centralized path constants for all mod file I/O.
    /// Initialized once from Mod.OnLoad() using Application.persistentDataPath.
    /// </summary>
    public static class ModPaths
    {
        private sealed class PathState
        {
            public readonly string Root;
            public readonly string Logs;
            public readonly string ModData;
            public readonly string ModInstall;

            public PathState(string gameDataRoot, string modInstall)
            {
                Root = gameDataRoot;
                Logs = Path.Combine(gameDataRoot, "Logs");
                ModData = Path.Combine(gameDataRoot, "ModData", "CivicSurvival");
                ModInstall = modInstall;
            }

            public static readonly PathState Empty = new(string.Empty, string.Empty);
        }

        private static PathState s_State = PathState.Empty;

        /// <summary>
        /// Fail-fast accessor: a path read before <see cref="Initialize"/> would
        /// otherwise silently return a RELATIVE path (PathState.Empty →
        /// Path.Combine("", "Logs") == "Logs"), sending file I/O to the process
        /// working directory. Surface the init-order bug loudly instead of
        /// fail-open — same standard as Initialize() itself.
        /// </summary>
        private static PathState State
        {
            get
            {
                var state = Volatile.Read(ref s_State);
                if (string.IsNullOrEmpty(state.Root))
                    throw new System.InvalidOperationException(
                        "ModPaths read before Initialize(). Call ModPaths.Initialize(Application.persistentDataPath) at the start of Mod.OnLoad() before any path access.");
                return state;
            }
        }

        // ── Directories ──────────────────────────────────────────

        /// <summary>
        /// Root game data: .../LocalLow/Colossal Order/Cities Skylines II
        /// </summary>
        public static string GameDataRoot => State.Root;

        /// <summary>
        /// .../Cities Skylines II/Logs
        /// </summary>
        public static string LogsDirectory => State.Logs;

        /// <summary>
        /// .../Cities Skylines II/ModData/CivicSurvival
        /// </summary>
        public static string ModDataDirectory => State.ModData;

        /// <summary>
        /// .../Cities Skylines II/Mods/CivicSurvival
        /// </summary>
        public static string ModInstallDirectory => State.ModInstall;

        // ── Filenames ────────────────────────────────────────────

        public const string CredentialsFile = "player_credentials.txt";
        public const string ArenaPendingFile = "arena_pending.json";
        public const string BalanceConfigFile = "balance_config.json";
        public const string PerfLogFile = "PERF.log";
        public const string BurstDiagFile = "BurstDiag.log";
        public const string NativeCrashBreadcrumbFile = "native_crash_breadcrumb.txt";
        public const string NativeCrashContextFile = "native_crash_context.json";
        public const string TelemetryOptInFile = "telemetry_optin.txt";
        public const string OnlineConnectionFile = "online_connection.txt";
        public const string BugReportFile = "CivicSurvival_BugReport.txt";
        public const string ModLogFile = "CivicSurvival.log";
        public const string PrevModLogFile = "CivicSurvival-prev.log";
        // Vanilla CS2 mod-load log (active playset, enabled mods + versions, load order). Read-only —
        // attached to manual reports for mod-conflict triage.
        public const string ModdingLogFile = "Modding.log";
        public const string BalanceMetricsFile = "BalanceMetrics.csv";
        public const string TelemetryPendingFile = "telemetry_pending.jsonl";
        public const string CleanShutdownFile = Engine.ErrorReporting.CLEAN_SHUTDOWN_FILE;

        // ── Init ─────────────────────────────────────────────────

        /// <summary>
        /// Call once at the start of Mod.OnLoad() with Application.persistentDataPath.
        /// <paramref name="modInstallDirectory"/> is the REQUIRED directory of the
        /// loaded mod assembly (from ExecutableAsset.path). It must come from the
        /// loaded DLL: the gameDataRoot/Mods/CivicSurvival layout is correct only for
        /// local dev deploys, while Paradox Mods subscribers load from the subscription
        /// cache where that path doesn't exist and asset-backed I/O (icons via
        /// coui://ui-mods/cs-icons, audio) would resolve to nothing. Throws if either argument is empty.
        /// </summary>
        public static void Initialize(string persistentDataPath, string modInstallDirectory)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
                throw new System.ArgumentException("Persistent data path must be non-empty.", nameof(persistentDataPath));
            if (string.IsNullOrWhiteSpace(modInstallDirectory))
                throw new System.ArgumentException("Mod install directory must be non-empty.", nameof(modInstallDirectory));

            var root = Path.GetFullPath(persistentDataPath);
            var modInstall = Path.GetFullPath(modInstallDirectory);
            var newState = new PathState(root, modInstall);

            while (true)
            {
                var current = Volatile.Read(ref s_State);
                if (!string.IsNullOrEmpty(current.Root))
                {
                    if (string.Equals(current.Root, root, System.StringComparison.OrdinalIgnoreCase))
                        return;

                    throw new System.InvalidOperationException(
                        $"ModPaths already initialized with '{current.Root}', cannot reinitialize with '{root}'.");
                }

                if (Interlocked.CompareExchange(ref s_State, newState, current) == current)
                    return;
            }
        }

        // ── Logging helpers ──────────────────────────────────────

        /// <summary>
        /// PII-safe rendering of a mod install path for logs: everything from the "pdx_mods" segment
        /// onward — the user-home prefix (which contains the account name) is stripped. A non-Paradox
        /// (local dev) path has no pdx_mods segment, so the leaf folder name is reported instead
        /// ("(non-pdx)/CivicSurvival" — no PII). Empty/null input → "(none)". Use anywhere a mod path
        /// would otherwise reach a log line that can be attached to a manual bug report.
        /// </summary>
        public static string SanitizePathTail(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "(none)";
            string dir = path.Replace('\\', '/');
            int idx = dir.IndexOf("pdx_mods", System.StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? dir.Substring(idx) : "(non-pdx)/" + Path.GetFileName(dir.TrimEnd('/'));
        }
    }
}
