using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public static class ResetOnlyStateCodec
    {
        public static void Write<TWriter>(TWriter writer)
            where TWriter : IWriter
            => KeyedSerializer.WriteBlockHeader(writer, 0);

        public static void Read<TReader>(TReader reader)
            where TReader : IReader
        {
            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out _);
                KeyedSerializer.Skip(reader, tag);
            }
        }
    }
}
