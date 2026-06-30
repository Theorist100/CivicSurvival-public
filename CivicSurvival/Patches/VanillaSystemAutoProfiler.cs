using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Unity.Entities;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Patches SystemBase.Update() to profile CivicSurvival systems only.
    /// Civic systems → "Full:{Name}.OnUpdate" keys (CivicSystemBase already records "{Name}.OnUpdate"
    /// for OnUpdateImpl only — delta = sync point cost from BeforeOnUpdate job completion).
    ///
    /// Vanilla / other-mod systems are classified as skip on first hit (no recording, no overhead
    /// past the first call). Engine-wide vanilla timing lives in VanillaProfiler (separate mod).
    ///
    /// Top-N sorting in Report() naturally surfaces the heaviest systems.
    ///
    /// Threading: UpdateSystem.Update() calls system.Update() sequentially on the main thread.
    /// Classification caches (s_NameCache, s_SkipTypes, s_ProfileTypes) are main-thread-only — no lock needed.
    ///
    /// Stopwatch is the only timing source. ProfilerMarker.Begin/End would only be visible
    /// to subscribers, and CS2 ships Profiler.enabled=false (no subscriber). See
    /// Docs/Reference/API/CS2_Profiling_API.md "What you can NOT do from a mod".
    /// </summary>
#pragma warning disable CIVIC092 // Update is sealed override with body — verified via ILSpy
    [HarmonyPatch(typeof(SystemBase), "Update")]
#pragma warning restore CIVIC092
    public static class VanillaSystemAutoProfiler
    {
        private const string PatchName = nameof(VanillaSystemAutoProfiler);
        private static readonly LogContext Log = new("VanillaAutoProfiler");

        // Per-call state carried via Harmony __state. Survives nested SystemBase.Update
        // invocations — vanilla CS2 nests these calls (ReplacePrefabSystem.FinalizeReplaces,
        // HeatmapPreviewSystem.OnUpdate, PhotoModeRenderSystem all run other systems' Update
        // synchronously inside their own OnUpdate).
        // Public to match [HarmonyPrefix]/[HarmonyPostfix] method visibility (CS0051).
#pragma warning disable CA1815
        public struct UpdateState
        {
            public long StartTicks;
        }
#pragma warning restore CA1815

        // Classification caches: main-thread-only (UpdateSystem iterates systems sequentially).
        // Stale data is harmless (Type→name mapping is stable across mod reload).
#pragma warning disable CIVIC148 // Debug-only caches — stale Type→name mappings are harmless across reload
#pragma warning disable CIVIC207 // Main-thread-only: UpdateSystem calls Update() sequentially, no concurrent access
        private static readonly Dictionary<Type, string> s_NameCache = new();
        private static readonly HashSet<Type> s_SkipTypes = new();
        private static readonly HashSet<Type> s_ProfileTypes = new();
#pragma warning restore CIVIC207
#pragma warning restore CIVIC148

        internal static void ResetCounters()
        {
            // Per-call state lives in __state — nothing thread-local to reset.
        }

        public static void Cleanup()
        {
            ResetCounters();
            s_NameCache.Clear();
            s_SkipTypes.Clear();
            s_ProfileTypes.Clear();
        }

        public static void VerifyAndReport()
        {
            PatchStatusTracker.VerifyPatchInfo(
                PatchName,
                AccessTools.Method(typeof(SystemBase), nameof(SystemBase.Update)),
                "SystemBase.Update",
                typeof(VanillaSystemAutoProfiler),
                expectPrefix: true,
                expectPostfix: true);
        }

        [HarmonyPrepare]
        public static bool Prepare()
        {
            var method = AccessTools.Method(typeof(SystemBase), "Update");
            if (method == null)
            {
                Log.Warn("SystemBase.Update not found — auto-profiler will not apply");
                return false;
            }
            Log.Info("Auto-profiler enabled (CivicSurvival systems only — vanilla covered by VanillaProfiler mod)");
            return true;
        }

        [HarmonyPrefix]
        public static void Prefix(out UpdateState __state)
        {
            // Per-system Update timing (the per-system PERF table / sync-point source),
            // gated by PerformanceProfiler.Enabled — its own switch, not the log level,
            // so it records at Level.Info where sync numbers are honest. Enabled=false →
            // no-op (StartTicks=0 → Postfix early-returns), zero cost (beta state).
            if (!PerformanceProfiler.Enabled)
            {
                __state.StartTicks = 0;
                return;
            }
            __state.StartTicks = Stopwatch.GetTimestamp();
        }

        [HarmonyPostfix]
#pragma warning disable CIVIC052 // Profiler: intentionally silent on errors
#pragma warning disable CIVIC207 // Main-thread-only: see class-level comment
        public static void Postfix(SystemBase __instance, UpdateState __state)
        {
            try
            {
                if (__state.StartTicks <= 0)
                    return;

                long elapsed = Stopwatch.GetTimestamp() - __state.StartTicks;
                var type = __instance.GetType();

                // Fast path: already classified as skip
                if (s_SkipTypes.Contains(type))
                    return;

                // Fast path: already classified as profile
                if (s_ProfileTypes.Contains(type))
                {
                    PerformanceProfiler.RecordExternalVanilla(s_NameCache[type], elapsed);
                    return;
                }

                // First time seeing this type: classify by namespace
                var ns = type.Namespace;

                // Skip profiler infrastructure to avoid recursion/self-attribution.
                if (ns != null && (ns.StartsWith("VanillaProfiler", StringComparison.Ordinal)
                    || ns.StartsWith("CivicSurvival.Patches", StringComparison.Ordinal)
                    || ns.StartsWith("CivicSurvival.Core.Utils", StringComparison.Ordinal)))
                {
                    s_SkipTypes.Add(type);
                    return;
                }

                // Civic systems: record full Update() cost (OnUpdateImpl already profiled by CivicSystemBase)
                // Delta between "Full:X.OnUpdate" and "X.OnUpdate" = sync point cost.
                // Vanilla / other-mod systems: skip — VanillaProfiler (separate mod) covers them.
                if (ns == null || !ns.StartsWith("CivicSurvival", StringComparison.Ordinal))
                {
                    s_SkipTypes.Add(type);
                    return;
                }

                string recordedName = "Full:" + type.Name + ".OnUpdate";
                s_NameCache[type] = recordedName;
                s_ProfileTypes.Add(type);
                PerformanceProfiler.RecordExternalVanilla(recordedName, elapsed);
            }
            catch { /* profiler — never crash game */ }
        }
#pragma warning restore CIVIC207
#pragma warning restore CIVIC052
    }
}
