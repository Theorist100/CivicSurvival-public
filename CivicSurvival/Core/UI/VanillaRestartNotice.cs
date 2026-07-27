using CivicSurvival.Core.Utils;
using Game.SceneFlow;
using Game.UI;
using Game.UI.Localization;
using CivicLoc = CivicSurvival.Localization.LocalizationManager;

namespace CivicSurvival.Core.UI
{
    /// <summary>
    /// Vanilla-channel "mod updated — restart required" notice.
    ///
    /// Why a second channel exists at all: our React modal is delivered by the same
    /// activation-driven registration as the .cok assets (the UI .mjs bundle rides the
    /// instance folder), so in exactly the situations that need this notice — install
    /// folder deleted under the live process, playset deactivation — the React layer is
    /// the first casualty. Prod confirmed the blind spot: 0 update_modal events across
    /// every α finalize report in history. Vanilla's own dialog UI ships with the game
    /// install and survives any state of our mod's folder, and vanilla ModManager uses
    /// this same route for its mod-load error dialogs.
    ///
    /// Strings come from our C# LocalizationManager (embedded resource, in memory since
    /// boot — readable after the folder is gone), passed as raw values: vanilla's
    /// localization dictionary never sees our keys, so LocalizedString.Id would render
    /// the bare key instead of text.
    /// </summary>
    internal static class VanillaRestartNotice
    {
        private static readonly LogContext Log = new("VanillaRestartNotice");

        /// <summary>
        /// Shows the vanilla MessageDialog. Returns a status string for the caller's
        /// telemetry line ("ok" / "no-ui" / "failed:&lt;type&gt;") instead of throwing:
        /// this is a last-resort notification channel — its failure must never take
        /// down the detection path it decorates.
        /// </summary>
        public static string Show()
        {
            try
            {
                var ui = GameManager.instance?.userInterface;
                if (ui == null)
                    return "no-ui";

                // _VANILLA key, not the React modal's _TEXT: vanilla's dialog body goes
                // through FormattedParagraphs + the markup renderer (decompile-verified:
                // game-ui/common/text/formatted-paragraphs.tsx splits on \n and DROPS blank
                // lines; markup-renderer.tsx turns leading "- " into styled list items with
                // 1em paragraph gaps and **…** into <b>). The React modal renders its key
                // as plain pre-line text — feeding it this markup would show raw symbols.
                var dialog = new MessageDialog(
                    LocalizedString.Value(CivicLoc.Get("MODAL_MOD_UPDATED_RESTART_TITLE")),
                    LocalizedString.Value(CivicLoc.Get("MODAL_MOD_UPDATED_RESTART_TEXT_VANILLA")),
                    LocalizedString.Value(CivicLoc.Get("MODAL_MOD_UPDATED_RESTART_OK")));
                // Warn, not Info: in the α case this ack is the ONLY proof the player saw
                // the notice (the React modal's shown/dismissed telemetry is dead there),
                // and Warn stands out in the log file that rides a manual bug report.
                // Not Error: prod telemetry forwards Error lines — an ack is not an error.
                ui.appBindings.ShowMessageDialog(dialog,
                    _ => Log.Warn("Vanilla restart notice acknowledged by player"));
                return "ok";
            }
#pragma warning disable CIVIC052 // False positive: the exception IS reported — the status
            // string rides the caller's Error line (same pattern as the ModInstanceWatch
            // sibling probe and ModInstallResolver.DescribeActiveSetSafe).
            catch (System.Exception e)
            {
                return $"failed:{e.GetType().Name}";
            }
#pragma warning restore CIVIC052
        }
    }
}
