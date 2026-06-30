using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct ShadowTradeDailyPersistState
    {
        public ShadowTradeDailyPersistState(int lastProcessedDay)
        {
            LastProcessedDay = lastProcessedDay < 0 ? 0 : lastProcessedDay;
        }

        public int LastProcessedDay { get; }
    }

    public static class ShadowTradeDailyCodec
    {
        private const int MaxProcessedDay = 100000;

        public static void Write<TWriter>(in ShadowTradeDailyPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 1);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
        }

        public static void Read<TReader>(TReader reader, out ShadowTradeDailyPersistState state)
            where TReader : IReader
        {
            int lastProcessedDay = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, MaxProcessedDay);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new ShadowTradeDailyPersistState(lastProcessedDay);
        }
    }

    public readonly struct ShadowWalletLockedOperationPersistEntry
    {
        public ShadowWalletLockedOperationPersistEntry(string operationId, long amount)
        {
            OperationId = operationId ?? string.Empty;
            Amount = amount < 0 ? 0 : amount;
        }

        public string OperationId { get; }
        public long Amount { get; }
    }

    public readonly struct ShadowWalletPersistState
    {
        public ShadowWalletPersistState(
            long balance,
            long totalIncome,
            long totalExpenses,
            FreezeReason freezeReason,
            float sanctionsMarkup,
            ShadowWalletLockedOperationPersistEntry[]? lockedOperations,
            string[]? appliedIncomeKeys,
            long lockedBalance = 0,
            int skippedLockedOperationCount = 0)
        {
            Balance = balance < 0 ? 0 : balance;
            TotalIncome = totalIncome < 0 ? 0 : totalIncome;
            TotalExpenses = totalExpenses < 0 ? 0 : totalExpenses;
#pragma warning disable CIVIC140 // FreezeReason is a flags enum; persisted values are masked, not Enum.IsDefined-checked.
            FreezeReason = (FreezeReason)((byte)freezeReason & (byte)FreezeReason.AllFlags);
#pragma warning restore CIVIC140
            SanctionsMarkup = SanitizeMarkup(sanctionsMarkup);
            LockedOperations = lockedOperations ?? System.Array.Empty<ShadowWalletLockedOperationPersistEntry>();
            AppliedIncomeKeys = appliedIncomeKeys ?? System.Array.Empty<string>();
            LockedBalance = lockedBalance < 0 ? 0 : lockedBalance;
            SkippedLockedOperationCount = skippedLockedOperationCount < 0 ? 0 : skippedLockedOperationCount;
        }

        public long Balance { get; }
        public long TotalIncome { get; }
        public long TotalExpenses { get; }
        public FreezeReason FreezeReason { get; }
        public float SanctionsMarkup { get; }
        public long LockedBalance { get; }
        public int SkippedLockedOperationCount { get; }
        public IReadOnlyList<ShadowWalletLockedOperationPersistEntry> LockedOperations { get; }
        public IReadOnlyList<string> AppliedIncomeKeys { get; }

        private static float SanitizeMarkup(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                return 0f;
            }

            return value > ShadowWalletCodec.MaxSanctionsMarkup ? ShadowWalletCodec.MaxSanctionsMarkup : value;
        }
    }

    public static class ShadowWalletCodec
    {
        internal const float MaxSanctionsMarkup = 10f;
        private const int MaxLockedOperations = 256;
        private const int MaxAppliedIncomeKeys = 8192;

        public static void Write<TWriter>(in ShadowWalletPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 8);
            KeyedSerializer.WriteField(writer, "balance", state.Balance);
            KeyedSerializer.WriteField(writer, "totalIncome", state.TotalIncome);
            KeyedSerializer.WriteField(writer, "totalExpenses", state.TotalExpenses);
            KeyedSerializer.WriteEnumByteField(writer, "freezeReason", (byte)state.FreezeReason);
            KeyedSerializer.WriteField(writer, "sanctionsMarkup", state.SanctionsMarkup);
            KeyedSerializer.WriteField(writer, "lockedBalance", state.LockedBalance);
            KeyedSerializer.WriteBufferHeader(writer, "lockedOps", state.LockedOperations.Count);
            for (int i = 0; i < state.LockedOperations.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "id", state.LockedOperations[i].OperationId);
                KeyedSerializer.WriteField(writer, "amount", state.LockedOperations[i].Amount);
            }
            WriteStringBuffer(writer, "appliedIncomeKeys", state.AppliedIncomeKeys);
        }

        public static void Read<TReader>(TReader reader, out ShadowWalletPersistState state)
            where TReader : IReader
        {
            long balance = 0;
            long totalIncome = 0;
            long totalExpenses = 0;
            byte freezeReasonRaw = 0;
            float sanctionsMarkup = 0f;
            long lockedBalance = 0;
            int skippedOps = 0;
            ShadowWalletLockedOperationPersistEntry[] lockedOperations = System.Array.Empty<ShadowWalletLockedOperationPersistEntry>();
            string[] appliedIncomeKeys = System.Array.Empty<string>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "balance":
                        balance = KeyedSerializer.ReadBoundedLong(reader, tag, "balance", 0, long.MaxValue);
                        break;
                    case "totalIncome":
                        totalIncome = KeyedSerializer.ReadBoundedLong(reader, tag, "totalIncome", 0, long.MaxValue);
                        break;
                    case "totalExpenses":
                        totalExpenses = KeyedSerializer.ReadBoundedLong(reader, tag, "totalExpenses", 0, long.MaxValue);
                        break;
                    case "freezeReason":
                        freezeReasonRaw = ReadFreezeReasonRaw(reader, tag);
                        break;
                    case "sanctionsMarkup":
                        sanctionsMarkup = KeyedSerializer.ReadSafeFloat(reader, tag, "sanctionsMarkup", 0f, MaxSanctionsMarkup, 0f);
                        break;
                    case "lockedBalance":
                        lockedBalance = KeyedSerializer.ReadBoundedLong(reader, tag, "lockedBalance", 0, long.MaxValue);
                        break;
                    case "lockedOps":
                        lockedOperations = ReadLockedOperations(reader, tag, out _, out skippedOps);
                        break;
                    case "appliedIncomeKeys":
                        appliedIncomeKeys = ReadStringBuffer(reader, tag, "appliedIncomeKeys", MaxAppliedIncomeKeys);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

#pragma warning disable CIVIC140 // FreezeReason is a flags enum; persisted values are masked, not Enum.IsDefined-checked.
            var freezeReason = (FreezeReason)(freezeReasonRaw & (byte)FreezeReason.AllFlags);
#pragma warning restore CIVIC140
            state = new ShadowWalletPersistState(
                balance,
                totalIncome,
                totalExpenses,
                freezeReason,
                sanctionsMarkup,
                lockedOperations,
                appliedIncomeKeys,
                lockedBalance,
                skippedOps);
        }

        private static byte ReadFreezeReasonRaw<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumByte, "freezeReason"))
            {
                return 0;
            }

            reader.Read(out byte value);
            return value;
        }

        private static ShadowWalletLockedOperationPersistEntry[] ReadLockedOperations<TReader>(
            TReader reader,
            TypeTag tag,
            out long lockedBalance,
            out int skippedOps)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "lockedOps", MaxLockedOperations);
            var entries = new ShadowWalletLockedOperationPersistEntry[count];
            lockedBalance = 0;
            skippedOps = 0;
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                string operationId = string.Empty;
                long amount = 0;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "id":
                            operationId = KeyedSerializer.ReadString(reader, fieldTag, "id");
                            break;
                        case "amount":
                            amount = KeyedSerializer.ReadBoundedLong(reader, fieldTag, "amount", 0, long.MaxValue);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                if (string.IsNullOrEmpty(operationId) || amount > long.MaxValue - lockedBalance)
                {
                    skippedOps++;
                    continue;
                }

                entries[written++] = new ShadowWalletLockedOperationPersistEntry(operationId, amount);
                lockedBalance += amount;
            }

            return Compact(entries, written);
        }

        private static ShadowWalletLockedOperationPersistEntry[] Compact(ShadowWalletLockedOperationPersistEntry[] entries, int count)
        {
            if (count == entries.Length)
            {
                return entries;
            }

            var compact = new ShadowWalletLockedOperationPersistEntry[count];
            System.Array.Copy(entries, compact, count);
            return compact;
        }

        private static string[] Compact(string[] entries, int count)
        {
            if (count == entries.Length)
                return entries;

            var compact = new string[count];
            System.Array.Copy(entries, compact, count);
            return compact;
        }

        private static void WriteStringBuffer<TWriter>(TWriter writer, string key, IReadOnlyList<string> values)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "v", values[i] ?? string.Empty);
            }
        }

        private static string[] ReadStringBuffer<TReader>(TReader reader, TypeTag tag, string name, int maxCount)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, maxCount);
            var entries = new string[count];
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                string value = string.Empty;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "v")
                        value = KeyedSerializer.ReadString(reader, fieldTag, "v");
                    else
                        KeyedSerializer.Skip(reader, fieldTag);
                }

                if (!string.IsNullOrEmpty(value))
                    entries[written++] = value;
            }

            return Compact(entries, written);
        }
    }
}
