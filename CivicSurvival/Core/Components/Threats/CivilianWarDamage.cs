using Colossal.Serialization.Entities;
using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using CivicSurvival.Core.Utils;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Threats
{
    /// <summary>
    /// Progressive damage tracker for non-power-plant buildings.
    /// Lives on separate mod entities (same pattern as PowerPlantDamage).
    /// Supports two-lane repair (Municipal / Shadow Ops) via RepairEndHour.
    /// </summary>
    public struct CivilianWarDamage : IComponentData, ISerializable
    {
        /// <summary>Reference to vanilla building (typed Index+Version).</summary>
        public BuildingRef Building;
        /// <summary>Number of hits received.</summary>
        public int HitCount;
        /// <summary>Game hour when repair completes (0 = not repairing).</summary>
        public float RepairEndHour;
        /// <summary>RepairType enum as byte (0=Municipal, 1=MunicipalWithKickback, 2=ShadowOps).</summary>
        public byte RepairTypeByte;

        /// <summary>True if currently under repair.</summary>
        public readonly bool IsUnderRepair => RepairEndHour > 0f;
        /// <summary>Repair type (decoded from byte).</summary>
        public readonly RepairType RepairType => (RepairType)RepairTypeByte;

        /// <summary>Reconstruct vanilla building Entity from typed ref.</summary>
        public readonly Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;
        private const float MAX_SERIALIZED_HOURS = 1_000_000f;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 4);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "hits", HitCount);
                KeyedSerializer.WriteField(writer, "repEnd", RepairEndHour);
                KeyedSerializer.WriteField(writer, "repType", RepairTypeByte);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(CivilianWarDamage)))
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
                            // Destroy threshold is config-scaled (DurabilityMultiplier) so HitCount
                            // legitimately exceeds 100; the old [0,100] read reset any such building
                            // to 0 on load = free repair (G7 A-7). Persist raw; guard only garbage.
                            case "hits": HitCount = KeyedSerializer.ReadBoundedInt(reader, tag, "hits", 0, int.MaxValue, 0); break;
                            case "repEnd": RepairEndHour = KeyedSerializer.ReadSafeFloat(reader, tag, "repEnd", 0f, MAX_SERIALIZED_HOURS, 0f); break;
                            case "repType": RepairTypeByte = KeyedSerializer.ReadBoundedByte(reader, tag, "repType", 0, 2, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                CivilianWarDamageLog.Log.Error($"Deserialize {nameof(CivilianWarDamage)} failed: {ex}");
                SetDefaults();
            }
            finally { SerializationGuard.EndBlock(reader, block); }
        }
    }

    internal static class CivilianWarDamageLog
    {
        public static readonly LogContext Log = new("CivilianWarDamage");
    }
}
