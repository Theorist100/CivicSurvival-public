using System;
using System.Diagnostics;
using Colossal.PSI.Common;
using CivicSurvival.Core.Utils;
using PdxMod = Colossal.PSI.Common.Mod;

namespace CivicSurvival.Core.Systems.Bootstrap
{
    /// <summary>
    /// Records platform subscription events for OUR mod id, so a folder deletion the player asked for
    /// can be told apart from one nobody asked for.
    ///
    /// The one question it answers: did the player unsubscribe from this mod in this process? Vanilla
    /// raises <c>PlatformManager.onModSubscriptionChanged</c> with <c>ModUnsubscribed</c> from
    /// <c>PdxSdkPlatform.OnModUnsubscribe</c> (decompile <c>PdxSdkPlatform.cs:1358-1382</c>), which sits
    /// on the SDK's own <c>Mods.Management.ModUnsubscribed</c> callback.
    ///
    /// ONE-SIDED BY CONSTRUCTION, and that is the whole design. The event is published only after an
    /// awaited <c>GetDetails</c> and only when that call succeeds (<c>:1362-1363</c>), so an offline
    /// client or a failed lookup produces no event at all for a real unsubscribe. Therefore:
    ///   * event seen  → the player removed the mod. Conclusive.
    ///   * no event    → proves NOTHING. Never read as "the player did not remove it".
    /// It also cannot see the other benign removal — taking the mod out of the playset without
    /// unsubscribing raises no subscription event — which is why the classifier keeps a separate,
    /// weaker signal (the backend listing) for that.
    ///
    /// Cost: one multicast handler for the life of the process, doing a string comparison. No polling,
    /// no allocation per event beyond the stamp string, nothing read from game state.
    /// </summary>
    internal static class ModSubscriptionWatch
    {
        private static readonly LogContext Log = new("ModSubscriptionWatch");

        // Wall-clock from arming, so the stamp is comparable with the watchdog's own elapsed seconds.
        private static readonly Stopwatch s_Clock = Stopwatch.StartNew();

        // Written from the platform's thread, read from the main thread at a verdict. A string
        // reference cannot tear; a struct with a timestamp could.
        private static volatile string? s_LastOurEvent;
        private static volatile bool s_UnsubscribeSeen;
        // Arm/Disarm are main-thread only (loader activation and teardown), but this sits in a type
        // whose other fields are written from the platform's thread — volatile keeps the whole class
        // on one memory-visibility rule instead of asking every future reader to re-derive which field
        // is safe on which thread (CIVIC059).
        private static volatile bool s_Armed;

        /// <summary>
        /// Did an unsubscribe for our id arrive in this process. See the type comment before using it:
        /// true is conclusive, false is not evidence of anything.
        /// </summary>
        public static bool UnsubscribeSeen => s_UnsubscribeSeen;

        /// <summary>The last subscription event seen for our id, for the report line. "none" if none.</summary>
        public static string Describe() => s_LastOurEvent ?? "none";

        /// <summary>
        /// Subscribe to the platform's subscription events. Called from the native loader's subscribe
        /// path, where the platform-handler convention already lives; self-guarded against a double arm.
        /// Uses <c>PlatformManager</c> rather than the concrete PdxSdk platform because the manager
        /// re-raises the same event (<c>PlatformManager.cs:904-906</c>) and outlives any single PSI.
        /// </summary>
        public static void Arm()
        {
            if (s_Armed)
                return;
            s_Armed = true;
            PlatformManager.instance.onModSubscriptionChanged += OnSubscriptionChanged;
        }

        /// <summary>
        /// Detach the handler — the loader calls this from its teardown. <c>PlatformManager.instance</c>
        /// outlives our world, so a handler left attached would leak past an assembly unload. Safe to
        /// call unarmed and twice.
        /// </summary>
        public static void Disarm()
        {
            if (!s_Armed)
                return;
            s_Armed = false;
            PlatformManager.instance.onModSubscriptionChanged -= OnSubscriptionChanged;
        }

        // Raised from the platform's async chain on an unknown thread. Nothing here reads game state or
        // touches the logger: an exception escaping a multicast handler breaks every subscriber queued
        // after us — vanilla's own included — and logging from an unknown thread inside that multicast
        // is what the sibling probes deliberately avoid too (ActiveSetTransitionProbe).
#pragma warning disable CIVIC052 // False positive: the failure IS reported — it lands in the stamp
        // string that rides the deletion Error line, rather than being swallowed silently.
        private static void OnSubscriptionChanged(IModSupport support, PdxMod mod, ModSubscriptionStatus status)
        {
            try
            {
                string modId = ModInstallResolver.StableModId();
                if (modId.Length == 0 || !string.Equals(mod.id, modId, StringComparison.Ordinal))
                    return;

                s_LastOurEvent = $"{status}@{s_Clock.ElapsedMilliseconds}ms";
                if (status == ModSubscriptionStatus.ModUnsubscribed)
                    s_UnsubscribeSeen = true;
            }
            catch (Exception e)
            {
                s_LastOurEvent = $"probe-failed:{e.GetType().Name}";
            }
        }
#pragma warning restore CIVIC052

        /// <summary>
        /// Diagnostic-only entry point for the loader to report that arming was skipped, so a silent
        /// "cause=unknown" cannot be mistaken for "no unsubscribe happened" when the watch never ran.
        /// </summary>
        public static void ReportNotArmed(string reason) => Log.Info(
            $"[ParadoxNative] subscription watch not armed ({reason}) — deletion cause 'unsubscribed' " +
            "cannot be proven in this process.");
    }
}
