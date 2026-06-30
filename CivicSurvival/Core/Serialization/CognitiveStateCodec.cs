using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CognitiveIntegrityPersistEntry
    {
        public CognitiveIntegrityPersistEntry(int districtIndex, float integrity, float lastUpdateTime, bool isCompromised)
        {
            DistrictIndex = districtIndex;
            Integrity = integrity;
            LastUpdateTime = lastUpdateTime;
            IsCompromised = isCompromised;
        }

        public int DistrictIndex { get; }
        public float Integrity { get; }
        public float LastUpdateTime { get; }
        public bool IsCompromised { get; }
    }

    public readonly struct CognitiveStatePersistState
    {
        public CognitiveStatePersistState(
            bool isActive,
            uint randomState,
            float lastDailyTick,
            GlobalInternetMode internetMode,
            GlobalInternetMode lastInternetMode,
            CognitiveIntegrityPersistEntry[] integrityBuffer)
        {
            IsActive = isActive;
            RandomState = randomState == 0u ? 0x434F474Eu : randomState;
            LastDailyTick = lastDailyTick;
            InternetMode = internetMode;
            LastInternetMode = lastInternetMode;
            IntegrityBuffer = integrityBuffer ?? Array.Empty<CognitiveIntegrityPersistEntry>();
        }

        public bool IsActive { get; }
        public uint RandomState { get; }
        public float LastDailyTick { get; }
        public GlobalInternetMode InternetMode { get; }
        public GlobalInternetMode LastInternetMode { get; }
        public IReadOnlyList<CognitiveIntegrityPersistEntry> IntegrityBuffer { get; }
    }

    public static class CognitiveStateCodec
    {
        // District keys go through KeyedSerializer.WriteDistrictKey/ReadDistrictKey
        // (the one unbounded district-identity contract). Only the record COUNT is
        // capped — the former MaxDistrictIndex=500 key clamp wiped every real
        // district's cognitive state on save/load (Cluster A A-1).
        public const int MaxBufferLength = 10000;

        public static void Write<TWriter>(in CognitiveStatePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 6);
            KeyedSerializer.WriteField(writer, "isActive", state.IsActive);
            KeyedSerializer.WriteField(writer, "randomState", unchecked((int)state.RandomState));
            KeyedSerializer.WriteField(writer, "lastDailyTick", state.LastDailyTick);
            KeyedSerializer.WriteEnumByteField(writer, "internetMode", (byte)state.InternetMode);
            KeyedSerializer.WriteEnumByteField(writer, "lastInternetMode", (byte)state.LastInternetMode);
            KeyedSerializer.WriteBufferHeader(writer, "integrityBuffer", state.IntegrityBuffer.Count);
            for (int i = 0; i < state.IntegrityBuffer.Count; i++)
            {
                var entry = state.IntegrityBuffer[i];
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteDistrictKey(writer, "d", entry.DistrictIndex);
                KeyedSerializer.WriteField(writer, "i", entry.Integrity);
                KeyedSerializer.WriteField(writer, "t", entry.LastUpdateTime);
                KeyedSerializer.WriteField(writer, "c", entry.IsCompromised);
            }
        }

        public static void Read<TReader>(TReader reader, out CognitiveStatePersistState state)
            where TReader : IReader
        {
            bool isActive = false;
            uint randomState = 0;
            float lastDailyTick = 0f;
            var internetMode = GlobalInternetMode.Open;
            var lastInternetMode = GlobalInternetMode.Open;
            var entries = Array.Empty<CognitiveIntegrityPersistEntry>();

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "isActive":
                        isActive = KeyedSerializer.ReadBool(reader, tag, "isActive");
                        break;
                    case "randomState":
                        if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.I32, "randomState"))
                            break;
                        reader.Read(out int rawRandomState);
                        randomState = unchecked((uint)rawRandomState);
                        break;
                    case "lastDailyTick":
                        lastDailyTick = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "lastDailyTick", 0f);
                        break;
                    case "internetMode":
                        internetMode = KeyedSerializer.ReadEnumByte<TReader, GlobalInternetMode>(reader, tag, "internetMode", GlobalInternetMode.Open);
                        break;
                    case "lastInternetMode":
                        lastInternetMode = KeyedSerializer.ReadEnumByte<TReader, GlobalInternetMode>(reader, tag, "lastInternetMode", GlobalInternetMode.Open);
                        break;
                    case "integrityBuffer":
                        entries = ReadIntegrityBuffer(reader, tag);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new CognitiveStatePersistState(
                isActive,
                randomState,
                lastDailyTick,
                internetMode,
                lastInternetMode,
                entries);
        }

        private static CognitiveIntegrityPersistEntry[] ReadIntegrityBuffer<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "integrityBuffer", MaxBufferLength);
            var entries = new CognitiveIntegrityPersistEntry[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                int district = 0;
                float integrity = 1f;
                float lastUpdate = 0f;
                bool isCompromised = false;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "d")
                    {
                        district = KeyedSerializer.ReadDistrictKey(reader, fieldTag, "d", 0);
                    }
                    else if (fieldKey == "i")
                    {
                        integrity = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "i", 0f, 1f, 1f);
                    }
                    else if (fieldKey == "t")
                    {
                        lastUpdate = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "t", 0f);
                    }
                    else if (fieldKey == "c")
                    {
                        isCompromised = KeyedSerializer.ReadBool(reader, fieldTag, "c");
                    }
                    else
                    {
                        KeyedSerializer.Skip(reader, fieldTag);
                    }
                }

                if (district >= 0)
                {
                    entries[written] = new CognitiveIntegrityPersistEntry(district, integrity, lastUpdate, isCompromised);
                    written++;
                }
            }

            if (written == entries.Length)
                return entries;

            var compact = new CognitiveIntegrityPersistEntry[written];
            Array.Copy(entries, compact, written);
            return compact;
        }
    }
}
