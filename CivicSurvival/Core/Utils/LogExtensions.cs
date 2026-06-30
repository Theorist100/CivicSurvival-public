using System;
using Colossal.Logging;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Extension methods for ILog to ensure consistent exception logging.
    /// Always includes full stack trace for debugging.
    /// </summary>
    public static class LogExtensions
    {
        /// <summary>
        /// Log an exception at Error level with full stack trace.
        /// </summary>
        /// <param name="log">The logger instance.</param>
        /// <param name="context">Context description (e.g., "[AudioManager] Init failed").</param>
        /// <param name="ex">The exception to log.</param>
        public static void Exception(this ILog log, string context, Exception ex)
        {
            log.Error($"{context}\n{ex}");
        }

        /// <summary>
        /// Log an exception at Warning level with full stack trace.
        /// Use for non-critical errors that don't break functionality.
        /// </summary>
        /// <param name="log">The logger instance.</param>
        /// <param name="context">Context description.</param>
        /// <param name="ex">The exception to log.</param>
        public static void WarnException(this ILog log, string context, Exception ex)
        {
            log.Warn($"{context}\n{ex}");
        }
    }
}
