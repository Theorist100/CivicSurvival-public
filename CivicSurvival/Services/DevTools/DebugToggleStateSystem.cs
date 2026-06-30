#if DEBUG
using System;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Utils;
using CivicSurvival.Core.UI;
using Colossal.Logging;
using Unity.Entities;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Services.DevTools
{
    internal static class DebugABTestUiState
    {
        public static string Status { get; private set; } = string.Empty;

        public static void SetStatus(string status) => Status = status ?? string.Empty;
    }

    /// <summary>
    /// DEBUG ONLY: publishes authoritative toggle state and handles toggle commands.
    /// </summary>
    [ActIndependent]
    public partial class DebugToggleStateSystem : TriggeredThrottledUISystemBase
    {
        private static readonly LogContext Log = new("DebugToggleState");

        private static readonly string[] s_AllThreatKeys =
            { "d:threatFlight", "d:threatDamage", "d:waves", "d:threatUI" };

        protected override int UpdateInterval => 60;

        private ProfiledBinding<string> m_ToggleStates = null!;

        private static bool IsSingleSystemKey(string key) => key switch
        {
            "movement" or "droneSpawn" or "target" or "damage" or "opDamage" or "identify" => true,
            _ => false
        };

        protected override void ConfigureTriggers(TriggerRegistry triggers)
        {
            triggers.AddUngated<string, bool>(DebugToggleSystem, OnDebugToggleSystem);
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            DomainToggleRegistry.ResetState();
            m_ToggleStates = new ProfiledBinding<string>(Group, DebugToggleStates, "{}");
            AddBinding(m_ToggleStates.Binding);
        }

        protected override void OnThrottledUpdate()
        {
            if (!PerformanceProfiler.ABTestRunning && DebugABTestUiState.Status.Length > 0)
                DebugABTestUiState.SetStatus("");
            UpdateSnapshot();
        }

        /// <summary>
        /// Toggle threat/scenario/power systems for performance debugging.
        /// Composite keys: "wave" -> d:waves domain pass, "airDefense" -> d:airDefense domain pass.
        /// </summary>
        private void OnDebugToggleSystem(string systemName, bool enabled)
        {
            try
            {
                if (World == null || !World.IsCreated) return;

                // Domain-level toggle (incl. the d:allThreats bulk expansion).
                if (systemName.StartsWith("d:"))
                {
                    ToggleDomainOrBulk(systemName, enabled);
                    UpdateSnapshot();
                    return;
                }

                // Composite keys route through the namespace domain pass.
                if (systemName == "wave")
                {
                    DomainToggleRegistry.ToggleDomain(World, "d:waves", enabled);
                    UpdateSnapshot();
                    return;
                }
                if (systemName == "airDefense")
                {
                    DomainToggleRegistry.ToggleDomain(World, "d:airDefense", enabled);
                    UpdateSnapshot();
                    return;
                }

                // A/B test commands.
                if (systemName.StartsWith("ab:"))
                {
                    if (PerformanceProfiler.ABTestRunning)
                    {
                        PerformanceProfiler.StopABTest();
                        DebugABTestUiState.SetStatus("");
                        UpdateSnapshot();
                        Log.Info("[DEBUG] A/B test cancelled");
                        return;
                    }

                    if (systemName == "ab:s:all")
                    {
                        var keys = new[] { "s:apply", "s:collect", "s:ballistic", "s:radar" };
                        var toggles = new Action<bool>[]
                        {
                            on => DomainToggleRegistry.ToggleSubToggle("s:apply", on),
                            on => DomainToggleRegistry.ToggleSubToggle("s:collect", on),
                            on => DomainToggleRegistry.ToggleSubToggle("s:ballistic", on),
                            on => DomainToggleRegistry.ToggleSubToggle("s:radar", on),
                        };
                        PerformanceProfiler.StartABTestSequence(keys, toggles, 3, 6);
                        DebugABTestUiState.SetStatus(systemName);
                        UpdateSnapshot();
                        Log.Info("[DEBUG] A/B sequence: shared overhead (4 tests x 3 cycles x 30s)");
                        return;
                    }

                    if (systemName.StartsWith("ab:t:") || systemName.StartsWith("ab:s:"))
                    {
                        string subKey = systemName.Substring(3); // ab:t:render -> t:render
                        PerformanceProfiler.StartABTest(subKey, 3, 6, on => DomainToggleRegistry.ToggleSubToggle(subKey, on));
                        DebugABTestUiState.SetStatus(systemName);
                        UpdateSnapshot();
                        Log.Info($"[DEBUG] A/B test started: {subKey} (3 cycles x 30s)");
                        return;
                    }

                    string domain = "d:" + systemName.Substring(3);
                    PerformanceProfiler.StartABTest(domain, 3, 6, on => ToggleDomainOrBulk(domain, on));
                    DebugABTestUiState.SetStatus(systemName);
                    UpdateSnapshot();
                    Log.Info($"[DEBUG] A/B test started: {domain} (3 cycles x 30s)");
                    return;
                }

                // TMS sub-toggles (static flags, not system.Enabled).
                if (systemName.StartsWith("t:") || systemName.StartsWith("s:"))
                {
                    DomainToggleRegistry.ToggleSubToggle(systemName, enabled);
                    UpdateSnapshot();
                    return;
                }

                // Per-system sub-buttons: flip a single named system's Enabled.
                if (IsSingleSystemKey(systemName))
                {
                    DomainToggleRegistry.ToggleSingleSystem(World, systemName, enabled);
                    UpdateSnapshot();
                    return;
                }

                Log.Warn($"[DEBUG] '{systemName}' is not a known debug-toggle key");
            }
            catch (Exception ex) { Log.Warn($"[DEBUG] OnDebugToggleSystem failed: {ex}"); }
        }

        /// <summary>
        /// Route a domain key through the namespace pass. The "d:allThreats" bulk
        /// key expands to its four threat-domain members; every other "d:" key is a
        /// single domain.
        /// </summary>
        private void ToggleDomainOrBulk(string domainKey, bool enabled)
        {
            if (domainKey == "d:allThreats")
            {
                foreach (var member in s_AllThreatKeys)
                    DomainToggleRegistry.ToggleDomain(World, member, enabled);
                return;
            }

            DomainToggleRegistry.ToggleDomain(World, domainKey, enabled);
        }

        private void UpdateSnapshot()
        {
            m_ToggleStates.Update(DomainToggleRegistry.GetSnapshotJson(World));
        }
    }
}
#endif
