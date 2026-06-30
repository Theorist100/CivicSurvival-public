using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct SpotterSpawnPersistState
    {
        public SpotterSpawnPersistState(float spawnTimer)
            => SpawnTimer = ClampSpawnTimer(spawnTimer);

        public float SpawnTimer { get; }

        private static float ClampSpawnTimer(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                return 0f;
            }

            float max = SpotterSpawnCodec.MaxSpawnTimerSeconds;
            return value > max ? max : value;
        }
    }

    public static class SpotterSpawnCodec
    {
        internal const float MaxSpawnTimerSeconds = 30f * GameRate.SECONDS_PER_DAY;

        public static void Write<TWriter>(in SpotterSpawnPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "m_SpawnTimer", state.SpawnTimer);
        }

        public static void Read<TReader>(TReader reader, out SpotterSpawnPersistState state)
            where TReader : IReader
        {
            float spawnTimer = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_SpawnTimer":
                        spawnTimer = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_SpawnTimer", 0f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new SpotterSpawnPersistState(spawnTimer);
        }
    }

    public readonly struct SpotterAggregatePersistState
    {
        public SpotterAggregatePersistState(
            double lastDailyTick,
            int totalSbuVisits,
            int totalEvacuations,
            bool counterOsintActive,
            uint randomState,
            int[]? internetDisabledDistricts,
            double[]? evacuatedReturnTimes)
        {
            LastDailyTick = double.IsNaN(lastDailyTick) || double.IsInfinity(lastDailyTick) ? 0.0 : lastDailyTick;
            TotalSbuVisits = totalSbuVisits < 0 ? 0 : totalSbuVisits;
            TotalEvacuations = totalEvacuations < 0 ? 0 : totalEvacuations;
            CounterOsintActive = counterOsintActive;
            RandomState = randomState;
            InternetDisabledDistricts = internetDisabledDistricts ?? System.Array.Empty<int>();
            EvacuatedReturnTimes = evacuatedReturnTimes ?? System.Array.Empty<double>();
        }

        public double LastDailyTick { get; }
        public int TotalSbuVisits { get; }
        public int TotalEvacuations { get; }
        public bool CounterOsintActive { get; }
        public uint RandomState { get; }
        public IReadOnlyList<int> InternetDisabledDistricts { get; }
        public IReadOnlyList<double> EvacuatedReturnTimes { get; }
    }

    public static class SpotterAggregateCodec
    {
        internal const int MaxSpotterBufferRecords = 256;

        public static void Write<TWriter>(in SpotterAggregatePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            int internetDisabledSkipped = CountSkipped(state.InternetDisabledDistricts.Count);
            int evacuatedReturnSkipped = CountSkipped(state.EvacuatedReturnTimes.Count);
            KeyedSerializer.WriteBlockHeader(writer, 9);
            KeyedSerializer.WriteField(writer, "lastDailyTick", state.LastDailyTick);
            KeyedSerializer.WriteField(writer, "totalSBUVisits", state.TotalSbuVisits);
            KeyedSerializer.WriteField(writer, "totalEvacuations", state.TotalEvacuations);
            KeyedSerializer.WriteField(writer, "counterOSINTActive", state.CounterOsintActive);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((int)state.RandomState));
            WriteInternetDisabled(writer, state.InternetDisabledDistricts);
            KeyedSerializer.WriteField(writer, "internetDisabledSkipped", internetDisabledSkipped);
            WriteEvacuatedReturns(writer, state.EvacuatedReturnTimes);
            KeyedSerializer.WriteField(writer, "evacuatedReturnSkipped", evacuatedReturnSkipped);
        }

        public static void Read<TReader>(TReader reader, out SpotterAggregatePersistState state)
            where TReader : IReader
        {
            double lastDailyTick = 0.0;
            int totalSbuVisits = 0;
            int totalEvacuations = 0;
            bool counterOsintActive = false;
            uint randomState = 0;
            int[] internetDisabledDistricts = System.Array.Empty<int>();
            double[] evacuatedReturnTimes = System.Array.Empty<double>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastDailyTick":
                        lastDailyTick = KeyedSerializer.ReadSafeDouble(reader, tag, "lastDailyTick", 0.0);
                        break;
                    case "totalSBUVisits":
                        totalSbuVisits = KeyedSerializer.ReadBoundedInt(reader, tag, "totalSBUVisits", 0, int.MaxValue, 0);
                        break;
                    case "totalEvacuations":
                        totalEvacuations = KeyedSerializer.ReadBoundedInt(reader, tag, "totalEvacuations", 0, int.MaxValue, 0);
                        break;
                    case "counterOSINTActive":
                        counterOsintActive = KeyedSerializer.ReadBool(reader, tag, "counterOSINTActive");
                        break;
                    case "randomState":
                        randomState = ReadRandomState(reader, tag);
                        break;
                    case "internetDisabled":
                        internetDisabledDistricts = ReadInternetDisabled(reader, tag);
                        break;
                    case "evacuatedReturn":
                        evacuatedReturnTimes = ReadEvacuatedReturns(reader, tag);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new SpotterAggregatePersistState(
                lastDailyTick,
                totalSbuVisits,
                totalEvacuations,
                counterOsintActive,
                randomState,
                internetDisabledDistricts,
                evacuatedReturnTimes);
        }

        private static uint ReadRandomState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "randomState"))
            {
                return 0;
            }

            reader.Read(out int rawState);
            return unchecked((uint)rawState);
        }

        private static void WriteInternetDisabled<TWriter>(TWriter writer, IReadOnlyList<int> districts)
            where TWriter : IWriter
        {
            int count = CountWritable(districts.Count);
            KeyedSerializer.WriteBufferHeader(writer, "internetDisabled", count);
            for (int i = 0; i < count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "d", districts[i]);
            }
        }

        private static int[] ReadInternetDisabled<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "internetDisabled", MaxSpotterBufferRecords);
            var districts = new int[count];
            for (int i = 0; i < count; i++)
            {
                int district = 0;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "d":
                            district = KeyedSerializer.ReadInt(reader, fieldTag, "d");
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                districts[i] = district;
            }

            return districts;
        }

        private static void WriteEvacuatedReturns<TWriter>(TWriter writer, IReadOnlyList<double> returnTimes)
            where TWriter : IWriter
        {
            int count = CountWritable(returnTimes.Count);
            KeyedSerializer.WriteBufferHeader(writer, "evacuatedReturn", count);
            for (int i = 0; i < count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "t", returnTimes[i]);
            }
        }

        private static double[] ReadEvacuatedReturns<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "evacuatedReturn", MaxSpotterBufferRecords);
            var returnTimes = new double[count];
            for (int i = 0; i < count; i++)
            {
                double returnTime = 0.0;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "t":
                            returnTime = KeyedSerializer.ReadDouble(reader, fieldTag, "t");
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                returnTimes[i] = returnTime;
            }

            return returnTimes;
        }

        private static int CountWritable(int count)
            => count > MaxSpotterBufferRecords ? MaxSpotterBufferRecords : count;

        private static int CountSkipped(int count)
            => count > MaxSpotterBufferRecords ? count - MaxSpotterBufferRecords : 0;
    }
}
