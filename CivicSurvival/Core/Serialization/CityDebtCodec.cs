using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct StringLongPersistEntry
    {
        public StringLongPersistEntry(string key, long value)
        {
            Key = key ?? string.Empty;
            Value = value;
        }

        public string Key { get; }
        public long Value { get; }
    }

    public readonly struct IncomeKindLongPersistEntry
    {
        public IncomeKindLongPersistEntry(int kind, long value)
        {
            Kind = kind;
            Value = value;
        }

        public int Kind { get; }
        public long Value { get; }
    }

    public readonly struct CityDebtPersistState
    {
        public CityDebtPersistState(
            StringLongPersistEntry[] debt,
            StringLongPersistEntry[] expenses,
            StringLongPersistEntry[] income,
            IncomeKindLongPersistEntry[] incomeByKind,
            int lastProcessedDay,
            long incomeAtPeriodStart,
            bool incomePeriodInitialized,
            bool restructureActive,
            bool warningPublishedThisCycle)
            : this(
                debt,
                expenses,
                income,
                incomeByKind,
                lastProcessedDay,
                incomeAtPeriodStart,
                incomePeriodInitialized,
                restructureActive,
                warningPublishedThisCycle,
                0,
                0,
                0)
        {
        }

        public CityDebtPersistState(
            StringLongPersistEntry[] debt,
            StringLongPersistEntry[] expenses,
            StringLongPersistEntry[] income,
            IncomeKindLongPersistEntry[] incomeByKind,
            int lastProcessedDay,
            long incomeAtPeriodStart,
            bool incomePeriodInitialized,
            bool restructureActive,
            bool warningPublishedThisCycle,
            int pendingBillingDay,
            long pendingDecisionDebt,
            long pendingPeriodIncome)
        {
            Debt = debt ?? Array.Empty<StringLongPersistEntry>();
            Expenses = expenses ?? Array.Empty<StringLongPersistEntry>();
            Income = income ?? Array.Empty<StringLongPersistEntry>();
            IncomeByKind = incomeByKind ?? Array.Empty<IncomeKindLongPersistEntry>();
            LastProcessedDay = lastProcessedDay;
            IncomeAtPeriodStart = incomeAtPeriodStart;
            IncomePeriodInitialized = incomePeriodInitialized;
            RestructureActive = restructureActive;
            WarningPublishedThisCycle = warningPublishedThisCycle;
            PendingBillingDay = pendingBillingDay < 0 ? 0 : pendingBillingDay;
            PendingDecisionDebt = pendingDecisionDebt < 0 ? 0 : pendingDecisionDebt;
            PendingPeriodIncome = pendingPeriodIncome < 0 ? 0 : pendingPeriodIncome;
        }

        public IReadOnlyList<StringLongPersistEntry> Debt { get; }
        public IReadOnlyList<StringLongPersistEntry> Expenses { get; }
        public IReadOnlyList<StringLongPersistEntry> Income { get; }
        public IReadOnlyList<IncomeKindLongPersistEntry> IncomeByKind { get; }
        public int LastProcessedDay { get; }
        public long IncomeAtPeriodStart { get; }
        public bool IncomePeriodInitialized { get; }
        public bool RestructureActive { get; }
        public bool WarningPublishedThisCycle { get; }
        public int PendingBillingDay { get; }
        public long PendingDecisionDebt { get; }
        public long PendingPeriodIncome { get; }
    }

    public static class CityDebtCodec
    {
        private const int FieldCount = 12;
        public const int MaxStringLongEntries = 256;
        public const int MaxIncomeKindEntries = 32;
        public const int MaxDay = 100000;
        public const int MaxIncomeKind = 5;

        public static void Write<TWriter>(in CityDebtPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, FieldCount);
            WriteStringLongBuffer(writer, "debt", state.Debt);
            WriteStringLongBuffer(writer, "expenses", state.Expenses);
            WriteStringLongBuffer(writer, "income", state.Income);
            WriteIncomeKindLongBuffer(writer, "incomeKinds", state.IncomeByKind);
            KeyedSerializer.WriteField(writer, "lastProcessedDay", state.LastProcessedDay);
            KeyedSerializer.WriteField(writer, "incomeAtPeriodStart", state.IncomeAtPeriodStart);
            KeyedSerializer.WriteField(writer, "incomePeriodInitialized", state.IncomePeriodInitialized);
            KeyedSerializer.WriteField(writer, "restructureActive", state.RestructureActive);
            KeyedSerializer.WriteField(writer, "warningPublishedThisCycle", state.WarningPublishedThisCycle);
            KeyedSerializer.WriteField(writer, "pendingBillingDay", state.PendingBillingDay);
            KeyedSerializer.WriteField(writer, "pendingDecisionDebt", state.PendingDecisionDebt);
            KeyedSerializer.WriteField(writer, "pendingPeriodIncome", state.PendingPeriodIncome);
        }

        public static void Read<TReader>(TReader reader, out CityDebtPersistState state)
            where TReader : IReader
        {
            var debt = Array.Empty<StringLongPersistEntry>();
            var expenses = Array.Empty<StringLongPersistEntry>();
            var income = Array.Empty<StringLongPersistEntry>();
            var incomeByKind = Array.Empty<IncomeKindLongPersistEntry>();
            int lastProcessedDay = 0;
            long incomeAtPeriodStart = 0;
            bool incomePeriodInitialized = false;
            bool restructureActive = false;
            bool warningPublishedThisCycle = false;
            int pendingBillingDay = 0;
            long pendingDecisionDebt = 0;
            long pendingPeriodIncome = 0;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "debt":
                        debt = ReadStringLongBuffer(reader, tag, "debt");
                        break;
                    case "expenses":
                        expenses = ReadStringLongBuffer(reader, tag, "expenses");
                        break;
                    case "income":
                        income = ReadStringLongBuffer(reader, tag, "income");
                        break;
                    case "incomeKinds":
                        incomeByKind = ReadIncomeKindLongBuffer(reader, tag, "incomeKinds");
                        break;
                    case "lastProcessedDay":
                        lastProcessedDay = KeyedSerializer.ReadMonotonicCounter(reader, tag, "lastProcessedDay", 0, MaxDay);
                        break;
                    case "incomeAtPeriodStart":
                        incomeAtPeriodStart = KeyedSerializer.ReadBoundedLong(reader, tag, "incomeAtPeriodStart", 0, long.MaxValue);
                        break;
                    case "incomePeriodInitialized":
                        incomePeriodInitialized = KeyedSerializer.ReadBool(reader, tag, "incomePeriodInitialized");
                        break;
                    case "restructureActive":
                        restructureActive = KeyedSerializer.ReadBool(reader, tag, "restructureActive");
                        break;
                    case "warningPublishedThisCycle":
                        warningPublishedThisCycle = KeyedSerializer.ReadBool(reader, tag, "warningPublishedThisCycle");
                        break;
                    case "pendingBillingDay":
                        pendingBillingDay = KeyedSerializer.ReadBoundedInt(reader, tag, "pendingBillingDay", 0, MaxDay, 0);
                        break;
                    case "pendingDecisionDebt":
                        pendingDecisionDebt = KeyedSerializer.ReadBoundedLong(reader, tag, "pendingDecisionDebt", 0, long.MaxValue);
                        break;
                    case "pendingPeriodIncome":
                        pendingPeriodIncome = KeyedSerializer.ReadBoundedLong(reader, tag, "pendingPeriodIncome", 0, long.MaxValue);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CityDebtPersistState(
                debt,
                expenses,
                income,
                incomeByKind,
                lastProcessedDay,
                incomeAtPeriodStart,
                incomePeriodInitialized,
                restructureActive,
                warningPublishedThisCycle,
                pendingBillingDay,
                pendingDecisionDebt,
                pendingPeriodIncome);
        }

        private static void WriteStringLongBuffer<TWriter>(
            TWriter writer,
            string key,
            IReadOnlyList<StringLongPersistEntry> entries)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "k", entries[i].Key);
                KeyedSerializer.WriteField(writer, "v", entries[i].Value);
            }
        }

        private static StringLongPersistEntry[] ReadStringLongBuffer<TReader>(
            TReader reader,
            TypeTag tag,
            string name)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxStringLongEntries);
            var values = new Dictionary<string, long>(count);
            for (int i = 0; i < count; i++)
            {
                string key = string.Empty;
                long value = 0;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "k":
                            key = KeyedSerializer.ReadString(reader, fieldTag, "k");
                            break;
                        case "v":
                            value = KeyedSerializer.ReadBoundedLong(reader, fieldTag, "v", 0, long.MaxValue);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(key))
                    values[key] = value;
            }

            var entries = new StringLongPersistEntry[values.Count];
            int index = 0;
            foreach (var kvp in values)
                entries[index++] = new StringLongPersistEntry(kvp.Key, kvp.Value);
            return entries;
        }

        private static void WriteIncomeKindLongBuffer<TWriter>(
            TWriter writer,
            string key,
            IReadOnlyList<IncomeKindLongPersistEntry> entries)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, key, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteField(writer, "k", entries[i].Kind);
                KeyedSerializer.WriteField(writer, "v", entries[i].Value);
            }
        }

        private static IncomeKindLongPersistEntry[] ReadIncomeKindLongBuffer<TReader>(
            TReader reader,
            TypeTag tag,
            string name)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, name, MaxIncomeKindEntries);
            var values = new Dictionary<int, long>(count);
            for (int i = 0; i < count; i++)
            {
                int kind = 0;
                long value = 0;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "k":
                            kind = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "k", 0, MaxIncomeKind, 0);
                            break;
                        case "v":
                            value = KeyedSerializer.ReadBoundedLong(reader, fieldTag, "v", 0, long.MaxValue);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                values[kind] = value;
            }

            var entries = new IncomeKindLongPersistEntry[values.Count];
            int index = 0;
            foreach (var kvp in values)
                entries[index++] = new IncomeKindLongPersistEntry(kvp.Key, kvp.Value);
            return entries;
        }
    }
}
