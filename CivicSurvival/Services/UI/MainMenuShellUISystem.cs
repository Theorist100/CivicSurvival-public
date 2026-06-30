using System;
using System.Collections.Generic;
using System.Text;
using Colossal.UI.Binding;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.UI;
using CivicSurvival.Core.Utils;
using static CivicSurvival.Core.UI.B;

namespace CivicSurvival.Services.UI
{
    /// <summary>
    /// Menu-safe half of the original MainUISystem. Owns global bindings and
    /// triggers that have no city/gameplay dependency and must remain available
    /// in the main menu (settings, mod info, JS bridge logs, UI theme).
    ///
    /// Pair with <see cref="GameSessionUISystem"/> which owns the city-loaded half
    /// (notification feed, initial notifications, threat focus).
    /// </summary>
    [ActIndependent]
    public partial class MainMenuShellUISystem : CivicUISystemBase
    {
        private static readonly LogContext Log = new("MainMenuShellUI");

        // ModSettings is process-lifetime (set up in Mod.OnLoad). UiTheme getter
        // can fire before OnStartRunning resolves it (in menu before vanilla
        // preload), so the getter has a defensive null fallback below.
        private ModSettings? m_Settings;

        protected override bool RequiresLoadedGame => false;

        protected override void OnUpdateImpl() { } // No per-frame logic — trigger/binding only

        protected override void OnCreate()
        {
            base.OnCreate();

            // JS log bridge — works in any context (Mod.Log writes to main log).
            AddBinding(new TriggerBinding<string>(Group, JsLog, OnJsLog));

            // UI React profiling trigger (JS sends pre-formatted report every 5s).
            // PerformanceProfiler.RecordUIReactReport stores in a static buffer —
            // safe to call before profiler is initialized.
            AddBinding(new TriggerBinding<string>(Group, UiProfileReport, OnUIProfileReport));

            // Global UI theme binding (needed by CommandBar ThemeProvider in menu too).
            AddUpdateBinding(new GetterValueBinding<int>(Group, UiTheme,
                () =>
                {
                    try
                    {
                        if (m_Settings == null)
                            return 0;

                        return m_Settings.UITheme;
                    }
                    catch (Exception e)
                    {
                        Log.Error($"UiTheme binding: {e}");
                        return 0;
                    }
                }));

            // Feature-wave manifest reads BalanceConfig snapshot — config is loaded
            // before SystemRegistrar so this works in menu too.
            AddUpdateBinding(new GetterValueBinding<string>(Group, FeatureWaveManifest,
                () =>
                {
                    try
                    {
                        return BuildFeatureWaveManifestJson();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"FeatureWaveManifest binding: {e}");
                        return BuildFeatureWaveManifestJson(new FeatureGatesConfig());
                    }
                }));

            Log.Info(" Initialized (menu-safe global bindings)");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Settings ??= ServiceRegistry.Instance.Require<ModSettings>();
        }

        private static void OnJsLog(string message)
        {
            Mod.Log.Info($"[JS] {message}");
        }

        private static void OnUIProfileReport(string report)
        {
            PerformanceProfiler.RecordUIReactReport(report);
        }

        private static string BuildFeatureWaveManifestJson()
        {
            return BuildFeatureWaveManifestJson(BalanceConfig.Current?.FeatureGates);
        }

        private static string BuildFeatureWaveManifestJson(FeatureGatesConfig? gates)
        {
            int current = gates?.CurrentWave ?? 1;
            var waves = gates?.Waves;

            var sb = new StringBuilder(256);
            sb.Append("{\"current\":").Append(current).Append(",\"waves\":{");
            AppendIntMap(sb, waves);
            sb.Append("}}");
            return sb.ToString();
        }

        private static void AppendIntMap(StringBuilder sb, IReadOnlyDictionary<string, int>? map)
        {
            if (map == null) return;
            bool first = true;
            foreach (var entry in map)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(EscapeJsonString(entry.Key)).Append("\":").Append(entry.Value);
            }
        }

        private static string EscapeJsonString(string value)
        {
            // Feature ids are alphanumeric; escape conservatively just in case.
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
