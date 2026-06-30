using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct RefugeeInfluxPersistState
    {
        public RefugeeInfluxPersistState(bool influxActivated)
            => InfluxActivated = influxActivated;

        public bool InfluxActivated { get; }
    }

    public static class RefugeeInfluxCodec
    {
        public static void Write<TWriter>(in RefugeeInfluxPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "influxActivated", state.InfluxActivated);
        }

        public static void Read<TReader>(TReader reader, out RefugeeInfluxPersistState state)
            where TReader : IReader
        {
            bool influxActivated = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "influxActivated":
                        influxActivated = KeyedSerializer.ReadBool(reader, tag, "influxActivated");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new RefugeeInfluxPersistState(influxActivated);
        }
    }

    public readonly struct RefugeeIntegrationPersistState
    {
        public RefugeeIntegrationPersistState(double lastCheckGameHours, int integrationBatchIndex)
        {
            LastCheckGameHours = lastCheckGameHours;
            IntegrationBatchIndex = integrationBatchIndex;
        }

        public double LastCheckGameHours { get; }
        public int IntegrationBatchIndex { get; }
    }

    public static class RefugeeIntegrationCodec
    {
        public static void Write<TWriter>(in RefugeeIntegrationPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_LastCheckGameHours", state.LastCheckGameHours);
            KeyedSerializer.WriteField(writer, "m_IntegrationBatchIndex", state.IntegrationBatchIndex);
        }

        public static void Read<TReader>(TReader reader, out RefugeeIntegrationPersistState state)
            where TReader : IReader
        {
            double lastCheckGameHours = 0.0;
            int integrationBatchIndex = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastCheckGameHours":
                        lastCheckGameHours = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastCheckGameHours", 0.0);
                        break;
                    case "m_IntegrationBatchIndex":
                        integrationBatchIndex = KeyedSerializer.ReadInt(reader, tag, "m_IntegrationBatchIndex");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new RefugeeIntegrationPersistState(lastCheckGameHours, integrationBatchIndex);
        }
    }

    public readonly struct RefugeeMigrationPersistState
    {
        public RefugeeMigrationPersistState(double lastCheckGameHours, int migrationBatchIndex, int randomState)
        {
            LastCheckGameHours = lastCheckGameHours;
            MigrationBatchIndex = migrationBatchIndex;
            RandomState = randomState;
        }

        public double LastCheckGameHours { get; }
        public int MigrationBatchIndex { get; }
        public int RandomState { get; }
    }

    public static class RefugeeMigrationCodec
    {
        public static void Write<TWriter>(in RefugeeMigrationPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "m_LastCheckGameHours", state.LastCheckGameHours);
            KeyedSerializer.WriteField(writer, "m_MigrationBatchIndex", state.MigrationBatchIndex);
            KeyedSerializer.WriteField(writer, "m_RandomState", state.RandomState);
        }

        public static void Read<TReader>(TReader reader, out RefugeeMigrationPersistState state)
            where TReader : IReader
        {
            double lastCheckGameHours = 0.0;
            int migrationBatchIndex = 0;
            int randomState = 1;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastCheckGameHours":
                        lastCheckGameHours = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastCheckGameHours", 0.0);
                        break;
                    case "m_MigrationBatchIndex":
                        migrationBatchIndex = KeyedSerializer.ReadInt(reader, tag, "m_MigrationBatchIndex");
                        break;
                    case "m_RandomState":
                        randomState = KeyedSerializer.ReadInt(reader, tag, "m_RandomState", 1);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new RefugeeMigrationPersistState(lastCheckGameHours, migrationBatchIndex, randomState);
        }
    }

    public readonly struct RefugeeSupportCostPersistState
    {
        public RefugeeSupportCostPersistState(
            double lastDeductionGameHours,
            int lastRefugeeCount,
            long lastDeductionAmount)
        {
            LastDeductionGameHours = lastDeductionGameHours;
            LastRefugeeCount = lastRefugeeCount;
            LastDeductionAmount = lastDeductionAmount;
        }

        public double LastDeductionGameHours { get; }
        public int LastRefugeeCount { get; }
        public long LastDeductionAmount { get; }
    }

    public static class RefugeeSupportCostCodec
    {
        public static void Write<TWriter>(in RefugeeSupportCostPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "m_LastDeductionGameHours", state.LastDeductionGameHours);
            KeyedSerializer.WriteField(writer, "m_LastRefugeeCount", state.LastRefugeeCount);
            KeyedSerializer.WriteField(writer, "m_LastDeductionAmount", state.LastDeductionAmount);
        }

        public static void Read<TReader>(TReader reader, out RefugeeSupportCostPersistState state)
            where TReader : IReader
        {
            double lastDeductionGameHours = 0.0;
            int lastRefugeeCount = 0;
            long lastDeductionAmount = 0L;

            int persistFieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < persistFieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastDeductionGameHours":
                        lastDeductionGameHours = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastDeductionGameHours", 0.0);
                        break;
                    case "m_LastRefugeeCount":
                        lastRefugeeCount = KeyedSerializer.ReadInt(reader, tag, "m_LastRefugeeCount");
                        break;
                    case "m_LastDeductionAmount":
                        lastDeductionAmount = KeyedSerializer.ReadLong(reader, tag, "m_LastDeductionAmount");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new RefugeeSupportCostPersistState(
                lastDeductionGameHours,
                lastRefugeeCount,
                lastDeductionAmount);
        }
    }

    public readonly struct RefugeeSpawnPersistState
    {
        public RefugeeSpawnPersistState(
            bool active,
            int hoursElapsed,
            int totalRefugeesAdded,
            int spawnCounter,
            int originalPopulation,
            int refugeesAtBorder,
            bool refugeeParkBuiltSent,
            double lastNagGameHour,
            double lastUpdateTime,
            bool shownRefugeeModal,
            bool shownCollapseModal,
            int pendingRefugeeUnits)
        {
            Active = active;
            HoursElapsed = hoursElapsed;
            TotalRefugeesAdded = totalRefugeesAdded;
            SpawnCounter = spawnCounter;
            OriginalPopulation = originalPopulation;
            RefugeesAtBorder = refugeesAtBorder;
            RefugeeParkBuiltSent = refugeeParkBuiltSent;
            LastNagGameHour = lastNagGameHour;
            LastUpdateTime = lastUpdateTime;
            ShownRefugeeModal = shownRefugeeModal;
            ShownCollapseModal = shownCollapseModal;
            PendingRefugeeUnits = pendingRefugeeUnits;
        }

        public bool Active { get; }
        public int HoursElapsed { get; }
        public int TotalRefugeesAdded { get; }
        public int SpawnCounter { get; }
        public int OriginalPopulation { get; }
        public int RefugeesAtBorder { get; }
        public bool RefugeeParkBuiltSent { get; }
        public double LastNagGameHour { get; }
        public double LastUpdateTime { get; }
        public bool ShownRefugeeModal { get; }
        public bool ShownCollapseModal { get; }
        public int PendingRefugeeUnits { get; }
    }

    public static class RefugeeSpawnCodec
    {
        public static void Write<TWriter>(in RefugeeSpawnPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 12);
            KeyedSerializer.WriteField(writer, "m_Active", state.Active);
            KeyedSerializer.WriteField(writer, "m_HoursElapsed", state.HoursElapsed);
            KeyedSerializer.WriteField(writer, "m_TotalRefugeesAdded", state.TotalRefugeesAdded);
            KeyedSerializer.WriteField(writer, "m_SpawnCounter", state.SpawnCounter);
            KeyedSerializer.WriteField(writer, "m_OriginalPopulation", state.OriginalPopulation);
            KeyedSerializer.WriteField(writer, "m_RefugeesAtBorder", state.RefugeesAtBorder);
            KeyedSerializer.WriteField(writer, "m_RefugeeParkBuiltSent", state.RefugeeParkBuiltSent);
            KeyedSerializer.WriteField(writer, "m_LastNagGameHour", state.LastNagGameHour);
            KeyedSerializer.WriteField(writer, "m_LastUpdateTime", state.LastUpdateTime);
            KeyedSerializer.WriteField(writer, "m_ShownRefugeeModal", state.ShownRefugeeModal);
            KeyedSerializer.WriteField(writer, "m_ShownCollapseModal", state.ShownCollapseModal);
            KeyedSerializer.WriteField(writer, "m_PendingRefugeeUnits", state.PendingRefugeeUnits);
        }

        public static void Read<TReader>(TReader reader, out RefugeeSpawnPersistState state)
            where TReader : IReader
        {
            bool active = false;
            int hoursElapsed = 0;
            int totalRefugeesAdded = 0;
            int spawnCounter = 0;
            int originalPopulation = 0;
            int refugeesAtBorder = 0;
            bool refugeeParkBuiltSent = false;
            double lastNagGameHour = 0.0;
            double lastUpdateTime = -1.0;
            bool shownRefugeeModal = false;
            bool shownCollapseModal = false;
            int pendingRefugeeUnits = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_Active":
                        active = KeyedSerializer.ReadBool(reader, tag, "m_Active");
                        break;
                    case "m_HoursElapsed":
                        hoursElapsed = KeyedSerializer.ReadInt(reader, tag, "m_HoursElapsed");
                        break;
                    case "m_TotalRefugeesAdded":
                        totalRefugeesAdded = KeyedSerializer.ReadInt(reader, tag, "m_TotalRefugeesAdded");
                        break;
                    case "m_SpawnCounter":
                        spawnCounter = KeyedSerializer.ReadInt(reader, tag, "m_SpawnCounter");
                        break;
                    case "m_OriginalPopulation":
                        originalPopulation = KeyedSerializer.ReadInt(reader, tag, "m_OriginalPopulation");
                        break;
                    case "m_RefugeesAtBorder":
                        refugeesAtBorder = KeyedSerializer.ReadInt(reader, tag, "m_RefugeesAtBorder");
                        break;
                    case "m_RefugeeParkBuiltSent":
                        refugeeParkBuiltSent = KeyedSerializer.ReadBool(reader, tag, "m_RefugeeParkBuiltSent");
                        break;
                    case "m_LastNagGameHour":
                        lastNagGameHour = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastNagGameHour", 0.0);
                        break;
                    case "m_LastUpdateTime":
                        lastUpdateTime = KeyedSerializer.ReadSafeDouble(reader, tag, "m_LastUpdateTime", -1.0);
                        break;
                    case "m_ShownRefugeeModal":
                        shownRefugeeModal = KeyedSerializer.ReadBool(reader, tag, "m_ShownRefugeeModal");
                        break;
                    case "m_ShownCollapseModal":
                        shownCollapseModal = KeyedSerializer.ReadBool(reader, tag, "m_ShownCollapseModal");
                        break;
                    case "m_PendingRefugeeUnits":
                        pendingRefugeeUnits = KeyedSerializer.ReadInt(reader, tag, "m_PendingRefugeeUnits");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new RefugeeSpawnPersistState(
                active,
                hoursElapsed,
                totalRefugeesAdded,
                spawnCounter,
                originalPopulation,
                refugeesAtBorder,
                refugeeParkBuiltSent,
                lastNagGameHour,
                lastUpdateTime,
                shownRefugeeModal,
                shownCollapseModal,
                pendingRefugeeUnits);
        }
    }

    public readonly struct RefugeeSpawnServicePersistState
    {
        public RefugeeSpawnServicePersistState(uint randomState)
            => RandomState = randomState == 0u ? 1u : randomState;

        public uint RandomState { get; }
    }

    public static class RefugeeSpawnServiceCodec
    {
        public const byte ServiceVersion = 1;

        public static void Write<TWriter>(in RefugeeSpawnServicePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            writer.Write(ServiceVersion);
            writer.Write(state.RandomState);
        }

        public static void Read<TReader>(TReader reader, out RefugeeSpawnServicePersistState state)
            where TReader : IReader
        {
            reader.Read(out byte version);
            if (version == 0 || version > ServiceVersion)
            {
                reader.Read(out uint _);
                state = new RefugeeSpawnServicePersistState(1u);
                return;
            }

            reader.Read(out uint randomState);
            state = new RefugeeSpawnServicePersistState(randomState);
        }

        public static void Skip<TReader>(TReader reader)
            where TReader : IReader
        {
            reader.Read(out byte _);
            reader.Read(out uint _);
        }
    }
}
