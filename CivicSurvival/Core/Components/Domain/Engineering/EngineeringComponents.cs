using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Collections;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Engineering
{
    internal static class UnderConstructionLog
    {
        public static readonly LogContext Log = new("UnderConstruction");
    }

    internal static class DisabledByDisasterLog
    {
        public static readonly LogContext Log = new("DisabledByDisaster");
    }

    internal static class CollapsedProducerLog
    {
        public static readonly LogContext Log = new("CollapsedProducer");
    }

    /// <summary>
    /// Marks a power plant as under construction with delayed activation.
    /// Lives on SEPARATE mod entity (not on vanilla building) to avoid archetype changes.
    /// </summary>
    public struct UnderConstruction : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>
        /// Reference to vanilla building (typed Index+Version).
        /// </summary>
        public BuildingRef Building;

        /// <summary>
        /// Original capacity to restore when construction completes.
        /// </summary>
        public int OriginalCapacity;

        /// <summary>
        /// Game day when construction will complete.
        /// </summary>
        public float CompletionDay;

        /// <summary>
        /// Total length of the construction window in game days (CompletionDay minus start day).
        /// Drives the linear capacity ramp (progress = 1 - remaining/TotalDays) so a plant
        /// delivers a growing fraction of capacity while building instead of a hard zero.
        /// </summary>
        public int TotalDays;

        /// <summary>
        /// Type of plant being constructed (for logging/UI).
        /// </summary>
        public FixedString64Bytes PlantType;

        /// <summary>
        /// Base (already-producing) capacity in kW that is NOT ramped — it produces full MW for
        /// the whole window. 0 for a brand-new plant (the whole nameplate ramps from 0). For an
        /// upgrade-delta window this is the served nameplate at the instant the upgrade was detected;
        /// only the delta (<see cref="OriginalCapacity"/> − this) ramps up. Absent on an older save
        /// (count-prefixed reader skips it) → 0, which is exactly the legacy whole-nameplate ramp.
        /// </summary>
        public int BaseCapacityKW;

        /// <summary>
        /// The <c>PowerCapacityClassifier.ComputeUpgradeHash</c> this sidecar's delta corresponds to.
        /// Lets ConstructionDelaySystem distinguish a further upgrade (hash changed again mid-window)
        /// from the same in-flight one. 0 for a brand-new-plant sidecar that carried no upgrades.
        /// </summary>
        public int UpgradeHash;

        public Entity GetBuildingEntity() => Building.ToEntity();

        // Pre-release: no backward-compat obligation, so the two new fields ("base"/"uHsh") are added
        // under SAVE_VERSION 1 with the field count bumped 5→7. The count-prefixed keyed reader
        // defaults absent fields to 0 for any pre-existing save (the default Skip case), so no version
        // gate is needed and the version constant stays 1.
        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "cap", OriginalCapacity);
                KeyedSerializer.WriteField(writer, "cDay", CompletionDay);
                KeyedSerializer.WriteField(writer, "tDay", TotalDays);
                KeyedSerializer.WriteField(writer, "pType", PlantType.ToString());
                KeyedSerializer.WriteField(writer, "base", BaseCapacityKW);
                KeyedSerializer.WriteField(writer, "uHsh", UpgradeHash);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(UnderConstruction)))
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
                            case "cap": OriginalCapacity = KeyedSerializer.ReadBoundedInt(reader, tag, "cap", 0, SerializationGuard.MaxPlantCapacityKW, 0); break;
                            case "cDay": CompletionDay = KeyedSerializer.ReadSafeFloatUnclamped(reader, tag, "cDay", 0f); break;
                            case "tDay": TotalDays = KeyedSerializer.ReadBoundedInt(reader, tag, "tDay", 0, 100000, 0); break;
                            case "pType": { string s = KeyedSerializer.ReadString(reader, tag, "pType"); PlantType = new FixedString64Bytes(s ?? ""); } break;
                            // New fields. Absent on a pre-existing save → default 0 = legacy whole-nameplate ramp.
                            case "base": BaseCapacityKW = KeyedSerializer.ReadBoundedInt(reader, tag, "base", 0, SerializationGuard.MaxPlantCapacityKW, 0); break;
                            case "uHsh": UpgradeHash = KeyedSerializer.ReadBoundedInt(reader, tag, "uHsh", int.MinValue, int.MaxValue, 0); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnderConstructionLog.Log.Error($"Deserialize {nameof(UnderConstruction)} failed: {ex}");
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
        }
    }

    /// <summary>
    /// Marks a power plant as disabled due to disaster (fire, explosion, etc).
    /// Lives on SEPARATE mod entity (not on vanilla building) to avoid archetype changes.
    /// </summary>
    public struct DisabledByDisaster : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>
        /// Reference to vanilla building (typed Index+Version).
        /// </summary>
        public BuildingRef Building;

        /// <summary>
        /// Original capacity to restore when repairs complete.
        /// </summary>
        public int OriginalCapacity;

        /// <summary>
        /// Game hour when plant will be restored. double to match CreatedHour /
        /// RepairedThroughHour / PlantWear timestamps (no precision split across the
        /// disaster-time fields on long playthroughs).
        /// </summary>
        public double RestoreHour;

        /// <summary>
        /// Game hour when this disaster was created. Repair-completed events
        /// older than this disaster must not cancel it after save/load.
        /// </summary>
        public double CreatedHour;

        /// <summary>
        /// Game hour through which this disaster has been durably repaired,
        /// stamped at the repair transaction point (PlantRepairService.
        /// CompleteRepair → IDisasterRepairSink). When &gt;= <see cref="CreatedHour"/>
        /// the disaster is cancelled — PPDS honours this on load independent of
        /// the transient RepairCompletedEvent (W2 row 3 root fix). 0 = not repaired.
        /// </summary>
        public double RepairedThroughHour;

        /// <summary>
        /// True for major disaster (100% capacity loss), false for minor (50%).
        /// </summary>
        public bool IsMajor;

        /// <summary>
        /// Name of the affected plant (for notifications).
        /// </summary>
        public FixedString64Bytes PlantName;

        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 7);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "cap", OriginalCapacity);
                KeyedSerializer.WriteField(writer, "rHr", RestoreHour);
                KeyedSerializer.WriteField(writer, "cHr", CreatedHour);
                KeyedSerializer.WriteField(writer, "rpH", RepairedThroughHour);
                KeyedSerializer.WriteField(writer, "maj", IsMajor);
                KeyedSerializer.WriteField(writer, "name", PlantName.ToString());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(DisabledByDisaster)))
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
                            case "cap": OriginalCapacity = KeyedSerializer.ReadBoundedInt(reader, tag, "cap", 0, SerializationGuard.MaxPlantCapacityKW, 0); break;
                            case "rHr": RestoreHour = KeyedSerializer.ReadSafeDouble(reader, tag, "rHr", 0.0); break;
                            case "cHr": CreatedHour = KeyedSerializer.ReadSafeDouble(reader, tag, "cHr", 0.0); break;
                            case "rpH": RepairedThroughHour = KeyedSerializer.ReadSafeDouble(reader, tag, "rpH", 0.0); break;
                            case "maj": IsMajor = KeyedSerializer.ReadBool(reader, tag, "maj"); break;
                            case "name": { string s = KeyedSerializer.ReadString(reader, tag, "name"); PlantName = new FixedString64Bytes(s ?? ""); } break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                DisabledByDisasterLog.Log.Error($"Deserialize {nameof(DisabledByDisaster)} failed: {ex}");
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
        }
    }

    /// <summary>
    /// Marks a power producer as disabled during grid collapse.
    /// Lives on SEPARATE mod entity (not on vanilla building) to avoid archetype changes.
    /// </summary>
    public struct CollapsedProducer : IComponentData, ISerializable, IBuildingLinked
    {
        /// <summary>
        /// Reference to vanilla building (typed Index+Version).
        /// </summary>
        public BuildingRef Building;

        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 1);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(CollapsedProducer)))
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
                            case "cap": KeyedSerializer.Skip(reader, tag); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                CollapsedProducerLog.Log.Error($"Deserialize {nameof(CollapsedProducer)} failed: {ex}");
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
        }
    }
}


