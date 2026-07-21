using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to record a fire event in a district's modernization program.
    /// Processed by DistrictModernizationSystem.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterCreation)]
    public struct FireRecordRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>District index (session-local runtime key).</summary>
        public int DistrictIndex;

        /// <summary>District entity version paired with <see cref="DistrictIndex"/> for save-stable
        /// persistence via DistrictKeyCodec (engine entity remap). 0 = none/unzoned.</summary>
        public int DistrictVersion;
        public int DayNumber;

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1 + DistrictKeyCodec.FIELDS_PER_KEY);
                DistrictKeyCodec.Write(writer, "distE", new DistrictRef(DistrictIndex, DistrictVersion));
                KeyedSerializer.WriteField(writer, "day", DayNumber);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(FireRecordRequest)))
            { SetDefaults(); return; }
            try
            {
                if (version >= 1)
                {
                    byte districtKind = 0;
                    Entity districtEntity = Entity.Null;
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "distEK": districtKind = DistrictKeyCodec.ReadKind(reader, tag, "distE"); break;
                            case "distE": districtEntity = DistrictKeyCodec.ReadEntity(reader, tag, "distE"); break;
                            case "day": DayNumber = KeyedSerializer.ReadBoundedInt(reader, tag, "day", 0, 100000, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }

                    DistrictKeyCodec.ResolveIndexVersion(districtKind, districtEntity, out DistrictIndex, out DistrictVersion);
                }
            }
            catch (System.Exception ex) { RequestDeserializeLog.Log.Error($"Deserialize failed: {ex}"); SetDefaults(); }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }
}
