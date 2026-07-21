using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Persisted scalar state of <c>ResidentPopulationModelSystem</c>. Holds only the
    /// cheap, serializable scalar result (the five population counts) plus the
    /// pending-day cursor — never the transient selection (eligibility set /
    /// live-citizen counts), which is rebuilt after load.
    /// </summary>
    public readonly struct ResidentPopulationPersistState
    {
        public ResidentPopulationPersistState(
            int aliveResidentCitizens,
            int eligibleHouseholdCount,
            int homelessHouseholdCount,
            int movedInHouseholdCount,
            int pendingDayChanges)
        {
            AliveResidentCitizens = aliveResidentCitizens;
            EligibleHouseholdCount = eligibleHouseholdCount;
            HomelessHouseholdCount = homelessHouseholdCount;
            MovedInHouseholdCount = movedInHouseholdCount;
            PendingDayChanges = pendingDayChanges;
        }

        public int AliveResidentCitizens { get; }
        public int EligibleHouseholdCount { get; }
        public int HomelessHouseholdCount { get; }
        public int MovedInHouseholdCount { get; }
        public int PendingDayChanges { get; }
    }

    /// <summary>
    /// Keyed (self-describing) codec for the resident-population scalar snapshot.
    /// Field drift (add/remove/reorder) is absorbed by <see cref="KeyedSerializer"/>
    /// tag fallback; schema drift is handled at the block-version layer by the
    /// caller (<c>SerializationGuard.BeginBlock/TryBeginBlock</c>). No bespoke
    /// per-field version is carried.
    /// </summary>
    public static class ResidentPopulationCodec
    {
        // Cities are large but bounded; a stream-desync garbage int is caught while
        // legitimate counts pass. Citizens dominate, households are far fewer.
        private const int MaxCitizens = 100_000_000;
        private const int MaxHouseholds = 100_000_000;
        // Pending days accumulate only between selection-ready transitions; a real
        // backlog is tiny. A wide ceiling still rejects corruption.
        private const int MaxPendingDays = 1_000_000;

        public static void Write<TWriter>(in ResidentPopulationPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 5);
            KeyedSerializer.WriteField(writer, "aliveResidentCitizens", state.AliveResidentCitizens);
            KeyedSerializer.WriteField(writer, "eligibleHouseholdCount", state.EligibleHouseholdCount);
            KeyedSerializer.WriteField(writer, "homelessHouseholdCount", state.HomelessHouseholdCount);
            KeyedSerializer.WriteField(writer, "movedInHouseholdCount", state.MovedInHouseholdCount);
            KeyedSerializer.WriteField(writer, "pendingDayChanges", state.PendingDayChanges);
        }

        public static void Read<TReader>(TReader reader, out ResidentPopulationPersistState state)
            where TReader : IReader
        {
            int aliveResidentCitizens = 0;
            int eligibleHouseholdCount = 0;
            int homelessHouseholdCount = 0;
            int movedInHouseholdCount = 0;
            int pendingDayChanges = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "aliveResidentCitizens":
                        aliveResidentCitizens = KeyedSerializer.ReadBoundedInt(reader, tag, "aliveResidentCitizens", 0, MaxCitizens, 0);
                        break;
                    case "eligibleHouseholdCount":
                        eligibleHouseholdCount = KeyedSerializer.ReadBoundedInt(reader, tag, "eligibleHouseholdCount", 0, MaxHouseholds, 0);
                        break;
                    case "homelessHouseholdCount":
                        homelessHouseholdCount = KeyedSerializer.ReadBoundedInt(reader, tag, "homelessHouseholdCount", 0, MaxHouseholds, 0);
                        break;
                    case "movedInHouseholdCount":
                        movedInHouseholdCount = KeyedSerializer.ReadBoundedInt(reader, tag, "movedInHouseholdCount", 0, MaxHouseholds, 0);
                        break;
                    case "pendingDayChanges":
                        pendingDayChanges = KeyedSerializer.ReadBoundedInt(reader, tag, "pendingDayChanges", 0, MaxPendingDays, 0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ResidentPopulationPersistState(
                aliveResidentCitizens,
                eligibleHouseholdCount,
                homelessHouseholdCount,
                movedInHouseholdCount,
                pendingDayChanges);
        }
    }
}
