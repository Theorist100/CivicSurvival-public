using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.Power;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Persisted form of the 24-hour demand-peak ring (<see cref="DemandPeakSingleton"/> +
    /// <see cref="DemandPeakBucket"/> buffer). 24 hourly maxima + cursor + last-sample game-hour.
    /// </summary>
    public readonly struct DemandPeakPersistState
    {
        public DemandPeakPersistState(int[] hourlyPeakKW, int cursorHour, double lastSampleGameHours)
        {
            HourlyPeakKW = NormaliseBuckets(hourlyPeakKW);
            CursorHour = cursorHour;
            LastSampleGameHours = lastSampleGameHours;
        }

        /// <summary>Exactly <see cref="DemandPeakSingleton.BUCKETS"/> hourly maxima, kW. Exposed as a
        /// read-only list (CA1819: never hand a mutable array out of a struct property — mirrors
        /// <see cref="BuckwheatPersistState"/>).</summary>
        public IReadOnlyList<int> HourlyPeakKW { get; }

        public int CursorHour { get; }

        public double LastSampleGameHours { get; }

        private static int[] NormaliseBuckets(int[] source)
        {
            var result = new int[DemandPeakSingleton.BUCKETS];
            if (source == null)
                return result;
            int n = Math.Min(source.Length, DemandPeakSingleton.BUCKETS);
            for (int i = 0; i < n; i++)
                result[i] = source[i];
            return result;
        }
    }

    public static class DemandPeakCodec
    {
        /// <summary>Sanity ceiling for one hourly demand bucket (kW). 100 GW — far above any city;
        /// clamps garbage out of a corrupted save without coercing a real value.</summary>
        public const int MaxDemandKW = 100_000_000;

        /// <summary>Upper bound for the persisted game-hour timestamp (shared convention with
        /// <see cref="BuckwheatCodec.MaxSerializedTotalGameHours"/>).</summary>
        public const double MaxSerializedTotalGameHours = 1_000_000.0;

        public static void Write<TWriter>(in DemandPeakPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            // 3 fields: buckets buffer, cursor, lastSample.
            KeyedSerializer.WriteBlockHeader(writer, 3);

            KeyedSerializer.WriteBufferHeader(writer, "buckets", DemandPeakSingleton.BUCKETS);
            for (int i = 0; i < DemandPeakSingleton.BUCKETS; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "kw", state.HourlyPeakKW[i]);
            }

            KeyedSerializer.WriteField(writer, "cursor", state.CursorHour);
            KeyedSerializer.WriteField(writer, "lastSample", state.LastSampleGameHours);
        }

        public static void Read<TReader>(TReader reader, out DemandPeakPersistState state)
            where TReader : IReader
        {
            var buckets = new int[DemandPeakSingleton.BUCKETS];
            int cursorHour = 0;
            double lastSampleGameHours = 0.0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "buckets":
                        ReadBuckets(reader, tag, buckets);
                        break;
                    case "cursor":
                        cursorHour = KeyedSerializer.ReadBoundedInt(reader, tag, "cursor", 0, DemandPeakSingleton.BUCKETS - 1, 0);
                        break;
                    case "lastSample":
                        lastSampleGameHours = KeyedSerializer.ReadSafeDouble(reader, tag, "lastSample", 0.0, MaxSerializedTotalGameHours, 0.0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DemandPeakPersistState(buckets, cursorHour, lastSampleGameHours);
        }

        private static void ReadBuckets<TReader>(TReader reader, TypeTag tag, int[] buckets)
            where TReader : IReader
        {
            // Bounded by BUCKETS — a corrupted larger count throws (stream integrity), a smaller
            // count leaves the tail at the zero default.
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "buckets", DemandPeakSingleton.BUCKETS);
            for (int i = 0; i < count; i++)
            {
                int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                int value = 0;
                for (int f = 0; f < fc; f++)
                {
                    var elemTag = KeyedSerializer.ReadFieldHeader(reader, out var elemKey);
                    if (elemKey == "kw")
                        value = KeyedSerializer.ReadBoundedInt(reader, elemTag, "kw", 0, MaxDemandKW, 0);
                    else
                        KeyedSerializer.Skip(reader, elemTag);
                }
                if (i < buckets.Length)
                    buckets[i] = value;
            }
        }
    }
}
