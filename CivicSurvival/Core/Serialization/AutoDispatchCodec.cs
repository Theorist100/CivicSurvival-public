using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct AutoDispatchPersistState
    {
        public AutoDispatchPersistState(float stabilitySeconds, double nextAllowedDispatchSecond, bool enabled, int autoSheddedCount, bool isBlockedByVip)
        {
            StabilitySeconds = stabilitySeconds;
            NextAllowedDispatchSecond = nextAllowedDispatchSecond;
            Enabled = enabled;
            AutoSheddedCount = autoSheddedCount;
            IsBlockedByVip = isBlockedByVip;
        }

        public float StabilitySeconds { get; }
        public double NextAllowedDispatchSecond { get; }
        public bool Enabled { get; }
        public int AutoSheddedCount { get; }
        public bool IsBlockedByVip { get; }
    }

    public static class AutoDispatchCodec
    {
        public static void Write<TWriter>(in AutoDispatchPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 5);
            KeyedSerializer.WriteField(writer, "m_StabilitySeconds", state.StabilitySeconds);
            KeyedSerializer.WriteField(writer, "m_NextAllowedDispatchSecond", state.NextAllowedDispatchSecond);
            KeyedSerializer.WriteField(writer, "enabled", state.Enabled);
            KeyedSerializer.WriteField(writer, "autoShedded", state.AutoSheddedCount);
            KeyedSerializer.WriteField(writer, "blockedByVip", state.IsBlockedByVip);
        }

        public static void Read<TReader>(TReader reader, out AutoDispatchPersistState state)
            where TReader : IReader
        {
            float stabilitySeconds = 0f;
            double nextAllowedDispatchSecond = 0.0;
            bool enabled = false;
            int autoSheddedCount = 0;
            bool isBlockedByVip = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_StabilitySeconds":
                        stabilitySeconds = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_StabilitySeconds", 0f);
                        break;
                    case "m_NextAllowedDispatchSecond":
                        nextAllowedDispatchSecond = ReadDispatchSecond(reader, tag);
                        break;
                    case "enabled":
                        enabled = KeyedSerializer.ReadBool(reader, tag, "enabled");
                        break;
                    case "autoShedded":
                        autoSheddedCount = KeyedSerializer.ReadBoundedInt(reader, tag, "autoShedded", 0, 10000, 0);
                        break;
                    case "blockedByVip":
                        isBlockedByVip = KeyedSerializer.ReadBool(reader, tag, "blockedByVip");
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new AutoDispatchPersistState(stabilitySeconds, nextAllowedDispatchSecond, enabled, autoSheddedCount, isBlockedByVip);
        }

        private static double ReadDispatchSecond<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            switch (tag)
            {
                case TypeTag.F32:
                    return KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_NextAllowedDispatchSecond", 0f);
                case TypeTag.F64:
                    return KeyedSerializer.ReadSafeDouble(reader, tag, "m_NextAllowedDispatchSecond", 0.0);
                default:
                    KeyedSerializer.Skip(reader, tag);
                    return 0.0;
            }
        }
    }
}
