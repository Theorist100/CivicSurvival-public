using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Prefabs;
using HarmonyLib;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Infrastructure
{
    /// <summary>
    /// Central cache for private vanilla reflection handles used outside Harmony patches.
    /// Reflection misses are compatibility failures, so each miss is reported once per
    /// generation.
    ///
    /// Lifecycle (Phase 6 — generation + unload freeze):
    /// <list type="bullet">
    ///   <item><see cref="StartGeneration"/> opens a fresh resolution era at Mod.OnLoad.
    ///         Increments <see cref="CurrentGeneration"/>, clears per-generation failure
    ///         dedup, leaves cached <see cref="FieldInfo"/> resolves intact (Game.dll
    ///         identity does not change across mod reloads — same AppDomain).</item>
    ///   <item><see cref="BeginUnload"/> freezes the current generation at Mod.OnDispose
    ///         BEFORE Harmony cleanup runs. Cached hits still return true so late
    ///         cleanup-path callers keep working; uncached lookups return false silently
    ///         (no AccessTools.Field call, no PatchStatusTracker report, no log line).</item>
    ///   <item><see cref="ResetForTests"/> is the only entry that drops both caches.
    ///         Marked <see cref="ObsoleteAttribute"/> so production code cannot tempt
    ///         itself into using it from <c>Mod.OnDispose</c>.</item>
    /// </list>
    ///
    /// Pre-first-load state (<see cref="CurrentGeneration"/> == 0, <see cref="IsFrozen"/>
    /// == true) is observably identical to the frozen state — no resolution, no report.
    /// </summary>
    public static class VanillaReflectionRegistry
    {
        private static readonly LogContext Log = new("VanillaReflectionRegistry");
        private static readonly object s_Lock = new();
        private static readonly Dictionary<string, FieldInfo?> s_Fields = new();
        private static readonly HashSet<string> s_ReportedFailures = new();
        [CivicSurvival.Core.Attributes.RegistryGenerationCursor("Monotonic hot-reload boundary marker; carries no published snapshot payload — StartGeneration bumps, BeginUnload freezes, no consumer compares cursors to detect snapshot change.")]
        private static uint s_CurrentGeneration; // 0 = pre-first-load / between BeginUnload and next StartGeneration
        private static volatile bool s_IsFrozen = true;   // pre-first-load is observably equivalent to frozen

        /// <summary>
        /// Monotonic generation id. 0 means "no generation has started yet" (pre-first-load).
        /// Incremented by <see cref="StartGeneration"/>; never decremented. Wraps around at
        /// uint.MaxValue but the game would have hot-reloaded billions of times by then.
        /// </summary>
        public static uint CurrentGeneration
        {
            get
            {
                lock (s_Lock)
                {
                    return s_CurrentGeneration;
                }
            }
        }

        /// <summary>
        /// True iff <see cref="BeginUnload"/> has been called for the current generation
        /// and the next <see cref="StartGeneration"/> has not yet run.
        /// Pre-first-load (<see cref="CurrentGeneration"/> == 0) is also frozen.
        /// </summary>
        public static bool IsFrozen
        {
            get
            {
                lock (s_Lock)
                {
                    return s_IsFrozen;
                }
            }
        }

        /// <summary>
        /// Open a new resolution era. Called once per <c>Mod.OnLoad</c>.
        /// Increments <see cref="CurrentGeneration"/>; clears <see cref="s_ReportedFailures"/>
        /// (per-generation dedup); does NOT clear <see cref="s_Fields"/> (cached resolves
        /// remain valid because the AppDomain still hosts the same Game.dll across the
        /// hot-reload boundary). After this call, <see cref="IsFrozen"/> is false.
        /// </summary>
        public static void StartGeneration(string reason)
        {
            uint gen;
            int cachedFieldCount;
            lock (s_Lock)
            {
                s_CurrentGeneration++;
                s_IsFrozen = false;
                s_ReportedFailures.Clear();
                gen = s_CurrentGeneration;
                cachedFieldCount = s_Fields.Count;
            }
            Log.Info($"StartGeneration gen={gen} reason={reason} cachedFields={cachedFieldCount}");
        }

        /// <summary>
        /// Freeze the current generation. Called once per <c>Mod.OnDispose</c> BEFORE
        /// Harmony cleanup or service teardown. After this call:
        /// <list type="bullet">
        ///   <item>cached successful <see cref="FieldInfo"/> entries still return true
        ///         (callers that already paid the resolve in this generation keep
        ///         working during cleanup-time restore);</item>
        ///   <item>cached misses (entries with <see cref="FieldInfo"/> == null) still
        ///         return false silently;</item>
        ///   <item>uncached fields return false WITHOUT invoking
        ///         <c>AccessTools.Field</c>;</item>
        ///   <item>no entries are added to <see cref="s_ReportedFailures"/>;</item>
        ///   <item>no <see cref="PatchStatusTracker.ReportFailure"/> call is made;</item>
        ///   <item>no log lines are emitted.</item>
        /// </list>
        /// Idempotent — re-calling on an already-frozen registry is a no-op.
        /// </summary>
        public static void BeginUnload(string reason)
        {
            uint gen;
            bool wasFrozen;
            lock (s_Lock)
            {
                wasFrozen = s_IsFrozen;
                s_IsFrozen = true;
                gen = s_CurrentGeneration;
            }
            if (!wasFrozen)
                Log.Info($"BeginUnload gen={gen} reason={reason}");
        }

        public static bool TryGetPrefabSystemPrefabs(PrefabSystem prefabSystem, out List<PrefabBase> prefabs)
        {
            prefabs = null!;
            if (!TryGetField(typeof(PrefabSystem), Engine.Reflection.PREFAB_SYSTEM_PREFABS_FIELD, out var field))
                return false;

            var list = field.GetValue(prefabSystem) as List<PrefabBase>;
            if (list == null)
                return false;

            prefabs = list;
            return true;
        }

        public static bool TryGetField(Type ownerType, string fieldName, out FieldInfo field)
        {
            string key = $"{ownerType.FullName}.{fieldName}";
            FieldInfo? resolved;
            bool frozen;
            bool cached;

            lock (s_Lock)
            {
                frozen = s_IsFrozen;
                cached = s_Fields.TryGetValue(key, out resolved);

                if (!cached)
                {
                    if (frozen)
                    {
                        // Freeze contract: uncached resolution is suppressed. Return false
                        // silently — no AccessTools.Field call, no failure report, no log.
                        field = null!;
                        return false;
                    }

                    resolved = AccessTools.Field(ownerType, fieldName);
                    s_Fields[key] = resolved;
                }
            }

            if (resolved != null)
            {
                field = resolved;
                return true;
            }

            field = null!;

            // Cached miss during freeze must NOT re-burn a report. Only an active
            // generation reports the failure.
            if (!frozen)
                ReportMissingFieldOnce(key, ownerType, fieldName);
            return false;
        }

        /// <summary>
        /// Test/diagnostic only. NOT called from production lifecycle.
        /// Drops both <see cref="s_Fields"/> and <see cref="s_ReportedFailures"/>, resets
        /// <see cref="CurrentGeneration"/> to 0, sets <see cref="IsFrozen"/> back to true.
        /// Marked <see cref="ObsoleteAttribute"/> with error=false so test assemblies can
        /// call it freely while production code surfaces a compiler warning if anybody
        /// is tempted to wire it into <c>Mod.OnDispose</c>.
        /// </summary>
        // S1133 false positive: the [Obsolete] marker is the contract, not a deprecation reminder —
        // production callers must use StartGeneration / BeginUnload; only tests reach this entry point.
#pragma warning disable S1133
        [Obsolete("Use StartGeneration / BeginUnload from production code. ResetForTests is a test-only entry point.")]
#pragma warning restore S1133
        public static void ResetForTests()
        {
            lock (s_Lock)
            {
                s_Fields.Clear();
                s_ReportedFailures.Clear();
                s_CurrentGeneration = 0;
                s_IsFrozen = true;
            }
        }

        private static void ReportMissingFieldOnce(string key, Type ownerType, string fieldName)
        {
            lock (s_Lock)
            {
                if (s_IsFrozen)
                    return; // double-check inside lock — freeze raced with the report.
                if (!s_ReportedFailures.Add(key))
                    return;
            }

            string reason = $"{ownerType.FullName}.{fieldName} field not found";
            Log.Error(reason);
            PatchStatusTracker.ReportFailure($"VanillaReflection:{ownerType.Name}.{fieldName}", reason);
        }

    }
}
