using CivicSurvival.Core.UI;

namespace CivicSurvival.Core.UI.DomainState
{
    /// <summary>
    /// Settings domain DTO.
    /// Most fields change only on user triggers (preset/toggle changes).
    /// OnPanelUpdate only refreshes ErrorCount.
    /// Localization strings are emitted through SettingsLocalizationDto.
    /// </summary>
    public partial struct SettingsDto : IDomainDto
    {
        public int DifficultyPreset;
        public int BasePreset;
        public int LegalImportMW;
        public int LegalExportMW;
        public bool ConstructionDelay;
        public bool RandomDisasters;
        public bool WinterMultiplier;
        public bool NeighborEnvy;
        public bool BackupPower;
        public bool ProtectCriticalInfra;
        public bool IsExpanded;
        public int UiTheme;
        public bool TelemetryEnabled;
        public bool MuteCivicAudio;
        public bool MuteDroneAudio;
        public bool MuteAlertAudio;
        public bool MuteCombatAudio;
        public int ErrorCount;
        public string ReportStatus;
        public string ReportStatusKey;
        public int LanguagePreference;
        public bool IsUncensored;
        public string AvailableLocalesJson;
        public string AvailableThemesJson;
        // Newest-first list of available native crash dumps (.dmp) for the bug-report tab,
        // serialized from CrashDumpEntry. Populated only while the settings panel is open.
        public string CrashDumpsJson;
        [Attributes.DtoEligibility(typeof(SettingsEligibility), nameof(SettingsEligibility.CanToggleTelemetry), "TelemetryLockedReasonId")]
        public bool CanToggleTelemetry;
        public string LocaleRequestJson;

        partial void WriteEligibility(DomainJsonHelper.JsonWriter w);
    }
}
