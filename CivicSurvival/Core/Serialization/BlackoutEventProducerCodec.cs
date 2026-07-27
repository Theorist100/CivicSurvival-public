using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Utils;
using Unity.Entities;

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
        // District identity rides DistrictKeyCodec (engine entity remap); a raw index does not
        // survive load. Only the record COUNT is capped — the former MaxDistrictIndex=10000 key
        // clamp dropped high-index blackout state on save/load (Cluster A A-3).
        public const int MaxDistrictRecords = 10000;
        // Absolute game-hour timestamps: NaN/Inf/negative-guard only, no upper cap — a timestamp
        // is not a duration; the former 1e5 ceiling collapsed a legitimate late-game value to 0.
        public const float MaxGameHours = float.MaxValue;

        public static void Write<TWriter>(in BlackoutEventProducerState state, TWriter writer, in LiveDistrictMap map)
            where TWriter : IWriter
        {
            int stale = 0;
            KeyedSerializer.WriteBlockHeader(writer, 5);
            WriteDistrictBoolMap(writer, "prevState", state.PreviousBlackoutState, map, ref stale);
            WriteDistrictFloatMap(writer, "startHours", state.BlackoutStartHours, map, ref stale);
            WriteDistrictSet(writer, "longFired", state.LongBlackoutFired, map, ref stale);
            WriteDistrictSet(writer, "vipFired", state.VipVisibleFired, map, ref stale);
            KeyedSerializer.WriteField(writer, "lastNonVipHour", state.LastNonVipBlackoutHour);
            int total = state.PreviousBlackoutState.Count + state.BlackoutStartHours.Count
                + state.LongBlackoutFired.Count + state.VipVisibleFired.Count;
            DistrictKeyCodec.LogWrite("BlackoutEventProducerCodec", total, stale);
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
                        blackoutStartHours = ReadDistrictFloatMap(reader, tag, "startHours", MaxDistrictRecords);
                        break;
                    case "longFired":
                        longBlackoutFired = ReadDistrictSet(reader, tag, "longFired", MaxDistrictRecords);
                        break;
                    case "vipFired":
                        vipVisibleFired = ReadDistrictSet(reader, tag, "vipFired", MaxDistrictRecords);
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

        private static void WriteDistrictBoolMap<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictBoolEntry> entries, in LiveDistrictMap map, ref int stale)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1 + DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "de", entries[i].DistrictIndex, map, ref stale);
                KeyedSerializer.WriteField(writer, "v", entries[i].Value);
            }
        }

        private static void WriteDistrictFloatMap<TWriter>(TWriter writer, string key, IReadOnlyList<DistrictFloatEntry> entries, in LiveDistrictMap map, ref int stale)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1 + DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "de", entries[i].DistrictIndex, map, ref stale);
                KeyedSerializer.WriteField(writer, "t", entries[i].Value);
            }
        }

        internal static void WriteDistrictSet<TWriter>(TWriter writer, string key, IReadOnlyList<int> entries, in LiveDistrictMap map, ref int stale)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "de", entries[i], map, ref stale);
            }
        }

        private static DistrictBoolEntry[] ReadDistrictBoolMap<TReader>(TReader reader, TypeTag tag, string name)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxDistrictRecords);
            var entries = new DistrictBoolEntry[count];
            int written = 0;
            int stale = 0;
            for (int i = 0; i < count; i++)
            {
                byte districtKind = 0;
                Entity districtEntity = Entity.Null;
                bool value = false;
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
                    else if (fieldKey == "v")
                    {
                        value = KeyedSerializer.ReadBool(reader, fieldTag, "v");
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (TryResolveDistrictIndex(districtKind, districtEntity, ref stale, out int district))
                    entries[written++] = new DistrictBoolEntry(district, value);
            }

            DistrictKeyCodec.LogRead($"BlackoutEventProducerCodec.{name}", written, stale);
            return Compact(entries, written);
        }

        internal static DistrictFloatEntry[] ReadDistrictFloatMap<TReader>(
            TReader reader,
            TypeTag tag,
            string name,
            int maxRecordCount)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, maxRecordCount);
            var entries = new DistrictFloatEntry[count];
            int written = 0;
            int stale = 0;
            for (int i = 0; i < count; i++)
            {
                byte districtKind = 0;
                Entity districtEntity = Entity.Null;
                float value = 0f;
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
                    else if (fieldKey == "t")
                    {
                        // Absolute game-hour timestamp (aid-expiry / blackout start): NaN/Inf-guarded but
                        // NOT clamped — the former 1e6 ceiling coerced late-city timestamps to 0 (S-PSY-10).
                        value = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "t", 0f);
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (TryResolveDistrictIndex(districtKind, districtEntity, ref stale, out int district))
                    entries[written++] = new DistrictFloatEntry(district, value);
            }

            DistrictKeyCodec.LogRead($"BlackoutEventProducerCodec.{name}", written, stale);
            return Compact(entries, written);
        }

        internal static int[] ReadDistrictSet<TReader>(TReader reader, TypeTag tag, string name, int maxRecordCount)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, maxRecordCount);
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
                    {
                        districtKind = DistrictKeyCodec.ReadKind(reader, fieldTag, "de");
                    }
                    else if (fieldKey == "de")
                    {
                        districtEntity = DistrictKeyCodec.ReadEntity(reader, fieldTag, "de");
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (TryResolveDistrictIndex(districtKind, districtEntity, ref stale, out int district))
                    entries[written++] = district;
            }

            DistrictKeyCodec.LogRead($"BlackoutEventProducerCodec.{name}", written, stale);
            return Compact(entries, written);
        }

        /// <summary>
        /// Resolve a read district key to a runtime int index. Live → live index, Unzoned → 0,
        /// None/stale → drop (increment <paramref name="stale"/>, return false).
        /// </summary>
        private static bool TryResolveDistrictIndex(byte kind, Entity entity, ref int stale, out int district)
        {
            var result = DistrictKeyCodec.Resolve(kind, entity, out var live);
            if (result == DistrictKeyReadResult.None)
            {
                stale++;
                district = -1;
                return false;
            }

            district = result == DistrictKeyReadResult.Unzoned ? DistrictUtils.UNZONED_AREA_INDEX : live.Index;
            return true;
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
