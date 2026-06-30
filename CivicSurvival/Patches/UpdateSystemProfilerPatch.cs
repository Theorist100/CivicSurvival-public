using HarmonyLib;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Hooks UpdateSystem.Update(SystemUpdatePhase) on the Rendering phase to drive
    /// PerformanceProfiler.OnFrame() — which advances FPS samples, the report timer,
    /// and Memory Profiler ticks. This must run in production (not Debug-only).
    ///
    /// Per-phase timing breakdown lives in VanillaProfiler (separate mod).
    /// </summary>
    public static class UpdateSystemProfilerPatch
    {
        private const string PatchName = nameof(UpdateSystemProfilerPatch);

        public static void Cleanup()
        {
            // Stateless Harmony bridge; kept for symmetric Mod.OnDispose cleanup.
        }

        public static void VerifyAndReport()
        {
            PatchStatusTracker.VerifyPatchInfo(
                PatchName,
                AccessTools.Method(typeof(UpdateSystem), nameof(UpdateSystem.Update), new[] { typeof(SystemUpdatePhase) }),
                "UpdateSystem.Update(SystemUpdatePhase)",
                typeof(UpdatePhase),
                expectPrefix: false,
                expectPostfix: true);
        }

        [HarmonyPatch(typeof(UpdateSystem), nameof(UpdateSystem.Update), typeof(SystemUpdatePhase))]
        public static class UpdatePhase
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return AccessTools.Method(typeof(UpdateSystem), "Update", new[] { typeof(SystemUpdatePhase) }) != null;
            }

            [HarmonyPostfix]
            public static void Postfix(SystemUpdatePhase phase)
            {
                try
                {
                    if (phase == SystemUpdatePhase.Rendering)
                    {
                        PerformanceProfiler.OnFrame();
                    }
                }
#pragma warning disable CIVIC052 // Profiler: intentionally silent
                catch { /* profiler — never crash game */ }
#pragma warning restore CIVIC052
            }
        }
    }
}
