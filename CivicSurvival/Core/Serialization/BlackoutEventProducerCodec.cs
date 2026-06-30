using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct BlackoutEventProducerState
    {
        public BlackoutEventProducerState(
            DistrictBoolEntry[] previousBlackoutState,
            DistrictFloatEntry[] blackoutStartHours,
            int[] longBlackoutFired,
            int[] vipVisibleFired,
            float lastNonVipBlackoutHour)
        {
            PreviousBlackoutState = previousBlackoutState ?? Array.Empty<DistrictBoolEntry>();
            BlackoutStartHours = blackoutStartHours ?? Array.Empty<DistrictFloatEntry>();
            LongBlackoutFired = longBlackoutFired ?? Array.Empty<int>();
            VipVisibleFired = vipVisibleFired ?? Array.Empty<int>();
            LastNonVipBlackoutHour = lastNonVipBlackoutHour;
        }

        public IReadOnlyList<DistrictBoolEntry> PreviousBlackoutState { get; }
        public IReadOnlyList<DistrictFloatEntry> BlackoutStartHours { get; }
        public IReadOnlyList<int> LongBlackoutFired { get; }
        public IReadOnlyList<int> VipVisibleFired { get; }
        public float LastNonVipBlackoutHour { get; }
    }

    public static class BlackoutEventProducerCodec
    {
        // District keys ride KeyedSerializer.WriteDistrictKey/ReadDistrictKey (the
        // one unbounded contract). Only the record COUNT is capped — the former
        // MaxDistrictIndex=10000 key clamp dropped high-index blackout state on
        // save/load (Cluster A A-3).
        public const int MaxDistrictRecords = 10000;
        public const float MaxGameHours = 100000f;

        public static void Write<TWriter>(in BlackoutEventProducerState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 5);
            WriteDistrictBoolMap(writer, "prevState", state.PreviousBlackoutState);
            WriteDistrictFloatMap(writer, "startHours", state.BlackoutStartHours);
            WriteDistrictSet(writer, "longFired", state.LongBlackoutFired);
            WriteDistrictSet(writer, "vipFired", state.VipVisibleFired);
            KeyedSerializer.WriteField(writer, "lastNonVipHour", state.LastNonVipBlackoutHour);
        }

        public static void Read<TReader>(TReader reader, out BlackoutEventProducerState state)
            where TReader : IReader
        {
            var previousBlackoutState = Array.Empty<DistrictBoolEntry>();
            var blackoutStartHours = Array.Empty<DistrictFloatEntry>();
            var longBlackoutFired = Array.Empty<int>();
            var vipVisibleFired = Array.Empty<int>();
            float lastNonVipBlackoutHour = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "prevState":
                        previousBlackoutState = ReadDistrictBoolMap(reader, tag, "prevState");
                        break;
                    case "startHours":
                        blackoutStartHours = ReadDistrictFloatMap(reader, tag, "startHours", MaxDistrictRecords, MaxGameHours);
                        break;
                    case "longFired":
                        longBlackoutFired = ReadDistrictSet(reader, tag, "longFired");
                        break;
                    case "vipFired":
                        vipVisibleFired = ReadDistrictSet(reader, tag, "vipFired");
                        break;
                    case "lastNonVipHour":
                        lastNonVipBlackoutHour = KeyedSerializer.ReadSafeFloat(reader, tag, "lastNonVipHour", 0f, MaxGameHours, 0f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new BlackoutEventProducerState(
                previousBlackoutState,
                blackoutStartHours,
                longBlackoutFired,
                vipVisibleFired,
                lastNonVipBlackoutHour);
        }

        private static void WriteDistrictBoolMap<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictBoolEntry> entries)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteDistrictKey(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "v", entries[i].Value);
            }
        }

        private static void WriteDistrictFloatMap<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictFloatEntry> entries)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteDistrictKey(writer, "d", entries[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "t", entries[i].Value);
            }
        }

        private static void WriteDistrictSet<TWriter>(TWriter writer, string key, IReadOnlyList<int> entries)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteDistrictKey(writer, "d", entries[i]);
            }
        }

        private static DistrictBoolEntry[] ReadDistrictBoolMap<TReader>(TReader reader, TypeTag tag, string name)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxDistrictRecords);
            var entries = new DistrictBoolEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                bool value = false;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                    {
                        district = KeyedSerializer.ReadDistrictKey(reader, fieldTag, "d");
                    }
                    else if (fieldKey == "v")
                    {
                        value = KeyedSerializer.ReadBool(reader, fieldTag, "v");
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (district >= 0)
                    entries[written++] = new DistrictBoolEntry(district, value);
            }

            return Compact(entries, written);
        }

        internal static DistrictFloatEntry[] ReadDistrictFloatMap<TReader>(
            TReader reader,
            TypeTag tag,
            string name,
            int maxRecordCount,
            float maxValue)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, maxRecordCount);
            var entries = new DistrictFloatEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                float value = 0f;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                    {
                        district = KeyedSerializer.ReadDistrictKey(reader, fieldTag, "d");
                    }
                    else if (fieldKey == "t")
                    {
                        value = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "t", 0f, maxValue, 0f);
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (district >= 0)
                    entries[written++] = new DistrictFloatEntry(district, value);
            }

            return Compact(entries, written);
        }

        private static int[] ReadDistrictSet<TReader>(TReader reader, TypeTag tag, string name)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxDistrictRecords);
            var entries = new int[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                    {
                        district = KeyedSerializer.ReadDistrictKey(reader, fieldTag, "d");
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (district >= 0)
                    entries[written++] = district;
            }

            return Compact(entries, written);
        }

        private static DistrictBoolEntry[] Compact(DistrictBoolEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictBoolEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }

        private static DistrictFloatEntry[] Compact(DistrictFloatEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new DistrictFloatEntry[count];
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
