using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct WarDamageDebtPersistState
    {
        public WarDamageDebtPersistState(int lastChargedWaveNumber)
            : this(lastChargedWaveNumber, lastChargedWaveNumber, -1, 0)
        {
        }

        public WarDamageDebtPersistState(int lastChargedWaveNumber, int pendingWaveNumber, long pendingDamageCost)
            : this(lastChargedWaveNumber, lastChargedWaveNumber, pendingWaveNumber, pendingDamageCost)
        {
        }

        public WarDamageDebtPersistState(
            int lastChargedWaveNumber,
            int lastSettledWaveNumber,
            int pendingWaveNumber,
            long pendingDamageCost)
        {
            LastChargedWaveNumber = lastChargedWaveNumber < -1 ? -1 : lastChargedWaveNumber;
            LastSettledWaveNumber = lastSettledWaveNumber < -1 ? -1 : lastSettledWaveNumber;
            PendingWaveNumber = pendingWaveNumber < -1 ? -1 : pendingWaveNumber;
            PendingDamageCost = pendingDamageCost < 0 ? 0 : pendingDamageCost;
        }

        public int LastChargedWaveNumber { get; }

        public int LastSettledWaveNumber { get; }

        public int PendingWaveNumber { get; }

        public long PendingDamageCost { get; }
    }

    public static class WarDamageDebtCodec
    {
        public static void Write<TWriter>(in WarDamageDebtPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 4);
            KeyedSerializer.WriteField(writer, "last", state.LastChargedWaveNumber);
            KeyedSerializer.WriteField(writer, "settled", state.LastSettledWaveNumber);
            KeyedSerializer.WriteField(writer, "pendingWave", state.PendingWaveNumber);
            KeyedSerializer.WriteField(writer, "pendingCost", state.PendingDamageCost);
        }

        public static void Read<TReader>(TReader reader, out WarDamageDebtPersistState state)
            where TReader : IReader
        {
            int lastChargedWaveNumber = -1;
            int lastSettledWaveNumber = -1;
            int pendingWaveNumber = -1;
            long pendingDamageCost = 0;

            int fc = KeyedSerializer.ReadBlockFieldCount(reader);
            // Raw legacy single-int payloads cannot be distinguished from a keyed block's
            // field count here. Select any raw migration outside this keyed reader.
            for (int i = 0; i < fc; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "last":
                        lastChargedWaveNumber = KeyedSerializer.ReadBoundedInt(reader, tag, "last", -1, int.MaxValue, -1);
                        break;
                    case "settled":
                        lastSettledWaveNumber = KeyedSerializer.ReadBoundedInt(reader, tag, "settled", -1, int.MaxValue, -1);
                        break;
                    case "pendingWave":
                        pendingWaveNumber = KeyedSerializer.ReadBoundedInt(reader, tag, "pendingWave", -1, int.MaxValue, -1);
                        break;
                    case "pendingCost":
                        pendingDamageCost = KeyedSerializer.ReadBoundedLong(reader, tag, "pendingCost", 0, long.MaxValue);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            if (pendingWaveNumber <= lastChargedWaveNumber)
            {
                pendingWaveNumber = -1;
                pendingDamageCost = 0;
            }

            if (lastSettledWaveNumber < 0 && lastChargedWaveNumber >= 0)
                lastSettledWaveNumber = lastChargedWaveNumber;

            state = new WarDamageDebtPersistState(lastChargedWaveNumber, lastSettledWaveNumber, pendingWaveNumber, pendingDamageCost);
        }
    }
}
