using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct DistrictModernizationProgramPersistEntry
    {
        public DistrictModernizationProgramPersistEntry(
            int districtIndex,
            bool hasProgram,
            ContractorType contractor,
            int activationDay,
            int buildingCount,
            int totalCost,
            int kickbackEarned,
            int expectedKickback,
            int lastFireDay,
            int fireCount)
        {
            DistrictIndex = districtIndex;
            HasProgram = hasProgram;
            Contractor = contractor;
            ActivationDay = activationDay;
            BuildingCount = buildingCount;
            TotalCost = totalCost;
            KickbackEarned = kickbackEarned;
            ExpectedKickback = expectedKickback;
            LastFireDay = lastFireDay;
            FireCount = fireCount;
        }

        public int DistrictIndex { get; }
        public bool HasProgram { get; }
        public ContractorType Contractor { get; }
        public int ActivationDay { get; }
        public int BuildingCount { get; }
        public int TotalCost { get; }
        public int KickbackEarned { get; }
        public int ExpectedKickback { get; }
        public int LastFireDay { get; }
        public int FireCount { get; }
    }

    public readonly struct DistrictModernizationPersistState
    {
        // Phase J terminality note: in-flight modernization commit state is owned
        // by DistrictModernizationIntent plus ModernizationInstallReceipt ECS
        // components. This codec persists only already committed program state
        // and cleanup queues.
        // PendingCleanupBuildingKeys is no longer persisted: building identity keys are
        // derived state (pending districts × live CounterfeitBattery components) and the
        // packed Index+Version is not remapped on load, so persisting it desynced the
        // cleanup matcher across save/load. The set is rebuilt from live components after
        // load instead. District identity rides DistrictKeyCodec (engine entity remap);
        // building identity keys are rebuilt from live components after load.
        public DistrictModernizationPersistState(
            int lastProcurementDay,
            int lastProcessedDay,
            DistrictModernizationProgramPersistEntry[] programs,
            int[] pendingCleanupDistricts)
        {
            LastProcurementDay = lastProcurementDay;
            LastProcessedDay = lastProcessedDay;
            Programs = programs ?? Array.Empty<DistrictModernizationProgramPersistEntry>();
            PendingCleanupDistricts = pendingCleanupDistricts ?? Array.Empty<int>();
        }

        public int LastProcurementDay { get; }
        public int LastProcessedDay { get; }
        public IReadOnlyList<DistrictModernizationProgramPersistEntry> Programs { get; }
        public IReadOnlyList<int> PendingCleanupDistricts { get; }
    }

    public static class DistrictModernizationCodec
    {
        public const int MaxProgramRecords = 256;
        public const int MaxPendingCleanupDistricts = 256;
        private const int ProgramBlockFieldCount = 9 + DistrictKeyCodec.FIELDS_PER_KEY;

        public static void Write<TWriter>(in DistrictModernizationPersistState state, TWriter writer, in LiveDistrictMap map)
            where TWriter : IWriter
        {
            int stale = 0;
            KeyedSerializer.WriteBlockHeader(writer, 4);
            KeyedSerializer.WriteField(writer, "lastProcurementDay", state.LastProcurementDay);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
            WritePrograms(writer, state.Programs, map, ref stale);
            WritePendingCleanupDistricts(writer, state.PendingCleanupDistricts, map, ref stale);
            DistrictKeyCodec.LogWrite("DistrictModernizationCodec", state.Programs.Count + state.PendingCleanupDistricts.Count, stale);
        }

        public static void Read<TReader>(TReader reader, int initialProcurementDay, out DistrictModernizationPersistState state)
            where TReader : IReader
        {
            int lastProcurementDay = initialProcurementDay;
            int lastProcessedDay = 0;
            var programs = Array.Empty<DistrictModernizationProgramPersistEntry>();
            var pendingCleanupDistricts = Array.Empty<int>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastProcurementDay":
                        lastProcurementDay = KeyedSerializer.ReadBoundedInt(reader, tag, "lastProcurementDay", initialProcurementDay, 100000, initialProcurementDay);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, 100000);
                        break;
                    case "programs":
                        programs = ReadPrograms(reader, tag);
                        break;
                    case "pendCleanup":
                        pendingCleanupDistricts = ReadPendingCleanupDistricts(reader, tag);
                        break;
                    // "pendCleanupKeys" (legacy raw-long building keys) intentionally has
                    // no case: it falls through to default Skip. The set is now rebuilt
                    // from live components after load instead of persisted.
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DistrictModernizationPersistState(
                lastProcurementDay,
                lastProcessedDay,
                programs,
                pendingCleanupDistricts);
        }

        private static void WritePrograms<TWriter>(TWriter writer, IReadOnlyList<DistrictModernizationProgramPersistEntry> programs, in LiveDistrictMap map, ref int stale)
            where TWriter : IWriter
        {
            int count = CountValidPrograms(programs);
            KeyedSerializer.WriteBufferHeader(writer, "programs", count);
            for (int i = 0; i < programs.Count; i++)
            {
                var p = programs[i];
                if (!IsValidProgramDistrict(p.DistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, ProgramBlockFieldCount);
                DistrictKeyCodec.Write(writer, "de", p.DistrictIndex, map, ref stale);
                KeyedSerializer.WriteField(writer, "has", p.HasProgram);
                KeyedSerializer.WriteEnumByteField(writer, "con", (byte)p.Contractor);
                KeyedSerializer.WriteField(writer, "actDay", p.ActivationDay);
                KeyedSerializer.WriteField(writer, "bCnt", p.BuildingCount);
                KeyedSerializer.WriteField(writer, "cost", p.TotalCost);
                KeyedSerializer.WriteField(writer, "kick", p.KickbackEarned);
                KeyedSerializer.WriteField(writer, "expKick", p.ExpectedKickback);
                KeyedSerializer.WriteField(writer, "fireDay", p.LastFireDay);
                KeyedSerializer.WriteField(writer, "fireCnt", p.FireCount);
            }
        }

        private static DistrictModernizationProgramPersistEntry[] ReadPrograms<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "programs", MaxProgramRecords);
            var entries = new DistrictModernizationProgramPersistEntry[count];
            int written = 0;
            int stale = 0;
            for (int i = 0; i < count; i++)
            {
                byte districtKind = 0;
                Entity districtEntity = Entity.Null;
                bool hasProgram = false;
                var contractor = ContractorType.None;
                int activationDay = 0;
                int buildingCount = 0;
                int totalCost = 0;
                int kickbackEarned = 0;
                int expectedKickback = 0;
                int lastFireDay = 0;
                int fireCount = 0;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "deK")
                    {
                        districtKind = DistrictKeyCodec.ReadKind(reader, fieldTag, "de");
                    }
                    else if (fieldKey == "de")
                    {
                        districtEntity = DistrictKeyCodec.ReadEntity(reader, fieldTag, "de");
                    }
                    else if (fieldKey == "has")
                    {
                        hasProgram = KeyedSerializer.ReadBool(reader, fieldTag, "has");
                    }
                    else if (fieldKey == "con")
                    {
                        contractor = KeyedSerializer.ReadEnumByte<TReader, ContractorType>(reader, fieldTag, "con", ContractorType.None);
                    }
                    else if (fieldKey == "actDay")
                    {
                        activationDay = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "actDay", 0, 100000, 0);
                    }
                    else if (fieldKey == "bCnt")
                    {
                        buildingCount = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "bCnt", 0, 10000, 0);
                    }
                    else if (fieldKey == "cost")
                    {
                        totalCost = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "cost", 0, int.MaxValue, 0);
                    }
                    else if (fieldKey == "kick")
                    {
                        kickbackEarned = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "kick", 0, int.MaxValue, 0);
                    }
                    else if (fieldKey == "expKick")
                    {
                        expectedKickback = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "expKick", 0, int.MaxValue, 0);
                    }
                    else if (fieldKey == "fireDay")
                    {
                        lastFireDay = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "fireDay", 0, 100000, 0);
                    }
                    else if (fieldKey == "fireCnt")
                    {
                        fireCount = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "fireCnt", 0, 10000, 0);
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                var result = DistrictKeyCodec.Resolve(districtKind, districtEntity, out var live);
                if (result == DistrictKeyReadResult.None)
                {
                    stale++;
                    continue;
                }

                int district = result == DistrictKeyReadResult.Unzoned ? DistrictUtils.UNZONED_AREA_INDEX : live.Index;
                entries[written++] = new DistrictModernizationProgramPersistEntry(
                    district,
                    hasProgram,
                    contractor,
                    activationDay,
                    buildingCount,
                    totalCost,
                    kickbackEarned,
                    expectedKickback,
                    lastFireDay,
                    fireCount);
            }

            DistrictKeyCodec.LogRead("DistrictModernizationCodec.Programs", written, stale);
            return Compact(entries, written);
        }

        private static void WritePendingCleanupDistricts<TWriter>(TWriter writer, IReadOnlyList<int> districtIndexes, in LiveDistrictMap map, ref int stale)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, "pendCleanup", districtIndexes.Count);
            for (int i = 0; i < districtIndexes.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "de", districtIndexes[i], map, ref stale);
            }
        }

        private static int[] ReadPendingCleanupDistricts<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "pendCleanup", MaxPendingCleanupDistricts);
            var entries = new int[count];
            int written = 0;
            int stale = 0;
            for (int i = 0; i < count; i++)
            {
                byte districtKind = 0;
                Entity districtEntity = Entity.Null;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "deK")
                        districtKind = DistrictKeyCodec.ReadKind(reader, fieldTag, "de");
                    else if (fieldKey == "de")
                        districtEntity = DistrictKeyCodec.ReadEntity(reader, fieldTag, "de");
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var result = DistrictKeyCodec.Resolve(districtKind, districtEntity, out var live);
                if (result == DistrictKeyReadResult.None)
                {
                    stale++;
                    continue;
                }

                entries[written++] = result == DistrictKeyReadResult.Unzoned ? DistrictUtils.UNZONED_AREA_INDEX : live.Index;
            }

            DistrictKeyCodec.LogRead("DistrictModernizationCodec.PendingCleanup", written, stale);
            return Compact(entries, written);
        }

        private static bool IsValidProgramDistrict(int districtIndex)
            => districtIndex >= 0;

        private static int CountValidPrograms(IReadOnlyList<DistrictModernizationProgramPersistEntry> programs)
        {
            int count = 0;
            for (int i = 0; i < programs.Count; i++)
            {
                if (IsValidProgramDistrict(programs[i].DistrictIndex))
                    count++;
            }
            return count;
        }

        private static DistrictModernizationProgramPersistEntry[] Compact(DistrictModernizationProgramPersistEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictModernizationProgramPersistEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }

        private static int[] Compact(int[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new int[count];
            Array.Copy(entries, compact, count);
            return compact;
        }
    }
}
