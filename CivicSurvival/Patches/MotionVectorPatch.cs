using System.Reflection;
using HarmonyLib;
using Game.Rendering;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Disables motion blur post-process while the camera tracks a live drone
    /// (<c>Shahed</c> + <c>ActiveThreat</c>) at 2× or 3× sim speed. Mod owns the
    /// HDRP MotionBlur override in that window. At 1× speed or for non-drone
    /// views MB respects the latest vanilla settings baseline, including changes
    /// applied while suppression was active.
    ///
    /// Pure Harmony bridge — all state and restore logic lives in the world-owned
    /// <see cref="MotionBlurHandlerSystem"/>, which reads
    /// <c>CameraTrackingState.Current.IsDroneTracking</c> and
    /// <c>SimulationSystem.selectedSpeed</c> per batch. Per-call we look up the
    /// handler from <c>__instance.World</c>; if no handler exists (boot race /
    /// not registered), the postfix no-ops.
    ///
    /// CS2 1.5.5 hardcoded GetMotionVectorsEnabled() to return true, so per-batch
    /// MV flag stripping no longer works. The motion blur setting now only controls
    /// MotionBlur.intensity via HDRP Volume override.
    /// </summary>
    public static class MotionVectorPatch
    {
        private static readonly LogContext Log = new("MotionVectorPatch");
        private static readonly string[] MethodCandidates = { "OnUpdate", "Update", "OnSystemUpdate" };
        [RuntimeInputDirtyCursor("Bumped from Harmony postfix on MotionBlurQualitySettings.Apply; observed by MotionBlurHandlerSystem.RefreshSavedBaselineFromSettings to detect user-driven baseline changes during suppression. Not a published-snapshot version.")]
        private static int s_UserSettingsVersion;
        private static bool s_HasUserSettingsBaseline;
        private static bool s_UserSettingsOverrideState;
        private static float s_UserSettingsIntensity;

        public static bool IsActive { get; private set; }

        public static void Apply(Harmony harmony)
        {
            if (IsActive)
                return;

            MethodInfo onUpdateMethod = null!;
            foreach (var methodName in MethodCandidates)
            {
                onUpdateMethod = AccessTools.Method(typeof(ManagedBatchSystem), methodName);
                if (onUpdateMethod != null)
                    break;
            }

            if (onUpdateMethod == null)
            {
                PatchStatusTracker.ReportFailure("MotionVectorPatch", $"ManagedBatchSystem update method not found. Tried: {string.Join(", ", MethodCandidates)}");
                return;
            }

            try
            {
                harmony.Patch(onUpdateMethod, postfix: new HarmonyMethod(typeof(MotionVectorPatch), nameof(OnUpdatePostfix)));
                PatchMotionBlurSettingsApply(harmony);
                IsActive = true;
                PatchStatusTracker.ReportSuccess("MotionVectorPatch");
                Log.Info("Applied — delegates to MotionBlurHandlerSystem; motion blur disabled at 2x+ game speed");
            }
            catch (System.Exception ex)
            {
                IsActive = false;
                PatchStatusTracker.ReportFailure("MotionVectorPatch", ex.Message);
                Log.Exception("Failed to patch ManagedBatchSystem", ex);
            }
        }

        /// <summary>
        /// Mod-unload teardown. In the normal exit-to-menu → mod-unload flow, the
        /// world dies first and <see cref="MotionBlurHandlerSystem.OnDestroy"/>
        /// restores intensity. In the hot-reload flow (mod unloaded while world
        /// alive), ECS lifecycle hasn't fired yet — call the handler's
        /// <see cref="MotionBlurHandlerSystem.RestoreOriginalIntensity"/> explicitly
        /// to avoid leaving the HDRP Volume forced to intensity=0.
        /// </summary>
        public static void Cleanup(World? world = null)
        {
            if (world != null && world.IsCreated)
            {
                var handler = world.GetExistingSystemManaged<MotionBlurHandlerSystem>();
                handler?.RestoreOriginalIntensity();
            }
            IsActive = false;
            s_HasUserSettingsBaseline = false;
            s_UserSettingsOverrideState = false;
            s_UserSettingsIntensity = 0f;
            s_UserSettingsVersion = 0;
        }

        internal static bool TryGetUserMotionBlurBaseline(out int version, out bool overrideState, out float intensity)
        {
            version = s_UserSettingsVersion;
            overrideState = s_UserSettingsOverrideState;
            intensity = s_UserSettingsIntensity;
            return s_HasUserSettingsBaseline;
        }

        private static void PatchMotionBlurSettingsApply(Harmony harmony)
        {
            try
            {
                var settingsType = AccessTools.TypeByName("Game.Settings.MotionBlurQualitySettings");
                var applyMethod = settingsType != null ? AccessTools.Method(settingsType, "Apply") : null;
                if (applyMethod == null)
                {
                    Log.Warn("MotionBlurQualitySettings.Apply not found — mid-suppression setting changes will be restored from observed HDRP state only");
                    return;
                }

                harmony.Patch(applyMethod, postfix: new HarmonyMethod(typeof(MotionVectorPatch), nameof(OnMotionBlurSettingsApplyPostfix)));
            }
            catch (System.Exception ex)
            {
                Log.Exception("MotionBlurQualitySettings.Apply patch failed — restore will use observed HDRP state only", ex);
            }
        }

        private static void OnMotionBlurSettingsApplyPostfix(object __instance)
        {
            try
            {
                var enabledProp = AccessTools.Property(__instance.GetType(), "enabled");
                if (enabledProp?.GetValue(__instance) is not bool enabled)
                    return;

                s_UserSettingsOverrideState = !enabled;
                s_UserSettingsIntensity = 0f;
                s_HasUserSettingsBaseline = true;
                s_UserSettingsVersion++;
            }
            catch (System.Exception ex)
            {
                Log.Exception("MotionBlurQualitySettings.Apply postfix error", ex);
            }
        }

        private static void OnUpdatePostfix(ManagedBatchSystem __instance)
        {
            // Top-level guard — handler does reflection that can throw; must never
            // escape into vanilla ManagedBatchSystem.OnUpdate (rendering pipeline).
            try
            {
                var world = __instance?.World;
                if (world == null) return;
                var handler = world.GetExistingSystemManaged<MotionBlurHandlerSystem>();
                handler?.OnBatch();
            }
            catch (System.Exception ex)
            {
                Log.Exception("Postfix error (patch disabled this frame)", ex);
            }
        }
    }
}
