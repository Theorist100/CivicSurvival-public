using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to modify Shadow Reputation trust level.
    /// Ephemeral entity pattern - created by any domain, processed by ShadowReputationSystem.
    ///
    /// This decouples domains from Corruption:
    /// - MobilizationSystem creates request (no import of Corruption.Systems)
    /// - HumanitarianAidSystem creates request
    /// - ShadowReputationSystem processes requests in its OnUpdate
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct TrustModificationRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Amount to add/subtract (can be negative).</summary>
        public float Amount;

        /// <summary>Reason for modification (for logging/tracking).</summary>
        public FixedString64Bytes Reason;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "amt", Amount);
                KeyedSerializer.WriteField(writer, "rsn", Reason.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(TrustModificationRequest)))
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
                            case "amt": Amount = KeyedSerializer.ReadSafeFloat(reader, tag, "amt", -100f, 100f, 0f); break;
                            case "rsn": { string r = KeyedSerializer.ReadString(reader, tag, "rsn"); Reason = new FixedString64Bytes(r ?? ""); } break;
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
