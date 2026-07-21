using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to release manpower back to pool.
    /// Processed by MobilizationSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct ManpowerReleaseRequest : IComponentData, ICommandRequest, ISerializable
    {
        public int Count;
        public int AATypeHash;     // (int)AAType or 0 for non-AA
        public int EntityIndex;
        public int EntityVersion;
        public bool DomainApplied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "cnt", Count);
                KeyedSerializer.WriteField(writer, "aaH", AATypeHash);
                KeyedSerializer.WriteField(writer, "idx", EntityIndex);
                KeyedSerializer.WriteField(writer, "ver", EntityVersion);
                KeyedSerializer.WriteField(writer, "applied", DomainApplied);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(ManpowerReleaseRequest)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "cnt": Count = KeyedSerializer.ReadBoundedInt(reader, tag, "cnt", 0, 10000, 0); break;
                            case "aaH": AATypeHash = KeyedSerializer.ReadInt(reader, tag, "aaH"); break;
                            case "idx": EntityIndex = KeyedSerializer.ReadBoundedInt(reader, tag, "idx", 0, int.MaxValue, 0); break;
                            case "ver": EntityVersion = KeyedSerializer.ReadBoundedInt(reader, tag, "ver", 0, int.MaxValue, 0); break;
                            case "applied": DomainApplied = KeyedSerializer.ReadBool(reader, tag, "applied"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    /// <summary>
    /// Request to report casualties.
    /// Processed by MobilizationSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct CasualtyReportRequest : IComponentData, ICommandRequest, ISerializable
    {
        public int Count;
        public int AATypeHash;
        public int EntityIndex;
        public int EntityVersion;
        public bool DomainApplied;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteField(writer, "cnt", Count);
                KeyedSerializer.WriteField(writer, "aaH", AATypeHash);
                KeyedSerializer.WriteField(writer, "idx", EntityIndex);
                KeyedSerializer.WriteField(writer, "ver", EntityVersion);
                KeyedSerializer.WriteField(writer, "applied", DomainApplied);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(CasualtyReportRequest)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "cnt": Count = KeyedSerializer.ReadBoundedInt(reader, tag, "cnt", 0, 10000, 0); break;
                            case "aaH": AATypeHash = KeyedSerializer.ReadInt(reader, tag, "aaH"); break;
                            case "idx": EntityIndex = KeyedSerializer.ReadBoundedInt(reader, tag, "idx", 0, int.MaxValue, 0); break;
                            case "ver": EntityVersion = KeyedSerializer.ReadBoundedInt(reader, tag, "ver", 0, int.MaxValue, 0); break;
                            case "applied": DomainApplied = KeyedSerializer.ReadBool(reader, tag, "applied"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
