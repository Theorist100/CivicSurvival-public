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
            DistrictFloatEntry[] lastDistributionTime,
            int[] pendingExpiryPenalties)
        {
            BuckwheatTons = buckwheatTons;
            ProcurementLevel = procurementLevel;
            LastProcurementHour = lastProcurementHour;
            AidExpiry = aidExpiry ?? Array.Empty<DistrictFloatEntry>();
            LastDistributionTime = lastDistributionTime ?? Array.Empty<DistrictFloatEntry>();
            PendingExpiryPenalties = pendingExpiryPenalties ?? Array.Empty<int>();
        }

        public float BuckwheatTons { get; }
        public int ProcurementLevel { get; }
        public float LastProcurementHour { get; }
        public IReadOnlyList<DistrictFloatEntry> AidExpiry { get; }
        public IReadOnlyList<DistrictFloatEntry> LastDistributionTime { get; }

        /// <summary>
        /// Districts whose food-aid effect expired while the PenaltyRequest pipeline was not ready:
        /// the aided status is already gone, only the FoodAid penalty removal is still undelivered.
        /// Persisted because the applied per-district penalty itself persists
        /// (DistrictSerializationData.Penalties) — dropping this queue on load would leave the
        /// happiness bonus applied with no removal ever coming.
        /// </summary>
        public IReadOnlyList<int> PendingExpiryPenalties { get; }
    }

    public static class BuckwheatCodec
    {
        // District identity rides DistrictKeyCodec (engine entity remap); a raw index does not
        // survive load. Only the record COUNT is capped. The former MaxDistrictIndex=500 dropped
        // real-district aid on BOTH write and read (Cluster A BuckwheatCodec).
        public const int MaxDistrictRecords = 10000;
        public const float MaxBuckwheatTons = 1000000f;
        // Absolute game-hour timestamps: NaN/Inf/negative-guard only, no upper cap — a timestamp
        // is not a duration; capping it would collapse a legitimate late-game value to default 0.
        public const float MaxSerializedTotalGameHours = float.MaxValue;
        public const int MaxProcurementLevel = 100;

        public static void Write<TWriter>(in BuckwheatPersistState state, TWriter writer, in LiveDistrictMap map)
            where TWriter : IWriter
        {
            int stale = 0;
            KeyedSerializer.WriteBlockHeader(writer, 6);
            KeyedSerializer.WriteField(writer, "buckwheatTons", state.BuckwheatTons);
            KeyedSerializer.WriteField(writer, "procurementLevel", Math.Clamp(state.ProcurementLevel, 0, MaxProcurementLevel));
            KeyedSerializer.WriteField(writer, "lastProcurementHour", state.LastProcurementHour);
            WriteDistrictFloatMap(writer, "aidExpiry", state.AidExpiry, map, ref stale);
            WriteDistrictFloatMap(writer, "distTime", state.LastDistributionTime, map, ref stale);
            BlackoutEventProducerCodec.WriteDistrictSet(writer, "pendingExpiry", state.PendingExpiryPenalties, map, ref stale);
            DistrictKeyCodec.LogWrite("BuckwheatCodec", state.AidExpiry.Count + state.LastDistributionTime.Count + state.PendingExpiryPenalties.Count, stale);
        }

        public static void Read<TReader>(TReader reader, out BuckwheatPersistState state)
            where TReader : IReader
        {
            float buckwheatTons = 0f;
            int procurementLevel = 0;
            float lastProcurementHour = 0f;
            var aidExpiry = Array.Empty<DistrictFloatEntry>();
            var lastDistributionTime = Array.Empty<DistrictFloatEntry>();
            var pendingExpiryPenalties = Array.Empty<int>();

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
                            MaxDistrictRecords);
                        break;
                    case "distTime":
                        lastDistributionTime = BlackoutEventProducerCodec.ReadDistrictFloatMap(
                            reader,
                            tag,
                            "distTime",
                            MaxDistrictRecords);
                        break;
                    case "pendingExpiry":
                        pendingExpiryPenalties = BlackoutEventProducerCodec.ReadDistrictSet(
                            reader,
                            tag,
                            "pendingExpiry",
                            MaxDistrictRecords);
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
                lastDistributionTime,
                pendingExpiryPenalties);
        }

        private static void WriteDistrictFloatMap<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictFloatEntry> entries, in LiveDistrictMap map, ref int stale)
            where TWriter : IWriter
        {
            int count = CountValidDistrictEntries(entries);
            KeyedSerializer.WriteBufferHeader(writer, key, count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictIndex(entries[i].DistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 1 + DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "de", entries[i].DistrictIndex, map, ref stale);
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
