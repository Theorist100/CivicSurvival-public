using System;
using System.Globalization;
using Backtrace.Unity;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Writes phase markers into the vanilla Backtrace/crashpad pipeline so a native
    /// SEGV during mod bootstrap (e.g. vanilla <c>lib_burst_generated</c> static init)
    /// can be located by reading the minidump's <c>LastPhase</c> attribute.
    /// Attributes pass through <see cref="BacktraceClient"/>'s indexer into the
    /// native crashpad shared memory mapping — they survive even if the managed
    /// log buffer is lost.
    /// </summary>
    public static class BacktraceMarkers
    {
        public static void Phase(string name)
        {
            try
            {
                var c = BacktraceClient.Instance;
                if (c == null) return;
                c["LastPhase"] = name;
                c["LastPhaseTime"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                c.Breadcrumbs?.Info(name);
            }
            catch (Exception ex)
            {
                // Bootstrap diagnostics must not abort. Backtrace and Colossal.Logging
                // are independent pipelines, so logging via Mod.Log here cannot recurse
                // into the same failure.
                Mod.Log.Warn($"[BacktraceMarkers] Phase '{name}' failed: {ex}");
            }
        }
    }
}
