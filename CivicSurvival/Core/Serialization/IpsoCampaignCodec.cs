using Colossal.Serialization.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Persisted IPSO campaign state. Only the post-wave spike countdown is carried:
    /// every other IPSOState field (IsActive, exposure, district counts, buffer) is
    /// derived and rebuilt each tick from the wave/act/telecom inputs, so persisting
    /// them would be redundant. The spike timer is the one value that cannot be
    /// recomputed after load — it is an in-flight countdown started by a wave-end event.
    /// </summary>
    public readonly struct IpsoCampaignPersistState
    {
        public IpsoCampaignPersistState(float postWaveSpikeTimer)
        {
            PostWaveSpikeTimer = postWaveSpikeTimer;
        }

        public float PostWaveSpikeTimer { get; }
    }

    public static class IpsoCampaignCodec
    {
        public static void Write<TWriter>(in IpsoCampaignPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "spike", state.PostWaveSpikeTimer);
        }

        public static void Read<TReader>(TReader reader, out IpsoCampaignPersistState state)
            where TReader : IReader
        {
            float postWaveSpikeTimer = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "spike":
                        postWaveSpikeTimer = KeyedSerializer.ReadSafeFloat(reader, tag, "spike", 0f, GameRate.SECONDS_PER_HOUR, 0f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new IpsoCampaignPersistState(postWaveSpikeTimer);
        }
    }
}
