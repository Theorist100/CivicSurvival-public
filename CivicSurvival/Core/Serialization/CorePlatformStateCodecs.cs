using Colossal.Serialization.Entities;
using CivicSurvival.Core.Utils;
using System;
using System.Collections.Generic;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct DistrictPenaltyState
    {
        public DistrictPenaltyState(
            bool infraCollapsed,
            float infraCollapseHoursRemaining,
            float lastInfraCheckHour,
            bool mourningActive,
            float mourningHoursRemaining,
            float mourningHappinessPenalty,
            float lastMourningCheckHour,
            bool preWarTensionActive,
            float preWarHappinessPenalty)
        {
            InfraCollapsed = infraCollapsed;
            InfraCollapseHoursRemaining = infraCollapseHoursRemaining;
            LastInfraCheckHour = lastInfraCheckHour;
            MourningActive = mourningActive;
            MourningHoursRemaining = mourningHoursRemaining;
            MourningHappinessPenalty = mourningHappinessPenalty;
            LastMourningCheckHour = lastMourningCheckHour;
            PreWarTensionActive = preWarTensionActive;
            PreWarHappinessPenalty = preWarHappinessPenalty;
        }

        public bool InfraCollapsed { get; }
        public float InfraCollapseHoursRemaining { get; }
        public float LastInfraCheckHour { get; }
        public bool MourningActive { get; }
        public float MourningHoursRemaining { get; }
        public float MourningHappinessPenalty { get; }
        public float LastMourningCheckHour { get; }
        public bool PreWarTensionActive { get; }
        public float PreWarHappinessPenalty { get; }
    }

    public static class DistrictPenaltyCodec
    {
        public const float MaxPenaltyHours = 8760f;

        public static void Write<TWriter>(in DistrictPenaltyState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 9);
            KeyedSerializer.WriteField(writer, "infraCollapsed", state.InfraCollapsed);
            KeyedSerializer.WriteField(writer, "infraCollapseHours", state.InfraCollapseHoursRemaining);
            KeyedSerializer.WriteField(writer, "lastInfraCheckHour", state.LastInfraCheckHour);
            KeyedSerializer.WriteField(writer, "mourningActive", state.MourningActive);
            KeyedSerializer.WriteField(writer, "mourningHours", state.MourningHoursRemaining);
            KeyedSerializer.WriteField(writer, "mourningPenalty", state.MourningHappinessPenalty);
            KeyedSerializer.WriteField(writer, "lastMourningCheckHour", state.LastMourningCheckHour);
            KeyedSerializer.WriteField(writer, "preWarTensionActive", state.PreWarTensionActive);
            KeyedSerializer.WriteField(writer, "preWarPenalty", state.PreWarHappinessPenalty);
        }

        public static void Read<TReader>(TReader reader, out DistrictPenaltyState state)
            where TReader : IReader
        {
            bool infraCollapsed = false;
            float infraCollapseHoursRemaining = 0f;
            float lastInfraCheckHour = -1f;
            bool mourningActive = false;
            float mourningHoursRemaining = 0f;
            float mourningHappinessPenalty = 0f;
            float lastMourningCheckHour = -1f;
            bool preWarTensionActive = false;
            float preWarHappinessPenalty = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "infraCollapsed":
                        infraCollapsed = KeyedSerializer.ReadBool(reader, tag, "infraCollapsed");
                        break;
                    case "infraCollapseHours":
                        infraCollapseHoursRemaining = KeyedSerializer.ReadSafeFloat(reader, tag, "infraCollapseHours", 0f, MaxPenaltyHours, 0f);
                        break;
                    case "lastInfraCheckHour":
                        lastInfraCheckHour = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "lastInfraCheckHour", -1f);
                        break;
                    case "mourningActive":
                        mourningActive = KeyedSerializer.ReadBool(reader, tag, "mourningActive");
                        break;
                    case "mourningHours":
                        mourningHoursRemaining = KeyedSerializer.ReadSafeFloat(reader, tag, "mourningHours", 0f, MaxPenaltyHours, 0f);
                        break;
                    case "mourningPenalty":
                        mourningHappinessPenalty = KeyedSerializer.ReadSafeFloat(reader, tag, "mourningPenalty", 0f, 1f, 0f);
                        break;
                    case "lastMourningCheckHour":
                        lastMourningCheckHour = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "lastMourningCheckHour", -1f);
                        break;
                    case "preWarTensionActive":
                        preWarTensionActive = KeyedSerializer.ReadBool(reader, tag, "preWarTensionActive");
                        break;
                    case "preWarPenalty":
                        preWarHappinessPenalty = KeyedSerializer.ReadSafeFloat(reader, tag, "preWarPenalty", 0f, 1f, 0f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new DistrictPenaltyState(
                infraCollapsed,
                infraCollapseHoursRemaining,
                lastInfraCheckHour,
                mourningActive,
                mourningHoursRemaining,
                mourningHappinessPenalty,
                lastMourningCheckHour,
                preWarTensionActive,
                preWarHappinessPenalty);
        }
    }

    public readonly struct GameTimePersistState
    {
        public GameTimePersistState(float lastGameHour, float lastNormalizedTime, int currentDay, bool warStarted, int warStartGameDay)
        {
            LastGameHour = lastGameHour;
            LastNormalizedTime = lastNormalizedTime;
            CurrentDay = currentDay;
            WarStarted = warStarted;
            WarStartGameDay = warStartGameDay;
        }

        public float LastGameHour { get; }
        public float LastNormalizedTime { get; }
        public int CurrentDay { get; }
        public bool WarStarted { get; }
        public int WarStartGameDay { get; }
    }

    public static class GameTimeCodec
    {
        public static void Write<TWriter>(in GameTimePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 5);
            KeyedSerializer.WriteField(writer, "m_LastGameHour", state.LastGameHour);
            KeyedSerializer.WriteField(writer, "m_LastNormalizedTime", state.LastNormalizedTime);
            KeyedSerializer.WriteField(writer, "m_CurrentDay", state.CurrentDay);
            KeyedSerializer.WriteField(writer, "m_WarStarted", state.WarStarted);
            KeyedSerializer.WriteField(writer, "m_WarStartGameDay", state.WarStartGameDay);
        }

        public static void Read<TReader>(TReader reader, out GameTimePersistState state)
            where TReader : IReader
        {
            float lastGameHour = 0f;
            float lastNormalizedTime = 0f;
            int currentDay = 0;
            bool warStarted = false;
            int warStartGameDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastGameHour":
                        lastGameHour = KeyedSerializer.ReadSafeFloat(reader, tag, "m_LastGameHour", 0f, GameRate.HOURS_PER_DAY, 0f);
                        break;
                    case "m_LastNormalizedTime":
                        lastNormalizedTime = KeyedSerializer.ReadSafeFloat(reader, tag, "m_LastNormalizedTime", 0f, 1f, 0f);
                        break;
                    case "m_CurrentDay":
                        currentDay = KeyedSerializer.ReadInt(reader, tag, "m_CurrentDay");
                        break;
                    case "m_WarStarted":
                        warStarted = KeyedSerializer.ReadBool(reader, tag, "m_WarStarted");
                        break;
                    case "m_WarStartGameDay":
                        warStartGameDay = KeyedSerializer.ReadInt(reader, tag, "m_WarStartGameDay");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new GameTimePersistState(lastGameHour, lastNormalizedTime, currentDay, warStarted, warStartGameDay);
        }
    }

    public readonly struct HelpState
    {
        public HelpState(bool gridHelpSeen, bool shadowHelpSeen)
        {
            GridHelpSeen = gridHelpSeen;
            ShadowHelpSeen = shadowHelpSeen;
        }

        public bool GridHelpSeen { get; }
        public bool ShadowHelpSeen { get; }
    }

    public static class HelpStateCodec
    {
        public static void Write<TWriter>(in HelpState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_GridHelpSeen", state.GridHelpSeen);
            KeyedSerializer.WriteField(writer, "m_ShadowHelpSeen", state.ShadowHelpSeen);
        }

        public static void Read<TReader>(TReader reader, out HelpState state)
            where TReader : IReader
        {
            bool gridHelpSeen = false;
            bool shadowHelpSeen = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_GridHelpSeen":
                        gridHelpSeen = KeyedSerializer.ReadBool(reader, tag, "m_GridHelpSeen");
                        break;
                    case "m_ShadowHelpSeen":
                        shadowHelpSeen = KeyedSerializer.ReadBool(reader, tag, "m_ShadowHelpSeen");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new HelpState(gridHelpSeen, shadowHelpSeen);
        }
    }

}
