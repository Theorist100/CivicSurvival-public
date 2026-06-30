using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CivicSurvival.Core.Events;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Threats;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.UI.DomainState;
using CivicSurvival.Localization;
using CivicSurvival.Services.DevTools;
using CivicSurvival.Core.UI;
using static CivicSurvival.Core.UI.B;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Services.UI
{
    /// <summary>
    /// UI system for game settings.
    ///
    /// Migrated from SettingsUIPanel → CivicUIPanelSystem.
    /// Gains: proper ECS lifecycle. IsSettingsExpanded is now own field (was MainUISystem.IsSettingsExpanded).
    /// </summary>
    [ActIndependent]
    [HandlesRequestKind(RequestKind.LocaleChange)]
    public partial class SettingsUISystem : CivicUIPanelSystem
    {
        private ModSettings? m_Settings;
        [UiBindingDirtyCursor("UI localization dirty cursor for settings JSON binding.")]
        private int m_LocaleVersion = 1;
        private const float REPORT_STATUS_DURATION = 5f;
        private const int MIN_UI_THEME = 0;
        private const int MAX_UI_THEME = 2;

        // Cached localization JSON (expensive to rebuild — only on language change)
        private string m_CachedLocalizationJson = "{}";
        // Cached report status (updated from triggers)
        private string m_ReportStatusKey = "";
        private float m_ReportStatusSetAt = float.NegativeInfinity;
        private bool m_EventBusDropWarned;

        // Own field (was MainUISystem.IsSettingsExpanded)
        public bool IsSettingsExpanded { get; set; }

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();
            LocalizationManager.SubscribeToLocaleChange(OnLocaleChanged);

            Log.Info("Created");
        }

        protected override void ConfigureBindings()
        {
            // Binding must ALWAYS be registered — UI reads it every 500ms.
            // ServiceRegistry may not be ready yet; recovery happens in OnPanelUpdate.
            // Raw diagnostics opt-in is surfaced to the UI only via the SettingsDto JSON
            // (SettingsState → SettingsDto.TelemetryEnabled). Sentry / the crash reporter
            // read the EffectiveDiagnostics value-binding (Online && opt-in), the single
            // C#-side gate; there is no standalone raw-opt-in value-binding consumer.
            if (EnsureSettings())
            {
                Bindings.Add<bool>(EffectiveDiagnostics, EffectiveDiagnosticsValue(m_Settings));
            }
            else
            {
                Bindings.Add<bool>(EffectiveDiagnostics, false);
            }

            Bindings.Add<string>(SettingsState, "{}");
            Bindings.Add<string>(SettingsLocalizationState, "{}");

            if (m_Settings != null)
            {
                EmitSettingsState();
                EmitLocalizationState();
            }
        }

        protected override void ConfigureTriggers()
        {
            Triggers.Add(TogglePanel, FeatureIds.Settings, OnTogglePanel);
            Triggers.Add<int>(SetDifficultyPreset, FeatureIds.Settings, OnSetDifficultyPreset);
            Triggers.Add<bool>(SetConstructionDelay, FeatureIds.Settings, OnSetConstructionDelay);
            Triggers.Add<bool>(SetRandomDisasters, FeatureIds.Settings, OnSetRandomDisasters);
            Triggers.Add<bool>(SetWinterMultiplier, FeatureIds.Settings, OnSetWinterMultiplier);
            Triggers.Add<bool>(SetNeighborEnvy, FeatureIds.Settings, OnSetNeighborEnvy);
            Triggers.Add<bool>(SetBackupPower, FeatureIds.Settings, OnSetBackupPower);
            Triggers.Add<bool>(SetProtectCriticalInfra, FeatureIds.Settings, OnSetProtectCriticalInfra);
            Triggers.Add<int>(SetUITheme, FeatureIds.Settings, OnSetUITheme);
            Triggers.Add<int>(SetLanguage, FeatureIds.Settings, RequestResultBridge.Locale, OnSetLanguage);
            Triggers.Add<bool>(SetTelemetryEnabled, FeatureIds.Settings, OnSetTelemetryEnabled);
            Triggers.Add<bool>(SetMuteCivicAudio, FeatureIds.Settings, OnSetMuteCivicAudio);
            Triggers.Add<bool>(SetMuteDroneAudio, FeatureIds.Settings, OnSetMuteDroneAudio);
            Triggers.Add<bool>(SetMuteAlertAudio, FeatureIds.Settings, OnSetMuteAlertAudio);
            Triggers.Add<bool>(SetMuteCombatAudio, FeatureIds.Settings, OnSetMuteCombatAudio);
            Triggers.Add(SendReport, FeatureIds.Settings, OnSendReport);
            Triggers.Add(CopyReport, FeatureIds.Settings, OnCopyReport);
            Triggers.Add(SendModLog, FeatureIds.Settings, OnSendModLog);
            Triggers.Add<string>(SendCrashDumps, FeatureIds.Settings, OnSendCrashDumps);
            Triggers.Add(ClearErrors, FeatureIds.Settings, OnClearErrors);
            Triggers.Add<int>(OpenExternalLink, FeatureIds.Settings, OnOpenExternalLink);
        }

        // External links are whitelisted by id — the UI never passes a raw URL,
        // so a compromised/buggy frontend cannot open arbitrary addresses.
        private const int LINK_DISCORD = 0;
        private const int LINK_PRIVACY = 1;

        private void OnOpenExternalLink(int linkId)
        {
#pragma warning disable S1075 // Hardcoded URI by design — the whitelist must live in code, not config
            string? url = linkId switch
            {
                LINK_DISCORD => "https://discord.gg/yg4G2rVrd",
                LINK_PRIVACY => "https://github.com/Theorist100/CivicSurvival-public/blob/main/PRIVACY.md",
                _ => null
            };
#pragma warning restore S1075

            if (url == null)
            {
                Log.Warn($"Rejected unknown external link id: {linkId}");
                return;
            }

            UnityEngine.Application.OpenURL(url);
            Log.Info($"Opened external link: {url}");
        }

        // H-08 fix: shared recovery for OnPanelUpdate + trigger handlers
        [MemberNotNullWhen(true, nameof(m_Settings))]
        private bool EnsureSettings()
        {
            if (m_Settings != null) return true;
            if (!ServiceRegistry.IsInitialized) return false;
            m_Settings = ServiceRegistry.Instance.Require<ModSettings>();
            if (m_Settings == null) return false;
            RefreshLocalizationCache();
            EmitLocalizationState();
            Log.Info($"Late init recovered: localization {m_CachedLocalizationJson.Length} chars");
            return true;
        }

        protected override void OnPanelUpdate()
        {
            if (!EnsureSettings()) return;
            ClearExpiredReportStatus();
            EmitSettingsState();
        }

        private void EmitSettingsState()
        {
            var settings = m_Settings;
            if (settings == null) return;

            Bindings.Update(EffectiveDiagnostics, EffectiveDiagnosticsValue(settings));

            // Diagnostics is a sub-option under the Online master: it can only be toggled
            // while Online is on. Online off → lock the toggle and surface a "enable Online
            // first" reason. The eligibility predicate carries the online verdict + reason.
            bool online = settings.NetworkConnectionEnabled;
            bool canToggleTelemetry = SettingsEligibility.CanToggleTelemetry(
                online, out var telemetryLockedReasonId, ReasonIds.SettingsTelemetryNeedsOnline);

            var dto = new SettingsDto
            {
                DifficultyPreset = (int)settings.CurrentPreset,
                BasePreset = (int)settings.BasePreset,
                LegalImportMW = settings.LegalImportMW,
                LegalExportMW = settings.LegalExportMW,
                ConstructionDelay = settings.ConstructionDelayEnabled,
                RandomDisasters = settings.RandomDisastersEnabled,
                WinterMultiplier = settings.WinterMultiplierEnabled,
                NeighborEnvy = settings.NeighborEnvyEnabled,
                BackupPower = settings.BackupPowerEnabled,
                ProtectCriticalInfra = settings.ProtectCriticalInfraEnabled,
                IsExpanded = IsSettingsExpanded,
                UiTheme = settings.UITheme,
                TelemetryEnabled = settings.TelemetryEnabled,
                MuteCivicAudio = settings.MuteCivicAudio,
                MuteDroneAudio = settings.MuteDroneAudio,
                MuteAlertAudio = settings.MuteAlertAudio,
                MuteCombatAudio = settings.MuteCombatAudio,
                ErrorCount = ErrorReportService.GetErrorCount(),
                ReportStatus = "",
                ReportStatusKey = m_ReportStatusKey,
                LanguagePreference = (int)settings.LanguagePreference,
                IsUncensored = LocalizationManager.IsUncensoredBuild,
                AvailableLocalesJson = LocalizationManager.AvailableLanguageIdsJson,
                AvailableThemesJson = "[0,1,2]",
                // Crash-dump list is a directory stat; only scan while the panel is open so a
                // closed settings panel never pays for it on the 500ms tick.
                CrashDumpsJson = IsSettingsExpanded ? ErrorReportService.GetCrashDumpListJson() : "[]",
                CanToggleTelemetry = canToggleTelemetry,
                TelemetryLockedReasonId = telemetryLockedReasonId,
                LocaleRequestJson = RequestResultBridge.Get(RequestResultBridge.Locale).ToJson()
            };

            PublishWhenComplete(SettingsState, NoSourceChecks, () => dto);
        }

        // Effective diagnostics = Online master AND the diagnostics opt-in. Single C#-side
        // source the UI crash reporter (Sentry) reads, so it never recombines two flags and
        // can never disagree with the server gate (TelemetryConfig.Enabled). Computed from
        // settings (no file I/O on the 500ms panel tick): the opt-in is seeded from
        // ConsentStore at boot and patched on toggle, the master is NetworkConnectionEnabled.
        private static bool EffectiveDiagnosticsValue(ModSettings settings)
            => settings.TelemetryEnabled && settings.NetworkConnectionEnabled;

        private void EmitLocalizationState()
        {
            var dto = new SettingsLocalizationDto
            {
                CurrentLocale = LocalizationManager.CurrentLocale,
                LocalizationStrings = m_CachedLocalizationJson,
                LocaleVersion = m_LocaleVersion
            };

            PublishWhenComplete(SettingsLocalizationState, NoSourceChecks, () => dto);
        }

        private void OnTogglePanel()
        {
            IsSettingsExpanded = !IsSettingsExpanded;
            Log.Info($"Panel toggled: {IsSettingsExpanded}");
        }

        private void RecordSetting(string name, string oldVal, string newVal)
        {
            var eventBus = EventBus;
            if (eventBus == null)
            {
                if (!m_EventBusDropWarned)
                {
                    Log.Warn("EventBus unavailable; SettingChangedEvent dropped");
                    m_EventBusDropWarned = true;
                }
                return;
            }

            eventBus.SafePublish(new SettingChangedEvent(name, oldVal, newVal));
        }

        private void OnSetDifficultyPreset(int presetId)
        {
            if (!EnsureSettings()) return;
            if (presetId < (int)CivicSurvival.DifficultyPreset.ManagedDeficit ||
                presetId > (int)CivicSurvival.DifficultyPreset.Custom)
            {
                Log.Warn($"Rejected invalid difficulty preset id: {presetId}");
                return;
            }

            string oldPreset = m_Settings.CurrentPreset.ToString();
            var preset = (CivicSurvival.DifficultyPreset)presetId;
            m_Settings.ApplyPatch(ModSettingsPatch.SetDifficultyPreset(preset));
            RecordSetting("difficulty_preset", oldPreset, preset.ToString());

            string presetName = preset switch
            {
                CivicSurvival.DifficultyPreset.ManagedDeficit => "Managed Deficit",
                CivicSurvival.DifficultyPreset.BlackoutProtocol => "Blackout Protocol",
                CivicSurvival.DifficultyPreset.IslandMode => "Island Mode",
                CivicSurvival.DifficultyPreset.Custom => "Custom",
                _ => "Unknown"
            };
            Log.Info($"DifficultyPreset set to: {presetName} (LegalImport: {m_Settings.LegalImportMW} MW, ShadowPrice: ${m_Settings.ShadowImportPrice}/MW)");
        }

        private void OnSetConstructionDelay(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("construction_delay", m_Settings.ConstructionDelayEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetConstructionDelayEnabled(enabled));
            Log.Info($"ConstructionDelay set to: {enabled}");
        }

        private void OnSetRandomDisasters(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("random_disasters", m_Settings.RandomDisastersEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetRandomDisastersEnabled(enabled));
            Log.Info($"RandomDisasters set to: {enabled}");
        }

        private void OnSetWinterMultiplier(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("winter_multiplier", m_Settings.WinterMultiplierEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetWinterMultiplierEnabled(enabled));
            Log.Info($"WinterMultiplier set to: {enabled}");
        }

        private void OnSetNeighborEnvy(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("neighbor_envy", m_Settings.NeighborEnvyEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetNeighborEnvyEnabled(enabled));
            Log.Info($"NeighborEnvy set to: {enabled}");
        }

        private void OnSetBackupPower(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("backup_power", m_Settings.BackupPowerEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetBackupPowerEnabled(enabled));
            Log.Info($"BackupPower set to: {enabled}");
        }

        private void OnSetProtectCriticalInfra(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("protect_critical_infra", m_Settings.ProtectCriticalInfraEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetProtectCriticalInfraEnabled(enabled));
            Log.Info($"ProtectCriticalInfra set to: {enabled}");
        }

        private void OnSetUITheme(int themeId)
        {
            if (!EnsureSettings()) return;
            if (themeId < MIN_UI_THEME || themeId > MAX_UI_THEME)
            {
                Log.Warn($"Rejected invalid UI theme id: {themeId}");
                return;
            }

            RecordSetting("ui_theme", m_Settings.UITheme.ToString(), themeId.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetUITheme(themeId));
            string themeName = themeId switch
            {
                0 => "Tech Noir",
                1 => "Classic Gold",
                2 => "Soft Focus",
                _ => "Unknown"
            };
            Log.Info($"UITheme set to: {themeName} ({themeId})");
        }

        private TriggerOutcome OnSetLanguage(int languageId)
        {
            if (!EnsureSettings()) return TriggerOutcome.Reject(ReasonIds.SettingsLocaleNotAvailable);
            if (languageId < (int)ModLanguage.GameDefault || languageId > (int)ModLanguage.Chinese)
            {
                Log.Warn($"Rejected invalid language id: {languageId}");
                return TriggerOutcome.Reject(ReasonIds.SettingsLocaleNotAvailable);
            }

            Log.Info($"[L10n] OnSetLanguage triggered with languageId={languageId}");

            var language = (ModLanguage)languageId;
            if (!LocalizationManager.IsLanguageAvailable(language))
            {
                Log.Warn($"Rejected unavailable language: {language}");
                return TriggerOutcome.Reject(ReasonIds.SettingsLocaleNotAvailable);
            }

            var oldLanguage = m_Settings.LanguagePreference;

            var localeBefore = LocalizationManager.CurrentLocale;
            LocalizationManager.SetLanguage(language);
            var localeAfter = LocalizationManager.CurrentLocale;
            m_Settings.ApplyPatch(ModSettingsPatch.SetLanguagePreference(language));
            RecordSetting("language", ((int)oldLanguage).ToString(), languageId.ToString());

            Log.Info($"[L10n] Locale change: {localeBefore} -> {localeAfter}");

            Log.Info($"[L10n] Updated: currentLocale={localeAfter}, jsonLength={m_CachedLocalizationJson.Length}, version={m_LocaleVersion}");

            string langName = language switch
            {
                ModLanguage.GameDefault => "Game Default",
                ModLanguage.English => "English",
                ModLanguage.Ukrainian => "Ukrainian",
                ModLanguage.German => "German",
                ModLanguage.Spanish => "Spanish",
                ModLanguage.French => "French",
                ModLanguage.Polish => "Polish",
                ModLanguage.Chinese => "Chinese",
                _ => "Unknown"
            };
            Log.Info($"[L10n] Language set to: {langName} (locale: {localeAfter})");
            return TriggerOutcome.SyncSuccess(((int)language).ToString());
        }

        private void OnSetTelemetryEnabled(bool enabled)
        {
            if (!EnsureSettings()) return;

            RecordSetting("telemetry_enabled", m_Settings.TelemetryEnabled.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetTelemetryEnabled(enabled));
            // Persist globally (save-independent) so the next mod init reads the real
            // opt-in before the save loads — required for crash-breadcrumb recovery.
            Core.Services.TelemetryOptInStore.Write(enabled);
            Services.Telemetry.TelemetryService.Instance?.SetEnabled(enabled);

            Log.Info($"Telemetry set to: {enabled}");
        }

        private void OnSetMuteCivicAudio(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("mute_civic_audio", m_Settings.MuteCivicAudio.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetMuteCivicAudio(enabled));
            ApplyAudioMuteState();
            Log.Info($"MuteCivicAudio set to: {enabled}");
        }

        private void OnSetMuteDroneAudio(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("mute_drone_audio", m_Settings.MuteDroneAudio.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetMuteDroneAudio(enabled));
            ApplyAudioMuteState();
            Log.Info($"MuteDroneAudio set to: {enabled}");
        }

        private void OnSetMuteAlertAudio(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("mute_alert_audio", m_Settings.MuteAlertAudio.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetMuteAlertAudio(enabled));
            ApplyAudioMuteState();
            Log.Info($"MuteAlertAudio set to: {enabled}");
        }

        private void OnSetMuteCombatAudio(bool enabled)
        {
            if (!EnsureSettings()) return;
            RecordSetting("mute_combat_audio", m_Settings.MuteCombatAudio.ToString(), enabled.ToString());
            m_Settings.ApplyPatch(ModSettingsPatch.SetMuteCombatAudio(enabled));
            ApplyAudioMuteState();
            Log.Info($"MuteCombatAudio set to: {enabled}");
        }

        // Sync UI-thread call so a mute toggle silences playing loops at once — pause-safe
        // (Axiom 14): does not route through the throttled GameSimulation orchestrator update.
        // Resolves to a no-op null-object when the ThreatUI audio feature is inactive.
        private void ApplyAudioMuteState()
        {
            // Boot path: TryGetOrNullObject throws before registration completes (it cannot
            // tell "feature unavailable" from a registration race). A mute toggle that arrives
            // that early has nothing to silence yet — the orchestrator re-reads the persisted
            // mute state on OnStartRunning — so treat it as a no-op instead of throwing.
            if (!FeatureRegistry.IsInitialized || !FeatureRegistry.Instance.IsRegistrationComplete)
                return;

            var audio = ServiceRegistryFeatureExtensions.TryGetOrNullObject(NullThreatAudioService.Instance);
            audio.ApplyAudioMuteState();
        }

        private void OnSendReport()
        {
            if (!EnsureSettings()) return;
            if (!m_Settings.TelemetryEnabled)
            {
                SetReportStatus(ReasonIds.SettingsStatusTelemetryDisabled);
                Log.Warn("Cannot send report: telemetry is disabled");
                return;
            }

            SetReportStatus(ReasonIds.SettingsStatusSending);

            var result = ErrorReportService.SubmitManualReport();
            string statusKey;
            switch (result)
            {
                case ErrorReportService.ManualReportResult.Sent:
                    statusKey = ReasonIds.SettingsStatusReportSent;
                    break;
                case ErrorReportService.ManualReportResult.TelemetryDisabled:
                    statusKey = ReasonIds.SettingsStatusTelemetryDisabled;
                    break;
                case ErrorReportService.ManualReportResult.TelemetryUnavailable:
                    statusKey = ReasonIds.SettingsStatusReportUnavailable;
                    break;
                case ErrorReportService.ManualReportResult.Failed:
                case ErrorReportService.ManualReportResult.Unknown:
                    statusKey = ReasonIds.SettingsStatusReportFailed;
                    break;
                default:
                    Log.Warn($"Unknown manual report result: {result}");
                    statusKey = ReasonIds.SettingsStatusReportFailed;
                    break;
            }

            SetReportStatus(statusKey);
        }

        private void OnSendModLog()
        {
            if (!EnsureSettings()) return;
            if (!m_Settings.TelemetryEnabled)
            {
                SetReportStatus(ReasonIds.SettingsStatusTelemetryDisabled);
                Log.Warn("Cannot send mod list: telemetry is disabled");
                return;
            }

            SetReportStatus(ReasonIds.SettingsStatusSending);

            var result = ErrorReportService.SubmitModList();
            string statusKey;
            switch (result)
            {
                case ErrorReportService.ManualReportResult.Sent:
                    statusKey = ReasonIds.SettingsStatusReportSent;
                    break;
                case ErrorReportService.ManualReportResult.TelemetryDisabled:
                    statusKey = ReasonIds.SettingsStatusTelemetryDisabled;
                    break;
                case ErrorReportService.ManualReportResult.TelemetryUnavailable:
                    statusKey = ReasonIds.SettingsStatusReportUnavailable;
                    break;
                case ErrorReportService.ManualReportResult.Failed:
                case ErrorReportService.ManualReportResult.Unknown:
                    statusKey = ReasonIds.SettingsStatusReportFailed;
                    break;
                default:
                    Log.Warn($"Unknown mod list result: {result}");
                    statusKey = ReasonIds.SettingsStatusReportFailed;
                    break;
            }

            SetReportStatus(statusKey);
        }

        // The UI passes a comma-separated list of selected dump file names (GUID-named .dmp,
        // never contains a comma). An empty payload falls back to the freshest dump.
        private void OnSendCrashDumps(string selectedCsv)
        {
            if (!EnsureSettings()) return;
            if (!m_Settings.TelemetryEnabled)
            {
                SetReportStatus(ReasonIds.SettingsStatusTelemetryDisabled);
                Log.Warn("Cannot send crash dump: telemetry is disabled");
                return;
            }

            SetReportStatus(ReasonIds.SettingsStatusSending);

            var names = ParseDumpNames(selectedCsv);
            var result = ErrorReportService.SubmitCrashDumps(names);
            string statusKey;
            switch (result)
            {
                case ErrorReportService.ManualReportResult.Sent:
                    statusKey = ReasonIds.SettingsStatusReportSent;
                    break;
                case ErrorReportService.ManualReportResult.NoDump:
                    statusKey = ReasonIds.SettingsStatusDumpNone;
                    break;
                case ErrorReportService.ManualReportResult.TelemetryDisabled:
                    statusKey = ReasonIds.SettingsStatusTelemetryDisabled;
                    break;
                case ErrorReportService.ManualReportResult.TelemetryUnavailable:
                    statusKey = ReasonIds.SettingsStatusReportUnavailable;
                    break;
                case ErrorReportService.ManualReportResult.Failed:
                case ErrorReportService.ManualReportResult.Unknown:
                    statusKey = ReasonIds.SettingsStatusReportFailed;
                    break;
                default:
                    Log.Warn($"Unknown crash dump result: {result}");
                    statusKey = ReasonIds.SettingsStatusReportFailed;
                    break;
            }

            SetReportStatus(statusKey);
        }

        private static System.Collections.Generic.List<string> ParseDumpNames(string selectedCsv)
        {
            var names = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(selectedCsv)) return names;

            foreach (var raw in selectedCsv.Split(','))
            {
                var name = raw.Trim();
                if (name.Length > 0)
                    names.Add(name);
            }
            return names;
        }

        private void OnCopyReport()
        {
            bool copied = ErrorReportService.CopyToClipboard();
            SetReportStatus(copied ? ReasonIds.SettingsStatusCopied : ReasonIds.SettingsStatusReportSaved);
        }

        private void OnClearErrors()
        {
            ErrorReportService.ClearErrors();
            SetReportStatus(ReasonIds.SettingsStatusErrorsCleared);
            Log.Info("Errors cleared by user");
        }

        private void SetReportStatus(string key)
        {
            m_ReportStatusKey = key;
            m_ReportStatusSetAt = UnityEngine.Time.realtimeSinceStartup;
        }

        private void ClearExpiredReportStatus()
        {
            if (m_ReportStatusKey.Length == 0) return;
            if (UnityEngine.Time.realtimeSinceStartup - m_ReportStatusSetAt <= REPORT_STATUS_DURATION) return;
            m_ReportStatusKey = "";
            m_ReportStatusSetAt = float.NegativeInfinity;
        }

        private void OnLocaleChanged()
        {
            RefreshLocalizationCache();
            EmitLocalizationState();
        }

        private void RefreshLocalizationCache()
        {
            string nextJson = LocalizationManager.GetAllStringsAsJson();
            if (nextJson == m_CachedLocalizationJson) return;

            m_CachedLocalizationJson = nextJson;
            m_LocaleVersion++;
        }

        protected override void OnDestroy()
        {
            LocalizationManager.UnsubscribeFromLocaleChange(OnLocaleChanged);
            base.OnDestroy();
        }
    }
}
