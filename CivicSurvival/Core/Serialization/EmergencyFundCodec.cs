using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct EmergencyFundPersistState
    {
        public EmergencyFundPersistState(
            double initialBalance,
            int withdrawPercent,
            double withdrawnAmount,
            int lastProcessedDay)
        {
            InitialBalance = initialBalance;
            WithdrawPercent = withdrawPercent;
            WithdrawnAmount = withdrawnAmount;
            LastProcessedDay = lastProcessedDay;
        }

        public double InitialBalance { get; }
        public int WithdrawPercent { get; }
        public double WithdrawnAmount { get; }
        public int LastProcessedDay { get; }
    }

    public static class EmergencyFundCodec
    {
        private static readonly int[] WithdrawPresets = { 0, 25, 50, 75, 100 };

        public static void Write<TWriter>(in EmergencyFundPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 4);
            KeyedSerializer.WriteField(writer, "initialBalance", double.IsFinite(state.InitialBalance) ? state.InitialBalance : 0.0);
            KeyedSerializer.WriteField(writer, "withdrawPercent", state.WithdrawPercent);
            KeyedSerializer.WriteField(writer, "withdrawnAmount", double.IsFinite(state.WithdrawnAmount) ? state.WithdrawnAmount : 0.0);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
        }

        public static void Read<TReader>(TReader reader, double defaultInitialBalance, out EmergencyFundPersistState state)
            where TReader : IReader
        {
            double initialBalance = 0.0;
            int withdrawPercent = 0;
            double withdrawnAmount = 0.0;
            int lastProcessedDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "initialBalance":
                        initialBalance = KeyedSerializer.ReadSafeDouble(reader, tag, "initialBalance", 0.0);
                        break;
                    case "withdrawPercent":
                        withdrawPercent = KeyedSerializer.ReadBoundedInt(reader, tag, "withdrawPercent", 0, 100, 0);
                        break;
                    case "withdrawnAmount":
                        withdrawnAmount = KeyedSerializer.ReadSafeDouble(reader, tag, "withdrawnAmount", 0.0);
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, 100000);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            if (initialBalance <= 0)
                initialBalance = defaultInitialBalance;

            withdrawPercent = CodecMath.SnapToPreset(withdrawPercent, WithdrawPresets);
            withdrawnAmount = initialBalance > 0
                ? Clamp(withdrawnAmount, 0.0, initialBalance)
                : 0.0;

            state = new EmergencyFundPersistState(
                initialBalance,
                withdrawPercent,
                withdrawnAmount,
                lastProcessedDay);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
