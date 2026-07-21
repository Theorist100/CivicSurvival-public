using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct DefeatCheckPersistState
    {
        public DefeatCheckPersistState(float integrityBelowThresholdHours)
            => IntegrityBelowThresholdHours = integrityBelowThresholdHours;

        public float IntegrityBelowThresholdHours { get; }
    }

    public static class DefeatCheckCodec
    {
        public static void Write<TWriter>(in DefeatCheckPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "m_IntegrityBelowThresholdHours", state.IntegrityBelowThresholdHours);
        }

        public static void Read<TReader>(TReader reader, out DefeatCheckPersistState state)
            where TReader : IReader
        {
            float integrityBelowThresholdHours = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_IntegrityBelowThresholdHours":
                        integrityBelowThresholdHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_IntegrityBelowThresholdHours", 0f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DefeatCheckPersistState(integrityBelowThresholdHours);
        }
    }
}
