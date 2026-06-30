using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Centralized logging helper for request processing.
    /// Provides consistent log format across all request systems.
    /// </summary>
    public static class RequestLogger
    {
        private static readonly LogContext Log = new("Request");

        /// <summary>
        /// Log orphaned command request cleanup (non-generic, for auto-discovered command types).
        /// </summary>
        public static void LogCommandCleanup(string typeName, float age)
        {
            if (Log.IsDebugEnabled) Log.Debug($"[Request] {typeName} COMMAND_ORPHAN (age: {age:F1}s) - destroyed");
        }

    }
}
