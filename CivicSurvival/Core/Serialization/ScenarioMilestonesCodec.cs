using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct ScenarioMilestonesPersistState
    {
        public ScenarioMilestonesPersistState(
            bool warFatigueShown,
            bool warFatigueDismissed,
            bool victoryShown,
            int victoryTargetDay,
            bool victoryDismissed,
            int oneMoreYearCount)
        {
            WarFatigueShown = warFatigueShown;
            WarFatigueDismissed = warFatigueDismissed;
            VictoryShown = victoryShown;
            VictoryTargetDay = victoryTargetDay;
            VictoryDismissed = victoryDismissed;
            OneMoreYearCount = oneMoreYearCount;
        }

        public bool WarFatigueShown { get; }
        public bool WarFatigueDismissed { get; }
        public bool VictoryShown { get; }
        public int VictoryTargetDay { get; }
        public bool VictoryDismissed { get; }
        public int OneMoreYearCount { get; }
    }

    public static class ScenarioMilestonesCodec
    {
        public static void Write<TWriter>(in ScenarioMilestonesPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 6);
            KeyedSerializer.WriteField(writer, "m_WarFatigueShown", state.WarFatigueShown);
            KeyedSerializer.WriteField(writer, "m_WarFatigueDismissed", state.WarFatigueDismissed);
            KeyedSerializer.WriteField(writer, "m_VictoryShown", state.VictoryShown);
            KeyedSerializer.WriteField(writer, "m_VictoryTargetDay", state.VictoryTargetDay);
            KeyedSerializer.WriteField(writer, "m_VictoryDismissed", state.VictoryDismissed);
            KeyedSerializer.WriteField(writer, "m_OneMoreYearCount", state.OneMoreYearCount);
        }

        public static void Read<TReader>(TReader reader, out ScenarioMilestonesPersistState state)
            where TReader : IReader
        {
            bool warFatigueShown = false;
            bool warFatigueDismissed = false;
            bool victoryShown = false;
            int victoryTargetDay = 0;
            bool victoryDismissed = false;
            int oneMoreYearCount = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_WarFatigueShown": warFatigueShown = KeyedSerializer.ReadBool(reader, tag, "m_WarFatigueShown"); break;
                    case "m_WarFatigueDismissed": warFatigueDismissed = KeyedSerializer.ReadBool(reader, tag, "m_WarFatigueDismissed"); break;
                    case "m_VictoryShown": victoryShown = KeyedSerializer.ReadBool(reader, tag, "m_VictoryShown"); break;
                    case "m_VictoryTargetDay": victoryTargetDay = KeyedSerializer.ReadInt(reader, tag, "m_VictoryTargetDay"); break;
                    case "m_VictoryDismissed": victoryDismissed = KeyedSerializer.ReadBool(reader, tag, "m_VictoryDismissed"); break;
                    case "m_OneMoreYearCount": oneMoreYearCount = KeyedSerializer.ReadInt(reader, tag, "m_OneMoreYearCount"); break;
                    default: KeyedSerializer.Skip(reader, tag); break;
                }
            }

            state = new ScenarioMilestonesPersistState(
                warFatigueShown,
                warFatigueDismissed,
                victoryShown,
                victoryTargetDay,
                victoryDismissed,
                oneMoreYearCount);
        }
    }
}
