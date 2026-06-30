using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;

namespace CivicSurvival
{
    /// <summary>
    /// Mod settings with difficulty preset support.
    /// </summary>
    [InfrastructureService]
    public class ModSettings
    {
        // Current difficulty preset
        private DifficultyPreset m_CurrentPreset = DifficultyPreset.BlackoutProtocol;
        private DifficultyPreset m_BasePreset = DifficultyPreset.BlackoutProtocol;

        // Current difficulty settings (can be modified from preset)
        private DifficultySettings m_Difficulty;

        public ModSettings()
        {
            // Default to recommended preset (Blackout Protocol)
            m_Difficulty = DifficultyPresets.BlackoutProtocol.Clone();
        }

        /// <summary>
        /// Current difficulty preset.
        /// Returns Custom if player has modified settings.
        /// </summary>
        public DifficultyPreset CurrentPreset => m_CurrentPreset;

        /// <summary>
        /// Base preset that Custom was derived from.
        /// Used for "Custom (based on Energy Crisis)" display.
        /// </summary>
        public DifficultyPreset BasePreset => m_BasePreset;

        /// <summary>
        /// Apply a difficulty preset.
        /// </summary>
        public void ApplyPatch(ModSettingsPatch patch)
        {
            switch (patch.Kind)
            {
                case ModSettingsPatchKind.DifficultyPreset:
                    ApplyPresetCore(patch.DifficultyPresetValue);
                    break;
                case ModSettingsPatchKind.ConstructionDelayEnabled:
                    ConstructionDelayEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.RandomDisastersEnabled:
                    RandomDisastersEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.WinterMultiplierEnabled:
                    WinterMultiplierEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.WinterSeverity:
                    WinterSeverity = patch.FloatValue;
                    break;
                case ModSettingsPatchKind.NeighborEnvyEnabled:
                    NeighborEnvyEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.BackupPowerEnabled:
                    BackupPowerEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.ProtectCriticalInfraEnabled:
                    ProtectCriticalInfraEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.AirAttacks:
                    AirAttacks = patch.AirAttackPresetValue;
                    break;
                case ModSettingsPatchKind.GridStressEnabled:
                    GridStressEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.ThresholdOperationEnabled:
                    ThresholdOperationEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.EquipmentWearEnabled:
                    EquipmentWearEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.RealisticMode:
                    RealisticMode = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.MuteCivicAudio:
                    MuteCivicAudio = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.MuteDroneAudio:
                    MuteDroneAudio = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.MuteAlertAudio:
                    MuteAlertAudio = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.MuteCombatAudio:
                    MuteCombatAudio = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.DarkHumorChirper:
                    DarkHumorChirper = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.UITheme:
                    UITheme = patch.IntValue;
                    break;
                case ModSettingsPatchKind.TelemetryEnabled:
                    TelemetryEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.NetworkConnectionEnabled:
                    NetworkConnectionEnabled = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.LanguagePreference:
                    LanguagePreference = patch.LanguageValue;
                    break;
                case ModSettingsPatchKind.SkipIntro:
                    SkipIntro = patch.BoolValue;
                    break;
                case ModSettingsPatchKind.PlayerNickname:
                    PlayerNickname = patch.StringValue;
                    break;
                case ModSettingsPatchKind.Unknown:
                default:
                    throw new System.InvalidOperationException($"Unsupported ModSettingsPatch kind: {patch.Kind}");
            }
        }

        private void ApplyPresetCore(DifficultyPreset preset)
        {
            m_CurrentPreset = preset;
            m_BasePreset = preset == DifficultyPreset.Custom ? m_BasePreset : preset;
            m_Difficulty = DifficultyPresets.GetPreset(preset).Clone();

            Mod.Log.Info($"Applied preset: {preset}");
            LogCurrentSettings();
        }

        public void ResetToDefaults()
        {
            m_CurrentPreset = DifficultyPreset.BlackoutProtocol;
            m_BasePreset = DifficultyPreset.BlackoutProtocol;
            m_Difficulty = DifficultyPresets.BlackoutProtocol.Clone();

            RealisticMode = false;
            MuteCivicAudio = false;
            MuteDroneAudio = false;
            MuteAlertAudio = false;
            MuteCombatAudio = false;
            DarkHumorChirper = true;
            UITheme = 0;
            TelemetryEnabled = false;
            NetworkConnectionEnabled = false;
            LanguagePreference = ModLanguage.GameDefault;
            SkipIntro = false;
            PlayerNickname = "";
        }

        /// <summary>
        /// Called when any individual setting is changed.
        /// Detects if we're now in Custom mode.
        /// </summary>
        private void OnSettingChanged()
        {
            var detected = DifficultyPresets.DetectPreset(m_Difficulty);
            if (detected == DifficultyPreset.Custom && m_CurrentPreset != DifficultyPreset.Custom)
            {
                // Player modified a preset - switch to Custom
                m_BasePreset = m_CurrentPreset;
                m_CurrentPreset = DifficultyPreset.Custom;
                Mod.Log.Info($"Settings modified - now Custom (based on {m_BasePreset})");
            }
            else if (detected != DifficultyPreset.Custom)
            {
                // Settings now match a preset exactly
                m_CurrentPreset = detected;
                m_BasePreset = detected;
            }
        }

