using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Utils;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Components.Domain.Power
{
    internal static class PlantBaseCapacityLog
    {
        public static readonly LogContext Log = new("PlantBaseCapacity");
    }

    /// <summary>
    /// Upgraded nameplate capacity of the power plant — the
    /// <c>UpgradeUtils.CombineStats</c>-folded sum across all typed power-data
    /// structs (PowerPlantData, EmergencyGeneratorData, WindPoweredData,
    /// SolarPoweredData, GarbagePoweredData, GroundWaterPoweredData,
    /// WaterPoweredData) at classification time. Used as a damage / disaster /
    /// import-cap baseline by the resolver when the per-frame vanilla baseline
    /// cache is empty (first-tick / post-load transient). This component is the
    /// durable post-load nameplate source; only <see cref="OriginalCapacity"/>
    /// is serialized.
    ///
    /// Written by <c>PowerCapacityIndexSystem</c> — added on first index via
    /// ECB, then mutated in place when the drift-detection sweep observes an
    /// <c>UpgradeHash</c> mismatch and re-indexes the plant. Not immutable —
    /// the "Original" prefix refers to the pre-runtime-modifier baseline, not
    /// the first-creation snapshot. Outside connections receive their value
    /// from <c>PowerCapacityIndexSystem.TrySeedOutsideConnectionBaseCapacity</c>
    /// using live slot <c>ElectricityProducer.m_Capacity</c> as the structural
    /// import-cap baseline.
    ///
    /// Present on ALL entities with ElectricityProducer (regular plants and
    /// OutsideConnection).
    /// </summary>
    public struct PlantBaseCapacity : IComponentData, ISerializable
    {
        private const byte SAVE_VERSION = 1;

        /// <summary>
        /// Upgraded nameplate capacity in kW. Reflects current
        /// <c>InstalledUpgrade</c> set, not the value at first creation —
        /// pipeline rewrites this when an upgrade adds/removes a typed power
        /// data contribution.
        /// </summary>
        public int OriginalCapacity;

        public void SetDefaults()
        {
            this = default;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteField(writer, "cap", OriginalCapacity);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(PlantBaseCapacity)))
            {
                SetDefaults();
                return;
            }

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
                            case "cap":
                                OriginalCapacity = KeyedSerializer.ReadBoundedInt(reader, tag, "cap", 0, SerializationGuard.MaxPlantCapacityKW, 0);
                                break;
                            default:
                                KeyedSerializer.Skip(reader, tag);
                                break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PlantBaseCapacityLog.Log.Error($"Deserialize {nameof(PlantBaseCapacity)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}
