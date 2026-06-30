using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Tracks which Harmony patches successfully applied.
    /// Used to warn users when game updates break mod compatibility.
    ///
    /// Lives in Core/Infrastructure (not Patches/) because Services and Bootstrap
    /// consume this data — Services cannot import Patches per CIVIC215.
    /// </summary>
    public static class PatchStatusTracker
    {
        private static readonly LogContext Log = new("PatchStatusTracker");
        private static readonly Dictionary<string, bool> s_PatchStatus = new();
        private static readonly List<string> s_FailedPatches = new();
        private static readonly object s_Lock = new();

        /// <summary>
        /// Inspect Harmony's runtime patch info for <paramref name="target"/> and report whether the
        /// prefix/postfix this mod owns (matched by <see cref="Mod.HARMONY_ID"/> + declaring type) is
        /// actually present after <c>Harmony.PatchAll</c>. Call from each patch's post-PatchAll hook.
        /// </summary>
        /// <param name="target">Resolved target method, or <c>null</c> if the caller's AccessTools lookup failed.</param>
        /// <param name="targetDisplayName">Human-readable target name used in failure messages.</param>
        /// <param name="patchDeclaringType">The class that declares the [HarmonyPrefix]/[HarmonyPostfix] methods. For nested patch types, pass the nested type.</param>
        public static void VerifyPatchInfo(
            string patchName,
            MethodInfo? target,
            string targetDisplayName,
            System.Type patchDeclaringType,
            bool expectPrefix,
            bool expectPostfix)
        {
            if (target == null)
            {
                ReportFailure(patchName, $"{targetDisplayName} method not found");
                return;
            }

            var patchInfo = Harmony.GetPatchInfo(target);
            bool prefixOk = !expectPrefix || OwnedBy(patchInfo?.Prefixes, patchDeclaringType);
            bool postfixOk = !expectPostfix || OwnedBy(patchInfo?.Postfixes, patchDeclaringType);

            if (prefixOk && postfixOk)
                ReportSuccess(patchName);
            else
                ReportFailure(patchName, $"{targetDisplayName} {ExpectedKindLabel(expectPrefix, expectPostfix)} not present after PatchAll");
        }

        private static bool OwnedBy(IReadOnlyCollection<Patch>? patches, System.Type declaringType)
        {
            if (patches == null) return false;
            foreach (var patch in patches)
            {
                if (patch.owner == Mod.HARMONY_ID && patch.PatchMethod.DeclaringType == declaringType)
                    return true;
            }
            return false;
        }

        private static string ExpectedKindLabel(bool expectPrefix, bool expectPostfix)
        {
            if (expectPrefix && expectPostfix) return "prefix/postfix";
            if (expectPrefix) return "prefix";
            return "postfix";
        }

        public static void ReportSuccess(string patchName)
        {
            lock (s_Lock)
            {
                s_PatchStatus[patchName] = true;
            }
            Log.Info($"{patchName}: SUCCESS");
        }

        public static void ReportFailure(string patchName, string reason)
        {
            lock (s_Lock)
            {
                s_PatchStatus[patchName] = false;
                if (!s_FailedPatches.Contains(patchName))
                    s_FailedPatches.Add(patchName);
            }
            Log.Error($"{patchName}: FAILED - {reason}");
        }

        public static bool HasFailures
        {
            get
            {
                lock (s_Lock)
                {
                    return s_FailedPatches.Count > 0;
                }
            }
        }

        public static IReadOnlyList<string> FailedPatches
        {
            get
            {
                lock (s_Lock)
                {
                    return s_FailedPatches.ToArray();
                }
            }
        }

        public static string GetFailureMessage()
        {
            lock (s_Lock)
            {
                if (s_FailedPatches.Count == 0)
                    return string.Empty;

                return $"Warning: {s_FailedPatches.Count} patches failed. Some features may not work. Check log for details.";
            }
        }

        /// <summary>
        /// Get detailed failure info for UI display.
        /// </summary>
        public static string GetDetailedFailureMessage()
        {
            lock (s_Lock)
            {
                if (s_FailedPatches.Count == 0)
                    return string.Empty;

                var failedList = string.Join(", ", s_FailedPatches);
                return $"Failed patches: {failedList}. Check CivicSurvival.log for details.";
            }
        }

        public static void Clear()
        {
            lock (s_Lock)
            {
                s_PatchStatus.Clear();
                s_FailedPatches.Clear();
            }
        }
    }
}
