using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    internal static class SpotterBudgetIntentLog
    {
        public static readonly LogContext Log = new("SpotterBudgetIntent");
    }

    /// <summary>
    /// Typed link from a retained spotter budget request to its target/action.
    /// Source remains diagnostic text only; target identity round-trips through
    /// KeyedSerializer.WriteEntityField so save/load remaps the entity reference.
    /// </summary>
    public struct SpotterBudgetIntent : IComponentData, ISerializable
    {
        public AirDefenseActionType Action;
        public EntityRef Target;
        public int Cost;
        public int Days;
        public double CoveredUntilGameHour;
        public bool ChargeFailed;
        public bool DomainApplied;
        public bool DomainRejected;
        public bool RefundQueued;
        public bool RefundResolved;
        public bool RefundFailed;
        public bool RefundCleanupQueued;
        public FixedString128Bytes RefundOperationKey;

        public readonly Entity TargetEntity => Target.ToEntity();

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 6;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 12);
                KeyedSerializer.WriteField(writer, "act", (int)Action);
                KeyedSerializer.WriteEntityField(writer, "target", TargetEntity);
                KeyedSerializer.WriteField(writer, "cost", Cost);
                KeyedSerializer.WriteField(writer, "days", Days);
                KeyedSerializer.WriteField(writer, "covered", CoveredUntilGameHour);
                KeyedSerializer.WriteField(writer, "chgFail", ChargeFailed);
                KeyedSerializer.WriteField(writer, "applied", DomainApplied);
                KeyedSerializer.WriteField(writer, "rejected", DomainRejected);
                KeyedSerializer.WriteField(writer, "refundQ", RefundQueued);
                KeyedSerializer.WriteField(writer, "refundR", RefundResolved);
                KeyedSerializer.WriteField(writer, "refundF", RefundFailed);
                KeyedSerializer.WriteField(writer, "refundOp", RefundOperationKey.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(SpotterBudgetIntent)))
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
                            case "act":
                            {
                                int raw = KeyedSerializer.ReadBoundedInt(reader, tag, "act", 0, 255, 0);
                                Action = System.Enum.IsDefined(typeof(AirDefenseActionType), (byte)raw)
                                    ? (AirDefenseActionType)(byte)raw
                                    : AirDefenseActionType.None;
                                break;
                            }
                            case "target":
                            {
                                var target = KeyedSerializer.ReadEntity(reader, tag, "target");
                                Target = EntityRef.FromEntity(target);
                                break;
                            }
                            case "cost":
                                Cost = KeyedSerializer.ReadBoundedInt(reader, tag, "cost", 0, int.MaxValue, 0);
                                break;
                            case "days":
                                Days = KeyedSerializer.ReadBoundedInt(reader, tag, "days", 0, 365, 0);
                                break;
                            case "covered":
                                CoveredUntilGameHour = KeyedSerializer.ReadDouble(reader, tag, "covered");
                                break;
                            case "applied":
                                DomainApplied = KeyedSerializer.ReadBool(reader, tag, "applied");
                                break;
                            case "chgFail":
                                ChargeFailed = KeyedSerializer.ReadBool(reader, tag, "chgFail");
                                break;
                            case "rejected":
                                DomainRejected = KeyedSerializer.ReadBool(reader, tag, "rejected");
                                break;
                            case "refundQ":
                                RefundQueued = KeyedSerializer.ReadBool(reader, tag, "refundQ");
                                break;
                            case "refundR":
                                RefundResolved = KeyedSerializer.ReadBool(reader, tag, "refundR");
                                break;
                            case "refundF":
                                RefundFailed = KeyedSerializer.ReadBool(reader, tag, "refundF");
                                break;
                            case "refundOp":
                            {
                                string s = KeyedSerializer.ReadString(reader, tag, "refundOp");
                                RefundOperationKey = new FixedString128Bytes(s ?? "");
                                break;
                            }
                            default:
                                KeyedSerializer.Skip(reader, tag);
                                break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                SpotterBudgetIntentLog.Log.Error($"Deserialize {nameof(SpotterBudgetIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
