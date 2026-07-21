using System;
using System.Text.RegularExpressions;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Single source of PII redaction for any free-text content that leaves the client in
    /// telemetry — manual bug reports (log tail) and auto crash/error stack traces.
    /// Masks OS user-profile paths (and the current OS user name) so a stack trace
    /// or log line carrying <c>C:\Users\&lt;name&gt;\…</c> cannot deanonymize the player by
    /// their Windows/Unix account.
    ///
    /// Every outbound free-text channel must route through <see cref="Redact"/> at the
    /// point where the event is built — do not hand-roll per-channel replacements.
    /// </summary>
    public static class PiiRedactor
    {
        private const string UserPlaceholder = "<user>";

        // Windows: C:\Users\<name>\  |  Unix: /home/<name>/ or /Users/<name>/.
        // Bounded character class (no nested quantifiers) — no catastrophic backtracking.
        // Named capture + ExplicitCapture: only the prefix group is kept, masked tail dropped.
#pragma warning disable MA0009 // simple bounded class, no backtracking risk
        private static readonly Regex WindowsUserProfile =
            new(@"(?<prefix>[A-Za-z]:\\Users\\)[^\\/\r\n]+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private static readonly Regex UnixUserProfile =
            new(@"(?<prefix>/(?:home|Users)/)[^/\r\n]+",
                RegexOptions.Compiled | RegexOptions.ExplicitCapture);
#pragma warning restore MA0009

        // Current OS account name, masked even when it appears outside a path. Cached once;
        // only used when long enough that masking it cannot hit unrelated short substrings.
        private static readonly string? CurrentUserName = TryGetUserName();

        /// <summary>
        /// Returns <paramref name="text"/> with OS user-profile paths and the current user
        /// name masked. Null/empty passes through unchanged.
        /// </summary>
        public static string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            var result = WindowsUserProfile.Replace(text!, "${prefix}" + UserPlaceholder);
            result = UnixUserProfile.Replace(result, "${prefix}" + UserPlaceholder);

            if (!string.IsNullOrEmpty(CurrentUserName))
                result = result.Replace(CurrentUserName, UserPlaceholder, StringComparison.OrdinalIgnoreCase);

            return result;
        }

        private static string? TryGetUserName()
        {
            try
            {
                var name = Environment.UserName;
                // Guard against masking unrelated short substrings (e.g. a 2-char account).
                return !string.IsNullOrEmpty(name) && name.Length >= 3 ? name : null;
            }
#pragma warning disable CIVIC052 // Best-effort: user name unavailable → bare-name masking off; paths still masked.
            catch (Exception)
            {
                return null;
            }
#pragma warning restore CIVIC052
        }
    }
}
