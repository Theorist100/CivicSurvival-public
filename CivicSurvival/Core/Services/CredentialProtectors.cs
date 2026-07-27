using System;
using System.Security.Cryptography;
using System.Text;

namespace CivicSurvival.Core.Services
{
    public interface ICredentialProtector
    {
        string Protect(string token);
        string Unprotect(string storedToken);
    }

    public static class CredentialProtectorFactory
    {
        public static ICredentialProtector Create()
            => DpapiAvailable() ? new DpapiCredentialProtector() : new NonPersistingCredentialProtector();

        // Only persist the auth_token when DPAPI actually encrypts it. Probing a
        // roundtrip (not just OSVersion) also catches a Wine/Proton stub that reports Windows
        // but does not implement CryptProtectData. When DPAPI is unavailable we refuse to write
        // the token in cleartext (NonPersistingCredentialProtector) — a re-register on next
        // launch is cheaper than a plaintext long-lived token on disk.
        private static bool DpapiAvailable()
        {
            if (!IsWindows())
                return false;

            try
            {
                var probe = Encoding.UTF8.GetBytes("dpapi-probe");
                var sealed_ = ProtectedData.Protect(probe, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var opened = ProtectedData.Unprotect(sealed_, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return opened.Length == probe.Length;
            }
#pragma warning disable CIVIC052 // Probe failure = DPAPI unavailable; intentional fallback, nothing to log here.
            catch (Exception)
            {
                return false;
            }
#pragma warning restore CIVIC052
        }

        private static bool IsWindows()
        {
            var platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT
                || platform == PlatformID.Win32S
                || platform == PlatformID.Win32Windows
                || platform == PlatformID.WinCE;
        }
    }

    internal sealed class DpapiCredentialProtector : ICredentialProtector
    {
        public string Protect(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            var bytes = Encoding.UTF8.GetBytes(token);
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Unprotect(string storedToken)
        {
            if (string.IsNullOrEmpty(storedToken))
                return string.Empty;

            // Do NOT accept a plaintext token. Reading one would let a planted/imported
            // unprotected credentials file bypass DPAPI on an otherwise protected machine. An
            // unreadable token is cheap to recover — the caller treats it as unregistered and
            // re-registers via /auth/register (TelemetryAuth.InvalidateToken).
            var protectedBytes = Convert.FromBase64String(storedToken);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>
    /// Used where DPAPI is unavailable (non-Windows, or a Wine/Proton stub): the auth_token is
    /// never written to disk in cleartext. It stays in memory only; the player
    /// re-registers via /auth/register on the next launch. Protect returns empty so
    /// TelemetryAuth.SaveCredentials persists no token line, and Unprotect always yields empty.
    /// </summary>
    internal sealed class NonPersistingCredentialProtector : ICredentialProtector
    {
        public string Protect(string token) => string.Empty;

        public string Unprotect(string storedToken) => string.Empty;
    }
}
