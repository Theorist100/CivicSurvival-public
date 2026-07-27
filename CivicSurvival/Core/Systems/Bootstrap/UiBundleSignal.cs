using System;
using System.Threading;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Process-lifetime latch: has the mod's React bundle (CivicSurvival.mjs) executed in this
    /// session, and when did it last speak? Fed by the JS log bridge (every scLog crosses it —
    /// index.tsx emits several on module load, production bundle included), read by the
    /// FinalizeMissing Error line as <c>uiAlive=</c>.
    ///
    /// Why it exists: the playset-desync bucket (form α, MOD_COK_LOADING §5) starves the
    /// activation-driven asset registration while the C# assembly keeps running from memory —
    /// the React layer (every panel and the ModLoadFailure modal with its player-facing fix
    /// instructions) then physically cannot render. Prod could not see this: zero
    /// diagnostics.mod_load_modal events across 14 α reports could not distinguish "modal never
    /// rendered" from "modal telemetry broken". This latch turns "was the React layer alive"
    /// into a per-report fact.
    /// </summary>
    internal static class UiBundleSignal
    {
        private static long s_Count;
        private static long s_LastTick;

        /// <summary>
        /// One JS-bridge message arrived — the bundle is executing. Called from the cohtml
        /// trigger callback; Interlocked keeps the long fields tear-free for readers on other
        /// threads. Environment.TickCount (process-monotonic) instead of wall-clock: the value
        /// only ever feeds a "seconds since" delta inside one process, and the diagnostic must
        /// not shift under system-clock changes (CIVIC003).
        /// </summary>
        public static void Record()
        {
            Interlocked.Increment(ref s_Count);
            Interlocked.Exchange(ref s_LastTick, Environment.TickCount);
        }

        /// <summary>
        /// For the FinalizeMissing Error line: <c>never</c> = not a single JS message this
        /// process (bundle absent or failed to load — no React surface can render), else the
        /// message count and seconds since the last one (a bundle that spoke at boot but went
        /// silent before finalize shows a large lastAgoS).
        /// </summary>
        public static string Describe()
        {
            long count = Interlocked.Read(ref s_Count);
            if (count == 0)
                return "never";
            // Unchecked int subtraction stays correct across the ~24.9-day TickCount wrap.
            int agoMs = unchecked(Environment.TickCount - (int)Interlocked.Read(ref s_LastTick));
            return $"js={count}, lastAgoS={agoMs / 1000}";
        }
    }
}
