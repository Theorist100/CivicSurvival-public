using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct ExodusPersistState
    {
        public ExodusPersistState(
            int totalExodus,
            float baseRatePercentPerDay,
            float effectiveRatePercentPerDay,
            uint randomState,
            int peakPopulation,
            Act lastProcessedAct,
            int lastProcessedDay)
        {
            TotalExodus = totalExodus;
            BaseRatePercentPerDay = baseRatePercentPerDay;
            EffectiveRatePercentPerDay = effectiveRatePercentPerDay;
            RandomState = randomState == 0u ? 1u : randomState;
            PeakPopulation = peakPopulation;
            LastProcessedAct = lastProcessedAct;
            LastProcessedDay = lastProcessedDay;
        }

        public int TotalExodus { get; }
        public float BaseRatePercentPerDay { get; }
        public float EffectiveRatePercentPerDay { get; }
        public uint RandomState { get; }
        public int PeakPopulation { get; }
        public Act LastProcessedAct { get; }
        public int LastProcessedDay { get; }
    }

    public static class ExodusCodec
    {
        public const float MaxExodusRate = 20f;

        public static void Write<TWriter>(in ExodusPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 7);
            KeyedSerializer.WriteField(writer, "totalExodus", state.TotalExodus);
            KeyedSerializer.WriteField(writer, "baseRatePercentPerDay", state.BaseRatePercentPerDay);
            KeyedSerializer.WriteField(writer, "effectiveRatePercentPerDay", state.EffectiveRatePercentPerDay);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((int)state.RandomState));
            KeyedSerializer.WriteField(writer, "peakPop", state.PeakPopulation);
            KeyedSerializer.WriteEnumIntField(writer, "lastProcessedAct", (int)state.LastProcessedAct);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
        }

        public static void Read<TReader>(TReader reader, out ExodusPersistState state)
            where TReader : IReader
        {
            int totalExodus = 0;
            float baseRatePercentPerDay = 0f;
            float effectiveRatePercentPerDay = 0f;
            uint randomState = 0;
            int peakPopulation = 0;
            Act lastProcessedAct = Act.PreWar;
            int lastProcessedDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "totalExodus":
                        totalExodus = KeyedSerializer.ReadBoundedInt(reader, tag, "totalExodus", 0, 10000000, 0);
                        break;
                    case "baseRatePercentPerDay":
                        baseRatePercentPerDay = KeyedSerializer.ReadSafeFloat(reader, tag, "baseRatePercentPerDay", 0f, MaxExodusRate, 0f);
                        break;
                    case "effectiveRatePercentPerDay":
                        effectiveRatePercentPerDay = KeyedSerializer.ReadSafeFloat(reader, tag, "effectiveRatePercentPerDay", 0f, MaxExodusRate, 0f);
                        break;
                    case "randomState":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "randomState"))
                            break;
                        reader.Read(out int rawRandomState);
                        randomState = unchecked((uint)rawRandomState);
                        break;
                    case "peakPop":
                        peakPopulation = KeyedSerializer.ReadBoundedInt(reader, tag, "peakPop", 0, 10000000, 0);
                        break;
                    case "lastProcessedAct":
                        lastProcessedAct = KeyedSerializer.ReadEnumInt<TReader, Act>(reader, tag, "lastProcessedAct", Act.PreWar);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadBoundedInt(reader, tag, "lastProcessedDay", -1, 100000, -1);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ExodusPersistState(
                totalExodus,
                baseRatePercentPerDay,
                effectiveRatePercentPerDay,
                randomState,
                peakPopulation,
                lastProcessedAct,
                lastProcessedDay);
        }
    }
}
