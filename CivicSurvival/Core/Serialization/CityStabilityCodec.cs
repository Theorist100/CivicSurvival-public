using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CityStabilityPersistState
    {
        public CityStabilityPersistState(float stability) => Stability = stability;

        public float Stability { get; }
    }

    public static class CityStabilityCodec
    {
        public const float DefaultStability = 1f;

        public static void Write<TWriter>(in CityStabilityPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "m_Stability", state.Stability);
        }

        public static void Read<TReader>(TReader reader, out CityStabilityPersistState state)
            where TReader : IReader
        {
            float stability = DefaultStability;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_Stability":
                        stability = KeyedSerializer.ReadSafeFloat(reader, tag, "m_Stability", 0f, 1f, DefaultStability);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CityStabilityPersistState(stability);
        }
    }
}
