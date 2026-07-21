using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Economy
{
    internal static class EquipmentWearLog
    {
        public static readonly LogContext Log = new("EquipmentWear");
    }

    /// <summary>
    /// Component tracking equipment wear for power producers.
    /// Lives on SEPARATE mod entity (not on vanilla building) to avoid archetype changes.
    /// Wear accumulates when equipment runs at high load (>90%) or overload (>100%).
    /// High wear increases explosion risk.
    ///
    /// Writers (Engineering domain):
    ///   - PlantWearSimulation (wear-percent, overload, IsUnderRepair, RepairEndHour, RepairEpoch)
    ///   - PlantRepairService (full reset on CompleteRepair, repair-end stamp on ApplyRepair)
    ///   - PlantExplosionService (HasExploded, SavedExplosionDamage, SavedExplosionRepairCost; opens ExplosionChargeSettled)
    ///   - DamageAccountingSystem (settles ExplosionChargeSettled — two-phase with PlantExplosionService)
    /// Readers: UI (PowerGridUIPanel), ThreatDamageSystem
    /// </summary>
    public struct EquipmentWear : IComponentData, ISerializable, IBuildingLinked
    {
        private const float MAX_SERIALIZED_HOURS = 1_000_000f;

        /// <summary>
        /// Reference to vanilla building (typed Index+Version).
        /// </summary>
        public BuildingRef Building;

        /// <summary>
        /// Stable unique ID for this plant (survives save/load).
        /// Used by UI to identify plants instead of Entity.Index which can change.
        /// </summary>
        public int StablePlantId;

        /// <summary>
        /// Current wear level (0-1).
        /// 0 = brand new, 1 = critical wear.
        /// </summary>
        public float WearPercent;

        /// <summary>
        /// Accumulated hours running at overload (>100% capacity).
        /// Tracked for statistics/UI.
        /// </summary>
        public float OverloadHours;

        /// <summary>
        /// Game hour when last maintenance/repair was performed.
        /// </summary>
        public float LastMaintenanceHour;

        /// <summary>
        /// True if this producer has experienced explosion damage.
        /// </summary>
        public bool HasExploded;

        /// <summary>
        /// True once the immediate explosion repair charge has been applied
        /// (BudgetDeductRequest issued by DamageAccountingSystem). Set false when
        /// a new explosion is recorded. <c>HasExploded &amp;&amp; !ExplosionChargeSettled</c>
        /// is the durable "charge owed" marker DamageAccountingSystem reconciles
        /// from on load — the transient DamageAppliedEvent is no longer the sole
        /// carrier of the charge (SAVE_LOAD_LIFECYCLE_DOCTRINE Invariant 5,
        /// ReconcilesFrom).
        /// </summary>
        public bool ExplosionChargeSettled;

        /// <summary>
        /// ExplosionDamagePercent baked at the moment of explosion.
        /// Used on load to restore the exact damage value instead of re-deriving from current config,
        /// which may have changed between save and load.
        /// 0 = no explosion or pre-fix save (falls back to current config).
        /// </summary>
        public float SavedExplosionDamage;

        /// <summary>
        /// F-16 (ACC-08): persisted explosion repair cost in city-budget units,
        /// resolved against BalanceConfig at <see cref="HasExploded"/>=true time.
        /// ReconcileUnsettledExplosionCharges and ReissueExplosionCharge read this
        /// stored amount on load, so a config drift (RepairCostPerPercent tuned
        /// between save and load) cannot make the post-load reissue charge differ
        /// from the in-session charge for the same logical event.
        /// 0 = no explosion or pre-fix save (drain falls back to live config).
        /// </summary>
        public long SavedExplosionRepairCost;

        /// <summary>
        /// Original capacity before repair (for restoration after repair completes).
        /// </summary>
        public int OriginalCapacity;

        /// <summary>
        /// Game hour when repair will complete (0 = not under repair).
        /// </summary>
        public float RepairEndHour;

        /// <summary>
        /// Monotonic repair generation. Async wear-job output is applied only if
        /// it was produced for the current repair generation.
        /// </summary>
        public byte RepairEpoch;

        /// <summary>
        /// True if currently under repair.
        /// </summary>
        public readonly bool IsUnderRepair => RepairEndHour > 0f;

        public Entity GetBuildingEntity() => Building.ToEntity();

        public void SetDefaults() => this = CreateDefault();

        // NOTE: IsInDangerZone moved to EquipmentWearUtils
        // to maintain ECS data-only principle and Burst compatibility.
        // Use: EquipmentWearUtils.IsInDangerZone(wear.WearPercent)

        /// <summary>
        /// Create default (new equipment) state.
        /// </summary>
        public static EquipmentWear CreateDefault() => new()
        {
            Building = BuildingRef.Null,
            StablePlantId = 0,
            WearPercent = 0f,
            OverloadHours = 0f,
            LastMaintenanceHour = 0f,
            HasExploded = false,
            ExplosionChargeSettled = false,
            SavedExplosionDamage = 0f,
            SavedExplosionRepairCost = 0L,
            OriginalCapacity = 0,
            RepairEndHour = 0f,
            RepairEpoch = 0
        };

        // v2: adds SavedExplosionRepairCost.
        private const byte SAVE_VERSION = 2;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 12);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteField(writer, "pid", StablePlantId);
                KeyedSerializer.WriteField(writer, "wear", WearPercent);
                KeyedSerializer.WriteField(writer, "ovH", OverloadHours);
                KeyedSerializer.WriteField(writer, "mntH", LastMaintenanceHour);
                KeyedSerializer.WriteField(writer, "expl", HasExploded);
                KeyedSerializer.WriteField(writer, "oCap", OriginalCapacity);
                KeyedSerializer.WriteField(writer, "repH", RepairEndHour);
                KeyedSerializer.WriteField(writer, "expDmg", SavedExplosionDamage);
                KeyedSerializer.WriteField(writer, "expRC", SavedExplosionRepairCost);
                KeyedSerializer.WriteField(writer, "repE", (int)RepairEpoch);
                KeyedSerializer.WriteField(writer, "expChg", ExplosionChargeSettled);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(EquipmentWear)))
            { SetDefaults(); return; }
            SetDefaults();
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
                            case "pid": StablePlantId = KeyedSerializer.ReadBoundedInt(reader, tag, "pid", 0, int.MaxValue, 0); break;
                            case "wear": WearPercent = KeyedSerializer.ReadSafeFloat(reader, tag, "wear", 0f, 1f, 0f); break;
                            case "ovH": OverloadHours = KeyedSerializer.ReadSafeFloat(reader, tag, "ovH", 0f, MAX_SERIALIZED_HOURS, 0f); break;
                            case "mntH": LastMaintenanceHour = KeyedSerializer.ReadSafeFloat(reader, tag, "mntH", 0f, MAX_SERIALIZED_HOURS, 0f); break;
                            case "expl": HasExploded = KeyedSerializer.ReadBool(reader, tag, "expl"); break;
                            case "oCap": OriginalCapacity = KeyedSerializer.ReadBoundedInt(reader, tag, "oCap", 0, SerializationGuard.MaxPlantCapacityKW, 0); break;
                            case "repH": RepairEndHour = KeyedSerializer.ReadSafeFloat(reader, tag, "repH", 0f, MAX_SERIALIZED_HOURS, 0f); break;
                            case "expDmg": SavedExplosionDamage = KeyedSerializer.ReadSafeFloat(reader, tag, "expDmg", 0f, 1f, 0f); break;
                            case "expRC": SavedExplosionRepairCost = KeyedSerializer.ReadLong(reader, tag, "expRC", 0L); break;
                            case "repE": RepairEpoch = (byte)KeyedSerializer.ReadBoundedInt(reader, tag, "repE", 0, byte.MaxValue, 0); break;
                            case "expChg": ExplosionChargeSettled = KeyedSerializer.ReadBool(reader, tag, "expChg"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                EquipmentWearLog.Log.Error($"Deserialize {nameof(EquipmentWear)} failed: {ex}");
                SetDefaults();
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }
    }
}


