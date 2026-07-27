using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Correlates a retained donor add-funds request with DonorConference terminal state.
    /// The conference use/cooldown is consumed only after BudgetAddFundsResult confirms.
    /// </summary>
    public struct DonorFundsGrantIntent : IComponentData, ISerializable
    {
        public long Amount;
        public int RequestId;
        public FixedString128Bytes OperationKey;
        public FixedString128Bytes DonorMessage;
        public bool TerminalResolved;
        public bool TerminalSucceeded;
        public bool TerminalApplied;

        private const byte SAVE_VERSION = 3;

        public void SetDefaults() => this = default;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "req", RequestId);
                KeyedSerializer.WriteField(writer, "op", OperationKey.ToString());
                KeyedSerializer.WriteField(writer, "msg", DonorMessage.ToString());
                KeyedSerializer.WriteField(writer, "resolved", TerminalResolved);
                KeyedSerializer.WriteField(writer, "succeeded", TerminalSucceeded);
                KeyedSerializer.WriteField(writer, "applied", TerminalApplied);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out _, out var block, nameof(DonorFundsGrantIntent)))
            {
                SetDefaults();
                return;
            }

            try
            {
                int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int i = 0; i < fc; i++)
                {
                    var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "amt":
                            Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue);
                            break;
                        case "req":
                            RequestId = KeyedSerializer.ReadBoundedInt(reader, tag, "req", 0, int.MaxValue, 0);
                            break;
                        case "op":
                            OperationKey = new FixedString128Bytes(KeyedSerializer.ReadString(reader, tag, "op") ?? string.Empty);
                            break;
                        case "msg":
                            DonorMessage = new FixedString128Bytes(KeyedSerializer.ReadString(reader, tag, "msg") ?? string.Empty);
                            break;
                        case "resolved":
                            TerminalResolved = KeyedSerializer.ReadBool(reader, tag, "resolved");
                            break;
                        case "succeeded":
                            TerminalSucceeded = KeyedSerializer.ReadBool(reader, tag, "succeeded");
                            break;
                        case "applied":
                            TerminalApplied = KeyedSerializer.ReadBool(reader, tag, "applied");
                            break;
                        default:
                            KeyedSerializer.Skip(reader, tag);
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                RequestDeserializeLog.Log.Error($"Deserialize {nameof(DonorFundsGrantIntent)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
