namespace CivicSurvival
{
    public enum ModSettingsPatchKind
    {
        Unknown = 0,
        DifficultyPreset,
        ConstructionDelayEnabled,
        RandomDisastersEnabled,
        WinterMultiplierEnabled,
        WinterSeverity,
        NeighborEnvyEnabled,
        BackupPowerEnabled,
        ProtectCriticalInfraEnabled,
        AirAttacks,
        GridStressEnabled,
        ThresholdOperationEnabled,
        EquipmentWearEnabled,
        RealisticMode,
        MuteCivicAudio,
        MuteDroneAudio,
        MuteAlertAudio,
        MuteCombatAudio,
        DarkHumorChirper,
        UITheme,
        TelemetryEnabled,
        NetworkConnectionEnabled,
        LanguagePreference,
        SkipIntro,
        PlayerNickname
    }

    public readonly struct ModSettingsPatch
    {
        public readonly ModSettingsPatchKind Kind;
        internal readonly bool BoolValue;
        internal readonly int IntValue;
        internal readonly float FloatValue;
        internal readonly string StringValue;
        internal readonly DifficultyPreset DifficultyPresetValue;
        internal readonly AirAttackPreset AirAttackPresetValue;
        internal readonly ModLanguage LanguageValue;

        private ModSettingsPatch(
            ModSettingsPatchKind kind,
            bool boolValue = default,
            int intValue = default,
            float floatValue = default,
            string stringValue = "",
            DifficultyPreset difficultyPresetValue = default,
            AirAttackPreset airAttackPresetValue = default,
            ModLanguage languageValue = default)
        {
            Kind = kind;
            BoolValue = boolValue;
            IntValue = intValue;
            FloatValue = floatValue;
            StringValue = stringValue ?? string.Empty;
            DifficultyPresetValue = difficultyPresetValue;
            AirAttackPresetValue = airAttackPresetValue;
            LanguageValue = languageValue;
        }

        public static ModSettingsPatch SetDifficultyPreset(DifficultyPreset value) =>
            new(ModSettingsPatchKind.DifficultyPreset, difficultyPresetValue: value);

        public static ModSettingsPatch SetConstructionDelayEnabled(bool value) =>
            new(ModSettingsPatchKind.ConstructionDelayEnabled, boolValue: value);

        public static ModSettingsPatch SetRandomDisastersEnabled(bool value) =>
            new(ModSettingsPatchKind.RandomDisastersEnabled, boolValue: value);

        public static ModSettingsPatch SetWinterMultiplierEnabled(bool value) =>
            new(ModSettingsPatchKind.WinterMultiplierEnabled, boolValue: value);

        public static ModSettingsPatch SetWinterSeverity(float value) =>
            new(ModSettingsPatchKind.WinterSeverity, floatValue: value);

        public static ModSettingsPatch SetNeighborEnvyEnabled(bool value) =>
            new(ModSettingsPatchKind.NeighborEnvyEnabled, boolValue: value);

        public static ModSettingsPatch SetBackupPowerEnabled(bool value) =>
            new(ModSettingsPatchKind.BackupPowerEnabled, boolValue: value);

        public static ModSettingsPatch SetProtectCriticalInfraEnabled(bool value) =>
            new(ModSettingsPatchKind.ProtectCriticalInfraEnabled, boolValue: value);

        public static ModSettingsPatch SetAirAttacks(AirAttackPreset value) =>
            new(ModSettingsPatchKind.AirAttacks, airAttackPresetValue: value);

        public static ModSettingsPatch SetGridStressEnabled(bool value) =>
            new(ModSettingsPatchKind.GridStressEnabled, boolValue: value);

        public static ModSettingsPatch SetThresholdOperationEnabled(bool value) =>
            new(ModSettingsPatchKind.ThresholdOperationEnabled, boolValue: value);

        public static ModSettingsPatch SetEquipmentWearEnabled(bool value) =>
            new(ModSettingsPatchKind.EquipmentWearEnabled, boolValue: value);

        public static ModSettingsPatch SetRealisticMode(bool value) =>
            new(ModSettingsPatchKind.RealisticMode, boolValue: value);

        public static ModSettingsPatch SetMuteCivicAudio(bool value) =>
            new(ModSettingsPatchKind.MuteCivicAudio, boolValue: value);

        public static ModSettingsPatch SetMuteDroneAudio(bool value) =>
            new(ModSettingsPatchKind.MuteDroneAudio, boolValue: value);

        public static ModSettingsPatch SetMuteAlertAudio(bool value) =>
            new(ModSettingsPatchKind.MuteAlertAudio, boolValue: value);

        public static ModSettingsPatch SetMuteCombatAudio(bool value) =>
            new(ModSettingsPatchKind.MuteCombatAudio, boolValue: value);

        public static ModSettingsPatch SetDarkHumorChirper(bool value) =>
            new(ModSettingsPatchKind.DarkHumorChirper, boolValue: value);

        public static ModSettingsPatch SetUITheme(int value) =>
            new(ModSettingsPatchKind.UITheme, intValue: value);

        public static ModSettingsPatch SetTelemetryEnabled(bool value) =>
            new(ModSettingsPatchKind.TelemetryEnabled, boolValue: value);

        public static ModSettingsPatch SetNetworkConnectionEnabled(bool value) =>
            new(ModSettingsPatchKind.NetworkConnectionEnabled, boolValue: value);

        public static ModSettingsPatch SetLanguagePreference(ModLanguage value) =>
            new(ModSettingsPatchKind.LanguagePreference, languageValue: value);

        public static ModSettingsPatch SetSkipIntro(bool value) =>
            new(ModSettingsPatchKind.SkipIntro, boolValue: value);

        public static ModSettingsPatch SetPlayerNickname(string value) =>
            new(ModSettingsPatchKind.PlayerNickname, stringValue: value);
    }
}
