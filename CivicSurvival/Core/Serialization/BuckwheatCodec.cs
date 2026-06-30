using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct BuckwheatPersistState
    {
        public BuckwheatPersistState(
            float buckwheatTons,
            int procurementLevel,
            float lastProcurementHour,
            DistrictFloatEntry[] aidExpiry,
            DistrictFloatEntry[] lastDistributionTime)
        {
            BuckwheatTons = buckwheatTons;
            ProcurementLevel = procurementLevel;
            LastProcurementHour = lastProcurementHour;
            AidExpiry = aidExpiry ?? Array.Empty<DistrictFloatEntry>();
            LastDistributionTime = lastDistributionTime ?? Array.Empty<DistrictFloatEntry>();
        }

        public float BuckwheatTons { get; }
        public int ProcurementLevel { get; }
        public float LastProcurementHour { get; }
        public IReadOnlyList<DistrictFloatEntry> AidExpiry { get; }
        public IReadOnlyList<DistrictFloatEntry> LastDistributionTime { get; }
    }

    public static class BuckwheatCodec
    {
        // District keys ride the one unbounded contract; only the record COUNT is
        // capped. The former MaxDistrictIndex=500 dropped real-district aid on
        // BOTH write and read (Cluster A BuckwheatCodec).
        public const int MaxDistrictRecords = 10000;
        public const float MaxBuckwheatTons = 1000000f;
        public const float MaxSerializedTotalGameHours = 1000000f;
        public const int MaxProcurementLevel = 100;

        public static void Write<TWriter>(in BuckwheatPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 5);
            KeyedSerializer.WriteField(writer, "buckwheatTons", state.BuckwheatTons);
            KeyedSerializer.WriteField(writer, "procurementLevel", Math.Clamp(state.ProcurementLevel, 0, MaxProcurementLevel));
            KeyedSerializer.WriteField(writer, "lastProcurementHour", state.LastProcurementHour);
            WriteDistrictFloatMap(writer, "aidExpiry", state.AidExpiry);
            WriteDistrictFloatMap(writer, "distTime", state.LastDistributionTime);
        }

        public static void Read<TReader>(TReader reader, out BuckwheatPersistState state)
            where TReader : IReader
        {
            float buckwheatTons = 0f;
            int procurementLevel = 0;
            float lastProcurementHour = 0f;
            var aidExpiry = Array.Empty<DistrictFloatEntry>();
            var lastDistributionTime = Array.Empty<DistrictFloatEntry>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "buckwheatTons":
                        buckwheatTons = KeyedSerializer.ReadSafeFloat(reader, tag, "buckwheatTons", 0f, MaxBuckwheatTons, 0f);
                        break;
                    case "procurementLevel":
                        procurementLevel = KeyedSerializer.ReadBoundedInt(reader, tag, "procurementLevel", 0, MaxProcurementLevel, 0);
                        break;
                    case "lastProcurementHour":
                        lastProcurementHour = KeyedSerializer.ReadSafeFloat(reader, tag, "lastProcurementHour", 0f, MaxSerializedTotalGameHours, 0f);
                        break;
                    case "aidExpiry":
                        aidExpiry = BlackoutEventProducerCodec.ReadDistrictFloatMap(
                            reader,
                            tag,
                            "aidExpiry",
                            MaxDistrictRecords,
                            MaxSerializedTotalGameHours);
                        break;
                    case "distTime":
                        lastDistributionTime = BlackoutEventProducerCodec.ReadDistrictFloatMap(
                            reader,
                            tag,
                            "distTime",
                            MaxDistrictRecords,
                            MaxSerializedTotalGameHours);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new BuckwheatPersistState(
                buckwheatTons,
                procurementLevel,
                lastProcurementHour,
                aidExpiry,
                lastDistributionTime);
        }

        private static void WriteDistrictFloatMap<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictFloatEntry> entries)
            where TWriter : IWriter
        {
            int count = CountValidDistrictEntries(entries);
            KeyedSerializer.WriteBufferHeader(writer, key, count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictIndex(entries[i].DistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteDistrictKey(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "t", entries[i].Value);
            }
        }

        private static bool IsValidDistrictIndex(int districtIndex)
            => districtIndex >= 0;

        private static int CountValidDistrictEntries(IReadOnlyList<DistrictFloatEntry> entries)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictIndex(entries[i].DistrictIndex))
                    count++;
            }
            return count;
        }
    }
}
