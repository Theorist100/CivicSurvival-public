using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to add income to Shadow Wallet.
    /// Ephemeral entity pattern - created by any domain, processed by ShadowWalletSystem.
    ///
    /// This decouples domains from ShadowEconomy:
    /// - VIPProtectionRacketSystem creates request (no import of ShadowEconomy.Systems)
    /// - RepairPaymentHelper creates request for kickbacks
    /// - ShadowWalletSystem processes requests in its OnUpdate
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct ShadowIncomeRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Amount to add (positive value, matches wallet's long type).</summary>
        public long Amount;

        /// <summary>Reason for income (for logging/tracking).</summary>
        public FixedString64Bytes Reason;

        /// <summary>Durable idempotency key. Wallet applies each key at most once.</summary>
        public FixedString128Bytes OperationKey;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "rsn", Reason.ToString());
                KeyedSerializer.WriteField(writer, "key", OperationKey.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ShadowIncomeRequest)))
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
                            case "amt": Amount = KeyedSerializer.ReadBoundedLong(reader, tag, "amt", 0, long.MaxValue); break;
                            case "rsn": { string r = KeyedSerializer.ReadString(reader, tag, "rsn"); Reason = new FixedString64Bytes(r ?? ""); } break;
                            case "key": { string k = KeyedSerializer.ReadString(reader, tag, "key"); OperationKey = new FixedString128Bytes(k ?? ""); } break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
