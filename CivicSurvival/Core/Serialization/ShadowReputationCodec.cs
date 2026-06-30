using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct ShadowReputationPersistState
    {
        public ShadowReputationPersistState(
            float trustLevel,
            float frozenUntilDay,
            int totalOffersAccepted,
            int totalOffersRejected,
            int totalSchemesSuccessful,
            int totalTimesCaught,
            int lastPassiveRecoveryDay)
        {
            TrustLevel = trustLevel;
            FrozenUntilDay = frozenUntilDay;
            TotalOffersAccepted = totalOffersAccepted;
            TotalOffersRejected = totalOffersRejected;
            TotalSchemesSuccessful = totalSchemesSuccessful;
            TotalTimesCaught = totalTimesCaught;
            LastPassiveRecoveryDay = lastPassiveRecoveryDay;
        }

        public float TrustLevel { get; }
        public float FrozenUntilDay { get; }
        public int TotalOffersAccepted { get; }
        public int TotalOffersRejected { get; }
        public int TotalSchemesSuccessful { get; }
        public int TotalTimesCaught { get; }
        public int LastPassiveRecoveryDay { get; }
    }

    public static class ShadowReputationCodec
    {
        public static void Write<TWriter>(in ShadowReputationPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 7);
            KeyedSerializer.WriteField(writer, "m_TrustLevel", state.TrustLevel);
            KeyedSerializer.WriteField(writer, "m_FrozenUntilDay", state.FrozenUntilDay);
            KeyedSerializer.WriteField(writer, "m_TotalOffersAccepted", state.TotalOffersAccepted);
            KeyedSerializer.WriteField(writer, "m_TotalOffersRejected", state.TotalOffersRejected);
            KeyedSerializer.WriteField(writer, "m_TotalSchemesSuccessful", state.TotalSchemesSuccessful);
            KeyedSerializer.WriteField(writer, "m_TotalTimesCaught", state.TotalTimesCaught);
            KeyedSerializer.WriteField(writer, "m_LastPassiveRecoveryDay", state.LastPassiveRecoveryDay);
        }

        public static void Read<TReader>(TReader reader, out ShadowReputationPersistState state)
            where TReader : IReader
        {
            float trustLevel = 0f;
            float frozenUntilDay = 0f;
            int totalOffersAccepted = 0;
            int totalOffersRejected = 0;
            int totalSchemesSuccessful = 0;
            int totalTimesCaught = 0;
            int lastPassiveRecoveryDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_TrustLevel":
                        trustLevel = KeyedSerializer.ReadSafeFloat(reader, tag, "m_TrustLevel", 0f, 100f, 0f);
                        break;
                    case "m_FrozenUntilDay":
                        frozenUntilDay = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "m_FrozenUntilDay", 0f);
                        break;
                    case "m_TotalOffersAccepted":
                        totalOffersAccepted = KeyedSerializer.ReadMonotonicCounter(reader, tag, "m_TotalOffersAccepted", 0, int.MaxValue);
                        break;
                    case "m_TotalOffersRejected":
                        totalOffersRejected = KeyedSerializer.ReadMonotonicCounter(reader, tag, "m_TotalOffersRejected", 0, int.MaxValue);
                        break;
                    case "m_TotalSchemesSuccessful":
                        totalSchemesSuccessful = KeyedSerializer.ReadMonotonicCounter(reader, tag, "m_TotalSchemesSuccessful", 0, int.MaxValue);
                        break;
                    case "m_TotalTimesCaught":
                        totalTimesCaught = KeyedSerializer.ReadMonotonicCounter(reader, tag, "m_TotalTimesCaught", 0, int.MaxValue);
                        break;
                    case "m_LastPassiveRecoveryDay":
                        lastPassiveRecoveryDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "m_LastPassiveRecoveryDay", 0, 100000);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ShadowReputationPersistState(
                trustLevel,
                frozenUntilDay,
                totalOffersAccepted,
                totalOffersRejected,
                totalSchemesSuccessful,
                totalTimesCaught,
                lastPassiveRecoveryDay);
        }
    }
}
