using Colossal.Serialization.Entities;
using CivicSurvival.Core.Components.Domain.Cognitive;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct TelemarathonPersistState
    {
        public TelemarathonPersistState(
            bool isActive,
            NarrativeMode mode,
            float trust,
            float lastModeChangeHour,
            float shockHoursRemaining,
            float shockEndHour,
            float audienceFatigue,
            float shockCooldownEndHour)
        {
            IsActive = isActive;
            Mode = mode;
            Trust = trust;
            LastModeChangeHour = lastModeChangeHour;
            ShockHoursRemaining = shockHoursRemaining;
            ShockEndHour = shockEndHour;
            AudienceFatigue = audienceFatigue;
            ShockCooldownEndHour = shockCooldownEndHour;
        }

        public bool IsActive { get; }
        public NarrativeMode Mode { get; }
        public float Trust { get; }
        public float LastModeChangeHour { get; }
        public float ShockHoursRemaining { get; }
        public float ShockEndHour { get; }
        public float AudienceFatigue { get; }
        public float ShockCooldownEndHour { get; }
    }

    public static class TelemarathonCodec
    {
        public const float MaxShockHoursRemaining = 48f;

        public static void Write<TWriter>(in TelemarathonPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 8);
            KeyedSerializer.WriteField(writer, "isActive", state.IsActive);
            KeyedSerializer.WriteEnumByteField(writer, "mode", (byte)state.Mode);
            KeyedSerializer.WriteField(writer, "trust", state.Trust);
            KeyedSerializer.WriteField(writer, "lastModeChangeHour", state.LastModeChangeHour);
            KeyedSerializer.WriteField(writer, "shockHoursRemaining", state.ShockHoursRemaining);
            KeyedSerializer.WriteField(writer, "shockEndHour", state.ShockEndHour);
            KeyedSerializer.WriteField(writer, "audienceFatigue", state.AudienceFatigue);
            KeyedSerializer.WriteField(writer, "shockCooldownEndHour", state.ShockCooldownEndHour);
        }

        public static void Read<TReader>(TReader reader, out TelemarathonPersistState state)
            where TReader : IReader
        {
            bool isActive = false;
            var mode = NarrativeMode.Realistic;
            float trust = TelemarathonDefaults.DefaultTrust;
            float lastModeChangeHour = 0f;
            float shockHoursRemaining = 0f;
            float shockEndHour = 0f;
            float audienceFatigue = 0f;
            float shockCooldownEndHour = 0f;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "isActive":
                        isActive = KeyedSerializer.ReadBool(reader, tag, "isActive");
                        break;
                    case "mode":
                        mode = KeyedSerializer.ReadEnumByte<TReader, NarrativeMode>(reader, tag, "mode", NarrativeMode.Realistic);
                        break;
                    case "trust":
                        trust = KeyedSerializer.ReadSafeFloat(reader, tag, "trust", 0f, 1f, 0.7f);
                        break;
                    case "lastModeChangeHour":
                        lastModeChangeHour = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "lastModeChangeHour", 0f);
                        break;
                    case "shockHoursRemaining":
                        shockHoursRemaining = KeyedSerializer.ReadSafeFloat(reader, tag, "shockHoursRemaining", 0f, MaxShockHoursRemaining, 0f);
                        break;
                    case "shockEndHour":
                        shockEndHour = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "shockEndHour", 0f);
                        break;
                    case "audienceFatigue":
                        audienceFatigue = KeyedSerializer.ReadSafeFloat(reader, tag, "audienceFatigue", 0f, 1f, 0f);
                        break;
                    case "shockCooldownEndHour":
                        shockCooldownEndHour = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "shockCooldownEndHour", 0f);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new TelemarathonPersistState(
                isActive,
                mode,
                trust,
                lastModeChangeHour,
                shockHoursRemaining,
                shockEndHour,
                audienceFatigue,
                shockCooldownEndHour);
        }
    }
}
