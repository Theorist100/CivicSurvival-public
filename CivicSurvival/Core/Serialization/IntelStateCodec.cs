using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct IntelPersistState
    {
        public IntelPersistState(bool hasInsider, int intelUpgradeLevel, int maxIntelUpgradeLevel)
        {
            HasInsider = hasInsider;
            IntelUpgradeLevel = SanitizeUpgradeLevel(intelUpgradeLevel, maxIntelUpgradeLevel);
        }

        public bool HasInsider { get; }
        public int IntelUpgradeLevel { get; }

        private static int SanitizeUpgradeLevel(int value, int maxIntelUpgradeLevel)
        {
            if (value < 0)
                return 0;
            return value > maxIntelUpgradeLevel ? maxIntelUpgradeLevel : value;
        }
    }

    public static class IntelStateCodec
    {
        public static void Write<TWriter>(in IntelPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_HasInsider", state.HasInsider);
            KeyedSerializer.WriteField(writer, "m_IntelUpgradeLevel", state.IntelUpgradeLevel);
        }

        public static void Read<TReader>(TReader reader, int maxIntelUpgradeLevel, out IntelPersistState state)
            where TReader : IReader
        {
            bool hasInsider = false;
            int intelUpgradeLevel = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_HasInsider":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.Bool, "m_HasInsider"))
                            break;
                        reader.Read(out hasInsider);
                        break;
                    case "m_IntelUpgradeLevel":
                        intelUpgradeLevel = KeyedSerializer.ReadBoundedInt(
                            reader,
                            tag,
                            "m_IntelUpgradeLevel",
                            0,
                            maxIntelUpgradeLevel,
                            0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new IntelPersistState(hasInsider, intelUpgradeLevel, maxIntelUpgradeLevel);
        }
    }
}
