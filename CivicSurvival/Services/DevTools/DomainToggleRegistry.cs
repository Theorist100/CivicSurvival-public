#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Logging;
using Unity.Entities;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Domains.ThreatFlight.Systems;
using CivicSurvival.Domains.ThreatDamage.Systems;
using CivicSurvival.Domains.ThreatUI.Systems;
using CivicSurvival.Domains.Waves.Systems;

namespace CivicSurvival.Services.DevTools
{
    /// <summary>
    /// Registry mapping domain / sub-toggle keys to concrete ECS systems.
    ///
    /// Domain keys disable every live <see cref="CivicSystemBase"/> of the domain's
    /// root namespace by calling each system's <c>SetSchedulerEnabled</c> command
    /// (namespace-driven, mirrors <c>VanillaSystemAutoProfiler</c>'s namespace
    /// classification). The command lives on the system itself because CIVIC481 bans
    /// cross-system <c>Enabled</c> pokes. A helper/cache created off the registration
    /// path is still caught because it lives under the same namespace prefix.
    ///
    /// Sub-keys toggle a single named system's scheduler. TMS sub-toggles
    /// (<c>t:</c>/<c>s:</c>) flip <c>ThreatMovementSystem</c> static skip flags.
    /// </summary>
    internal static class DomainToggleRegistry
    {
        private static readonly LogContext Log = new("DomainToggle");
        private static readonly HashSet<string> s_DebugDisabledDomains = new();

        public static void ResetState()
        {
            s_DebugDisabledDomains.Clear();
        }

        private const int SNAPSHOT_JSON_CAPACITY = 4096;

        /// <summary>
        /// Toggle ThreatMovementSystem static skip flags (sub-component toggles).
        /// </summary>
        public static void ToggleSubToggle(string subKey, bool enabled)
        {
            bool skip = !enabled;
#pragma warning disable S2696 // Debug toggle must set static flags from static context
#pragma warning disable CIVIC135 // Debug toggle sub-keys are string-based by design
            switch (subKey)
            {
                case "t:render":    ThreatMovementSystem.SkipRender = skip; break;
                case "t:move":      ThreatMovementSystem.SkipMovement = skip; break;
                case "t:obstacle":  ThreatMovementSystem.SkipObstacles = skip; break;
                case "s:apply":     ThreatMovementSystem.SkipApply = skip; break;
                case "s:collect":   ThreatMovementSystem.SkipCollect = skip; break;
                case "s:ballistic": ThreatMovementSystem.SkipBallistic = skip; break;
                case "s:radar":     ThreatMovementSystem.SkipRadar = skip; break;
                default: Log.Warn($"[DEBUG] Unknown sub-toggle: {SanitizeKey(subKey)}"); return;
            }
#pragma warning restore CIVIC135
#pragma warning restore S2696
            PerformanceProfiler.LogMarker($"TOGGLE {subKey} = {(enabled ? "ON" : "OFF")}");
            s_SnapshotVersion++;
        }

        /// <summary>
        /// Disable every live system of a domain by flipping <c>system.Enabled</c>
        /// for each managed system whose namespace matches the domain's root prefix.
        /// Keys: "d:" prefix for domains (Countermeasures is a domain, key "d:countermeasures").
        /// </summary>
        public static void ToggleDomain(World world, string domain, bool enabled)
        {
            if (world == null || !world.IsCreated)
            {
                Log.Warn($"[DEBUG] World unavailable - cannot toggle {SanitizeKey(domain)}");
                return;
            }

            if (IsFeatureClosedForKey(domain)
                && (!enabled || !s_DebugDisabledDomains.Contains(domain)))
            {
                Log.Warn($"[DEBUG] Refusing to toggle closed feature entry: {SanitizeKey(domain)}");
                return;
            }

            if (!IsRuntimeControlKey(domain))
            {
                Log.Warn($"[DEBUG] Refusing to toggle display-only entry: {SanitizeKey(domain)}");
                return;
            }

            string? prefix = NamespacePrefixForKey(domain);
            if (prefix == null)
            {
                Log.Warn($"[DEBUG] Unknown domain: {SanitizeKey(domain)}");
                return;
            }

            int flipped = ApplyNamespaceEnabled(world, prefix, enabled);

            TrackDomainToggle(domain, enabled);
            PerformanceProfiler.LogMarker(
                $"TOGGLE {domain} = {(enabled ? "ON" : "OFF")} ({flipped} systems) @ {CurrentGameHours():F1}h");
            s_SnapshotVersion++;
        }

