using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct FuelSiphoningPersistState
    {
        public FuelSiphoningPersistState(int siphonPercent, int lastProcessedDay)
        {
            SiphonPercent = siphonPercent;
            LastProcessedDay = lastProcessedDay;
        }

        public int SiphonPercent { get; }
        public int LastProcessedDay { get; }
    }

    public static class FuelSiphoningCodec
    {
        private static readonly int[] SiphonPresets = { 0, 15, 30, 50 };

        public static void Write<TWriter>(in FuelSiphoningPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "siphonPercent", state.SiphonPercent);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
        }

        public static void Read<TReader>(TReader reader, out FuelSiphoningPersistState state)
            where TReader : IReader
        {
            int siphonPercent = 0;
            int lastProcessedDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "siphonPercent":
                        siphonPercent = KeyedSerializer.ReadBoundedInt(reader, tag, "siphonPercent", 0, 100, 0);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, 100000);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new FuelSiphoningPersistState(
                CodecMath.SnapToPreset(siphonPercent, SiphonPresets),
                lastProcessedDay);
        }
    }
}
