using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct HeritageGrantState
    {
        public HeritageGrantState(int zeroProductionFrames) => ZeroProductionFrames = zeroProductionFrames;
        public int ZeroProductionFrames { get; }
    }

    public static class HeritageGrantCodec
    {
        public const int MaxZeroProductionFrames = 100000;

        public static void Write<TWriter>(in HeritageGrantState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "zeroFrames", state.ZeroProductionFrames);
        }

        public static void Read<TReader>(TReader reader, out HeritageGrantState state) where TReader : IReader
        {
            int zeroFrames = 0;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "zeroFrames": zeroFrames = KeyedSerializer.ReadBoundedInt(reader, tag, "zeroFrames", 0, MaxZeroProductionFrames, 0); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new HeritageGrantState(zeroFrames);
        }
    }

    public readonly struct WorldShockDecayState
    {
        public WorldShockDecayState(double lastCheckTime) => LastCheckTime = lastCheckTime;
        public double LastCheckTime { get; }
    }

    public static class WorldShockDecayCodec
    {
        public static void Write<TWriter>(in WorldShockDecayState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "lastCheckTime", state.LastCheckTime);
        }

        public static void Read<TReader>(TReader reader, out WorldShockDecayState state) where TReader : IReader
        {
            double lastCheckTime = 0.0;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastCheckTime": lastCheckTime = KeyedSerializer.ReadSafeDouble(reader, tag, "lastCheckTime", 0.0); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new WorldShockDecayState(lastCheckTime);
        }
    }

    public readonly struct CorruptionExposureState
    {
        public CorruptionExposureState(float exposure) => Exposure = exposure;
        public float Exposure { get; }
    }

    public static class CorruptionExposureCodec
    {
        public const float MaxExposure = 10000f;

        public static void Write<TWriter>(in CorruptionExposureState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "exposure", state.Exposure);
        }

        public static void Read<TReader>(TReader reader, out CorruptionExposureState state) where TReader : IReader
        {
            float exposure = 0f;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "exposure": exposure = KeyedSerializer.ReadSafeFloat(reader, tag, "exposure", 0f, MaxExposure, 0f); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new CorruptionExposureState(exposure);
        }
    }

    public readonly struct NeighborEnvyState
    {
        public NeighborEnvyState(bool featureEnabled) => FeatureEnabled = featureEnabled;
        public bool FeatureEnabled { get; }
    }

    public static class NeighborEnvyCodec
    {
        public static void Write<TWriter>(in NeighborEnvyState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "FeatureEnabled", state.FeatureEnabled);
        }

        public static void Read<TReader>(TReader reader, out NeighborEnvyState state) where TReader : IReader
        {
            bool enabled = true;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "FeatureEnabled": enabled = KeyedSerializer.ReadBool(reader, tag, "FeatureEnabled", true); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new NeighborEnvyState(enabled);
        }
    }

    public readonly struct BackupPowerDistributionState
    {
        public BackupPowerDistributionState(bool migrationComplete, bool layerTagMigrationComplete)
            : this(migrationComplete, layerTagMigrationComplete, Array.Empty<BuildingRef>())
        {
        }

        public BackupPowerDistributionState(
            bool migrationComplete,
            bool layerTagMigrationComplete,
            IReadOnlyList<BuildingRef> noBackupBuildings)
        {
            MigrationComplete = migrationComplete;
            LayerTagMigrationComplete = layerTagMigrationComplete;
            NoBackupBuildings = noBackupBuildings ?? Array.Empty<BuildingRef>();
        }

        public bool MigrationComplete { get; }
        public bool LayerTagMigrationComplete { get; }
        public IReadOnlyList<BuildingRef> NoBackupBuildings { get; }
    }

    public static class BackupPowerDistributionCodec
    {
        public const int MaxNoBackupBuildings = 100000;

        public static void Write<TWriter>(in BackupPowerDistributionState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "migrationComplete", state.MigrationComplete);
            KeyedSerializer.WriteField(writer, "layerTagMigrationComplete", state.LayerTagMigrationComplete);
            int noBackupCount = Math.Min(state.NoBackupBuildings.Count, MaxNoBackupBuildings);
            KeyedSerializer.WriteBufferHeader(writer, "noBackupBuildings", noBackupCount);
            for (int i = 0; i < noBackupCount; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteEntityField(writer, "building", state.NoBackupBuildings[i].ToEntity());
            }
        }

        public static void Read<TReader>(TReader reader, out BackupPowerDistributionState state) where TReader : IReader
        {
            bool migrationComplete = false;
            bool layerTagMigrationComplete = false;
            BuildingRef[] noBackupBuildings = Array.Empty<BuildingRef>();
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "migrationComplete": migrationComplete = KeyedSerializer.ReadBool(reader, tag, "migrationComplete"); break;
                    case "layerTagMigrationComplete": layerTagMigrationComplete = KeyedSerializer.ReadBool(reader, tag, "layerTagMigrationComplete"); break;
                    case "noBackupBuildings": noBackupBuildings = ReadNoBackupBuildings(reader, tag); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new BackupPowerDistributionState(migrationComplete, layerTagMigrationComplete, noBackupBuildings);
        }

        private static BuildingRef[] ReadNoBackupBuildings<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "noBackupBuildings", MaxNoBackupBuildings);
            var entries = new BuildingRef[count];
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
                    entries[written++] = BuildingRef.FromEntity(building);
            }

            if (written == entries.Length)
                return entries;

            var compact = new BuildingRef[written];
            Array.Copy(entries, compact, written);
            return compact;
        }
    }

    public readonly struct BackupPowerEffectsState
    {
        public BackupPowerEffectsState(uint randomState, uint effectTick, float lastGameTimeSeconds)
        {
            RandomState = randomState;
            EffectTick = effectTick;
            LastGameTimeSeconds = lastGameTimeSeconds;
        }

        public uint RandomState { get; }
        public uint EffectTick { get; }
        public float LastGameTimeSeconds { get; }
    }

    public static class BackupPowerEffectsCodec
    {
        public const uint DefaultRandomState = 0x42504553u; // "BPES"

        public static void Write<TWriter>(in BackupPowerEffectsState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((int)state.RandomState));
            KeyedSerializer.WriteField(writer, "effectTick", unchecked((int)state.EffectTick));
            KeyedSerializer.WriteField(writer, "lastGameTimeSeconds", state.LastGameTimeSeconds);
        }

        public static void Read<TReader>(TReader reader, out BackupPowerEffectsState state) where TReader : IReader
        {
            uint randomState = 0;
            uint effectTick = 0;
            float lastGameTimeSeconds = -1f;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "randomState":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "randomState")) break;
                        reader.Read(out int rs);
                        randomState = unchecked((uint)rs);
                        break;
                    case "effectTick":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "effectTick")) break;
                        reader.Read(out int et);
                        effectTick = unchecked((uint)et);
                        break;
                    case "lastGameTimeSeconds":
                        lastGameTimeSeconds = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "lastGameTimeSeconds", -1f);
                        break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new BackupPowerEffectsState(randomState == 0 ? DefaultRandomState : randomState, effectTick, lastGameTimeSeconds);
        }
    }

    public readonly struct TracerSpawnState
    {
        public TracerSpawnState(uint randomState) => RandomState = randomState;
        public uint RandomState { get; }
    }

    public static class TracerSpawnCodec
    {
        public static void Write<TWriter>(in TracerSpawnState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "rng", unchecked((int)state.RandomState));
        }

        public static void Read<TReader>(TReader reader, out TracerSpawnState state) where TReader : IReader
        {
            uint rng = 0;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "rng":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "rng")) break;
                        reader.Read(out int rs);
                        rng = unchecked((uint)rs);
                        break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new TracerSpawnState(rng);
        }
    }

    public static class ConstructionDelayCodec
    {
        public static void Write<TWriter>(TWriter writer) where TWriter : IWriter
            => KeyedSerializer.WriteBlockHeader(writer, 0);

        public static void Read<TReader>(TReader reader) where TReader : IReader
        {
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out _);
                KeyedSerializer.Skip(reader, tag);
            }
        }
    }

    public readonly struct BackupPowerRuntimeState
    {
        public BackupPowerRuntimeState(double lastGameHour, int lastBatteryTier, bool depletionNotified, bool rechargeNotified, bool generatorDepletionNotified, BackupPolicy policy)
        {
            LastGameHour = lastGameHour;
            LastBatteryTier = lastBatteryTier;
            DepletionNotified = depletionNotified;
            RechargeNotified = rechargeNotified;
            GeneratorDepletionNotified = generatorDepletionNotified;
            Policy = policy;
        }

        public double LastGameHour { get; }
        public int LastBatteryTier { get; }
        public bool DepletionNotified { get; }
        public bool RechargeNotified { get; }
        public bool GeneratorDepletionNotified { get; }
        public BackupPolicy Policy { get; }
    }

    public static class BackupPowerRuntimeCodec
    {
        public const int FieldCount = 6;

        public static void Write<TWriter>(in BackupPowerRuntimeState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, FieldCount);
            KeyedSerializer.WriteField(writer, "lastGameHour", state.LastGameHour);
            KeyedSerializer.WriteField(writer, "lastBatteryTier", state.LastBatteryTier);
            KeyedSerializer.WriteField(writer, "depletionNotified", state.DepletionNotified);
            KeyedSerializer.WriteField(writer, "rechargeNotified", state.RechargeNotified);
            KeyedSerializer.WriteField(writer, "generatorDepletionNotified", state.GeneratorDepletionNotified);
            KeyedSerializer.WriteEnumByteField(writer, "policy", (byte)state.Policy);
        }

        public static void Read<TReader>(TReader reader, out BackupPowerRuntimeState state) where TReader : IReader
        {
            double lastGameHour = 0.0;
            int lastBatteryTier = int.MaxValue;
            bool depletionNotified = false;
            bool rechargeNotified = false;
            bool generatorDepletionNotified = false;
            BackupPolicy policy = BackupPolicy.Reserve;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastGameHour": lastGameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "lastGameHour", 0.0); break;
                    case "lastBatteryTier": lastBatteryTier = KeyedSerializer.ReadBoundedInt(reader, tag, "lastBatteryTier", 0, int.MaxValue, int.MaxValue); break;
                    case "depletionNotified": depletionNotified = KeyedSerializer.ReadBool(reader, tag, "depletionNotified"); break;
                    case "rechargeNotified": rechargeNotified = KeyedSerializer.ReadBool(reader, tag, "rechargeNotified"); break;
                    case "generatorDepletionNotified": generatorDepletionNotified = KeyedSerializer.ReadBool(reader, tag, "generatorDepletionNotified"); break;
                    case "policy": policy = KeyedSerializer.ReadEnumByte<TReader, BackupPolicy>(reader, tag, "policy", BackupPolicy.Reserve); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }
            state = new BackupPowerRuntimeState(lastGameHour, lastBatteryTier, depletionNotified, rechargeNotified, generatorDepletionNotified, policy);
        }
    }

    public readonly struct VipProtectionRacketState
    {
        public VipProtectionRacketState(int lastProcessedDay, int pendingPayoutDay)
        {
            LastProcessedDay = lastProcessedDay;
            PendingPayoutDay = pendingPayoutDay;
        }

        public int LastProcessedDay { get; }
        public int PendingPayoutDay { get; }
    }

    public static class VipProtectionRacketCodec
    {
        public const int MaxDay = 100000;

        public static void Write<TWriter>(in VipProtectionRacketState state, TWriter writer) where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
            KeyedSerializer.WriteField(writer, "pendingPayoutDay", state.PendingPayoutDay);
        }

        public static void Read<TReader>(TReader reader, out VipProtectionRacketState state) where TReader : IReader
        {
            int lastProcessedDay = 0;
            int pendingPayoutDay = -1;
            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastProcessedDay": lastProcessedDay = KeyedSerializer.ReadMonotonicCounterWithSentinel(reader, tag, "lastProcessedDay", -1, 0, MaxDay); break;
                    case "pendingPayoutDay": pendingPayoutDay = KeyedSerializer.ReadMonotonicCounterWithSentinel(reader, tag, "pendingPayoutDay", -1, 0, MaxDay); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            int normalizedLastProcessedDay = DayChangedDedupCodec.NormalizeFromSave(lastProcessedDay);
            int normalizedPendingPayoutDay = pendingPayoutDay > normalizedLastProcessedDay ? pendingPayoutDay : -1;
            state = new VipProtectionRacketState(normalizedLastProcessedDay, normalizedPendingPayoutDay);
        }
    }
}