        private void LogCurrentSettings()
        {
            Mod.Log.Info($"  LegalImportMW: {m_Difficulty.LegalImportMW}");
            Mod.Log.Info($"  LegalExportMW: {m_Difficulty.LegalExportMW}");
            Mod.Log.Info($"  ShadowImportPrice: ${m_Difficulty.ShadowImportPrice}/MW");
            Mod.Log.Info($"  ConstructionDelay: {m_Difficulty.ConstructionDelay}");
            Mod.Log.Info($"  RandomDisasters: {m_Difficulty.RandomDisasters}");
            Mod.Log.Info($"  WinterMultiplier: {m_Difficulty.WinterMultiplier} (severity: {m_Difficulty.WinterSeverity:F2})");
            Mod.Log.Info($"  NeighborEnvy: {m_Difficulty.NeighborEnvy}");
            Mod.Log.Info($"  GridStress: {m_Difficulty.GridStress}");
            Mod.Log.Info($"  ThresholdOperation: {m_Difficulty.ThresholdOperation}");
            Mod.Log.Info($"  EquipmentWear: {m_Difficulty.EquipmentWear}");
            Mod.Log.Info($"  BackupPower: {m_Difficulty.BackupPower}");
            Mod.Log.Info($"  ProtectCriticalInfra: {m_Difficulty.ProtectCriticalInfra}");
            Mod.Log.Info($"  AirAttacks: {m_Difficulty.AirAttacks}");
        }

        // === Accessors for difficulty settings ===

        [PersistedSetting("constructionDelay")]
        public bool ConstructionDelayEnabled
        {
            get => m_Difficulty.ConstructionDelay;
            private set { m_Difficulty.ConstructionDelay = value; OnSettingChanged(); }
        }

        [PersistedSetting("randomDisasters")]
        public bool RandomDisastersEnabled
        {
            get => m_Difficulty.RandomDisasters;
            private set { m_Difficulty.RandomDisasters = value; OnSettingChanged(); }
        }

        [PersistedSetting("winterMultiplier")]
        public bool WinterMultiplierEnabled
        {
            get => m_Difficulty.WinterMultiplier;
            private set { m_Difficulty.WinterMultiplier = value; OnSettingChanged(); }
        }

        // NOTE: CascadeEffect REMOVED - vanilla handles via EfficiencyFactor.ElectricitySupply

        [PersistedSetting("winterSeverity", Min = 0, Max = 2, Default = 1)]
        public float WinterSeverity
        {
            get => m_Difficulty.WinterSeverity;
            private set { m_Difficulty.WinterSeverity = value; OnSettingChanged(); }
        }

        [PersistedSetting("neighborEnvy")]
        public bool NeighborEnvyEnabled
        {
            get => m_Difficulty.NeighborEnvy;
            private set { m_Difficulty.NeighborEnvy = value; OnSettingChanged(); }
        }

        [PersistedSetting("backupPower")]
        public bool BackupPowerEnabled
        {
            get => m_Difficulty.BackupPower;
            private set { m_Difficulty.BackupPower = value; OnSettingChanged(); }
        }

        [PersistedSetting("protectCritical")]
        public bool ProtectCriticalInfraEnabled
        {
            get => m_Difficulty.ProtectCriticalInfra;
            private set { m_Difficulty.ProtectCriticalInfra = value; OnSettingChanged(); }
        }

        [PersistedSetting("airAttacks")]
        public AirAttackPreset AirAttacks
        {
            get => m_Difficulty.AirAttacks;
            private set { m_Difficulty.AirAttacks = value; OnSettingChanged(); }
        }

        [PersistedSetting("gridStress")]
        public bool GridStressEnabled
        {
            get => m_Difficulty.GridStress;
            private set { m_Difficulty.GridStress = value; OnSettingChanged(); }
        }

        [PersistedSetting("thresholdOp")]
        public bool ThresholdOperationEnabled
        {
            get => m_Difficulty.ThresholdOperation;
            private set { m_Difficulty.ThresholdOperation = value; OnSettingChanged(); }
        }

        [PersistedSetting("equipWear")]
        public bool EquipmentWearEnabled
        {
            get => m_Difficulty.EquipmentWear;
            private set { m_Difficulty.EquipmentWear = value; OnSettingChanged(); }
        }

        // === Non-difficulty settings ===

        /// <summary>
        /// Enable realistic mode (Ukrainian context).
        /// </summary>
        [PersistedSetting("realisticMode")]
        public bool RealisticMode { get; private set; } = false;

        /// <summary>
        /// Master mute for all CivicSurvival-emitted sounds. When true it overrides the
        /// per-category toggles below. Vanilla and other mods' audio are unaffected.
        /// </summary>
        [PersistedSetting("muteCivicAudio")]
        public bool MuteCivicAudio { get; private set; } = false;

        /// <summary>Mute the continuous drone buzz.</summary>
        [PersistedSetting("muteDroneAudio")]
        public bool MuteDroneAudio { get; private set; } = false;

