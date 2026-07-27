namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Global, save-independent persistence for the telemetry opt-in flag.
    ///
    /// Thin wrapper over <see cref="ConsentStore"/> bound to
    /// <see cref="ConsentKey.Telemetry"/> (file <c>telemetry_optin.txt</c>). The flag
    /// lives next to the native crash breadcrumb and is readable at mod init — before
    /// any city save is deserialized. This is what lets <c>TelemetryConfig.Load</c>
    /// resolve the real opt-in state during <c>TelemetryService.OnCreate</c>, which in
    /// turn lets <c>TelemetryCrashDetector.DetectPreviousCrash</c> consume a breadcrumb
    /// from a prior crash. The in-save <c>ModSettings.TelemetryEnabled</c> arrives too
    /// late (load-time) for that init-time decision.
    /// </summary>
    public static class TelemetryOptInStore
    {
        /// <inheritdoc cref="ConsentStore.Exists"/>
        public static bool Exists => ConsentStore.Exists(ConsentKey.Telemetry);

        /// <inheritdoc cref="ConsentStore.Read"/>
        public static bool Read() => ConsentStore.Read(ConsentKey.Telemetry);

        /// <inheritdoc cref="ConsentStore.Write"/>
        public static void Write(bool enabled) => ConsentStore.Write(ConsentKey.Telemetry, enabled);
    }
}
