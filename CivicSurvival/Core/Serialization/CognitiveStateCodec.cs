using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct CognitiveIntegrityPersistEntry
    {
        public CognitiveIntegrityPersistEntry(int districtIndex, float integrity, bool isCompromised)
        {
            DistrictIndex = districtIndex;
            Integrity = integrity;
            IsCompromised = isCompromised;
        }

        public int DistrictIndex { get; }
        public float Integrity { get; }
        public bool IsCompromised { get; }
    }

    public readonly struct CognitiveStatePersistState
    {
        public CognitiveStatePersistState(
            bool isActive,
            GlobalInternetMode internetMode,
            CognitiveIntegrityPersistEntry[] integrityBuffer)
        {
            IsActive = isActive;
            InternetMode = internetMode;
            IntegrityBuffer = integrityBuffer ?? Array.Empty<CognitiveIntegrityPersistEntry>();
        }

        public bool IsActive { get; }
        public GlobalInternetMode InternetMode { get; }
        public IReadOnlyList<CognitiveIntegrityPersistEntry> IntegrityBuffer { get; }
    }

    public static class CognitiveStateCodec
    {
        // District identity rides DistrictKeyCodec (engine entity remap); a raw index does not
        // survive load. Only the record COUNT is capped — the former MaxDistrictIndex=500 key
        // clamp wiped every real district's cognitive state on save/load (Cluster A A-1).
        public const int MaxBufferLength = 10000;

        public static void Write<TWriter>(in CognitiveStatePersistState state, TWriter writer, in LiveDistrictMap map)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 3);
            KeyedSerializer.WriteField(writer, "isActive", state.IsActive);
            KeyedSerializer.WriteEnumByteField(writer, "internetMode", (byte)state.InternetMode);
            KeyedSerializer.WriteBufferHeader(writer, "integrityBuffer", state.IntegrityBuffer.Count);
            int stale = 0;
            for (int i = 0; i < state.IntegrityBuffer.Count; i++)
            {
                var entry = state.IntegrityBuffer[i];
                KeyedSerializer.WriteBlockHeader(writer, 2 + DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "de", entry.DistrictIndex, map, ref stale);
                KeyedSerializer.WriteField(writer, "i", entry.Integrity);
                KeyedSerializer.WriteField(writer, "c", entry.IsCompromised);
            }
            DistrictKeyCodec.LogWrite("CognitiveStateCodec", state.IntegrityBuffer.Count, stale);
        }

        public static void Read<TReader>(TReader reader, out CognitiveStatePersistState state)
            where TReader : IReader
        {
            bool isActive = false;
            var internetMode = GlobalInternetMode.Open;
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
                    case "internetMode":
                        internetMode = KeyedSerializer.ReadEnumByte<TReader, GlobalInternetMode>(reader, tag, "internetMode", GlobalInternetMode.Open);
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
                internetMode,
                entries);
        }

        private static CognitiveIntegrityPersistEntry[] ReadIntegrityBuffer<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "integrityBuffer", MaxBufferLength);
            var entries = new CognitiveIntegrityPersistEntry[count];
            int written = 0;
            int stale = 0;
            for (int i = 0; i < count; i++)
            {
                byte districtKind = 0;
                Entity districtEntity = Entity.Null;
                float integrity = 1f;
                bool isCompromised = false;
                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    if (fieldKey == "deK")
                    {
                        districtKind = DistrictKeyCodec.ReadKind(reader, fieldTag, "de");
                    }
                    else if (fieldKey == "de")
                    {
                        districtEntity = DistrictKeyCodec.ReadEntity(reader, fieldTag, "de");
                    }
                    else if (fieldKey == "i")
                    {
                        integrity = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "i", 0f, 1f, 1f);
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

                var result = DistrictKeyCodec.Resolve(districtKind, districtEntity, out var live);
                if (result == DistrictKeyReadResult.None)
                {
                    // Stale/dropped key — drop the record instead of collapsing it into unzoned (0),
                    // which the former default did (S-PSY-02).
                    stale++;
                    continue;
                }

                int district = result == DistrictKeyReadResult.Unzoned ? DistrictUtils.UNZONED_AREA_INDEX : live.Index;
                entries[written] = new CognitiveIntegrityPersistEntry(district, integrity, isCompromised);
                written++;
            }

            DistrictKeyCodec.LogRead("CognitiveStateCodec", written, stale);

            if (written == entries.Length)
                return entries;

            var compact = new CognitiveIntegrityPersistEntry[written];
            Array.Copy(entries, compact, written);
            return compact;
        }
    }
}
