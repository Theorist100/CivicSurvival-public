using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Threats
{
    internal static class SpotterDataLog
    {
        public static readonly LogContext Log = new("SpotterData");
    }

    /// <summary>
    /// Spotter (OSINT) data on separate mod entity.
    /// References vanilla building via Index+Version (NEVER Entity field!).
    ///
    /// IMPORTANT: This is on a SEPARATE entity from the vanilla building.
    /// We NEVER AddComponent to vanilla buildings (causes homeless spike cascade).
    ///
    /// SSOT: No cached Position. Use GetBuildingEntity() + ComponentLookup&lt;Transform&gt;
    /// to resolve building position at runtime (always current after relocate).
    ///
    /// Pattern: Same as AirDefenseInstallation (ThreatData.cs:317-405).
    /// </summary>
    public struct SpotterData : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>Building reference (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Penalty contribution: default 0.02 (-2% per spotter)</summary>
        public float PenaltyContribution;

        /// <summary>Is spotter currently posting? Can be deactivated by SBU/civilian</summary>
        public bool IsActive;

        /// <summary>When to reactivate after being silenced (game time in hours). 0 = no pending</summary>
        public double ReactivateTime;

        /// <summary>District for internet shutdown checks</summary>
#pragma warning disable CIVIC262 // Not an entity ref — vanilla district buffer position
        public int DistrictIndex;
#pragma warning restore CIVIC262

        /// <summary>
        /// Set to true when evacuation is pending budget confirmation.
        /// Serialized — auto-rollback after load (budget request entity is dead).
        /// Also allows SpotterRequestSystem to skip stale dedup
        /// for spotters that are pending ECB deletion in the same frame.
        /// </summary>
        public bool IsEvacuating;

        /// <summary>
        /// True when this spotter is owned by a story character (currently Valera).
        /// Character spotters are singleton — SpotterRequestSystem destroys existing
        /// character spotters before creating a new one, preventing orphan accumulation
        /// across save/load rebind cycles.
        /// </summary>
        public bool IsCharacterSpotter;

        /// <summary>Reconstruct building Entity from typed ref.</summary>
        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void SetDefaults()
        {
            this = default;
            DistrictIndex = -1; // sentinel: -1 = no district (global spotter)
            PenaltyContribution = 0.02f;
            IsActive = true;
            IsEvacuating = false; // L10: defensive — must not survive SetDefaults (ghost spotter prevention)
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "pen", PenaltyContribution);
                KeyedSerializer.WriteField(writer, "act", IsActive);
                KeyedSerializer.WriteField(writer, "react", ReactivateTime);
                KeyedSerializer.WriteDistrictKey(writer, "dist", DistrictIndex);
                KeyedSerializer.WriteField(writer, "evac", IsEvacuating);
                KeyedSerializer.WriteField(writer, "char", IsCharacterSpotter);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(SpotterData)))
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
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "pen": PenaltyContribution = KeyedSerializer.ReadSafeFloat(reader, tag, "pen", 0f, 1f, 0.02f); break;
                            case "act": IsActive = KeyedSerializer.ReadBool(reader, tag, "act"); break;
                            case "react": ReactivateTime = KeyedSerializer.ReadSafeDouble(reader, tag, "react", 0.0); break;
                            case "dist": DistrictIndex = KeyedSerializer.ReadDistrictKey(reader, tag, "dist", -1); break;
                            case "evac": IsEvacuating = KeyedSerializer.ReadBool(reader, tag, "evac"); break;
                            case "char": IsCharacterSpotter = KeyedSerializer.ReadBool(reader, tag, "char"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                    if (Building.Index < 0 || Building.Version == 0)
                    {
                        SpotterDataLog.Log.Warn($"Deserialized SpotterData with invalid {Building} — resetting to defaults");
                        SetDefaults();
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                SpotterDataLog.Log.Error($"Deserialize {nameof(SpotterData)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}


