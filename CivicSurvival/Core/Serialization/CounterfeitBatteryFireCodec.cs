using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CounterfeitBatteryFirePersistState
    {
        public CounterfeitBatteryFirePersistState(
            int lastProcessedDay,
            EntityPersistEntry[] firedToday)
        {
            LastProcessedDay = lastProcessedDay;
            FiredToday = firedToday ?? Array.Empty<EntityPersistEntry>();
        }

        public int LastProcessedDay { get; }
        public IReadOnlyList<EntityPersistEntry> FiredToday { get; }
    }

    public static class CounterfeitBatteryFireCodec
    {
        public const int MaxFiredTodayEntities = 10000;

        public static void Write<TWriter>(in CounterfeitBatteryFirePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
            KeyedSerializer.WriteBufferHeader(writer, "firedToday", state.FiredToday.Count);
            for (int i = 0; i < state.FiredToday.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteEntityField(
                    writer,
                    "building",
                    new Entity { Index = state.FiredToday[i].Index, Version = state.FiredToday[i].Version });
            }
        }

        public static void Read<TReader>(TReader reader, out CounterfeitBatteryFirePersistState state)
            where TReader : IReader
        {
            int lastProcessedDay = 0;
            var firedToday = Array.Empty<EntityPersistEntry>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, 100000);
                        break;
                    case "firedToday":
                        firedToday = ReadFiredToday(reader, tag);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CounterfeitBatteryFirePersistState(
                lastProcessedDay,
                firedToday);
        }

        private static EntityPersistEntry[] ReadFiredToday<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "firedToday", MaxFiredTodayEntities);
            var entries = new EntityPersistEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                Entity building = Entity.Null;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "building")
                        building = KeyedSerializer.ReadEntity(reader, fieldTag, "building");
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                if (building != Entity.Null)
                    entries[written++] = new EntityPersistEntry(building.Index, building.Version);
            }

            return Compact(entries, written);
        }

        private static EntityPersistEntry[] Compact(EntityPersistEntry[] entries, int count)
        {
            if (count == entries.Length)
                return entries;
            var compact = new EntityPersistEntry[count];
            Array.Copy(entries, compact, count);
            return compact;
        }
    }
}
