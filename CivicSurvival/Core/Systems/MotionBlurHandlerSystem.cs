using System;
using System.Reflection;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// World-owned host for <see cref="CivicSurvival.Patches.MotionVectorPatch"/>.
    /// Holds the per-World <c>SimulationSystem</c> ref, reflection cache, and the
    /// captured baseline MB intensity. On OnDestroy restores the captured intensity
    /// in the dying world.
    ///
    /// Policy: motion blur is forced off while the camera tracks a live drone
    /// (<c>CameraSnapshot.IsDroneTracking</c>) at 2× or 3× sim speed
    /// (<c>SimulationSystem.selectedSpeed &gt; 1f</c>). Mod owns the HDRP override
    /// in that window, but tracks vanilla setting changes while suppression is
    /// active so restore returns to the latest user-selected baseline.
    ///
    /// Passive event-driven host: <see cref="OnUpdate"/> is empty by design — all
    /// work is triggered by Harmony postfix calling <see cref="OnBatch"/>. The
    /// <c>class-level UpdateInGroup attribute</c> registration is required only so the system exists
    /// in the world and is reachable via <c>GetExistingSystemManaged&lt;T&gt;()</c>.
    /// </summary>
    [FrameworkSystem]
    [ActIndependent]
    public partial class MotionBlurHandlerSystem : SystemBase
    {
        private static readonly LogContext Log = new("MotionBlurHandler");

        private SimulationSystem m_SimulationSystem = null!;

        private FieldInfo m_MbComponentField = null!;
        private FieldInfo m_MbIntensityField = null!;
        private PropertyInfo m_ParamValueProp = null!;
        private PropertyInfo m_OverrideStateProp = null!;

        private bool m_SavedOverrideState;
        private float m_SavedMbIntensity = -1f;
        private int m_UserSettingsBaselineVersion = -1;
        private bool m_ReflectionDone;
        // m_LastWantMotionBlur is a per-World dedup guard; host dies with the world,
        // so the field naturally resets to default for the next world — no save/load
        // serialisation needed (CIVIC241 false-positive in this context).
        [System.NonSerialized] private bool? m_LastWantMotionBlur;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
        }

        protected override void OnDestroy()
        {
            RestoreOriginalIntensity();
            m_SimulationSystem = null!;
            ClearReflectionCache();
            m_SavedMbIntensity = -1f;
            m_UserSettingsBaselineVersion = -1;
            m_LastWantMotionBlur = null;

            base.OnDestroy();
        }

        protected override void OnUpdate() { /* handler is event-driven via OnBatch */ }

        /// <summary>
        /// Restore baseline intensity in the live HDRP Volume and clear saved state.
        /// Called from OnDestroy (normal world teardown) and from
        /// <c>MotionVectorPatch.Cleanup</c> (mod hot-reload while world alive).
        /// Idempotent — does nothing if no baseline was captured.
        /// </summary>
        internal void RestoreOriginalIntensity()
        {
            try
            {
                if (m_SavedMbIntensity < 0f || m_MbComponentField == null) return;
                var mbComponent = m_MbComponentField.GetValue(null);
                if (mbComponent == null || m_MbIntensityField == null) return;
                var intensityParam = m_MbIntensityField.GetValue(mbComponent);
                if (intensityParam == null || m_OverrideStateProp == null || m_ParamValueProp == null) return;
                m_OverrideStateProp.SetValue(intensityParam, m_SavedOverrideState);
                m_ParamValueProp.SetValue(intensityParam, m_SavedMbIntensity);
                m_SavedMbIntensity = -1f;
            }
            catch (Exception ex)
            {
                Log.Warn($"RestoreOriginalIntensity failed: {ex}");
            }
            finally
            {
                ClearReflectionCache();
                m_UserSettingsBaselineVersion = -1;
            }
        }

        /// <summary>
        /// Called from the Harmony bridge in MotionVectorPatch. Suppresses motion
        /// blur while camera tracks a live drone at 2×/3× sim speed; restores
        /// baseline otherwise. Top-level try/catch keeps reflection failures
        /// (HDRP API drift, vanilla setting renames) from escaping into the
        /// rendering pipeline.
        /// </summary>
        internal void OnBatch()
        {
            try
            {
                // Lazy resolve: CameraTrackingState is registered by CameraTrackingSystem.OnCreate,
                // which may run after this host's OnCreate; resolving here avoids the order race.
                // TryGet is atomic under ServiceRegistry's lock and intentionally not cached:
                // MotionBlurHandlerSystem is world-owned and survives mod hot-reload, while the
                // process-lifetime facade is recreated with the ServiceRegistry.
                var cameraTrackingState = ServiceRegistry.TryGet<CameraTrackingState>();

                bool isDroneTracking = cameraTrackingState?.Current.IsDroneTracking == true;
                bool fastSpeed = m_SimulationSystem.selectedSpeed > 1f;
                bool suppressMotionBlur = isDroneTracking && fastSpeed;
                ToggleMotionBlurIntensity(!suppressMotionBlur);
            }
            catch (Exception ex)
            {
                // Defense-in-depth: MotionVectorPatch.OnUpdatePostfix also catches, but a
                // top-level guard here keeps reflection failures (HDRP API drift) confined
                // to the handler so a single bad batch can't escape upstream guards.
                Log.Exception("OnBatch error (handler skipped this frame)", ex);
            }
        }

        private void ToggleMotionBlurIntensity(bool wantMotionBlur)
        {
            if (wantMotionBlur && m_LastWantMotionBlur == true && m_SavedMbIntensity < 0f)
                return;

            if (!m_ReflectionDone)
            {
                m_ReflectionDone = true;
                try
                {
                    var settingsType = AccessTools.TypeByName("Game.Settings.MotionBlurQualitySettings");
                    if (settingsType != null)
                        m_MbComponentField = AccessTools.Field(settingsType, "m_MotionBlurComponent");
                    if (m_MbComponentField == null)
                        Log.Warn("MotionBlurQualitySettings.m_MotionBlurComponent not found — MB override disabled");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Reflection init failed: {ex}");
                }
            }

            if (m_MbComponentField == null) return;

            var mbComponent = m_MbComponentField.GetValue(null);
            if (mbComponent == null) return;

            if (m_MbIntensityField == null)
            {
                m_MbIntensityField = AccessTools.Field(mbComponent.GetType(), "intensity");
                if (m_MbIntensityField == null)
                {
                    Log.Warn("MotionBlur.intensity field not found");
                    ClearReflectionCache();
                    return;
                }
            }

            var intensityParam = m_MbIntensityField.GetValue(mbComponent);
            if (intensityParam == null) return;

            if (m_ParamValueProp == null)
            {
                m_ParamValueProp = AccessTools.Property(intensityParam.GetType(), "value");
                m_OverrideStateProp = AccessTools.Property(intensityParam.GetType(), "overrideState");
                if (m_ParamValueProp == null || m_OverrideStateProp == null)
                {
                    Log.Warn("intensity value/overrideState properties not found");
                    ClearReflectionCache();
                    return;
                }
            }

            bool currentOverride = (bool)m_OverrideStateProp.GetValue(intensityParam);
            float currentVal = (float)m_ParamValueProp.GetValue(intensityParam);

            if (!wantMotionBlur)
            {
                RefreshSavedBaselineFromSettings();
                if (!currentOverride || currentVal > float.Epsilon)
                {
                    // Re-capture from live state every time: if we had no baseline yet
                    // we just take what's there; if we had one, the live value reflects
                    // either an observed Apply() bump (RefreshSavedBaselineFromSettings
                    // already updated it) or a direct HDRP write we want to honour on
                    // restore.
                    m_SavedOverrideState = currentOverride;
                    m_SavedMbIntensity = currentVal;
                    m_OverrideStateProp.SetValue(intensityParam, true);
                    m_ParamValueProp.SetValue(intensityParam, 0f);
                }
            }
            else
            {
                if (m_SavedMbIntensity >= 0f)
                {
                    m_OverrideStateProp.SetValue(intensityParam, m_SavedOverrideState);
                    m_ParamValueProp.SetValue(intensityParam, m_SavedMbIntensity);
                    m_SavedMbIntensity = -1f;
                    m_UserSettingsBaselineVersion = -1;
                }
            }

            m_LastWantMotionBlur = wantMotionBlur;
        }

        private void ClearReflectionCache()
        {
            m_MbComponentField = null!;
            m_MbIntensityField = null!;
            m_ParamValueProp = null!;
            m_OverrideStateProp = null!;
            m_ReflectionDone = false;
        }

        private void RefreshSavedBaselineFromSettings()
        {
            if (m_SavedMbIntensity < 0f)
                return;

            if (!CivicSurvival.Patches.MotionVectorPatch.TryGetUserMotionBlurBaseline(
                    out int version,
                    out bool overrideState,
                    out float intensity))
                return;

            if (version == m_UserSettingsBaselineVersion)
                return;

            m_UserSettingsBaselineVersion = version;
            m_SavedOverrideState = overrideState;
            m_SavedMbIntensity = intensity;
        }
    }
}
