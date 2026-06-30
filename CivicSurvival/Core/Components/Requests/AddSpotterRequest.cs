using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Attributes;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Requests
{
    /// <summary>
    /// Request to add SpotterData to a building.
    /// Ephemeral entity pattern - created by Narrative domain, processed by SpotterRequestSystem.
    ///
    /// This decouples Narrative domain from AirDefense domain:
    /// - Narrative creates request entity (no import of AirDefense.Systems)
    /// - SpotterSystem processes requests in its own OnUpdate
    ///
    /// NOTE: Entity is stored as Index+Version in memory, but serialized as Entity
    /// so CS2 remaps it across load.
    /// </summary>
    [RequestPersistence(RequestPersistenceKind.RetainedInput, RetainedRequestTtlPolicy.SimFramesAfterMetaCreatedTime)]
    public struct AddSpotterRequest : IComponentData, ICommandRequest, ISerializable
    {
        /// <summary>Building entity Index (use with BuildingEntityVersion to reconstruct).</summary>
        public int BuildingEntityIndex;

        /// <summary>Building entity Version (use with BuildingEntityIndex to reconstruct).</summary>
        public int BuildingEntityVersion;

        /// <summary>District index for the spotter (-1 if unknown).</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>
        /// True when spotter is owned by a story character.
        /// SpotterRequestSystem destroys existing character spotters before creating
        /// a new one, preventing orphan accumulation across save/load rebind cycles.
        /// </summary>
        public bool IsCharacterSpotter;

        /// <summary>Reconstructs BuildingEntity from Index+Version.</summary>
        public Entity GetBuildingEntity() => new Entity { Index = BuildingEntityIndex, Version = BuildingEntityVersion };

        public void SetDefaults() => this = default;

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 3);
                KeyedSerializer.WriteEntityField(writer, "bldg", GetBuildingEntity());
                KeyedSerializer.WriteDistrictKey(writer, "dist", DistrictIndex);
                KeyedSerializer.WriteField(writer, "char", IsCharacterSpotter);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(AddSpotterRequest)))
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
                            case "bldg":
                                var building = KeyedSerializer.ReadEntity(reader, tag, "bldg");
                                BuildingEntityIndex = building.Index;
                                BuildingEntityVersion = building.Version;
                                break;
                            case "dist": DistrictIndex = KeyedSerializer.ReadDistrictKey(reader, tag, "dist", -1); break;
                            case "char": IsCharacterSpotter = KeyedSerializer.ReadBool(reader, tag, "char"); break;
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
