using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Services.Economy
{
    /// <summary>
    /// Serialization helper for ShadowImportState + ShadowExportState.
    /// Single block — binary format matches old ShadowTradeState (no save version bump).
    ///
    /// Caller: ShadowTradeDailySystem (sole caller)
    /// </summary>
    public static class ShadowTradeSerializer
    {
        private static readonly LogContext Log = new("ShadowTradeSerializer");

        public static void WriteAll<TWriter>(
            TWriter writer,
            in ShadowImportState import,
            in ShadowExportState export)
            where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SaveVersions.GLOBAL);
            try
            {

                KeyedSerializer.WriteBlockHeader(writer, 14);
                KeyedSerializer.WriteField(writer, "importMW", import.ImportMW);
                KeyedSerializer.WriteField(writer, "importDaysActive", import.ImportDaysActive);
                KeyedSerializer.WriteField(writer, "importDiscoveryRisk", import.ImportDiscoveryRisk);
                KeyedSerializer.WriteField(writer, "importIsSanctioned", import.ImportIsSanctioned);
                KeyedSerializer.WriteField(writer, "importSanctionDays", import.ImportSanctionDaysRemaining);
                KeyedSerializer.WriteField(writer, "importWasActive", import.ImportWasActiveYesterday);
                KeyedSerializer.WriteField(writer, "exportPercentage", export.ExportPercentage);
                KeyedSerializer.WriteField(writer, "exportedMW", export.ExportedMW);
                KeyedSerializer.WriteField(writer, "exportDailyIncome", export.ExportDailyIncome);
                KeyedSerializer.WriteField(writer, "exportLastAccum", 0.0);
                KeyedSerializer.WriteField(writer, "exportIncomeRemainder", export.ExportIncomeRemainder);
                KeyedSerializer.WriteField(writer, "suspicionCooldown", export.SuspicionCooldown);
                KeyedSerializer.WriteField(writer, "importRng", unchecked((int)import.RngState));
                KeyedSerializer.WriteField(writer, "exportRng", unchecked((int)export.RngState));

            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
            SerializationGuard.LogSerialized("ShadowTradeState", SaveVersions.GLOBAL);
        }

        public static void ReadAll<TReader>(
            TReader reader,
            out ShadowImportState import,
            out ShadowExportState export)
            where TReader : IReader
        {
            import = ShadowImportState.CreateDefault();
            export = ShadowExportState.CreateDefault();

            if (!SerializationGuard.TryBeginBlock(reader, SaveVersions.GLOBAL, out var version, out var block, "ShadowTradeState"))
                return;

            try
            {
                uint importRngFallback = import.RngState;
                uint exportRngFallback = export.RngState;
                uint importRng = 0, exportRng = 0;
                bool hasImportRng = false, hasExportRng = false;
                int __fc = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int __i = 0; __i < __fc; __i++)
                {
                    var __tag = KeyedSerializer.ReadFieldHeader(reader, out var __key);
                    switch (__key)
                    {
                        case "importMW": import.ImportMW = ReadSavedImportMw(reader, __tag); break;
                        case "importDaysActive": import.ImportDaysActive = KeyedSerializer.ReadBoundedInt(reader, __tag, "importDaysActive", 0, int.MaxValue, 0); break;
                        case "importDiscoveryRisk": import.ImportDiscoveryRisk = KeyedSerializer.ReadSafeFloat(reader, __tag, "importDiscoveryRisk", 0f, 1f, 0f); break;
                        case "importIsSanctioned": import.ImportIsSanctioned = KeyedSerializer.ReadBool(reader, __tag, "importIsSanctioned"); break;
                        case "importSanctionDays": import.ImportSanctionDaysRemaining = KeyedSerializer.ReadBoundedInt(reader, __tag, "importSanctionDays", 0, int.MaxValue, 0); break;
                        case "importWasActive": import.ImportWasActiveYesterday = KeyedSerializer.ReadBool(reader, __tag, "importWasActive"); break;
                        case "exportPercentage": export.ExportPercentage = KeyedSerializer.ReadBoundedInt(reader, __tag, "exportPercentage", 0, 100, 0); break;
                        case "exportedMW": export.ExportedMW = KeyedSerializer.ReadBoundedInt(reader, __tag, "exportedMW", 0, int.MaxValue, 0); break;
                        case "exportDailyIncome": export.ExportDailyIncome = KeyedSerializer.ReadBoundedInt(reader, __tag, "exportDailyIncome", 0, int.MaxValue, 0); break;
                        case "exportLastAccum":
                            KeyedSerializer.Skip(reader, __tag);
                            export.ExportLastAccumulationTime = 0.0;
                            break;
                        case "exportIncomeRemainder": export.ExportIncomeRemainder = KeyedSerializer.ReadSafeDouble(reader, __tag, "exportIncomeRemainder", 0.0, 1.0, 0.0); break;
                        case "suspicionCooldown": export.SuspicionCooldown = KeyedSerializer.ReadBoundedInt(reader, __tag, "suspicionCooldown", 0, int.MaxValue, 0); break;
                        case "importRng":
                            if (!KeyedSerializer.ExpectTag(reader, __tag, TypeTag.I32, "importRng")) break;
                            reader.Read(out int __ir); importRng = unchecked((uint)__ir); hasImportRng = true;
                            break;
                        case "exportRng":
                            if (!KeyedSerializer.ExpectTag(reader, __tag, TypeTag.I32, "exportRng")) break;
                            reader.Read(out int __er); exportRng = unchecked((uint)__er); hasExportRng = true;
                            break;
                        default: KeyedSerializer.Skip(reader, __tag); break;
                    }
                }
                if (!hasImportRng || importRng == 0)
                {
                    import.RngState = importRngFallback;
                    Log.Warn("Import RNG missing/zero; restored deterministic default");
                }
                else
                {
                    import.RngState = importRng;
                }

                if (!hasExportRng || exportRng == 0)
                {
                    export.RngState = exportRngFallback;
                    Log.Warn("Export RNG missing/zero; restored deterministic default");
                }
                else
                {
                    export.RngState = exportRng;
                }

                Log.Info($"Deserialized v{version}: Import={import.ImportMW}MW, Export={export.ExportPercentage}%");
            }
            catch (System.Exception ex)
            {
                Log.Error($"Deserialize ShadowTradeState failed: {ex}");
                import = ShadowImportState.CreateDefault();
                export = ShadowExportState.CreateDefault();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        private static int ReadSavedImportMw<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int savedImportMw = KeyedSerializer.ReadBoundedInt(reader, tag, "importMW", 0, int.MaxValue, 0);
            int liveMaxImportMw = MaxImportMwForDeserialize();
            return math.min(savedImportMw, liveMaxImportMw);
        }

        private static int MaxImportMwForDeserialize()
        {
            return math.max(0, BalanceConfig.Current.ShadowImport.AbsoluteMaxMw);
        }
    }
}
