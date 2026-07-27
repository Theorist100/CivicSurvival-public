using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct ConstructionKickbackPersistState
    {
        public ConstructionKickbackPersistState(int kickbackPercent, long pendingPayout, int lastProcessedDay)
        {
            KickbackPercent = kickbackPercent;
            PendingPayout = pendingPayout;
            LastProcessedDay = lastProcessedDay;
        }

        public int KickbackPercent { get; }
        public long PendingPayout { get; }
        public int LastProcessedDay { get; }
    }

    public static class ConstructionKickbackCodec
    {
        private static readonly int[] KickbackPresets = { 0, 5, 10, 20 };

        public static void Write<TWriter>(in ConstructionKickbackPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "kickbackPercent", state.KickbackPercent);
            KeyedSerializer.WriteField(writer, "pendingPayout", state.PendingPayout);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
        }

        public static void Read<TReader>(TReader reader, out ConstructionKickbackPersistState state)
            where TReader : IReader
        {
            int kickbackPercent = 0;
            long pendingPayout = 0;
            int lastProcessedDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "kickbackPercent":
                        kickbackPercent = KeyedSerializer.ReadBoundedInt(reader, tag, "kickbackPercent", 0, 100, 0);
                        break;
                    case "pendingPayout":
                        pendingPayout = KeyedSerializer.ReadBoundedLong(reader, tag, "pendingPayout", 0, long.MaxValue);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, 100000);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ConstructionKickbackPersistState(
                CodecMath.SnapToPreset(kickbackPercent, KickbackPresets),
                pendingPayout,
                lastProcessedDay);
        }
    }
}
