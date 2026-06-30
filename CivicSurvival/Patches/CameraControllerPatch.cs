using System.Reflection;
using HarmonyLib;
using Unity.Entities;
using Game;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Harmony patch for CameraController to allow camera tracking of flying objects.
    ///
    /// Problem: CameraController.UpdateCamera() forces pivot.y to terrain via lerp:
    ///   m_Pivot.y = math.lerp(terrain, m_Pivot.y, m_MoveSmoothing)
    /// With m_MoveSmoothing = 0.000001, this effectively snaps pivot to terrain height.
    /// Also, lines 358-359 add input movement to pivot which causes jitter.
    ///
    /// Solution:
    /// 1. Save pivot position in Prefix
    /// 2. Set m_MoveSmoothing to 1.0 (disables terrain lerp)
    /// 3. Restore pivot position in Postfix (overrides any game modifications)
    ///
    /// H3.3 FIX: Uses Harmony __state pattern instead of static fields to avoid
    /// thread-safety issues when multiple UpdateCamera calls could overlap.
    /// </summary>
    public static class CameraControllerPatch
    {
        private static readonly LogContext Log = new("CameraControllerPatch");

        // M3.4: Version resilience - fallback method names
        private static readonly string[] MethodCandidates = { "UpdateCamera", "Update", "LateUpdate" };
        private static readonly string[] FieldCandidates = { "m_MoveSmoothing", "moveSmoothing", "m_Smoothing" };
        /// <summary>
        /// State passed from Prefix to Postfix via Harmony __state mechanism.
        /// Avoids thread-safety issues with static fields.
        /// </summary>
        public struct CameraPatchState
        {
            public float OriginalSmoothing;
            public UnityEngine.Vector3 SavedPivot;
            public bool IsTracking;
            public bool FreezePivot;
        }

        private static AccessTools.FieldRef<CameraController, float> s_SmoothingRef = null!;
        private static CameraController s_LastPatchedInstance = null!;
        private static float s_LastOriginalSmoothing;
        private static bool s_HasLastOriginalSmoothing;

        public static bool IsActive { get; private set; }

        /// <summary>
        /// Clear cached FieldRef before unpatch. Call from Mod.OnDispose().
        /// </summary>
        public static void Cleanup()
        {
            if (s_HasLastOriginalSmoothing && s_LastPatchedInstance != null && s_SmoothingRef != null)
            {
                try
                {
                    ref float smoothing = ref s_SmoothingRef(s_LastPatchedInstance);
                    smoothing = s_LastOriginalSmoothing;
                }
#pragma warning disable CIVIC052 // Cleanup restore is best-effort during world teardown
                catch (System.Exception ex)
                {
                    if (Log.IsDebugEnabled) Log.Debug($"Cleanup restore skipped: {ex.GetType().Name}");
                }
#pragma warning restore CIVIC052
            }

            s_SmoothingRef = null!;
            s_LastPatchedInstance = null!;
            s_HasLastOriginalSmoothing = false;
            IsActive = false;
        }

        /// <summary>
        /// HarmonyPrepare: Check if target method and field exist before applying patch.
        /// M3.1 FIX: Returns false if no valid targets found (patch will not apply).
        /// </summary>
        [HarmonyPrepare]
        public static bool Prepare()
        {
            // Check for smoothing field
            bool fieldFound = false;
            foreach (var fieldName in FieldCandidates)
            {
                if (AccessTools.Field(typeof(CameraController), fieldName) != null)
                {
                    fieldFound = true;
                    break;
                }
            }

            // Check for method
            bool methodFound = false;
            foreach (var methodName in MethodCandidates)
            {
                if (AccessTools.Method(typeof(CameraController), methodName) != null)
                {
                    methodFound = true;
                    break;
                }
            }

            if (!fieldFound || !methodFound)
            {
                Log.Warn("Target field or method not found - patch will not apply");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Apply the camera controller patch.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            if (IsActive)
            {
                Log.Info("Apply skipped — patch already active");
                return;
            }

            // M3.4: Try multiple field names for version resilience
            string foundFieldName = null!;
            foreach (var fieldName in FieldCandidates)
            {
                if (AccessTools.Field(typeof(CameraController), fieldName) != null)
                {
                    foundFieldName = fieldName;
                    break;
                }
            }

            if (foundFieldName == null)
            {
                PatchStatusTracker.ReportFailure("CameraControllerPatch", $"Smoothing field not found. Tried: {string.Join(", ", FieldCandidates)}");
                return;
            }

            s_SmoothingRef = AccessTools.FieldRefAccess<CameraController, float>(foundFieldName);

            // M3.4: Try multiple method names for version resilience
            MethodInfo targetMethod = null!;
            foreach (var methodName in MethodCandidates)
            {
                targetMethod = AccessTools.Method(typeof(CameraController), methodName);
                if (targetMethod != null)
                    break;
            }

            if (targetMethod == null)
            {
                PatchStatusTracker.ReportFailure("CameraControllerPatch", $"Target method not found. Tried: {string.Join(", ", MethodCandidates)}");
                return;
            }

            var prefix = new HarmonyMethod(typeof(CameraControllerPatch), nameof(Prefix));
            var postfix = new HarmonyMethod(typeof(CameraControllerPatch), nameof(Postfix));

            harmony.Patch(targetMethod, prefix: prefix, postfix: postfix);

            IsActive = true;
            PatchStatusTracker.ReportSuccess("CameraControllerPatch");
            Log.Info("Applied - camera can now track flying objects");
        }

        /// <summary>
        /// Prefix: Save pivot and disable terrain lerp.
        /// H3.3 FIX: Uses __state to pass data to Postfix instead of static fields.
        /// </summary>
        public static void Prefix(CameraController __instance, out CameraPatchState __state)
        {
            __state = default;

            try
            {
                bool srInit = ServiceRegistry.IsInitialized;
                var reader = srInit ? ServiceRegistry.TryGet<CameraTrackingState>() : null;
                var snapshot = reader != null ? reader.Current : default;
                bool isTracking = snapshot.TrackedEntity != Entity.Null;
                bool isTransitioning = snapshot.TransitionProgress > 0f;

                if (!srInit)
                    return;
                // No map-view guard: vanilla m_MapTileToolViewEnabled is a serialized prefab
                // capability flag (always true at runtime), useless as state. Map-view hand-off
                // is owned by CameraTrackingSystem, which detaches tracking when the vanilla
                // MapTilesUISystem.mapTileViewActive is set — so when this patch engages
                // (isTracking/isTransitioning), the player is not in map-view.
                if (reader == null)
                    return;
                if (!isTracking && !isTransitioning)
                    return;

                ref float smoothing = ref s_SmoothingRef(__instance);
                __state.OriginalSmoothing = smoothing;
                __state.IsTracking = true;
                s_LastPatchedInstance = __instance;
                s_LastOriginalSmoothing = smoothing;
                s_HasLastOriginalSmoothing = true;

                if (isTracking)
                {
                    __state.SavedPivot = __instance.pivot;
                    __state.FreezePivot = true;
                    smoothing = 1f;
                }
                else
                {
                    smoothing += (1f - smoothing) * snapshot.TransitionProgress;
                }

            }
            catch (System.Exception ex)
            {
                Log.Error($"Prefix error: {ex}");
            }
        }

        /// <summary>
        /// Postfix: Restore pivot and smoothing.
        /// H3.3 FIX: Uses __state from Prefix instead of static fields.
        /// </summary>
        public static void Postfix(CameraController __instance, CameraPatchState __state)
        {
            try
            {
                if (__state.IsTracking)
                {
                    if (__state.FreezePivot)
                    {
                        __instance.pivot = __state.SavedPivot;
                    }

                    ref float smoothing = ref s_SmoothingRef(__instance);
                    smoothing = __state.OriginalSmoothing;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Postfix error: {ex}");
            }
        }
    }
}
