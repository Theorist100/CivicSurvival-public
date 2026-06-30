using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct DistrictScheduleEntry
    {
        public DistrictScheduleEntry(DistrictRef district, int schedule)
        {
            District = district;
            Schedule = schedule;
        }

        public DistrictRef District { get; }
        public int DistrictIndex => District.Index;
        public int DistrictVersion => District.Version;
        public int Schedule { get; }
    }

    public readonly struct DistrictCategoriesEntry
    {
        public DistrictCategoriesEntry(DistrictRef district, int[] categories)
        {
            District = district;
            Categories = categories ?? Array.Empty<int>();
        }

        public DistrictRef District { get; }
        public int DistrictIndex => District.Index;
        public int DistrictVersion => District.Version;
        public IReadOnlyList<int> Categories { get; }
    }

    public readonly struct DistrictRefEntry
    {
        public DistrictRefEntry(DistrictRef district)
        {
            District = district;
        }

        public DistrictRef District { get; }
        public int DistrictIndex => District.Index;
        public int DistrictVersion => District.Version;
    }

    public readonly struct DistrictPenaltyPersistEntry
    {
        public DistrictPenaltyPersistEntry(DistrictRef district, int activeSources, float happinessPenalty, float commercePenalty)
        {
            District = district;
            ActiveSources = SanitizePenaltySources(activeSources);
            HappinessPenalty = happinessPenalty;
            CommercePenalty = commercePenalty;
        }

        public DistrictRef District { get; }
        public int DistrictIndex => District.Index;
        public int DistrictVersion => District.Version;
        public int ActiveSources { get; }
        public float HappinessPenalty { get; }
        public float CommercePenalty { get; }

        private static int SanitizePenaltySources(int value)
            => value & BlackoutStateCodec.AllPenaltySourceFlags;
    }

    public readonly struct PreShedPersistEntry
    {
        public PreShedPersistEntry(
            DistrictRef district,
            int schedule,
            bool wasVip,
            bool hadExplicitSchedule,
            int[] categoriesOff,
            bool wasVipBypass = false)
        {
            District = district;
            Schedule = schedule;
            WasVip = wasVip;
            HadExplicitSchedule = hadExplicitSchedule;
            CategoriesOff = categoriesOff ?? Array.Empty<int>();
            WasVipBypass = wasVipBypass;
        }

        public DistrictRef District { get; }
        public int DistrictIndex => District.Index;
        public int DistrictVersion => District.Version;
        public int Schedule { get; }
        public bool WasVip { get; }
        public bool HadExplicitSchedule { get; }
        public bool WasVipBypass { get; }
        public IReadOnlyList<int> CategoriesOff { get; }
    }

    public readonly struct DistrictPriorityEntry
    {
        public DistrictPriorityEntry(DistrictRef district, int priority)
        {
            District = district;
            Priority = priority;
        }

        public DistrictRef District { get; }
        public int DistrictIndex => District.Index;
        public int DistrictVersion => District.Version;
        public int Priority { get; }
    }

    public readonly struct BlackoutPersistState
    {
        public BlackoutPersistState(
            int citySchedule,
            DistrictScheduleEntry[] districtOverrides,
            DistrictCategoriesEntry[] blackouts,
            DistrictRefEntry[] vips,
            DistrictRefEntry[] vipBypass,
            DistrictPenaltyPersistEntry[] penalties,
            PreShedPersistEntry[] preShedStates,
            DistrictPriorityEntry[] priorities)
        {
            CitySchedule = IsValidSchedule(citySchedule) ? citySchedule : 0;
            DistrictOverrides = districtOverrides ?? Array.Empty<DistrictScheduleEntry>();
            Blackouts = blackouts ?? Array.Empty<DistrictCategoriesEntry>();
            Vips = vips ?? Array.Empty<DistrictRefEntry>();
            VipBypass = vipBypass ?? Array.Empty<DistrictRefEntry>();
            Penalties = penalties ?? Array.Empty<DistrictPenaltyPersistEntry>();
            PreShedStates = preShedStates ?? Array.Empty<PreShedPersistEntry>();
            Priorities = priorities ?? Array.Empty<DistrictPriorityEntry>();
        }

        public int CitySchedule { get; }
        public IReadOnlyList<DistrictScheduleEntry> DistrictOverrides { get; }
        public IReadOnlyList<DistrictCategoriesEntry> Blackouts { get; }
        public IReadOnlyList<DistrictRefEntry> Vips { get; }
        public IReadOnlyList<DistrictRefEntry> VipBypass { get; }
        public IReadOnlyList<DistrictPenaltyPersistEntry> Penalties { get; }
        public IReadOnlyList<PreShedPersistEntry> PreShedStates { get; }
        public IReadOnlyList<DistrictPriorityEntry> Priorities { get; }

        internal static bool IsValidSchedule(int value)
            => value >= 0 && value <= 4;
    }

    public static class BlackoutStateCodec
    {
        public const int MaxDistrictRecords = 10000;
        public const int MaxCategoryRecords = 64;
        public const int AllPenaltySourceFlags = 8191;
        public const int LegacyDistrictVersion = -1;

        public static void Write<TWriter>(in BlackoutPersistState state, int maxDistrictIndex, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 8);
            KeyedSerializer.WriteEnumIntField(writer, "citySchedule", state.CitySchedule);
            WriteDistrictOverrides(writer, state.DistrictOverrides, maxDistrictIndex);
            WriteBlackouts(writer, state.Blackouts, maxDistrictIndex);
            WriteDistrictRefSet(writer, "vips", state.Vips, maxDistrictIndex);
            WriteDistrictRefSet(writer, "vipBypass", state.VipBypass, maxDistrictIndex);
            WritePenalties(writer, state.Penalties, maxDistrictIndex);
            WritePreShedStates(writer, state.PreShedStates, maxDistrictIndex);
            WritePriorities(writer, state.Priorities, maxDistrictIndex);
        }

        public static void Read<TReader>(TReader reader, int maxDistrictIndex, out BlackoutPersistState state)
            where TReader : IReader
        {
            int citySchedule = 0;
            var districtOverrides = Array.Empty<DistrictScheduleEntry>();
            var blackouts = Array.Empty<DistrictCategoriesEntry>();
            var vips = Array.Empty<DistrictRefEntry>();
            var vipBypass = Array.Empty<DistrictRefEntry>();
            var penalties = Array.Empty<DistrictPenaltyPersistEntry>();
            var preShedStates = Array.Empty<PreShedPersistEntry>();
            var priorities = Array.Empty<DistrictPriorityEntry>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "citySchedule":
                        citySchedule = ReadScheduleOrDefault(reader, tag, "citySchedule", 0);
                        break;
                    case "districtOverrides":
                        districtOverrides = ReadDistrictOverrides(reader, tag, maxDistrictIndex);
                        break;
                    case "blackouts":
                        blackouts = ReadBlackouts(reader, tag, maxDistrictIndex);
                        break;
                    case "vips":
                        vips = ReadDistrictRefSet(reader, tag, "vips", maxDistrictIndex);
                        break;
                    case "vipBypass":
                        vipBypass = ReadDistrictRefSet(reader, tag, "vipBypass", maxDistrictIndex);
                        break;
                    case "penalties":
                        penalties = ReadPenalties(reader, tag, maxDistrictIndex);
                        break;
                    case "preShed":
                        preShedStates = ReadPreShedStates(reader, tag, maxDistrictIndex);
                        break;
                    case "priorities":
                        priorities = ReadPriorities(reader, tag, maxDistrictIndex);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new BlackoutPersistState(
                citySchedule,
                districtOverrides,
                blackouts,
                vips,
                vipBypass,
                penalties,
                preShedStates,
                priorities);
        }

        private static void WriteDistrictOverrides<TWriter>(TWriter writer, IReadOnlyList<DistrictScheduleEntry> entries, int maxDistrictIndex)
            where TWriter : IWriter
        {
            int count = CountValidDistrictSchedules(entries, maxDistrictIndex);
            KeyedSerializer.WriteBufferHeader(writer, "districtOverrides", count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictRef(entries[i].District, maxDistrictIndex) || !BlackoutPersistState.IsValidSchedule(entries[i].Schedule))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].DistrictVersion);
                KeyedSerializer.WriteEnumIntField(writer, "p", entries[i].Schedule);
            }
        }

        private static void WriteBlackouts<TWriter>(TWriter writer, IReadOnlyList<DistrictCategoriesEntry> entries, int maxDistrictIndex)
            where TWriter : IWriter
        {
            int count = CountValidDistrictCategoryEntries(entries, maxDistrictIndex);
            KeyedSerializer.WriteBufferHeader(writer, "blackouts", count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].DistrictVersion);
                WriteCategories(writer, "cats", entries[i].Categories);
            }
        }

        private static void WriteDistrictRefSet<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictRefEntry> entries, int maxDistrictIndex)
            where TWriter : IWriter
        {
            int count = CountValidDistrictRefs(entries, maxDistrictIndex);
            KeyedSerializer.WriteBufferHeader(writer, key, count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].DistrictVersion);
            }
        }

        private static void WritePenalties<TWriter>(TWriter writer, IReadOnlyList<DistrictPenaltyPersistEntry> entries, int maxDistrictIndex)
            where TWriter : IWriter
        {
            int count = CountValidPenaltyEntries(entries, maxDistrictIndex);
            KeyedSerializer.WriteBufferHeader(writer, "penalties", count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].DistrictVersion);
                KeyedSerializer.WriteEnumIntField(writer, "src", entries[i].ActiveSources);
                KeyedSerializer.WriteField(writer, "hap", entries[i].HappinessPenalty);
                KeyedSerializer.WriteField(writer, "com", entries[i].CommercePenalty);
            }
        }

        private static void WritePreShedStates<TWriter>(TWriter writer, IReadOnlyList<PreShedPersistEntry> entries, int maxDistrictIndex)
            where TWriter : IWriter
        {
            int count = CountValidPreShedEntries(entries, maxDistrictIndex);
            KeyedSerializer.WriteBufferHeader(writer, "preShed", count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictRef(entries[i].District, maxDistrictIndex) || !BlackoutPersistState.IsValidSchedule(entries[i].Schedule))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteField(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].DistrictVersion);
                KeyedSerializer.WriteEnumIntField(writer, "sch", entries[i].Schedule);
                KeyedSerializer.WriteField(writer, "vip", entries[i].WasVip);
                KeyedSerializer.WriteField(writer, "exp", entries[i].HadExplicitSchedule);
                KeyedSerializer.WriteField(writer, "vbp", entries[i].WasVipBypass);
                WriteCategories(writer, "cats", entries[i].CategoriesOff);
            }
        }

        private static void WritePriorities<TWriter>(TWriter writer, IReadOnlyList<DistrictPriorityEntry> entries, int maxDistrictIndex)
            where TWriter : IWriter
        {
            int count = CountValidPriorityEntries(entries, maxDistrictIndex);
            KeyedSerializer.WriteBufferHeader(writer, "priorities", count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].DistrictVersion);
                KeyedSerializer.WriteField(writer, "p", entries[i].Priority);
            }
        }

        private static void WriteCategories<TWriter>(TWriter writer, string key, IReadOnlyList<int> categories)
            where TWriter : IWriter
        {
            int count = CountValidCategories(categories);
            KeyedSerializer.WriteBufferHeader(writer, key, count);
            for (int i = 0; i < categories.Count; i++)
            {
                if (!IsValidCategory(categories[i]))
                    continue;

                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteEnumIntField(writer, "c", categories[i]);
            }
        }

        private static DistrictScheduleEntry[] ReadDistrictOverrides<TReader>(TReader reader, TypeTag tag, int maxDistrictIndex)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "districtOverrides", MaxDistrictRecords);
            var entries = new DistrictScheduleEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int version = LegacyDistrictVersion;
                int schedule = 0;
                bool hasSchedule = false;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                        district = ReadDistrictIndex(reader, fieldTag, maxDistrictIndex);
                    else if (fieldKey == "v")
                        version = ReadDistrictVersion(reader, fieldTag);
                    else if (fieldKey == "p")
                        hasSchedule = TryReadSchedule(reader, fieldTag, "p", out schedule);
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var districtRef = new DistrictRef(district, version);
                if (IsReadableDistrictRef(districtRef, maxDistrictIndex) && hasSchedule)
                    entries[written++] = new DistrictScheduleEntry(districtRef, schedule);
            }
            return Compact(entries, written);
        }

        private static DistrictCategoriesEntry[] ReadBlackouts<TReader>(TReader reader, TypeTag tag, int maxDistrictIndex)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "blackouts", MaxDistrictRecords);
            var entries = new DistrictCategoriesEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int version = LegacyDistrictVersion;
                var categories = Array.Empty<int>();
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                        district = ReadDistrictIndex(reader, fieldTag, maxDistrictIndex);
                    else if (fieldKey == "v")
                        version = ReadDistrictVersion(reader, fieldTag);
                    else if (fieldKey == "cats")
                        categories = ReadCategories(reader, fieldTag, "cats");
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var districtRef = new DistrictRef(district, version);
                if (IsReadableDistrictRef(districtRef, maxDistrictIndex))
                    entries[written++] = new DistrictCategoriesEntry(districtRef, categories);
            }
            return Compact(entries, written);
        }

        private static DistrictRefEntry[] ReadDistrictRefSet<TReader>(TReader reader, TypeTag tag, string name, int maxDistrictIndex)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxDistrictRecords);
            var entries = new DistrictRefEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int version = LegacyDistrictVersion;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                        district = ReadDistrictIndex(reader, fieldTag, maxDistrictIndex);
                    else if (fieldKey == "v")
                        version = ReadDistrictVersion(reader, fieldTag);
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var districtRef = new DistrictRef(district, version);
                if (IsReadableDistrictRef(districtRef, maxDistrictIndex))
                    entries[written++] = new DistrictRefEntry(districtRef);
            }
            return Compact(entries, written);
        }

        private static DistrictPenaltyPersistEntry[] ReadPenalties<TReader>(TReader reader, TypeTag tag, int maxDistrictIndex)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "penalties", MaxDistrictRecords);
            var entries = new DistrictPenaltyPersistEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int version = LegacyDistrictVersion;
                int sources = 0;
                float happiness = 0f;
                float commerce = 0f;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                        district = ReadDistrictIndex(reader, fieldTag, maxDistrictIndex);
                    else if (fieldKey == "v")
                        version = ReadDistrictVersion(reader, fieldTag);
                    else if (fieldKey == "src")
                        sources = ReadPenaltySources(reader, fieldTag);
                    else if (fieldKey == "hap")
                        happiness = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "hap", -1f, 1f, 0f);
                    else if (fieldKey == "com")
                        commerce = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "com", 0f, 1f, 0f);
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var districtRef = new DistrictRef(district, version);
                if (IsReadableDistrictRef(districtRef, maxDistrictIndex))
                    entries[written++] = new DistrictPenaltyPersistEntry(districtRef, sources, happiness, commerce);
            }
            return Compact(entries, written);
        }

        private static PreShedPersistEntry[] ReadPreShedStates<TReader>(TReader reader, TypeTag tag, int maxDistrictIndex)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "preShed", MaxDistrictRecords);
            var entries = new PreShedPersistEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int version = LegacyDistrictVersion;
                int schedule = 0;
                bool wasVip = false;
                bool wasVipBypass = false;
                bool hadExplicitSchedule = true;
                bool hasSchedule = false;
                var categories = Array.Empty<int>();
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                        district = ReadDistrictIndex(reader, fieldTag, maxDistrictIndex);
                    else if (fieldKey == "v")
                        version = ReadDistrictVersion(reader, fieldTag);
                    else if (fieldKey == "sch")
                        hasSchedule = TryReadSchedule(reader, fieldTag, "sch", out schedule);
                    else if (fieldKey == "vip")
                        wasVip = KeyedSerializer.ReadBool(reader, fieldTag, "vip");
                    else if (fieldKey == "vbp")
                        wasVipBypass = KeyedSerializer.ReadBool(reader, fieldTag, "vbp");
                    else if (fieldKey == "exp")
                        hadExplicitSchedule = KeyedSerializer.ReadBool(reader, fieldTag, "exp");
                    else if (fieldKey == "cats")
                        categories = ReadCategories(reader, fieldTag, "cats");
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var districtRef = new DistrictRef(district, version);
                if (IsReadableDistrictRef(districtRef, maxDistrictIndex) && hasSchedule)
                    entries[written++] = new PreShedPersistEntry(districtRef, schedule, wasVip, hadExplicitSchedule, categories, wasVipBypass);
            }
            return Compact(entries, written);
        }

        private static DistrictPriorityEntry[] ReadPriorities<TReader>(TReader reader, TypeTag tag, int maxDistrictIndex)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "priorities", MaxDistrictRecords);
            var entries = new DistrictPriorityEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int version = LegacyDistrictVersion;
                int priority = 1;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                        district = ReadDistrictIndex(reader, fieldTag, maxDistrictIndex);
                    else if (fieldKey == "v")
                        version = ReadDistrictVersion(reader, fieldTag);
                    else if (fieldKey == "p")
                        // Priority is written raw; the valid ceiling is BalanceConfig
                        // MaxPriority which can be tuned >5. The old [1,5] read clamped
                        // every higher player priority to 1 on load (G7 A-6 / G6 BL-8).
                        // Persist raw, the district-priority consumer applies config bounds.
                        priority = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "p", 1, int.MaxValue, 1);
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                var districtRef = new DistrictRef(district, version);
                if (IsReadableDistrictRef(districtRef, maxDistrictIndex))
                    entries[written++] = new DistrictPriorityEntry(districtRef, priority);
            }
            return Compact(entries, written);
        }

        private static int[] ReadCategories<TReader>(TReader reader, TypeTag tag, string name)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxCategoryRecords);
            var entries = new int[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int category = 0;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "c")
                        category = ReadCategoryOrNone(reader, fieldTag);
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                if (category != 0)
                    entries[written++] = category;
            }
            return Compact(entries, written);
        }

        private static int ReadDistrictIndex<TReader>(TReader reader, TypeTag tag, int maxDistrictIndex)
            where TReader : IReader
            => KeyedSerializer.ReadBoundedInt(reader, tag, "d", 0, maxDistrictIndex, -1);

        private static int ReadDistrictVersion<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
            => KeyedSerializer.ReadBoundedInt(reader, tag, "v", LegacyDistrictVersion, int.MaxValue, LegacyDistrictVersion);

        private static int ReadScheduleOrDefault<TReader>(TReader reader, TypeTag tag, string key, int defaultValue)
            where TReader : IReader
            => TryReadSchedule(reader, tag, key, out var value) ? value : defaultValue;

        private static bool TryReadSchedule<TReader>(TReader reader, TypeTag tag, string key, out int value)
            where TReader : IReader
        {
            value = 0;
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumInt, key))
                return false;

            reader.Read(out int raw);
            if (!BlackoutPersistState.IsValidSchedule(raw))
                return false;

            value = raw;
            return true;
        }

        private static int ReadCategoryOrNone<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumInt, "c"))
                return 0;

            reader.Read(out int raw);
            return IsValidCategory(raw) ? raw : 0;
        }

        private static int ReadPenaltySources<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumInt, "src"))
                return 0;

            reader.Read(out int raw);
            return raw & AllPenaltySourceFlags;
        }

        private static bool IsValidDistrict(int districtIndex, int maxDistrictIndex)
            => districtIndex >= 0 && districtIndex <= maxDistrictIndex;

        private static bool IsValidDistrictRef(DistrictRef district, int maxDistrictIndex)
            => IsValidDistrict(district.Index, maxDistrictIndex) && district.Version >= 0;

        private static bool IsReadableDistrictRef(DistrictRef district, int maxDistrictIndex)
            => IsValidDistrict(district.Index, maxDistrictIndex)
               && (district.Version >= 0 || district.Version == LegacyDistrictVersion);

        private static bool IsValidCategory(int value)
            => value >= 1 && value <= 5;

        private static int CountValidDistrictSchedules(IReadOnlyList<DistrictScheduleEntry> entries, int maxDistrictIndex)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictRef(entries[i].District, maxDistrictIndex) && BlackoutPersistState.IsValidSchedule(entries[i].Schedule))
                    count++;
            }
            return count;
        }

        private static int CountValidDistrictCategoryEntries(IReadOnlyList<DistrictCategoriesEntry> entries, int maxDistrictIndex)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    count++;
            }
            return count;
        }

        private static int CountValidDistrictRefs(IReadOnlyList<DistrictRefEntry> entries, int maxDistrictIndex)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    count++;
            }
            return count;
        }

        private static int CountValidPenaltyEntries(IReadOnlyList<DistrictPenaltyPersistEntry> entries, int maxDistrictIndex)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    count++;
            }
            return count;
        }

        private static int CountValidPreShedEntries(IReadOnlyList<PreShedPersistEntry> entries, int maxDistrictIndex)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictRef(entries[i].District, maxDistrictIndex) && BlackoutPersistState.IsValidSchedule(entries[i].Schedule))
                    count++;
            }
            return count;
        }

        private static int CountValidPriorityEntries(IReadOnlyList<DistrictPriorityEntry> entries, int maxDistrictIndex)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsValidDistrictRef(entries[i].District, maxDistrictIndex))
                    count++;
            }
            return count;
        }

        private static int CountValidCategories(IReadOnlyList<int> categories)
        {
            int count = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                if (IsValidCategory(categories[i]))
                    count++;
            }
            return count;
        }

        private static DistrictScheduleEntry[] Compact(DistrictScheduleEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictScheduleEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }

        private static DistrictCategoriesEntry[] Compact(DistrictCategoriesEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictCategoriesEntry[count];
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

        private static DistrictRefEntry[] Compact(DistrictRefEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictRefEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }

        private static DistrictPenaltyPersistEntry[] Compact(DistrictPenaltyPersistEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictPenaltyPersistEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }

        private static PreShedPersistEntry[] Compact(PreShedPersistEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new PreShedPersistEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }

        private static DistrictPriorityEntry[] Compact(DistrictPriorityEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictPriorityEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }
    }
}
