using Game;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.Systems.UI;
using CivicSurvival.Core.Utils;
#if DEBUG
using CivicSurvival.Services.DevTools;
#endif

namespace CivicSurvival.Services.UI
{
    /// <summary>
    /// UI domain - main UI, settings, debug panels.
    /// Priority 3000 = UI tier (runs AFTER GameSimulation).
    ///
    /// NOTE: Located in Services/UI/ (not Core/) because it registers systems
    /// that import domain namespaces. Core should remain domain-agnostic.
    ///
    /// All domain UI panels migrated to their own CivicUIPanelSystem per domain.
    /// Feature-specific UI systems live in their owning feature modules.
    ///
    /// Global UI is split into two halves:
    /// - MainMenuShellUISystem: menu-safe global bindings (UiTheme, JsLog,
    ///   UiProfileReport, FeatureWaveManifest). Has no city/gameplay dependency.
    /// - GameSessionUISystem: city-loaded bindings (NotificationSystem feed,
    ///   FocusNextThreat trigger, SendInitialNotifications). Requires
    ///   NotificationSystem (covered by Dependencies = Notifications).
    /// </summary>
    public class UIDomain : IFeatureModule, IDependentFeatureModule
    {
        public IReadOnlyList<string> Dependencies { get; } = new[] { "Notifications" };

        private static readonly LogContext Log = new("UIDomain");
        private const int PRIORITY = 3000;

        public string Name => "UI";
        public int Priority => PRIORITY;

        public void RegisterSystems(UpdateSystem updateSystem)
        {
            Log.Info(" Registering systems...");

            // Menu-safe global shell — UiTheme, JsLog, UiProfileReport,
            // FeatureWaveManifest. Stays live in menu (future Phase B will
            // whitelist this instead of the old mixed MainUISystem).
            updateSystem.RegisterAt<MainMenuShellUISystem>(SystemUpdatePhase.UIUpdate);

            // Game-session bindings — notification feed, FocusNextThreat,
            // initial city-load notifications. Must be after the menu shell
            // for stable invocation order; needs NotificationSystem (provided
            // by Notifications dependency above).
            updateSystem.RegisterAt<GameSessionUISystem>(SystemUpdatePhase.UIUpdate);

            // Modal coordinator state - single active modal contract for React
            updateSystem.RegisterAt<ModalCoordinatorUISystem>(SystemUpdatePhase.UIUpdate);

            // Settings UI system - game settings panel
            updateSystem.RegisterAt<SettingsUISystem>(SystemUpdatePhase.UIUpdate);

            // Crisis sweep UI - in-game balance/diagnostics tool. Production-registered (the
            // trigger + DTO binding are cheap, throttled): the dev panel only fires the trigger
            // and reads the binding, but the request/result lifecycle is real (not #if DEBUG).
            updateSystem.RegisterAt<CrisisSweepUISystem>(SystemUpdatePhase.UIUpdate);

            // Help state system - tracks help button "seen" state
            updateSystem.RegisterAt<HelpStateSystem>(SystemUpdatePhase.UIUpdate);

            // Trigger result - pushes rejection FailReason to UI for toast display
            updateSystem.RegisterAt<TriggerResultUISystem>(SystemUpdatePhase.UIUpdate);

#if DEBUG
            updateSystem.RegisterAt<BalanceMetricsSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<ScenarioInspectorSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<ScenarioDebugActionSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<DebugToggleStateSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<ThreatDebugSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.RegisterAt<PersonalChronicleDebugSystem>(SystemUpdatePhase.UIUpdate);
            Log.Info(" [DEBUG] DevTools systems registered (6)");
#else
            Log.Info(" Release build - DevTools systems skipped");
#endif

            Log.Info(" Systems registered");
        }
    }
}
