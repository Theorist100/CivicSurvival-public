using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct DefeatCheckPersistState
    {
        /// <summary>
        /// War day value meaning "no population-collapse observation is waiting for the day
        /// boundary". Saves written before the field existed carry no key and read back as
        /// this, i.e. as an unarmed confirmation.
        /// </summary>
        public const int NoPendingCollapseWarDay = -1;

        public DefeatCheckPersistState(float integrityBelowThresholdHours, int pendingCollapseWarDay)
        {
            IntegrityBelowThresholdHours = integrityBelowThresholdHours;
            PendingCollapseWarDay = pendingCollapseWarDay;
        }

        public float IntegrityBelowThresholdHours { get; }

        /// <summary>
        /// War day on which the population-collapse condition was first observed, or
        /// <see cref="NoPendingCollapseWarDay"/> when nothing is pending.
        /// </summary>
        public int PendingCollapseWarDay { get; }
    }

    public static class DefeatCheckCodec
    {
        // Same ceiling the war-day counter itself is read with (ScenarioStateMachineCodec).
        private const int MaxWarDay = 100_000;

        public static void Write<TWriter>(in DefeatCheckPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_IntegrityBelowThresholdHours", state.IntegrityBelowThresholdHours);
            KeyedSerializer.WriteField(writer, "m_PendingCollapseWarDay", state.PendingCollapseWarDay);
        }

        public static void Read<TReader>(TReader reader, out DefeatCheckPersistState state)
            where TReader : IReader
        {
            float integrityBelowThresholdHours = 0f;
            int pendingCollapseWarDay = DefeatCheckPersistState.NoPendingCollapseWarDay;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_IntegrityBelowThresholdHours":
                        integrityBelowThresholdHours = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_IntegrityBelowThresholdHours", 0f);
                        break;
                    case "m_PendingCollapseWarDay":
                        pendingCollapseWarDay = KeyedSerializer.ReadBoundedInt(
                            reader,
                            tag,
                            "m_PendingCollapseWarDay",
                            DefeatCheckPersistState.NoPendingCollapseWarDay,
                            MaxWarDay,
                            DefeatCheckPersistState.NoPendingCollapseWarDay);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DefeatCheckPersistState(integrityBelowThresholdHours, pendingCollapseWarDay);
        }
    }
}
