using System;
using System.Collections.Generic;
using Colossal.Logging;
using Colossal.Serialization.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct StringStringPersistEntry
    {
        public StringStringPersistEntry(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Key { get; }
        public string Value { get; }
    }

    public readonly struct NarrativeTriggerPersistState
    {
        public NarrativeTriggerPersistState(string triggerKey, StringStringPersistEntry[] context)
            : this(triggerKey, context, 0L)
        {
        }

        public NarrativeTriggerPersistState(string triggerKey, StringStringPersistEntry[] context, long enqueuedGameTimeSeconds)
        {
            TriggerKey = triggerKey ?? string.Empty;
            Context = context ?? Array.Empty<StringStringPersistEntry>();
            EnqueuedGameTimeSeconds = enqueuedGameTimeSeconds < 0L ? 0L : enqueuedGameTimeSeconds;
        }

        public string TriggerKey { get; }
        public IReadOnlyList<StringStringPersistEntry> Context { get; }
        public long EnqueuedGameTimeSeconds { get; }
    }

    public readonly struct NarrativeToastPersistState
    {
        public NarrativeToastPersistState(
            int channel,
            string id,
            string title,
            string message,
            int mood,
            int status)
        {
            Channel = channel;
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            Mood = mood;
            Status = status;
        }

        public int Channel { get; }
        public string Id { get; }
        public string Title { get; }
        public string Message { get; }
        public int Mood { get; }
        public int Status { get; }
    }

    public readonly struct NarrativeNotificationPersistState
    {
        public NarrativeNotificationPersistState(
            NarrativeTriggerPersistState[] pendingTriggers,
            NarrativeToastPersistState[] pendingToasts,
            NarrativeResolverPersistState resolverState = default,
            bool hasResolverState = false)
        {
            PendingTriggers = pendingTriggers ?? Array.Empty<NarrativeTriggerPersistState>();
            PendingToasts = pendingToasts ?? Array.Empty<NarrativeToastPersistState>();
            ResolverState = resolverState;
            HasResolverState = hasResolverState;
        }

        public IReadOnlyList<NarrativeTriggerPersistState> PendingTriggers { get; }
        public IReadOnlyList<NarrativeToastPersistState> PendingToasts { get; }
        public NarrativeResolverPersistState ResolverState { get; }
        public bool HasResolverState { get; }
    }

    public readonly struct NarrativeInfraResolverPersistState
    {
        public NarrativeInfraResolverPersistState(
            int previousBatteryPercent,
            bool wasWinterActive,
            int lastStressZone,
            bool isGridCollapsed,
            bool emittedGridCollapseNarrative,
            bool isImportLimited)
        {
            PreviousBatteryPercent = previousBatteryPercent;
            WasWinterActive = wasWinterActive;
            LastStressZone = lastStressZone;
            IsGridCollapsed = isGridCollapsed;
            EmittedGridCollapseNarrative = emittedGridCollapseNarrative;
            IsImportLimited = isImportLimited;
        }

        public int PreviousBatteryPercent { get; }
        public bool WasWinterActive { get; }
        public int LastStressZone { get; }
        public bool IsGridCollapsed { get; }
        public bool EmittedGridCollapseNarrative { get; }
        public bool IsImportLimited { get; }
    }

    public readonly struct NarrativeCorruptionResolverPersistState
    {
        public NarrativeCorruptionResolverPersistState(int previousExportedMw, bool investigationActive, bool policeActive)
        {
            PreviousExportedMw = previousExportedMw;
            InvestigationActive = investigationActive;
            PoliceActive = policeActive;
        }

        public int PreviousExportedMw { get; }
        public bool InvestigationActive { get; }
        public bool PoliceActive { get; }
    }

    public readonly struct NarrativeCognitiveCooldownPersistEntry
    {
        public NarrativeCognitiveCooldownPersistEntry(int districtIndex, float lastTransitionHour)
        {
            DistrictIndex = districtIndex;
            LastTransitionHour = lastTransitionHour;
        }

        public int DistrictIndex { get; }
        public float LastTransitionHour { get; }
    }

    public readonly struct NarrativeResolverPersistState
    {
        private readonly NarrativeCognitiveCooldownPersistEntry[] m_CognitiveCooldowns;

        public NarrativeResolverPersistState(
            NarrativeInfraResolverPersistState infra,
            NarrativeCorruptionResolverPersistState corruption,
            NarrativeCognitiveCooldownPersistEntry[] cognitiveCooldowns)
        {
            Infra = infra;
            Corruption = corruption;
            m_CognitiveCooldowns = cognitiveCooldowns ?? Array.Empty<NarrativeCognitiveCooldownPersistEntry>();
        }

        public NarrativeInfraResolverPersistState Infra { get; }
        public NarrativeCorruptionResolverPersistState Corruption { get; }
        public IReadOnlyList<NarrativeCognitiveCooldownPersistEntry> CognitiveCooldowns
            => m_CognitiveCooldowns ?? Array.Empty<NarrativeCognitiveCooldownPersistEntry>();
    }

    public static class NarrativeNotificationCodec
    {
        private static readonly ILog Log = Mod.Log;

        public const int MaxMood = 6;
        public const int MaxStatus = 3;
        private const int MaxCognitiveCooldowns = 1024;

        public static void Write<TWriter>(in NarrativeNotificationPersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, state.HasResolverState ? 3 : 2);
            WritePendingTriggers(writer, state.PendingTriggers);
            WritePendingToasts(writer, state.PendingToasts);
            if (state.HasResolverState)
                WriteResolverState(writer, state.ResolverState);
        }

        public static void Read<TReader>(
            TReader reader,
            int maxPendingTriggers,
            int maxPendingToasts,
            int maxContextEntries,
            out NarrativeNotificationPersistState state)
            where TReader : IReader
        {
            var pendingTriggers = Array.Empty<NarrativeTriggerPersistState>();
            var pendingToasts = Array.Empty<NarrativeToastPersistState>();
            var resolverState = default(NarrativeResolverPersistState);
            bool hasResolverState = false;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "pendingTriggers":
                        pendingTriggers = ReadPendingTriggers(reader, tag, maxPendingTriggers, maxContextEntries);
                        break;
                    case "pendingToasts":
                        pendingToasts = ReadPendingToasts(reader, tag, maxPendingToasts);
                        break;
                    case "resolverState":
                        resolverState = ReadResolverState(reader, tag);
                        hasResolverState = true;
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new NarrativeNotificationPersistState(pendingTriggers, pendingToasts, resolverState, hasResolverState);
        }

        private static void WriteResolverState<TWriter>(TWriter writer, in NarrativeResolverPersistState state)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, "resolverState", 1);
            KeyedSerializer.WriteBlockHeader(writer, 3);

            KeyedSerializer.WriteBufferHeader(writer, "infra", 1);
            KeyedSerializer.WriteBlockHeader(writer, 6);
            KeyedSerializer.WriteField(writer, "prevBattery", state.Infra.PreviousBatteryPercent);
            KeyedSerializer.WriteField(writer, "winter", state.Infra.WasWinterActive);
            KeyedSerializer.WriteField(writer, "stressZone", state.Infra.LastStressZone);
            KeyedSerializer.WriteField(writer, "collapsed", state.Infra.IsGridCollapsed);
            KeyedSerializer.WriteField(writer, "collapseNarr", state.Infra.EmittedGridCollapseNarrative);
            KeyedSerializer.WriteField(writer, "importLimited", state.Infra.IsImportLimited);

            KeyedSerializer.WriteBufferHeader(writer, "corruption", 1);
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "prevExportMw", state.Corruption.PreviousExportedMw);
            KeyedSerializer.WriteField(writer, "investigation", state.Corruption.InvestigationActive);
            KeyedSerializer.WriteField(writer, "police", state.Corruption.PoliceActive);

            int cognitiveCount = Math.Min(state.CognitiveCooldowns.Count, MaxCognitiveCooldowns);
            if (state.CognitiveCooldowns.Count > MaxCognitiveCooldowns)
                Log.Warn($"Dropping {state.CognitiveCooldowns.Count - MaxCognitiveCooldowns} narrative cognitive cooldown entries above cap {MaxCognitiveCooldowns}");

            KeyedSerializer.WriteBufferHeader(writer, "cognitive", cognitiveCount);
            for (int i = 0; i < cognitiveCount; i++)
            {
                KeyedSerializer.WriteBlockHeader(writer, 2);
                KeyedSerializer.WriteDistrictKey(writer, "d", state.CognitiveCooldowns[i].DistrictIndex);
                KeyedSerializer.WriteField(writer, "last", state.CognitiveCooldowns[i].LastTransitionHour);
            }
        }

        private static NarrativeResolverPersistState ReadResolverState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "resolverState", 1);
            var infra = default(NarrativeInfraResolverPersistState);
            var corruption = default(NarrativeCorruptionResolverPersistState);
            var cognitive = Array.Empty<NarrativeCognitiveCooldownPersistEntry>();
            for (int i = 0; i < count; i++)
            {
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "infra":
                            infra = ReadInfraResolverState(reader, fieldTag);
                            break;
                        case "corruption":
                            corruption = ReadCorruptionResolverState(reader, fieldTag);
                            break;
                        case "cognitive":
                            cognitive = ReadCognitiveResolverState(reader, fieldTag);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }
            }

            return new NarrativeResolverPersistState(infra, corruption, cognitive);
        }

        private static NarrativeInfraResolverPersistState ReadInfraResolverState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "infra", 1);
            int previousBattery = 100;
            bool winter = false;
            int stressZone = 0;
            bool collapsed = false;
            bool collapseNarr = false;
            bool importLimited = false;
            for (int i = 0; i < count; i++)
            {
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "prevBattery": previousBattery = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "prevBattery", 0, 100, 100); break;
                        case "winter": winter = KeyedSerializer.ReadBool(reader, fieldTag, "winter"); break;
                        case "stressZone": stressZone = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "stressZone", 0, 8, 0); break;
                        case "collapsed": collapsed = KeyedSerializer.ReadBool(reader, fieldTag, "collapsed"); break;
                        case "collapseNarr": collapseNarr = KeyedSerializer.ReadBool(reader, fieldTag, "collapseNarr"); break;
                        case "importLimited": importLimited = KeyedSerializer.ReadBool(reader, fieldTag, "importLimited"); break;
                        default: KeyedSerializer.Skip(reader, fieldTag); break;
                    }
                }
            }
            return new NarrativeInfraResolverPersistState(previousBattery, winter, stressZone, collapsed, collapseNarr, importLimited);
        }

        private static NarrativeCorruptionResolverPersistState ReadCorruptionResolverState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "corruption", 1);
            int previousExport = 0;
            bool investigation = false;
            bool police = false;
            for (int i = 0; i < count; i++)
            {
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        // Signed contract: WriteField persists this as I32, so read preserves the full signed range.
                        case "prevExportMw": previousExport = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "prevExportMw", int.MinValue, int.MaxValue, 0); break;
                        case "investigation": investigation = KeyedSerializer.ReadBool(reader, fieldTag, "investigation"); break;
                        case "police": police = KeyedSerializer.ReadBool(reader, fieldTag, "police"); break;
                        default: KeyedSerializer.Skip(reader, fieldTag); break;
                    }
                }
            }
            return new NarrativeCorruptionResolverPersistState(previousExport, investigation, police);
        }

        private static NarrativeCognitiveCooldownPersistEntry[] ReadCognitiveResolverState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "cognitive", MaxCognitiveCooldowns);
            var entries = new NarrativeCognitiveCooldownPersistEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = -1;
                float last = NarrativeSystemCodec.ReadyForImmediateReactionTime;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "d": district = KeyedSerializer.ReadDistrictKey(reader, fieldTag, "d"); break;
                        case "last": last = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "last", NarrativeSystemCodec.ReadyForImmediateReactionTime); break;
                        default: KeyedSerializer.Skip(reader, fieldTag); break;
                    }
                }
                if (district >= 0)
                    entries[written++] = new NarrativeCognitiveCooldownPersistEntry(district, last);
            }
            if (written == entries.Length)
                return entries;
            var compact = new NarrativeCognitiveCooldownPersistEntry[written];
            Array.Copy(entries, compact, written);
            return compact;
        }

        private static void WritePendingTriggers<TWriter>(
            TWriter writer,
            IReadOnlyList<NarrativeTriggerPersistState> pendingTriggers)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, "pendingTriggers", pendingTriggers.Count);
            for (int i = 0; i < pendingTriggers.Count; i++)
            {
                var trigger = pendingTriggers[i];
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteField(writer, "triggerKey", trigger.TriggerKey);
                KeyedSerializer.WriteField(writer, "time", trigger.EnqueuedGameTimeSeconds);
                KeyedSerializer.WriteBufferHeader(writer, "context", trigger.Context.Count);
                for (int c = 0; c < trigger.Context.Count; c++)
                {
                    KeyedSerializer.WriteBlockHeader(writer, 2);
                    KeyedSerializer.WriteField(writer, "key", trigger.Context[c].Key);
                    KeyedSerializer.WriteField(writer, "value", trigger.Context[c].Value);
                }
            }
        }

        private static void WritePendingToasts<TWriter>(
            TWriter writer,
            IReadOnlyList<NarrativeToastPersistState> pendingToasts)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBufferHeader(writer, "pendingToasts", pendingToasts.Count);
            for (int i = 0; i < pendingToasts.Count; i++)
            {
                var toast = pendingToasts[i];
                KeyedSerializer.WriteBlockHeader(writer, 6);
                KeyedSerializer.WriteField(writer, "channel", toast.Channel);
                KeyedSerializer.WriteField(writer, "id", toast.Id);
                KeyedSerializer.WriteField(writer, "title", toast.Title);
                KeyedSerializer.WriteField(writer, "message", toast.Message);
                KeyedSerializer.WriteField(writer, "mood", toast.Mood);
                KeyedSerializer.WriteField(writer, "status", toast.Status);
            }
        }

        private static NarrativeToastPersistState[] ReadPendingToasts<TReader>(
            TReader reader,
            TypeTag tag,
            int maxPendingToasts)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "pendingToasts", maxPendingToasts);
            var result = new List<NarrativeToastPersistState>(count);
            for (int i = 0; i < count; i++)
            {
                int channel = 0;
                string id = string.Empty;
                string title = string.Empty;
                string message = string.Empty;
                int mood = 0;
                int status = 0;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "channel":
                            channel = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "channel", 0, 1, 0);
                            break;
                        case "id":
                            id = KeyedSerializer.ReadString(reader, fieldTag, "id");
                            break;
                        case "title":
                            title = KeyedSerializer.ReadString(reader, fieldTag, "title");
                            break;
                        case "message":
                            message = KeyedSerializer.ReadString(reader, fieldTag, "message");
                            break;
                        case "mood":
                            mood = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "mood", 0, MaxMood, 0);
                            break;
                        case "status":
                            status = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "status", 0, MaxStatus, 0);
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(id))
                    result.Add(new NarrativeToastPersistState(channel, id, title, message, mood, status));
            }

            return result.ToArray();
        }

        private static NarrativeTriggerPersistState[] ReadPendingTriggers<TReader>(
            TReader reader,
            TypeTag tag,
            int maxPendingTriggers,
            int maxContextEntries)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "pendingTriggers", maxPendingTriggers);
            var result = new List<NarrativeTriggerPersistState>(count);
            for (int i = 0; i < count; i++)
            {
                string triggerKey = string.Empty;
                var context = Array.Empty<StringStringPersistEntry>();
                long enqueuedGameTimeSeconds = 0L;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                    switch (key)
                    {
                        case "triggerKey":
                            triggerKey = KeyedSerializer.ReadString(reader, fieldTag, "triggerKey");
                            break;
                        case "context":
                            context = ReadTriggerContext(reader, fieldTag, maxContextEntries);
                            break;
                        case "time":
                            enqueuedGameTimeSeconds = KeyedSerializer.ReadLong(reader, fieldTag, "time", 0L);
                            if (enqueuedGameTimeSeconds < 0L)
                                enqueuedGameTimeSeconds = 0L;
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(triggerKey))
                    result.Add(new NarrativeTriggerPersistState(triggerKey, context, enqueuedGameTimeSeconds));
            }

            return result.ToArray();
        }

        private static StringStringPersistEntry[] ReadTriggerContext<TReader>(
            TReader reader,
            TypeTag tag,
            int maxContextEntries)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "context", maxContextEntries);
            var context = new Dictionary<string, string>(count);
            for (int i = 0; i < count; i++)
            {
                string key = string.Empty;
                string value = string.Empty;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "key":
                            key = KeyedSerializer.ReadString(reader, fieldTag, "key");
                            break;
                        case "value":
                            value = KeyedSerializer.ReadString(reader, fieldTag, "value");
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(key))
                    context[key] = value;
            }

            var entries = new StringStringPersistEntry[context.Count];
            int index = 0;
            foreach (var kvp in context)
                entries[index++] = new StringStringPersistEntry(kvp.Key, kvp.Value);
            return entries;
        }
    }
}