        /// <summary>
        /// Toggle a single named system's <c>Enabled</c> (per-system sub-button).
        /// </summary>
        public static void ToggleSingleSystem(World world, string key, bool enabled)
        {
            if (world == null || !world.IsCreated)
            {
                Log.Warn($"[DEBUG] World unavailable - cannot toggle {SanitizeKey(key)}");
                return;
            }

            if (ResolveSingleSystem(world, key) is not CivicSystemBase system)
            {
                Log.Warn($"[DEBUG] Unknown sub-system key: {SanitizeKey(key)}");
                return;
            }

            system.SetSchedulerEnabled(enabled);
            PerformanceProfiler.LogMarker(
                $"TOGGLE {key} = {(enabled ? "ON" : "OFF")} ({system.GetType().Name}) @ {CurrentGameHours():F1}h");
            s_SnapshotVersion++;
        }

        /// <summary>
        /// Toggle every non-allowlisted <see cref="CivicSystemBase"/> under
        /// <paramref name="prefix"/> via its <c>SetSchedulerEnabled</c> command.
        /// Returns the count of systems toggled.
        /// </summary>
        private static int ApplyNamespaceEnabled(World world, string prefix, bool enabled)
        {
            int flipped = 0;
            foreach (var system in world.Systems)
            {
                if (system == null)
                    continue;

                var ns = system.GetType().Namespace;
                if (ns == null)
                    continue;
                if (ns != prefix && !ns.StartsWith(prefix + ".", StringComparison.Ordinal))
                    continue;
                if (IsAllowlisted(system))
                    continue;
                if (!TrySetSchedulerEnabled(system, enabled))
                    continue;

                flipped++;
            }
            return flipped;
        }

        /// <summary>
        /// Systems that must never be disabled: ECB barriers (their OnUpdate plays
        /// back queued structural commands) and [FrameworkSystem] infrastructure.
        /// Excluded by TYPE, never by namespace string. Defence-in-depth: barriers
        /// physically live in Core.Systems.Scheduling, outside Domains.*.
        /// </summary>
        private static bool IsAllowlisted(ComponentSystemBase system)
        {
            if (system is EntityCommandBufferSystem)
                return true;
            return Attribute.IsDefined(system.GetType(), typeof(FrameworkSystemAttribute));
        }

