using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Countermeasures
{
    /// <summary>
    /// Companion component on a retained countermeasure bribe budget entity.
    /// </summary>
    public struct CountermeasureBribeIntent : IComponentData, ISerializable
    {
        public byte Kind;
        public bool ChargeFailed;
        public bool DomainApplied;
        public bool DomainRejected;
        public bool RefundQueued;
        public bool RefundFailed;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;
        public const byte InvestigationKind = 0;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 6);
                KeyedSerializer.WriteField(writer, "kind", (int)Kind);
                KeyedSerializer.WriteField(writer, "chgFail", ChargeFailed);
                KeyedSerializer.WriteField(writer, "applied", DomainApplied);
                KeyedSerializer.WriteField(writer, "rejected", DomainRejected);
                KeyedSerializer.WriteField(writer, "refundQ", RefundQueued);
                KeyedSerializer.WriteField(writer, "refundF", RefundFailed);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(CountermeasureBribeIntent)))
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
                            case "kind":
                                Kind = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "kind", InvestigationKind, InvestigationKind, InvestigationKind);
                                break;
                            case "chgFail": ChargeFailed = KeyedSerializer.ReadBool(reader, tag, "chgFail"); break;
                            case "applied": DomainApplied = KeyedSerializer.ReadBool(reader, tag, "applied"); break;
                            case "rejected": DomainRejected = KeyedSerializer.ReadBool(reader, tag, "rejected"); break;
                            case "refundQ": RefundQueued = KeyedSerializer.ReadBool(reader, tag, "refundQ"); break;
                            case "refundF": RefundFailed = KeyedSerializer.ReadBool(reader, tag, "refundF"); break;
                            default:
                                KeyedSerializer.Skip(reader, tag);
                                break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(CountermeasureBribeIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
