using Game.Simulation;
using HarmonyLib;
using Unity.Mathematics;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Patches
{
    /// <summary>
    /// Harmony patch to amplify vanilla's temperature consumption multiplier.
    ///
    /// Vanilla's GetTemperatureMultiplier returns mild values (1.0-1.3x).
    /// We amplify based on WinterSeverity setting for gameplay challenge:
    /// - Easy (0.67): up to 2.0x
    /// - Normal (1.0): up to 3.0x
    /// - Hardcore (1.5): up to 4.0x
    ///
    /// This approach avoids race conditions and conflicts with vanilla's
    /// time-sliced consumption updates in AdjustElectricityConsumptionSystem.
    /// </summary>
    [HarmonyPatch(typeof(AdjustElectricityConsumptionSystem), nameof(AdjustElectricityConsumptionSystem.GetTemperatureMultiplier))]
    public static class WinterMultiplierPatch
    {
        private const string PatchName = nameof(WinterMultiplierPatch);
        private static readonly LogContext Log = new("WinterMultiplierPatch");
        private static ModSettings? s_Settings;

        public static void Cleanup()
        {
            s_Settings = null;
        }

        /// <summary>
        /// HarmonyPrepare: Check if target method exists before applying patch.
        /// M3.1 FIX: Returns false if no valid target found (patch will not apply).
        /// </summary>
        [HarmonyPrepare]
        public static bool Prepare()
        {
            if (AccessTools.Method(typeof(AdjustElectricityConsumptionSystem), nameof(AdjustElectricityConsumptionSystem.GetTemperatureMultiplier)) != null)
                return true;
            Log.Warn("No target method found - patch will not apply");
            return false;
        }

        /// <summary>
        /// Verify that the target method exists and report status.
        /// Called after Harmony.PatchAll() to track patch success.
        /// </summary>
        public static void VerifyAndReport()
        {
            PatchStatusTracker.VerifyPatchInfo(
                PatchName,
                AccessTools.Method(
                    typeof(AdjustElectricityConsumptionSystem),
                    nameof(AdjustElectricityConsumptionSystem.GetTemperatureMultiplier)),
                "GetTemperatureMultiplier",
                typeof(WinterMultiplierPatch),
                expectPrefix: false,
                expectPostfix: true);
        }

        [HarmonyPostfix]
        public static void Postfix(float temperature, ref float __result)
        {
            // AUDIT FIX: Harmony patches must NEVER throw — wrap in try/catch
            try
            {
                using var _ = PerformanceProfiler.Measure("Patch.WinterMultiplier");
                var settings = s_Settings;
                if (settings == null && ServiceRegistry.IsInitialized)
                {
                    settings = ServiceRegistry.TryGet<ModSettings>();
                    if (settings != null)
                        s_Settings = settings;
                }

                if (settings?.WinterMultiplierEnabled != true)
                    return;

                if (!math.isfinite(__result))
                    return;

                float winterSeverity = settings.WinterSeverity;
                float amplification = CalculateAmplification(temperature, winterSeverity);

                if (math.isfinite(amplification) && amplification > 1f)
                {
                    __result *= amplification;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Postfix error: {ex}");
            }
        }

        /// <summary>
        /// Calculate amplification factor based on temperature and difficulty.
        ///
        /// Temperature ranges:
        /// - Above 10C: x1.0 (no amplification)
        /// - 0C to 10C: linear interpolation to mid
        /// - -10C to 0C: linear interpolation to max
        /// - Below -10C: max amplification
        /// </summary>
        private static float CalculateAmplification(float temperature, float winterSeverity)
        {
            return WinterAmplificationCalculator.Calculate(temperature, winterSeverity);
        }
    }
}
