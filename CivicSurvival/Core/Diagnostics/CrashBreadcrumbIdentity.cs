namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Identity of the CURRENTLY running session, captured once at telemetry init and
    /// embedded by the native-crash writers (<see cref="NativeCrashBreadcrumb"/> and
    /// <see cref="CrashContextProvider"/>) at write time. The breadcrumb / context files are
    /// written DURING the crashing run, so these fields record the build and session that
    /// actually crashed — not the relaunch session that recovers and reports them on next
    /// boot (which is what <c>sessions.mod_version</c> resolves to, and is wrong whenever the
    /// player updated the mod between the crash and the restart).
    /// <para/>
    /// Lives in <c>Core/Diagnostics</c> so the writers need no dependency on the telemetry
    /// layer; the telemetry layer pushes the values in once via <see cref="Set"/>. Version is
    /// deliberately NOT part of <see cref="CrashContextSnapshot"/> — that struct has
    /// value-semantics used for dedup/equality, and the version is identity, not gameplay state.
    /// </summary>
    public static class CrashBreadcrumbIdentity
    {
        private static readonly object s_Lock = new();
        private static string s_ModVersion = string.Empty;
        private static string s_SessionId = string.Empty;

        /// <summary>Set once at telemetry init, next to <see cref="NativeCrashBreadcrumb.SetEnabled"/>.</summary>
        public static void Set(string modVersion, string sessionId)
        {
            lock (s_Lock)
            {
                s_ModVersion = modVersion ?? string.Empty;
                s_SessionId = sessionId ?? string.Empty;
            }
        }

        /// <summary>The running session's (mod version, session id), read by the breadcrumb writers.</summary>
        public static (string ModVersion, string SessionId) Current
        {
            get
            {
                lock (s_Lock)
                {
                    return (s_ModVersion, s_SessionId);
                }
            }
        }
    }
}
