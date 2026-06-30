using System;
using System.Diagnostics;
using System.Threading;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Fire-and-forget ThreadPool helper with IsUnloading guard.
    /// Centralizes the CIVIC029 pragma for all simple background work.
    /// </summary>
    public static class BackgroundTask
    {
#pragma warning disable CIVIC029 // Central fire-and-forget with IsUnloading guard
        public static void Run(Action action)
        {
            Run(action, null);
        }

        public static void Run(Action action, Action? onFinished)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (Mod.IsUnloading) return;
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        // S25-H2 FIX: Log instead of silently swallowing.
                        // Multiple callers (RemoteConfig, Telemetry, Arena) rely on this.
                        if (Mod.IsUnloading) return;
                        try
                        {
                            Mod.Log.Error($"[BackgroundTask] Unhandled exception: {ex}");
                        }
                        catch (Exception logEx)
                        {
                            // Last-resort shutdown path: logging may already be torn down.
                            DiagnosticTracker.IncrementError("BackgroundTask.LogFailed");
                            Debug.WriteLine(logEx);
                        }
                    }
                }
                finally
                {
                    onFinished?.Invoke();
                }
            });
        }
#pragma warning restore CIVIC029
    }
}
