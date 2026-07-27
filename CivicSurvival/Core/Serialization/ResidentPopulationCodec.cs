using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    /// <summary>
    /// Persisted state of <c>ResidentPopulationModelSystem</c>. Exactly one field: the
    /// pending-day cursor of the exodus catch-up, which is the only thing this system
    /// owns that cannot be recomputed. The population counts that used to travel here
    /// were the mod's second measure of the city and are gone; what the system publishes
    /// now (the eligible-household selection, the per-household live counts and their
    /// sum) is derived from the live entity world and rebuilt after load.
    /// </summary>
    public readonly struct ResidentPopulationPersistState
    {
        public ResidentPopulationPersistState(int pendingDayChanges)
        {
            PendingDayChanges = pendingDayChanges;
        }

        public int PendingDayChanges { get; }
    }

    /// <summary>
    /// Keyed (self-describing) codec for the resident-population persisted state.
    /// Field drift (add/remove/reorder) is absorbed by <see cref="KeyedSerializer"/>
    /// tag fallback; schema drift is handled at the block-version layer by the
    /// caller (<c>SerializationGuard.BeginBlock/TryBeginBlock</c>). No bespoke
    /// per-field version is carried.
    ///
    /// <para>That fallback is what makes the removal of the four count fields a
    /// non-event for existing saves: a save written before the removal still declares
    /// them in its block header, and the read loop below sends every key it does not
    /// recognise to <see cref="KeyedSerializer.Skip"/>. The block version is unchanged
    /// on purpose — nothing about the stream's framing changed, only its contents.</para>
    /// </summary>
    public static class ResidentPopulationCodec
    {
        // Pending days accumulate only between selection-ready transitions; a real
        // backlog is tiny. A wide ceiling still rejects corruption.
        private const int MaxPendingDays = 1_000_000;

        public static void Write<TWriter>(in ResidentPopulationPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "pendingDayChanges", state.PendingDayChanges);
        }

        public static void Read<TReader>(TReader reader, out ResidentPopulationPersistState state)
            where TReader : IReader
        {
            int pendingDayChanges = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "pendingDayChanges":
                        pendingDayChanges = KeyedSerializer.ReadBoundedInt(reader, tag, "pendingDayChanges", 0, MaxPendingDays, 0);
                        break;
                    default:
                        // Retired count fields from pre-removal saves land here.
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ResidentPopulationPersistState(pendingDayChanges);
        }
    }
}
