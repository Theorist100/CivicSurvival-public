using HarmonyLib;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Owns Harmony patch application and patch-local cleanup.
    /// </summary>
    internal static class HarmonyPatchBootstrapper
    {
        public static Harmony Apply(string harmonyId)
        {
            Mod.Log.Info("Initializing Harmony patches...");

            var harmony = new Harmony(harmonyId);

            // Apply patches with explicit Apply() method
            BacktraceMarkers.Phase("Harmony/ExplicitPatches-start");
            ElectricityPatch.Apply(harmony);
            CrisisEconomicsPatch.Apply(harmony);
            CameraControllerPatch.Apply(harmony);
            MotionVectorPatch.Apply(harmony);
            BacktraceMarkers.Phase("Harmony/ExplicitPatches-done");

            // Apply attribute-based patches (VanillaSystemAutoProfiler hooks SystemBase.Update
            // for CivicSurvival systems only; vanilla coverage lives in VanillaProfiler mod).
            TryPatchAll(harmony);

            // Verify attribute-based patches applied correctly
            WinterMultiplierPatch.VerifyAndReport();
            UpdateSystemProfilerPatch.VerifyAndReport();
            VanillaSystemAutoProfiler.VerifyAndReport();
            SimulationProfilerPatch.VerifyAndReport();
#if DEBUG
            BarrierGateViolationDetector.VerifyAndReport();
#endif

            if (PatchStatusTracker.HasFailures)
            {
                Mod.Log.Warn($"[Harmony] Some patches failed: {PatchStatusTracker.GetDetailedFailureMessage()}");
            }
            else
            {
                Mod.Log.Info("[Harmony] All patches applied successfully");
            }

            return harmony;
        }

        public static void Cleanup(string harmonyId, Harmony? harmony, World? world)
        {
            // Mod.OnDispose calls VanillaReflectionRegistry.BeginUnload BEFORE we run.
            // The registry is generation-aware: cached FieldInfo resolves still return
            // true for cleanup-time callers, but uncached lookups during the frozen
            // window return false silently (no AccessTools.Field call, no
            // PatchStatusTracker.ReportFailure, no log noise). That guarantees the
            // PatchStatusTracker.Clear() call below is the last write to the tracker.

            // Unpatch Harmony first so no vanilla update can re-enter patch code while
            // patch-local cleanup tears down process state.
            TryUnpatchAll(harmonyId, harmony);

            // Pass world so each Cleanup can perform an explicit handler-restore in
            // the hot-reload scenario. Each Cleanup checks world.IsCreated itself.
            RunCleanup("ElectricityPatch", () => ElectricityPatch.Cleanup(world));
            RunCleanup("CrisisEconomicsPatch", () => CrisisEconomicsPatch.Cleanup(world));
            RunCleanup("CameraControllerPatch", CameraControllerPatch.Cleanup);
            RunCleanup("MotionVectorPatch", () => MotionVectorPatch.Cleanup(world));
            RunCleanup("WinterMultiplierPatch", WinterMultiplierPatch.Cleanup);
            RunCleanup("UpdateSystemProfilerPatch", UpdateSystemProfilerPatch.Cleanup);
            RunCleanup("VanillaSystemAutoProfiler", VanillaSystemAutoProfiler.Cleanup);
            RunCleanup("SimulationProfilerPatch", SimulationProfilerPatch.Cleanup);
#if DEBUG
            RunCleanup("BarrierGateViolationDetector", BarrierGateViolationDetector.Cleanup);
#endif
            RunCleanup("PatchStatusTracker", PatchStatusTracker.Clear);
        }

        private static void TryPatchAll(Harmony harmony)
        {
            BacktraceMarkers.Phase("Harmony/PatchAll-start");
            try
            {
                harmony.PatchAll(typeof(WinterMultiplierPatch).Assembly);
                BacktraceMarkers.Phase("Harmony/PatchAll-done");
            }
            catch (System.Exception ex)
            {
                BacktraceMarkers.Phase("Harmony/PatchAll-failed");
                PatchStatusTracker.ReportFailure(
                    "Harmony.PatchAll",
                    $"PatchAll threw: {ex.GetType().Name}: {ex.Message}");
                Mod.Log.WarnException("Harmony.PatchAll failed; attribute patches disabled this session", ex);
            }
        }

        private static void TryUnpatchAll(string harmonyId, Harmony? harmony)
        {
            if (harmony == null)
                return;

            try
            {
                harmony.UnpatchAll(harmonyId);
            }
            catch (System.Exception ex)
            {
                Mod.Log.WarnException("Harmony.UnpatchAll failed; continuing patch-local cleanup", ex);
            }
        }

        private static void RunCleanup(string name, System.Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (System.Exception ex)
            {
                Mod.Log.WarnException($"{name}.Cleanup failed", ex);
            }
        }
    }
}
