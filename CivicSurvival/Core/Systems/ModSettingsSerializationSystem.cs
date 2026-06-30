using Colossal.Serialization.Entities;
using CivicSurvival.Core.Infrastructure;
using CivicSurvival.Core.Interfaces.Services;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems.Base;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using CivicSurvival.Localization;
using Game.Simulation;
using Unity.Entities;
using CivicSurvival.Core.Attributes;

namespace CivicSurvival.Core.Systems
{
    /// <summary>
    /// Dedicated serialization for ModSettings.
    /// Previously embedded in BlackoutSystem.Serialization — extracted for single ownership.
    /// BlackoutSystem still reads old format for backward compatibility with pre-extraction saves.
    /// </summary>
    [ActIndependent]
    [ServiceOnlySystem("Serialization host only; Serialize/Deserialize called by Unity outside the update loop, OnUpdateImpl is empty.")]
    public partial class ModSettingsSerializationSystem : CivicSystemBase, IDefaultSerializable, IResettable, IBootDefaultsReset
    {
        private static readonly LogContext Log = new("ModSettingsSer");
        private ModSettings? m_Settings;

        protected override bool RequiresLoadedGame => false;

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false; // No per-frame work — serialization only
            Log.Info("Created");
        }

        protected override void OnUpdateImpl() { }

        public void ResetState()
        {
            var settings = ResolveSettings();
            if (settings == null)
            {
                Log.Error("ResetState skipped: ModSettings not registered");
                return;
            }

            ApplyBootDefaults(settings);
        }

        public void SetDefaults(Context context) => ResetState();

        public void ResetToBootDefaults(ResetReason reason)
        {
            // CIVIC462: field-only failure boundary. Apply defaults to the
            // already-resolved instance; do NOT resolve through ServiceRegistry
            // on the load-failure path. If settings were never resolved, nothing
            // was touched — their natural initial state is already boot-default.
            if (m_Settings == null)
                return;

            ApplyBootDefaults(m_Settings);
        }

