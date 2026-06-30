using Unity.Collections;
using Unity.Entities;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Systems.Requests;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to activate district modernization (backup power procurement).
    /// Ephemeral entity pattern - created by UI, processed by ShadowProcurementSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.TransientInput, typeof(CommandRequestCleanupSystem))]
    public struct ModernizationRequest : IComponentData, ICommandRequest, IEmptySerializable
    {
        /// <summary>District index to modernize.</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>Contractor type for the modernization.</summary>
        public ContractorType Contractor;
    }

    /// <summary>
    /// Durable snapshot attached to retained modernization budget requests.
    /// Keeps budget results matchable across save/load without managed runtime state.
    /// </summary>
    public struct DistrictModernizationIntent : IComponentData, ISerializable
    {
        public FixedString128Bytes OperationKey;
        public int DistrictId;
        public ContractorType Contractor;
        public int BuildingCount;
        public int TotalCost;
        public int Kickback;
        public bool ReplacingCorrupt;
        public int ActivationDay;
        public bool ChargeFailed;
        public bool DomainApplied;
        public bool DomainRejected;
        public bool RefundQueued;
        public bool RefundFailed;
        public bool BudgetSucceeded;
        public bool InstallQueued;
        public bool InstallVerified;
        public bool ProgramCommitted;
        public bool TerminalEmitted;
        public int InstallCommandCount;
        public int ActualInstalled;
        public int EffectiveKickback;
        public bool KickbackRequestDurable;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 3;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 22);
                KeyedSerializer.WriteField(writer, "op", OperationKey.ToString());
                KeyedSerializer.WriteField(writer, "d", DistrictId);
                KeyedSerializer.WriteEnumByteField(writer, "con", (byte)Contractor);
                KeyedSerializer.WriteField(writer, "bCnt", BuildingCount);
                KeyedSerializer.WriteField(writer, "cost", TotalCost);
                KeyedSerializer.WriteField(writer, "kick", Kickback);
                KeyedSerializer.WriteField(writer, "rep", ReplacingCorrupt);
                KeyedSerializer.WriteField(writer, "actDay", ActivationDay);
                KeyedSerializer.WriteField(writer, "chgFail", ChargeFailed);
                KeyedSerializer.WriteField(writer, "applied", DomainApplied);
                KeyedSerializer.WriteField(writer, "rejected", DomainRejected);
                KeyedSerializer.WriteField(writer, "refundQ", RefundQueued);
                KeyedSerializer.WriteField(writer, "refundF", RefundFailed);
                KeyedSerializer.WriteField(writer, "budgetOk", BudgetSucceeded);
                KeyedSerializer.WriteField(writer, "instQ", InstallQueued);
                KeyedSerializer.WriteField(writer, "instV", InstallVerified);
                KeyedSerializer.WriteField(writer, "progC", ProgramCommitted);
                KeyedSerializer.WriteField(writer, "termE", TerminalEmitted);
                KeyedSerializer.WriteField(writer, "instCnt", InstallCommandCount);
                KeyedSerializer.WriteField(writer, "actual", ActualInstalled);
                KeyedSerializer.WriteField(writer, "effKick", EffectiveKickback);
                KeyedSerializer.WriteField(writer, "kickDur", KickbackRequestDurable);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DistrictModernizationIntent)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "op": { string s = KeyedSerializer.ReadString(reader, tag, "op"); OperationKey = new FixedString128Bytes(s ?? ""); } break;
                            case "d": DistrictId = KeyedSerializer.ReadBoundedInt(reader, tag, "d", 0, int.MaxValue, 0); break;
                            case "con": Contractor = KeyedSerializer.ReadEnumByte<TReader, ContractorType>(reader, tag, "con", ContractorType.None); break;
                            case "bCnt": BuildingCount = KeyedSerializer.ReadBoundedInt(reader, tag, "bCnt", 0, 10000, 0); break;
                            case "cost": TotalCost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0); break;
                            case "kick": Kickback = KeyedSerializer.ReadBoundedInt(reader, tag, "kick", 0, int.MaxValue, 0); break;
                            case "rep": ReplacingCorrupt = KeyedSerializer.ReadBool(reader, tag, "rep"); break;
                            case "actDay": ActivationDay = KeyedSerializer.ReadBoundedInt(reader, tag, "actDay", 0, 100000, 0); break;
                            case "chgFail": ChargeFailed = KeyedSerializer.ReadBool(reader, tag, "chgFail"); break;
                            case "applied": DomainApplied = KeyedSerializer.ReadBool(reader, tag, "applied"); break;
                            case "rejected": DomainRejected = KeyedSerializer.ReadBool(reader, tag, "rejected"); break;
                            case "refundQ": RefundQueued = KeyedSerializer.ReadBool(reader, tag, "refundQ"); break;
                            case "refundF": RefundFailed = KeyedSerializer.ReadBool(reader, tag, "refundF"); break;
                            case "budgetOk": BudgetSucceeded = KeyedSerializer.ReadBool(reader, tag, "budgetOk"); break;
                            case "instQ": InstallQueued = KeyedSerializer.ReadBool(reader, tag, "instQ"); break;
                            case "instV": InstallVerified = KeyedSerializer.ReadBool(reader, tag, "instV"); break;
                            case "progC": ProgramCommitted = KeyedSerializer.ReadBool(reader, tag, "progC"); break;
                            case "termE": TerminalEmitted = KeyedSerializer.ReadBool(reader, tag, "termE"); break;
                            case "instCnt": InstallCommandCount = KeyedSerializer.ReadBoundedInt(reader, tag, "instCnt", 0, 10000, 0); break;
                            case "actual": ActualInstalled = KeyedSerializer.ReadBoundedInt(reader, tag, "actual", 0, 10000, 0); break;
                            case "effKick": EffectiveKickback = KeyedSerializer.ReadBoundedInt(reader, tag, "effKick", 0, int.MaxValue, 0); break;
                            case "kickDur": KickbackRequestDurable = KeyedSerializer.ReadBool(reader, tag, "kickDur"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
                if (version < 3)
                {
                    BudgetSucceeded = DomainApplied || DomainRejected || ChargeFailed;
                    InstallVerified = DomainApplied;
                    ProgramCommitted = DomainApplied;
                    TerminalEmitted = DomainApplied || DomainRejected || ChargeFailed;
                    KickbackRequestDurable = true;
                }
            }
            catch (System.Exception ex)
            {
                RequestDeserializeLog.Log.Error($"Deserialize {nameof(DistrictModernizationIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }

    public enum ModernizationReceiptKind : byte
    {
        None = 0,
        BackupPower = 1,
        CounterfeitBattery = 2
    }

    /// <summary>
    /// Durable receipt attached to modernization-created sidecars. A retained
    /// modernization intent may only become terminal after these receipts exist.
    /// </summary>
    public struct ModernizationInstallReceipt : IComponentData, ISerializable
    {
        public FixedString128Bytes OperationKey;
        public int DistrictId;
        public ContractorType Contractor;
        public int ActivationDay;
        public int TotalCost;
        public long BuildingKey;
        public ModernizationReceiptKind Kind;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteField(writer, "op", OperationKey.ToString());
                KeyedSerializer.WriteDistrictKey(writer, "d", DistrictId);
                KeyedSerializer.WriteEnumByteField(writer, "con", (byte)Contractor);
                KeyedSerializer.WriteField(writer, "actDay", ActivationDay);
                KeyedSerializer.WriteField(writer, "cost", TotalCost);
                KeyedSerializer.WriteField(writer, "bKey", BuildingKey);
                KeyedSerializer.WriteEnumByteField(writer, "kind", (byte)Kind);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ModernizationInstallReceipt)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "op": { string s = KeyedSerializer.ReadString(reader, tag, "op"); OperationKey = new FixedString128Bytes(s ?? ""); } break;
                            case "d": DistrictId = KeyedSerializer.ReadDistrictKey(reader, tag, "d", -1); break;
                            case "con": Contractor = KeyedSerializer.ReadEnumByte<TReader, ContractorType>(reader, tag, "con", ContractorType.None); break;
                            case "actDay": ActivationDay = KeyedSerializer.ReadBoundedInt(reader, tag, "actDay", 0, 100000, 0); break;
                            case "cost": TotalCost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0); break;
                            case "bKey": BuildingKey = KeyedSerializer.ReadBoundedLong(reader, tag, "bKey", 0, long.MaxValue); break;
                            case "kind": Kind = KeyedSerializer.ReadEnumByte<TReader, ModernizationReceiptKind>(reader, tag, "kind", ModernizationReceiptKind.None); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                RequestDeserializeLog.Log.Error($"Deserialize {nameof(ModernizationInstallReceipt)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