        /// <summary>
        /// Toggle a system's scheduler through the command method on whichever civic
        /// base it derives from — <see cref="CivicSystemBase"/> (gameplay) or
        /// <see cref="CivicUISystemBase"/> (UI panels). Returns false for systems that
        /// expose no command (e.g. a raw vanilla <c>GameSystemBase</c>); those are then
        /// neither toggled nor counted, so the snapshot stays consistent with what the
        /// pass can actually disable.
        /// </summary>
        private static bool TrySetSchedulerEnabled(ComponentSystemBase system, bool enabled)
        {
            switch (system)
            {
                case CivicSystemBase civic:
                    civic.SetSchedulerEnabled(enabled);
                    return true;
                case CivicUISystemBase ui:
                    ui.SetSchedulerEnabled(enabled);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether <see cref="TrySetSchedulerEnabled"/> can toggle this system.</summary>
        private static bool IsToggleableSystem(ComponentSystemBase system)
            => system is CivicSystemBase or CivicUISystemBase;

        private static ComponentSystemBase? ResolveSingleSystem(World world, string key)
        {
#pragma warning disable CIVIC135 // Debug sub-keys are string-based by design
            return key switch
            {
                "movement"   => world.GetExistingSystemManaged<ThreatMovementSystem>(),
                "droneSpawn" => world.GetExistingSystemManaged<ThreatSpawnSystem>(),
                "target"     => world.GetExistingSystemManaged<ThreatTargetSystem>(),
                "damage"     => world.GetExistingSystemManaged<ThreatDamageSystem>(),
                "opDamage"   => world.GetExistingSystemManaged<OperationalDamageSystem>(),
                "identify"   => world.GetExistingSystemManaged<ThreatIdentifySystem>(),
                _ => null
            };
#pragma warning restore CIVIC135
        }

        private static float CurrentGameHours()
            => GameTimeSystem.TryGetGameHours(out float hours) ? hours : 0f;

        private static void TrackDomainToggle(string domain, bool enabled)
        {
            if (enabled)
                s_DebugDisabledDomains.Remove(domain);
            else
                s_DebugDisabledDomains.Add(domain);
        }

        [CivicSurvival.Core.Attributes.DebugSnapshotDirtyCursor("Debug UI dirty cursor for domain-toggle JSON snapshots.")]
        private static int s_SnapshotVersion;

        private readonly struct SnapshotEntry
        {
            public readonly string Key;
            public readonly string Group;
            public readonly string Parent;

            public SnapshotEntry(string key, string group, string parent = "")
            {
                Key = key;
                Group = group;
                Parent = parent;
            }
        }

        private static readonly SnapshotEntry[] s_SnapshotEntries =
        {
            new("d:threatFlight", "threat"),
            new("d:threatDamage", "threat"),
            new("d:waves", "threat"),
            new("d:threatUI", "threat"),

            new("d:airDefense", "domain"),
            new("d:intel", "domain"),
            new("d:spotters", "domain"),
            new("d:cognitive", "domain"),
            new("d:engineering", "domain"),
            new("d:scenario", "domain"),
            new("d:corruption", "domain"),
            new("d:refugees", "domain"),
            new("d:powerGrid", "domain"),
            new("d:powerBackup", "domain"),
            new("d:attention", "domain"),
            new("d:blackout", "domain"),
            new("d:diplomacy", "domain"),
            new("d:gridWarfare", "domain"),
            new("d:mobilization", "domain"),
            new("d:narrative", "domain"),
            new("d:economics", "domain"),
            new("d:finance", "domain"),
            new("d:shadowEconomy", "domain"),
            new("d:network", "domain"),
            new("d:neighborEnvy", "domain"),
            new("d:notifications", "domain"),
            new("d:tutorial", "domain"),
            new("d:countermeasures", "domain"),

            new("t:render", "sub", "d:threatFlight"),
            new("t:move", "sub", "d:threatFlight"),
            new("t:obstacle", "sub", "d:threatFlight"),
            new("s:apply", "sub", "d:threatFlight"),
            new("s:collect", "sub", "d:threatFlight"),
            new("s:ballistic", "sub", "d:threatFlight"),
            new("s:radar", "sub", "d:threatFlight"),
            new("movement", "sub", "d:threatFlight"),
            new("damage", "sub", "d:threatDamage"),
            new("opDamage", "sub", "d:threatDamage"),
            new("droneSpawn", "sub", "d:waves"),
            new("target", "sub", "d:waves"),
            new("identify", "sub", "d:threatUI"),
        };

        public static string GetSnapshotJson(World world)
        {
            // One pass over world.Systems tallies every domain key, instead of one
            // scan per domain entry (~28× cheaper for the throttled rebuild).
            var domainState = new Dictionary<string, (int count, bool anyOn)>(StringComparer.Ordinal);
            if (world != null && world.IsCreated)
                BuildDomainStateMap(world, BuildPrefixToKey(), domainState);

            var sb = new StringBuilder(SNAPSHOT_JSON_CAPACITY);
            sb.Append("{\"version\":").Append(s_SnapshotVersion).Append(",\"entries\":[");
            for (int i = 0; i < s_SnapshotEntries.Length; i++)
            {
                if (i > 0) sb.Append(',');
                SnapshotEntry entry = s_SnapshotEntries[i];
                string lockedReasonId = LockedReasonFor(entry.Key);
                bool canDisable = lockedReasonId.Length == 0;

                ResolveEntryState(world, entry.Key, domainState, out bool enabled, out int systemCount);

                sb.Append("{\"key\":");
                AppendJsonString(sb, entry.Key);
                sb.Append(",\"enabled\":")
                    .Append(enabled ? "true" : "false")
                    .Append(",\"canDisable\":").Append(canDisable ? "true" : "false")
                    .Append(",\"lockedReasonId\":");
                AppendJsonString(sb, lockedReasonId);
                sb.Append(",\"group\":");
                AppendJsonString(sb, entry.Group);
                sb.Append(",\"parent\":");
                AppendJsonString(sb, entry.Parent);
                sb.Append(",\"systemCount\":").Append(systemCount).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// One pass over <c>world.Systems</c> tallying, per domain key, the count of
        /// toggleable non-allowlisted systems and whether any is Enabled. Replaces the
        /// former per-entry scan (O(entries × systems)) so the throttled snapshot
        /// rebuild stays cheap with ~28 domain entries.
        /// </summary>
        private static void BuildDomainStateMap(
            World world,
            Dictionary<string, string> prefixToKey,
            Dictionary<string, (int count, bool anyOn)> acc)
        {
            foreach (var system in world.Systems)
            {
                if (system == null)
                    continue;
                if (IsAllowlisted(system) || !IsToggleableSystem(system))
                    continue;
                var ns = system.GetType().Namespace;
                if (ns == null)
                    continue;
                string? rootPrefix = RootPrefixOf(ns);
                if (rootPrefix == null || !prefixToKey.TryGetValue(rootPrefix, out string key))
                    continue;

                (int count, bool anyOn) prev = acc.TryGetValue(key, out var v) ? v : default;
                acc[key] = (prev.count + 1, prev.anyOn || system.Enabled);
            }
        }

        /// <summary>
        /// Extracts the <c>CivicSurvival.Domains.&lt;Root&gt;</c> prefix from a system
        /// namespace (folding sub-namespaces into their domain root), or null when the
        /// namespace is not under <c>Domains</c>.
        /// </summary>
        private static string? RootPrefixOf(string ns)
        {
            const string DomainsRoot = "CivicSurvival.Domains.";
            if (!ns.StartsWith(DomainsRoot, StringComparison.Ordinal))
                return null;
            int dot = ns.IndexOf('.', DomainsRoot.Length);
            return dot < 0 ? ns : ns.Substring(0, dot);
        }

        /// <summary>
        /// Reverse of <see cref="NamespacePrefixForKey"/>: domain root prefix → key.
        /// Rebuilt per snapshot (28 entries, negligible) so there is no static
        /// reload-local state to reset.
        /// </summary>
        private static Dictionary<string, string> BuildPrefixToKey()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in s_SnapshotEntries)
            {
                string? prefix = NamespacePrefixForKey(entry.Key);
                if (prefix != null)
                    map[prefix] = entry.Key;
            }
            return map;
        }

        /// <summary>
        /// Resolves an entry's snapshot state. Sub-keys / single-system keys read their
        /// one system directly; domain keys read the pre-tallied
        /// <paramref name="domainState"/> from <see cref="BuildDomainStateMap"/> (no
        /// per-entry scan).
        /// </summary>
        private static void ResolveEntryState(
            World? world,
            string key,
            Dictionary<string, (int count, bool anyOn)> domainState,
            out bool enabled,
            out int systemCount)
        {
            if (key.StartsWith("t:", StringComparison.Ordinal)
                || key.StartsWith("s:", StringComparison.Ordinal))
            {
                enabled = IsSubToggleEnabled(key);
                systemCount = 1;
                return;
            }

            if (world == null || !world.IsCreated)
            {
                enabled = true;
                systemCount = IsSingleSystemKey(key) ? 1 : 0;
                return;
            }

            if (IsSingleSystemKey(key))
            {
                ComponentSystemBase? system = ResolveSingleSystem(world, key);
                enabled = system == null || system.Enabled;
                systemCount = 1;
                return;
            }

            // Domain key: read the single-pass tally. ON iff at least one toggleable
            // non-allowlisted system of the namespace is Enabled.
            (int count, bool anyOn) tally = domainState.TryGetValue(key, out var v) ? v : default;
            enabled = tally.anyOn;
            systemCount = tally.count;
        }

        private static string LockedReasonFor(string key)
        {
            if (!IsRuntimeControlKey(key))
                return ReasonIds.ToggleDisplayOnly;
            return IsFeatureClosedForKey(key)
                ? ReasonIds.ToggleFeatureClosed
                : "";
        }

        /// <summary>
        /// A key is runtime-controllable when it maps to a concrete disable
        /// mechanism: a domain namespace, a single sub-system, or a TMS sub-flag.
        /// </summary>
        private static bool IsRuntimeControlKey(string key)
        {
            if (key.StartsWith("t:", StringComparison.Ordinal)
                || key.StartsWith("s:", StringComparison.Ordinal))
                return IsTmsSubToggleKey(key);
            if (IsSingleSystemKey(key))
                return true;
            return NamespacePrefixForKey(key) != null;
        }

        private static bool IsSingleSystemKey(string key) => key switch
        {
            "movement" or "droneSpawn" or "target" or "damage" or "opDamage" or "identify" => true,
            _ => false
        };

        private static bool IsTmsSubToggleKey(string key) => key switch
        {
            "t:render" or "t:move" or "t:obstacle"
                or "s:apply" or "s:collect" or "s:ballistic" or "s:radar" => true,
            _ => false
        };

        private static bool IsFeatureClosedForKey(string key)
        {
            return FeatureRegistry.IsInitialized
                && FeatureIdForKey(key) is { Length: > 0 } featureId
                && FeatureRegistry.Instance.IsUnavailable(featureId, out _);
        }

        /// <summary>
        /// Domain key → root namespace prefix to match in <c>World.Systems</c>.
        /// Mirrors <see cref="FeatureIdForKey"/>. Countermeasures historically lived
        /// under the Core tab ("c:countermeasures") but is physically a Domains.*
        /// namespace, so it is now a domain button "d:countermeasures".
        /// </summary>
        private static string? NamespacePrefixForKey(string key)
        {
#pragma warning disable CIVIC135 // Debug toggle keys intentionally mirror debug toggle command keys.
            return key switch
            {
                "d:threatFlight"  => "CivicSurvival.Domains.ThreatFlight",
                "d:threatDamage"  => "CivicSurvival.Domains.ThreatDamage",
                "d:waves"         => "CivicSurvival.Domains.Waves",
                "d:threatUI"      => "CivicSurvival.Domains.ThreatUI",
                "d:airDefense"    => "CivicSurvival.Domains.AirDefense",
                "d:intel"         => "CivicSurvival.Domains.Intel",
                "d:spotters"      => "CivicSurvival.Domains.Spotters",
                "d:cognitive"     => "CivicSurvival.Domains.Cognitive",
                "d:engineering"   => "CivicSurvival.Domains.Engineering",
                "d:scenario"      => "CivicSurvival.Domains.Scenario",
                "d:corruption"    => "CivicSurvival.Domains.Corruption",
                "d:refugees"      => "CivicSurvival.Domains.Refugees",
                "d:powerGrid"     => "CivicSurvival.Domains.PowerGrid",
                "d:powerBackup"   => "CivicSurvival.Domains.PowerBackup",
                "d:attention"     => "CivicSurvival.Domains.Attention",
                "d:blackout"      => "CivicSurvival.Domains.Blackout",
                "d:diplomacy"     => "CivicSurvival.Domains.Diplomacy",
                "d:gridWarfare"   => "CivicSurvival.Domains.GridWarfare",
                "d:mobilization"  => "CivicSurvival.Domains.Mobilization",
                "d:narrative"     => "CivicSurvival.Domains.Narrative",
                "d:economics"     => "CivicSurvival.Domains.Economics",
                "d:finance"       => "CivicSurvival.Domains.Finance",
                "d:shadowEconomy" => "CivicSurvival.Domains.ShadowEconomy",
                "d:network"       => "CivicSurvival.Domains.Network",
                "d:neighborEnvy"  => "CivicSurvival.Domains.NeighborEnvy",
                "d:notifications" => "CivicSurvival.Domains.Notifications",
                "d:tutorial"      => "CivicSurvival.Domains.Tutorial",
                "d:countermeasures" => "CivicSurvival.Domains.Countermeasures",
                _ => null
            };
#pragma warning restore CIVIC135
        }

        private static string FeatureIdForKey(string key)
        {
#pragma warning disable CIVIC135 // Debug snapshot keys intentionally mirror debug toggle command keys.
            return key switch
            {
                "d:threatFlight" => "ThreatFlight",
                "d:threatDamage" => "ThreatDamage",
                "d:waves" => "Waves",
                "d:threatUI" => "ThreatUI",
                "d:airDefense" => "AirDefense",
                "d:intel" => "Intel",
                "d:spotters" => "Spotters",
                "d:cognitive" => "Cognitive",
                "d:engineering" => "Engineering",
                "d:scenario" => "Scenario",
                "d:corruption" => "Corruption",
                "d:refugees" => "Refugees",
                "d:powerGrid" => "PowerGrid",
                "d:powerBackup" => "PowerBackup",
                "d:attention" => "Attention",
                "d:blackout" => "Blackout",
                "d:diplomacy" => "Diplomacy",
                "d:gridWarfare" => "GridWarfare",
                "d:mobilization" => "Mobilization",
                "d:narrative" => "Narrative",
                "d:economics" => "Economy",
                "d:finance" => "Finance",
                "d:shadowEconomy" => "ShadowEconomy",
                "d:network" => "Network",
                "d:neighborEnvy" => "NeighborEnvy",
                "d:notifications" => "Notifications",
                "d:tutorial" => "Tutorial",
                "d:countermeasures" => "Countermeasures",
                "t:render" or "t:move" or "t:obstacle" or "s:apply" or "s:collect" or "s:ballistic" or "s:radar" or "movement" => "ThreatFlight",
                "damage" or "opDamage" => "ThreatDamage",
                "droneSpawn" or "target" => "Waves",
                "identify" => "ThreatUI",
                _ => ""
            };
#pragma warning restore CIVIC135
        }

        private static bool IsSubToggleEnabled(string key) => key switch
        {
            "t:render" => !ThreatMovementSystem.SkipRender,
            "t:move" => !ThreatMovementSystem.SkipMovement,
            "t:obstacle" => !ThreatMovementSystem.SkipObstacles,
            "s:apply" => !ThreatMovementSystem.SkipApply,
            "s:collect" => !ThreatMovementSystem.SkipCollect,
            "s:ballistic" => !ThreatMovementSystem.SkipBallistic,
            "s:radar" => !ThreatMovementSystem.SkipRadar,
            _ => false
        };

        private static string SanitizeKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            value = value.Replace('\r', '?').Replace('\n', '?');
            return value.Length <= 32 ? value : value.Substring(0, 32);
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '"' || c == '\\')
                        sb.Append('\\').Append(c);
                    else if (c == '\r')
                        sb.Append("\\r");
                    else if (c == '\n')
                        sb.Append("\\n");
                    else
                        sb.Append(c);
                }
            }
            sb.Append('"');
        }
    }
}
#endif
