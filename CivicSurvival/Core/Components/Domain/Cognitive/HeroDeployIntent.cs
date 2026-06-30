using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Components.CrossDomain;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Cognitive
{
    /// <summary>
    /// Companion component on a retained hero deploy budget entity.
    /// Carries the deferred side-effect data until BudgetResolutionSystem confirms payment.
    /// </summary>
    public struct HeroDeployIntent : IComponentData, ISerializable
    {
        public HeroStatus Mode;
        public bool ChargeFailed;
        public bool DomainApplied;
        public bool DomainRejected;
        public bool RefundQueued;
        public bool RefundFailed;
        public bool IsRefundRequest;

        public void SetDefaults() => Mode = HeroStatus.Inactive;

        private const byte SAVE_VERSION = 3;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteEnumByteField(writer, "mode", (byte)Mode);
                KeyedSerializer.WriteField(writer, "chgFail", ChargeFailed);
                KeyedSerializer.WriteField(writer, "applied", DomainApplied);
                KeyedSerializer.WriteField(writer, "rejected", DomainRejected);
                KeyedSerializer.WriteField(writer, "refundQ", RefundQueued);
                KeyedSerializer.WriteField(writer, "refundF", RefundFailed);
                KeyedSerializer.WriteField(writer, "isRefund", IsRefundRequest);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(HeroDeployIntent)))
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
                            case "mode":
                                Mode = KeyedSerializer.ReadBoundedByte(reader, tag, "mode", 0, 2, 0) switch
                                {
                                    1 => HeroStatus.Deployed,
                                    2 => HeroStatus.Lecturing,
                                    _ => HeroStatus.Inactive
                                };
                                break;
                            case "chgFail": ChargeFailed = KeyedSerializer.ReadBool(reader, tag, "chgFail"); break;
                            case "applied": DomainApplied = KeyedSerializer.ReadBool(reader, tag, "applied"); break;
                            case "rejected": DomainRejected = KeyedSerializer.ReadBool(reader, tag, "rejected"); break;
                            case "refundQ": RefundQueued = KeyedSerializer.ReadBool(reader, tag, "refundQ"); break;
                            case "refundF": RefundFailed = KeyedSerializer.ReadBool(reader, tag, "refundF"); break;
                            case "isRefund": IsRefundRequest = KeyedSerializer.ReadBool(reader, tag, "isRefund"); break;
                            default:
                                KeyedSerializer.Skip(reader, tag);
                                break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Error($"Deserialize {nameof(HeroDeployIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
