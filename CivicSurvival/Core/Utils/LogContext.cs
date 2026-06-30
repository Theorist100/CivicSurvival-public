namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Structured logging context with automatic source prefixing.
    /// Replaces inconsistent manual prefixes like "[ThreatUIPanel]", "[DIAG] DistrictUIPanel:", etc.
    ///
    /// Usage:
    /// <code>
    /// private static readonly LogContext Log = new("ThreatUIPanel");
    /// Log.Info("Something happened");      // → [ThreatUIPanel] Something happened
    /// Log.Diag($"value={x}");              // → [DIAG:ThreatUIPanel] value=5
    /// </code>
    /// </summary>
    public readonly struct LogContext
    {
        private readonly string _source;

        public static readonly LogContext Default = new("Global");

        public LogContext(string source)
        {
            _source = source ?? "Unknown";
        }

        /// <summary>
        /// True if Debug logging is enabled. Use to guard expensive interpolation at call sites.
        /// </summary>
        public bool IsDebugEnabled
        {
            get
            {
                try
                {
                    return Mod.Log.isDebugEnabled;
                }
                catch (System.Exception ex) when (IsLogUnavailable(ex))
                {
                    System.Console.Error.WriteLine(ex.ToString());
                    return false;
                }
            }
        }

        /// <summary>
        /// Log informational message.
        /// </summary>
        public void Info(string message)
        {
            string source = _source;
            SafeLog(() => Mod.Log.Info($"[{source}] {message}"));
        }

        /// <summary>
        /// Log formatted info message.
        /// </summary>
        public void Info(string format, params object[] args)
        {
            try
            {
                string source = _source;
                SafeLog(() => Mod.Log.Info($"[{source}] {string.Format(format, args)}"));
            }
            catch (System.Exception ex) when (ex is System.FormatException || ex is System.ArgumentNullException || ex is System.NullReferenceException)
            {
                string source = _source;
                SafeLog(() => Mod.Log.Info($"[{source}] {format} (format error: {ex.GetType().Name}, args={CountArgs(args)})"));
            }
        }

        /// <summary>
        /// Log debug message (only in debug builds).
        /// </summary>
        public void Debug(string message)
        {
            if (!IsDebugEnabled) return;
            string source = _source;
            SafeLog(() => Mod.Log.Debug($"[{source}] {message}"));
        }

        /// <summary>
        /// Log formatted debug message.
        /// </summary>
        public void Debug(string format, params object[] args)
        {
            if (!IsDebugEnabled) return;
            try
            {
                string source = _source;
                SafeLog(() => Mod.Log.Debug($"[{source}] {string.Format(format, args)}"));
            }
            catch (System.Exception ex) when (ex is System.FormatException || ex is System.ArgumentNullException || ex is System.NullReferenceException)
            {
                string source = _source;
                SafeLog(() => Mod.Log.Debug($"[{source}] {format} (format error: {ex.GetType().Name}, args={CountArgs(args)})"));
            }
        }

        /// <summary>
        /// Log warning message.
        /// </summary>
        public void Warn(string message)
        {
            string source = _source;
            SafeLog(() => Mod.Log.Warn($"[{source}] {message}"));
        }

        /// <summary>
        /// Log error message.
        /// </summary>
        public void Error(string message)
        {
            string source = _source;
            SafeLog(() => Mod.Log.Error($"[{source}] {message}"));
        }

        /// <summary>
        /// Log diagnostic message (debug level with DIAG prefix).
        /// Used for temporary debugging that may be removed later.
        /// </summary>
        public void Diag(string message)
        {
            if (!IsDebugEnabled) return;
            string source = _source;
            SafeLog(() => Mod.Log.Debug($"[DIAG:{source}] {message}"));
        }

        /// <summary>
        /// Log warning with exception details (matches Colossal ILog.WarnException).
        /// </summary>
        public void WarnException(string message, System.Exception ex)
        {
            string source = _source;
            SafeLog(() => Mod.Log.Warn($"[{source}] {message}\n{ex}"));
        }

        /// <summary>
        /// Log exception with full stack trace at Error level.
        /// </summary>
        public void Exception(string message, System.Exception ex)
        {
            string source = _source;
            SafeLog(() => Mod.Log.Error($"[{source}] {message}\n{ex}"));
        }

        private static void SafeLog(System.Action write)
        {
            try
            {
                write();
            }
            catch (System.Exception ex) when (IsLogUnavailable(ex))
            {
                System.Console.Error.WriteLine(ex.ToString());
            }
        }

        private static bool IsLogUnavailable(System.Exception ex)
        {
            return ex is System.TypeInitializationException
                || ex is System.Security.SecurityException
                || ex.InnerException != null && IsLogUnavailable(ex.InnerException);
        }

        private static int CountArgs(object[] args)
        {
            return args == null ? 0 : args.Length;
        }
    }
}
