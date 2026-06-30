using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct WorldShockPersistState
    {
        public WorldShockPersistState(
            float shockLevel,
            AidTier currentTier,
            double lastUpdateTime,
            float decayPerDay,
            int casualtiesThisWeek,
            int buildingsDestroyedThisWeek,
            int criticalHitsThisWeek,
            double lastTragedyTime,
            AidTier lastTier,
            long totalCasualties,
            long totalBuildingsDestroyed,
            long totalCivilianBuildingsDestroyed,
            long totalCriticalHits,
            int ringIndex,
            int[] dailyCasualties,
            int[] dailyBuildings,
            int[] dailyCritical,
            long prevTotalCasualties,
            long prevTotalBuildings,
            long prevTotalCritical,
            bool dayChanged)
        {
            ShockLevel = shockLevel;
            CurrentTier = currentTier;
            LastUpdateTime = lastUpdateTime;
            DecayPerDay = decayPerDay;
            CasualtiesThisWeek = casualtiesThisWeek;
            BuildingsDestroyedThisWeek = buildingsDestroyedThisWeek;
            CriticalHitsThisWeek = criticalHitsThisWeek;
            LastTragedyTime = lastTragedyTime;
            LastTier = lastTier;
            TotalCasualties = totalCasualties;
            TotalBuildingsDestroyed = totalBuildingsDestroyed;
            TotalCivilianBuildingsDestroyed = totalCivilianBuildingsDestroyed;
            TotalCriticalHits = totalCriticalHits;
            RingIndex = ringIndex;
            DailyCasualties = dailyCasualties ?? Array.Empty<int>();
            DailyBuildings = dailyBuildings ?? Array.Empty<int>();
            DailyCritical = dailyCritical ?? Array.Empty<int>();
            PrevTotalCasualties = prevTotalCasualties;
            PrevTotalBuildings = prevTotalBuildings;
            PrevTotalCritical = prevTotalCritical;
            DayChanged = dayChanged;
        }

        public float ShockLevel { get; }
        public AidTier CurrentTier { get; }
        public double LastUpdateTime { get; }
        public float DecayPerDay { get; }
        public int CasualtiesThisWeek { get; }
        public int BuildingsDestroyedThisWeek { get; }
        public int CriticalHitsThisWeek { get; }
        public double LastTragedyTime { get; }
        public AidTier LastTier { get; }
        public long TotalCasualties { get; }
        public long TotalBuildingsDestroyed { get; }
        public long TotalCivilianBuildingsDestroyed { get; }
        public long TotalCriticalHits { get; }
        public int RingIndex { get; }
        public IReadOnlyList<int> DailyCasualties { get; }
        public IReadOnlyList<int> DailyBuildings { get; }
        public IReadOnlyList<int> DailyCritical { get; }
        public long PrevTotalCasualties { get; }
        public long PrevTotalBuildings { get; }
        public long PrevTotalCritical { get; }
        public bool DayChanged { get; }
    }

    public static class WorldShockCodec
    {
        public const int RollingWindowDays = 7;
        public const float MaxShockLevel = 100f;
        public const float MaxDecayPerDay = 100f;
        private const int MaxWeeklyCount = 10000;
        private const int MaxDailyCount = 10000000;

        public static void Write<TWriter>(in WorldShockPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 21);
            KeyedSerializer.WriteField(writer, "shockLevel", state.ShockLevel);
            KeyedSerializer.WriteEnumByteField(writer, "currentTier", (byte)state.CurrentTier);
            KeyedSerializer.WriteField(writer, "lastUpdateTime", state.LastUpdateTime);
            KeyedSerializer.WriteField(writer, "decayPerDay", state.DecayPerDay);
            KeyedSerializer.WriteField(writer, "casualtiesWeek", state.CasualtiesThisWeek);
            KeyedSerializer.WriteField(writer, "buildingsWeek", state.BuildingsDestroyedThisWeek);
            KeyedSerializer.WriteField(writer, "criticalWeek", state.CriticalHitsThisWeek);
            KeyedSerializer.WriteField(writer, "lastTragedyTime", state.LastTragedyTime);
            KeyedSerializer.WriteEnumByteField(writer, "lastTier", (byte)state.LastTier);
            KeyedSerializer.WriteField(writer, "totalCasualties", state.TotalCasualties);
            KeyedSerializer.WriteField(writer, "totalBuildings", state.TotalBuildingsDestroyed);
            KeyedSerializer.WriteField(writer, "totalCivilianBuildings", state.TotalCivilianBuildingsDestroyed);
            KeyedSerializer.WriteField(writer, "totalCritical", state.TotalCriticalHits);
            KeyedSerializer.WriteField(writer, "ringIndex", state.RingIndex);
            WriteRingBuffer(writer, "dailyCasualties", state.DailyCasualties);
            WriteRingBuffer(writer, "dailyBuildings", state.DailyBuildings);
            WriteRingBuffer(writer, "dailyCritical", state.DailyCritical);
            KeyedSerializer.WriteField(writer, "prevTotalCasualties", state.PrevTotalCasualties);
            KeyedSerializer.WriteField(writer, "prevTotalBuildings", state.PrevTotalBuildings);
            KeyedSerializer.WriteField(writer, "prevTotalCritical", state.PrevTotalCritical);
            KeyedSerializer.WriteField(writer, "dayChanged", state.DayChanged);
        }

        public static void Read<TReader>(TReader reader, float defaultDecayPerDay, out WorldShockPersistState state)
            where TReader : IReader
        {
            float shockLevel = 0f;
            var currentTier = AidTier.DeepConcern;
            double lastUpdateTime = 0.0;
            float decayPerDay = Clamp(defaultDecayPerDay, 0f, MaxDecayPerDay);
            int casualtiesThisWeek = 0;
            int buildingsDestroyedThisWeek = 0;
            int criticalHitsThisWeek = 0;
            double lastTragedyTime = 0.0;
            var lastTier = AidTier.DeepConcern;
            long totalCasualties = 0;
            long totalBuildingsDestroyed = 0;
            long totalCivilianBuildingsDestroyed = 0;
            long totalCriticalHits = 0;
            int ringIndex = 0;
            var dailyCasualties = new int[RollingWindowDays];
            var dailyBuildings = new int[RollingWindowDays];
            var dailyCritical = new int[RollingWindowDays];
            long prevTotalCasualties = 0;
            long prevTotalBuildings = 0;
            long prevTotalCritical = 0;
            bool dayChanged = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "shockLevel":
                        shockLevel = KeyedSerializer.ReadSafeFloat(reader, tag, "shockLevel", 0f, MaxShockLevel, 0f);
                        break;
                    case "currentTier":
                        currentTier = KeyedSerializer.ReadEnumByte<TReader, AidTier>(reader, tag, "currentTier", AidTier.DeepConcern);
                        break;
                    case "lastUpdateTime":
                        lastUpdateTime = KeyedSerializer.ReadSafeDouble(reader, tag, "lastUpdateTime", 0.0);
                        break;
                    case "decayPerDay":
                        decayPerDay = KeyedSerializer.ReadSafeFloat(reader, tag, "decayPerDay", 0f, MaxDecayPerDay, Clamp(defaultDecayPerDay, 0f, MaxDecayPerDay));
                        break;
                    case "casualtiesWeek":
                        casualtiesThisWeek = KeyedSerializer.ReadBoundedInt(reader, tag, "casualtiesWeek", 0, MaxWeeklyCount, 0);
                        break;
                    case "buildingsWeek":
                        buildingsDestroyedThisWeek = KeyedSerializer.ReadBoundedInt(reader, tag, "buildingsWeek", 0, MaxWeeklyCount, 0);
                        break;
                    case "criticalWeek":
                        criticalHitsThisWeek = KeyedSerializer.ReadBoundedInt(reader, tag, "criticalWeek", 0, MaxWeeklyCount, 0);
                        break;
                    case "lastTragedyTime":
                        lastTragedyTime = KeyedSerializer.ReadSafeDouble(reader, tag, "lastTragedyTime", 0.0);
                        break;
                    case "lastTier":
                        lastTier = KeyedSerializer.ReadEnumByte<TReader, AidTier>(reader, tag, "lastTier", AidTier.DeepConcern);
                        break;
                    case "totalCasualties":
                        totalCasualties = KeyedSerializer.ReadBoundedLong(reader, tag, "totalCasualties", 0, long.MaxValue);
                        break;
                    case "totalBuildings":
                        totalBuildingsDestroyed = KeyedSerializer.ReadBoundedLong(reader, tag, "totalBuildings", 0, long.MaxValue);
                        break;
                    case "totalCivilianBuildings":
                        totalCivilianBuildingsDestroyed = KeyedSerializer.ReadBoundedLong(reader, tag, "totalCivilianBuildings", 0, long.MaxValue);
                        break;
                    case "totalCritical":
                        totalCriticalHits = KeyedSerializer.ReadBoundedLong(reader, tag, "totalCritical", 0, long.MaxValue);
                        break;
                    case "ringIndex":
                        ringIndex = KeyedSerializer.ReadBoundedInt(reader, tag, "ringIndex", 0, RollingWindowDays - 1, 0);
                        break;
                    case "dailyCasualties":
                        dailyCasualties = ReadRingBuffer(reader, tag, "dailyCasualties");
                        break;
                    case "dailyBuildings":
                        dailyBuildings = ReadRingBuffer(reader, tag, "dailyBuildings");
                        break;
                    case "dailyCritical":
                        dailyCritical = ReadRingBuffer(reader, tag, "dailyCritical");
                        break;
                    case "prevTotalCasualties":
                        prevTotalCasualties = KeyedSerializer.ReadBoundedLong(reader, tag, "prevTotalCasualties", 0, long.MaxValue);
                        break;
                    case "prevTotalBuildings":
                        prevTotalBuildings = KeyedSerializer.ReadBoundedLong(reader, tag, "prevTotalBuildings", 0, long.MaxValue);
                        break;
                    case "prevTotalCritical":
                        prevTotalCritical = KeyedSerializer.ReadBoundedLong(reader, tag, "prevTotalCritical", 0, long.MaxValue);
                        break;
                    case "dayChanged":
                        dayChanged = KeyedSerializer.ReadBool(reader, tag, "dayChanged");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new WorldShockPersistState(
                shockLevel,
                currentTier,
                lastUpdateTime,
                decayPerDay,
                casualtiesThisWeek,
                buildingsDestroyedThisWeek,
                criticalHitsThisWeek,
                lastTragedyTime,
                lastTier,
                totalCasualties,
                totalBuildingsDestroyed,
                totalCivilianBuildingsDestroyed,
                totalCriticalHits,
                ringIndex,
                dailyCasualties,
                dailyBuildings,
                dailyCritical,
                prevTotalCasualties,
                prevTotalBuildings,
                prevTotalCritical,
                dayChanged);
        }

        private static void WriteRingBuffer<TWriter>(TWriter writer, string key, IReadOnlyList<int> values)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, RollingWindowDays);
            for (int i = 0; i < RollingWindowDays; i++)
            {
                int value = i < values.Count ? values[i] : 0;
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "v", value);
            }
        }

        private static int[] ReadRingBuffer<TReader>(TReader reader, TypeTag tag, string name)
            where TReader : IReader
        {
            var values = new int[RollingWindowDays];
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, RollingWindowDays);
            for (int i = 0; i < count && i < values.Length; i++)
            {
                int value = 0;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "v")
                    {
                        value = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "v", 0, MaxDailyCount, 0);
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }
                values[i] = value;
            }

            for (int i = values.Length; i < count; i++)
                KeyedSerializer.SkipKeyedBlock(reader);

            return values;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
