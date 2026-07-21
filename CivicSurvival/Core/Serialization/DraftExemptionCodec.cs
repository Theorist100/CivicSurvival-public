using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct DraftExemptionPersistState
    {
        public DraftExemptionPersistState(int ratePercent, int disabilityHeadcount, int lastProcessedDay)
        {
            RatePercent = ratePercent;
            DisabilityHeadcount = disabilityHeadcount;
            LastProcessedDay = lastProcessedDay;
        }

        public int RatePercent { get; }
        public int DisabilityHeadcount { get; }
        public int LastProcessedDay { get; }
    }

    public static class DraftExemptionCodec
    {
        private static readonly int[] RatePresets = { 0, 15, 30, 50 };

        public static void Write<TWriter>(in DraftExemptionPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "ratePercent", state.RatePercent);
            KeyedSerializer.WriteField(writer, "disabilityHeadcount", state.DisabilityHeadcount);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
        }

        public static void Read<TReader>(TReader reader, out DraftExemptionPersistState state)
            where TReader : IReader
        {
            int ratePercent = 0;
            int disabilityHeadcount = 0;
            int lastProcessedDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "ratePercent":
                        ratePercent = KeyedSerializer.ReadBoundedInt(reader, tag, "ratePercent", 0, 100, 0);
                        break;
                    case "disabilityHeadcount":
                        disabilityHeadcount = KeyedSerializer.ReadBoundedInt(reader, tag, "disabilityHeadcount", 0, 100000, 0);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, 100000);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DraftExemptionPersistState(
                CodecMath.SnapToPreset(ratePercent, RatePresets),
                disabilityHeadcount,
                lastProcessedDay);
        }
    }
}
