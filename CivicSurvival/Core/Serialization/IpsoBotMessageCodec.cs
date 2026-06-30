using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct IpsoBotMessagePersistState
    {
        public IpsoBotMessagePersistState(uint randomState)
        {
            RandomState = randomState;
        }

        public uint RandomState { get; }
    }

    public static class IpsoBotMessageCodec
    {
        public static void Write<TWriter>(in IpsoBotMessagePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "rng", (long)state.RandomState);
        }

        public static void Read<TReader>(TReader reader, out IpsoBotMessagePersistState state)
            where TReader : IReader
        {
            uint randomState = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "rng":
                        randomState = (uint)KeyedSerializer.ReadBoundedLong(reader, tag, "rng", 1, uint.MaxValue);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new IpsoBotMessagePersistState(randomState);
        }
    }
}
