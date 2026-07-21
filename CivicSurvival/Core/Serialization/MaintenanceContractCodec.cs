using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct MaintenanceContractPersistState
    {
        public MaintenanceContractPersistState(float lastOfferGameDay, double prevGameHours)
        {
            LastOfferGameDay = lastOfferGameDay;
            PrevGameHours = prevGameHours;
        }

        public float LastOfferGameDay { get; }
        public double PrevGameHours { get; }
    }

    public static class MaintenanceContractCodec
    {
        public const float MaxLastOfferGameDay = 100000f;
        public const double MaxPrevGameHours = 10000000.0;

        public static void Write<TWriter>(in MaintenanceContractPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);
            KeyedSerializer.WriteField(writer, "m_LastOfferGameDay", state.LastOfferGameDay);
            KeyedSerializer.WriteField(writer, "m_PrevGameHours", state.PrevGameHours);
        }

        public static void Read<TReader>(TReader reader, out MaintenanceContractPersistState state)
            where TReader : IReader
        {
            float lastOfferGameDay = 0f;
            double prevGameHours = 0.0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "m_LastOfferGameDay":
                        lastOfferGameDay = KeyedSerializer.ReadSafeFloat(reader, tag, "m_LastOfferGameDay", 0f, MaxLastOfferGameDay, 0f);
                        break;
                    case "m_PrevGameHours":
                        prevGameHours = KeyedSerializer.ReadSafeDouble(reader, tag, "m_PrevGameHours", 0.0, MaxPrevGameHours, 0.0);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new MaintenanceContractPersistState(lastOfferGameDay, prevGameHours);
        }
    }
}
