using System;
using System.Reflection;
using Unity.Burst;

namespace CivicSurvival.Services.Bootstrap
{
    /// <summary>
    /// Logs Burst compiler state for startup diagnostics.
    /// </summary>
    internal static class BurstDiagnosticsLogger
    {
        public static void LogStatus()
        {
            try
            {
#if ENABLE_BURST
                const bool civicBurstEnabled = true;
#else
                const bool civicBurstEnabled = false;
#endif
                bool burstEnabled = BurstCompiler.IsEnabled;
                Mod.Log.Info($"[Burst] Civic ENABLE_BURST: {civicBurstEnabled}; Unity compiler enabled: {burstEnabled}");

                if (!civicBurstEnabled || !burstEnabled) return;

                var jobTypes = new[]
                {
                    typeof(Domains.ThreatFlight.Jobs.ShahedMovementJob),
                    typeof(Domains.ThreatFlight.Jobs.BallisticMovementJobEntity),
                    typeof(Domains.AirDefense.Jobs.EngagementScoringJob)
                };

                foreach (var jobType in jobTypes)
                {
                    var method = typeof(BurstCompiler).GetMethod(
                        "IsMethodCompiled",
                        BindingFlags.Public | BindingFlags.Static);
                    if (method == null) continue;

                    var executeMethod = jobType.GetMethod("Execute");
                    if (executeMethod == null) continue;

                    bool compiled = (bool)method.Invoke(null, new object[] { executeMethod });
                    Mod.Log.Info($"[Burst] {jobType.Name}.Execute compiled: {compiled}");
                }
            }
            catch (Exception ex)
            {
                if (Mod.Log.isDebugEnabled) Mod.Log.Debug($"[Burst] Status check failed: {ex.Message}");
            }
        }
    }
}