        /// <summary>Mute air-raid sirens and pre-war ominous-sign atmosphere SFX.</summary>
        [PersistedSetting("muteAlertAudio")]
        public bool MuteAlertAudio { get; private set; } = false;

        /// <summary>Mute AA fire / intercepts and explosions (incl. collapse/lightning SFX).</summary>
        [PersistedSetting("muteCombatAudio")]
        public bool MuteCombatAudio { get; private set; } = false;

        /// <summary>
        /// Single source of truth for the "master OR category" mute rule. Source systems
        /// classify each sound and call this before playing — no duplicated || by call site.
        /// </summary>
        public bool IsAudioMuted(AudioCategory category)
        {
            if (MuteCivicAudio)
                return true;

            return category switch
            {
                // None (sentinel = default(AudioCategory)) is not a real channel — never muted.
                AudioCategory.None => false,
                AudioCategory.Drone => MuteDroneAudio,
                AudioCategory.Alert => MuteAlertAudio,
                AudioCategory.Combat => MuteCombatAudio,
                _ => throw new System.ArgumentOutOfRangeException(
                    nameof(category), category, "Unknown AudioCategory"),
            };
        }

        /// <summary>
        /// Enable dark humor Chirper messages.
        /// </summary>
        [PersistedSetting("darkHumor")]
        public bool DarkHumorChirper { get; private set; } = true;

        /// <summary>
        /// UI Theme selection.
        /// 0 = Tech Noir (dark + colored accents)
        /// 1 = Classic Gold (gold-on-dark)
        /// </summary>
        [PersistedSetting("uiTheme")]
        public int UITheme { get; private set; } = 0;

        /// <summary>
        /// Enable anonymous telemetry collection.
        /// Sends gameplay metrics to help improve balance and fix bugs.
        /// No personal data collected. Opt-in by default.
        /// </summary>
        [PersistedSetting("telemetryEnabled")]
        public bool TelemetryEnabled { get; private set; } = false;

        /// <summary>
        /// Enable global news and online stats polling.
        /// </summary>
        [PersistedSetting("networkConnectionEnabled")]
        public bool NetworkConnectionEnabled { get; private set; } = false;

        /// <summary>
        /// Language preference for mod UI and messages.
        /// GameDefault follows game locale, others force specific language.
        /// </summary>
        [PersistedSetting("languagePreference")]
        public ModLanguage LanguagePreference { get; private set; } = ModLanguage.GameDefault;

        /// <summary>
        /// Skip intro sequence on game load.
        /// Useful for development and returning players.
        /// </summary>
        [PersistedSetting("skipIntro")]
        public bool SkipIntro { get; private set; } = false;

        /// <summary>
        /// Player nickname for leaderboards and global news.
        /// 3-20 characters, alphanumeric and underscore only.
        /// </summary>
        [PersistedSetting("playerNickname")]
        public string PlayerNickname { get; private set; } = "";

        /// <summary>
        /// Legal import limit in MW for current preset.
        /// This is the artificial limit our mod enforces.
        /// Shadow Import is the way to bypass this limit.
        /// </summary>
        public int LegalImportMW => m_Difficulty.LegalImportMW;

        /// <summary>
        /// Legal export limit in MW for current preset (interconnector capacity).
        /// </summary>
        public int LegalExportMW => m_Difficulty.LegalExportMW;

        /// <summary>
        /// Shadow Import price per MW per day for current preset.
        /// Scales with difficulty: $300 (easy) → $600 (normal) → $1200 (hardcore).
        /// </summary>
        public float ShadowImportPrice => m_Difficulty.ShadowImportPrice;

        /// <summary>
        /// FIX M-101: Restore Custom preset's numeric fields after load.
        /// Called from ModSettingsSerializationSystem.Deserialize after ApplyPreset(Custom)
        /// to overwrite the BlackoutProtocol clone with the player's saved values.
        /// Does not call OnSettingChanged — restoring a saved state, not creating a new one.
        /// </summary>
        internal void ApplyCustomNumericFields(int legalImportMW, int legalExportMW, float shadowImportPrice, DifficultyPreset basePreset)
        {
            m_Difficulty.LegalImportMW = legalImportMW;
            m_Difficulty.LegalExportMW = legalExportMW;
            m_Difficulty.ShadowImportPrice = shadowImportPrice;
            m_BasePreset = basePreset;
            Mod.Log.Info($"Custom preset restored: LegalImportMW={legalImportMW}, LegalExportMW={legalExportMW}, ShadowImportPrice={shadowImportPrice}, BasePreset={basePreset}");
        }
    }

    /// <summary>
    /// Language preference for mod UI and messages.
    /// </summary>
    public enum ModLanguage
    {
        GameDefault = 0,  // Follow game locale
        English = 1,      // Force English
        Ukrainian = 2,    // Force Ukrainian
        German = 3,       // Force German
        Spanish = 4,      // Force Spanish
        French = 5,       // Force French
        Polish = 6,       // Force Polish
        Chinese = 7       // Force Chinese (Simplified)
    }
}
