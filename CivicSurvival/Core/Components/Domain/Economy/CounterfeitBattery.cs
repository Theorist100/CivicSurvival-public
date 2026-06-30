using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Economy
{
    internal static class CounterfeitBatteryLog
    {
        public static readonly LogContext Log = new("CounterfeitBattery");
    }

    /// <summary>
    /// Marks building as having counterfeit backup equipment.
    /// Added when "Your Guy" contractor is chosen in Shadow Procurement.
    /// Lives on SEPARATE mod entity (not on vanilla building) to avoid archetype changes.
    ///
    /// Consequences:
    /// - 5x fire risk multiplier
    /// - 2x faster degradation
    /// - Guaranteed periodic fires (60-90 days)
    /// </summary>
    public struct CounterfeitBattery : IComponentData, ISerializable, IBuildingLinked
    {
        private const float DEFAULT_FIRE_RISK_MULTIPLIER = 5f;
        private const float DEFAULT_DEGRADATION_RATE = 2f;

        /// <summary>Reference to vanilla building (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Fire risk multiplier (5.0x normal).</summary>
        public float FireRiskMultiplier;

        /// <summary>Degradation rate multiplier (2.0x faster wear).</summary>
        public float DegradationRate;

        /// <summary>Game day when counterfeit equipment was installed (for tracking age).</summary>
        public int InstallationDay;

        /// <summary>District index at install time; used for cleanup after boundary redraw.</summary>
        public int InstallationDistrictId;

        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 5);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "frm", FireRiskMultiplier);
                KeyedSerializer.WriteField(writer, "deg", DegradationRate);
                KeyedSerializer.WriteField(writer, "day", InstallationDay);
                KeyedSerializer.WriteField(writer, "dist", InstallationDistrictId);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(CounterfeitBattery)))
            { SetDefaults(); return; }
            try
            {
                SetDefaults();
                if (version >= 1)
                {
                    int fc = KeyedSerializer.ReadBlockFieldCount(reader);
                    for (int i = 0; i < fc; i++)
                    {
                        var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                        switch (key)
                        {
                            case "bldg": Building = BuildingRef.FromEntity(KeyedSerializer.ReadEntity(reader, tag, "bldg")); break;
                            case "frm": FireRiskMultiplier = KeyedSerializer.ReadSafeFloat(reader, tag, "frm", 1f, 100f, DEFAULT_FIRE_RISK_MULTIPLIER); break;
                            case "deg": DegradationRate = KeyedSerializer.ReadSafeFloat(reader, tag, "deg", 0f, 10f, DEFAULT_DEGRADATION_RATE); break;
                            case "day": InstallationDay = KeyedSerializer.ReadBoundedInt(reader, tag, "day", 0, 100000, 0); break;
                            case "dist": InstallationDistrictId = KeyedSerializer.ReadBoundedInt(reader, tag, "dist", -1, int.MaxValue, -1); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                CounterfeitBatteryLog.Log.Error($"Deserialize {nameof(CounterfeitBattery)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void SetDefaults()
        {
            this = default;
            FireRiskMultiplier = DEFAULT_FIRE_RISK_MULTIPLIER;
            DegradationRate = DEFAULT_DEGRADATION_RATE;
            InstallationDistrictId = -1;
        }
    }
}