        private static void ApplyBootDefaults(ModSettings settings)
        {
            settings.ResetToDefaults();

            // Telemetry opt-in is global (save-independent), not part of preset defaults.
            // A new game / SetDefaults must NOT reset it to the preset default — restore
            // the persisted consent so the Options toggle and the intro modal reflect the
            // real state (ResetToDefaults above zeroed it).
            settings.ApplyPatch(ModSettingsPatch.SetTelemetryEnabled(Core.Services.TelemetryOptInStore.Read()));

            // Online connection (Global Grid) is likewise a global preference, not a
            // per-city default. Restore it from the global store so a new game / reset
            // keeps the player's chosen connection state.
            settings.ApplyPatch(ModSettingsPatch.SetNetworkConnectionEnabled(Core.Services.ConsentStore.Read(Core.Services.ConsentKey.OnlineConnection)));
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            bool hasSettings = false;
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                var settings = ResolveSettings();
                hasSettings = settings != null;

                KeyedSerializer.WriteBlockHeader(writer, hasSettings ? PersistedSettingsFieldCount + 6 : 1);
                KeyedSerializer.WriteField(writer, "hasSettings", hasSettings);

                if (hasSettings)
                {
                    KeyedSerializer.WriteEnumIntField(writer, "preset", (int)settings!.CurrentPreset);
                    SerializePersistedSettings(writer, settings);
                    // FIX M-101: Persist Custom preset numeric fields to prevent silent revert on load
                    KeyedSerializer.WriteField(writer, "legalImportMW", settings.LegalImportMW);
                    KeyedSerializer.WriteField(writer, "legalExportMW", settings.LegalExportMW);
                    KeyedSerializer.WriteField(writer, "shadowImportPrice", settings.ShadowImportPrice);
                    KeyedSerializer.WriteEnumIntField(writer, "basePreset", (int)settings.BasePreset);
                }

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            Log.Info($"Serialized (hasSettings={hasSettings})");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out _, out var block, nameof(ModSettingsSerializationSystem)))
            {
                ResetToBootDefaults(ResetReason.VersionMismatch);
                return;
            }
            try
            {
                var settings = ResolveSettings();

                // Order-independent: always read all keyed fields into ModSettings.
                // If hasSettings=false, the settings fields won't be present in the stream.
                int __fc = KeyedSerializer.ReadBlockFieldCount(reader);
                if (settings == null)
                {
                    Log.Warn($"ModSettings not registered — skipping {__fc} serialized setting fields");
                    for (int __i = 0; __i < __fc; __i++)
                    {
                        var __tag = KeyedSerializer.ReadFieldHeader(reader, out _);
                        KeyedSerializer.Skip(reader, __tag);
                    }
                    return;
                }

                settings.ResetToDefaults();

                // Track custom numeric fields to apply once after all fields read (avoids stale intermediate calls)
                int? pendingLegalMW = null;
                int? pendingLegalExportMW = null;
                float? pendingShadowPrice = null;
                DifficultyPreset? pendingBasePreset = null;
                for (int __i = 0; __i < __fc; __i++)
                {
                    var __tag = KeyedSerializer.ReadFieldHeader(reader, out var __key);
                    switch (__key)
                    {
                        case "hasSettings":
                            KeyedSerializer.ReadBool(reader, __tag, "hasSettings"); // consumed but not used — settings fields are self-describing
                            break;
                        case "preset":
                            settings.ApplyPatch(ModSettingsPatch.SetDifficultyPreset(KeyedSerializer.ReadEnumInt<TReader, DifficultyPreset>(reader, __tag, "preset", DifficultyPreset.BlackoutProtocol)));
                            break;
                        case "legalImportMW":
                        case "legalExportMW":
                        case "shadowImportPrice":
                        case "basePreset":
                            ReadCustomNumericField(reader, settings, __key, __tag, ref pendingLegalMW, ref pendingLegalExportMW, ref pendingShadowPrice, ref pendingBasePreset);
                            break;
                        default:
                            if (!TryReadPersistedSetting(reader, settings, __key, __tag))
                                KeyedSerializer.Skip(reader, __tag);
                            break;
                    }
                }
                // Apply custom numeric fields once with all values resolved
                if (pendingLegalMW.HasValue || pendingLegalExportMW.HasValue || pendingShadowPrice.HasValue || pendingBasePreset.HasValue)
                {
                    settings.ApplyCustomNumericFields(
                        pendingLegalMW ?? settings.LegalImportMW,
                        pendingLegalExportMW ?? settings.LegalExportMW,
                        pendingShadowPrice ?? settings.ShadowImportPrice,
                        pendingBasePreset ?? settings.BasePreset);
                }

                if (!LocalizationManager.IsLanguageAvailable(settings.LanguagePreference))
                {
                    settings.ApplyPatch(ModSettingsPatch.SetLanguagePreference(ModLanguage.GameDefault));
                }

                // Telemetry opt-in lives in a global file (save-independent), not the save.
                // One-time migration: a save made before the global store existed carries the
                // consent only in-save (settings.TelemetryEnabled here). If the store file is
                // absent and the save granted consent, persist it globally so it survives this
                // load instead of being silently reset to false by the Read() override below.
                if (settings.TelemetryEnabled && !Core.Services.TelemetryOptInStore.Exists)
                {
                    Core.Services.TelemetryOptInStore.Write(true);
                    Log.Info("Migrated in-save telemetry consent to global opt-in store");
                }

                // Override whatever the save carried with the real persisted consent so the
                // Options toggle and crash-breadcrumb gating stay consistent across saves.
                settings.ApplyPatch(ModSettingsPatch.SetTelemetryEnabled(Core.Services.TelemetryOptInStore.Read()));

                // Online connection (Global Grid) is also a global preference stored in a
                // save-independent file. One-time migration: a save made before the global
                // store existed carries the consent only in-save. If the store file is absent
                // and the save had the connection enabled, persist it globally so an existing
                // player's enabled Global Grid is not reset to false by the Read() override below.
                if (settings.NetworkConnectionEnabled && !Core.Services.ConsentStore.Exists(Core.Services.ConsentKey.OnlineConnection))
                {
                    Core.Services.ConsentStore.Write(Core.Services.ConsentKey.OnlineConnection, true);
                    Log.Info("Migrated in-save online connection consent to global store");
                }

                // Override whatever the save carried with the real persisted preference so
                // the global file (not the per-city save) is the source of truth.
                settings.ApplyPatch(ModSettingsPatch.SetNetworkConnectionEnabled(Core.Services.ConsentStore.Read(Core.Services.ConsentKey.OnlineConnection)));
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize failed: {ex}");
                ResetToBootDefaults(ResetReason.DeserializeFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        // FIX M-101: Read custom numeric fields into pending locals (applied once after loop in Deserialize)
        private static void ReadCustomNumericField<TReader>(TReader reader, ModSettings settings, string __key, TypeTag tag,
            ref int? pendingLegalMW, ref int? pendingLegalExportMW, ref float? pendingShadowPrice, ref DifficultyPreset? pendingBasePreset) where TReader : IReader
        {
            switch (__key)
            {
                case "legalImportMW":
                    pendingLegalMW = KeyedSerializer.ReadInt(reader, tag, "legalImportMW", settings.LegalImportMW);
                    break;
                case "legalExportMW":
                    pendingLegalExportMW = KeyedSerializer.ReadInt(reader, tag, "legalExportMW", settings.LegalExportMW);
                    break;
                case "shadowImportPrice":
                    pendingShadowPrice = KeyedSerializer.ReadSafeFloat(reader, tag, "shadowImportPrice", 0f, 10000f, settings.ShadowImportPrice);
                    break;
                case "basePreset":
                    pendingBasePreset = KeyedSerializer.ReadEnumInt<TReader, DifficultyPreset>(reader, tag, "basePreset", settings.BasePreset);
                    break;
                default: KeyedSerializer.Skip(reader, tag); break;
            }
        }

        private ModSettings? ResolveSettings()
        {
            if (m_Settings == null)
            {
                m_Settings = ServiceRegistry.TryGet<ModSettings>();
            }

            return m_Settings;
        }
    }
}
