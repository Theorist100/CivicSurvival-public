using CivicSurvival.Core.Serialization;
using CivicSurvival.Core.Types;
using System;
using Colossal.Serialization.Entities;
using Unity.Entities;
using CivicSurvival.Core.Interfaces.Core;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Components.Domain.Power
{
    internal static class BackupPowerLog
    {
        public static readonly LogContext Log = new("BackupPower");
    }

    /// <summary>
    /// Backup power type - determines characteristics.
    /// Schema only - no logic, no balance values.
    /// </summary>
    public enum BackupPowerType : byte
    {
        None = 0,
        HomeBattery = 1,        // EcoFlow 2 kWh, quiet, low fire risk
        BusinessUPS = 2,        // UPS 10 kWh, quiet, low fire risk
        IndustrialBattery = 3,  // 100 kWh, quiet, medium fire risk
        DieselGenerator = 4     // Fuel-based, noisy, fire risk
    }

    /// <summary>
    /// Backup power component - lives on SEPARATE mod entity (not on vanilla building).
    /// Provides power during grid outages.
    ///
    /// Schema only - this is a DATA CONTRACT, not logic.
    /// Config/balance logic lives in BackupPower domain.
    /// </summary>
    public struct BackupPower : IComponentData, ISerializable, IBuildingLinked
    {
        private const float DEFAULT_EFFICIENCY = 0.9f;
        private const float DEFAULT_BATTERY_FUEL_HOURS = -1f;
        public const float MAX_DEGRADATION = 1f;

        /// <summary>Reference to vanilla building (typed Index+Version).</summary>
        public BuildingRef Building;

        /// <summary>Type of backup power source.</summary>
        public BackupPowerType Type;

        /// <summary>Maximum capacity in Watt-hours. Zero for fuel-only generators.</summary>
        public int CapacityWh;

        /// <summary>Current charge in Watt-hours.</summary>
        public int CurrentChargeWh;

        /// <summary>Charge rate in Watts (how fast it charges from grid).</summary>
        public int ChargeRateW;

        /// <summary>Discharge rate in Watts (max power output).</summary>
        public int DischargeRateW;

        /// <summary>Efficiency 0.8-0.95 (energy loss during charge/discharge).</summary>
        public float Efficiency;

        /// <summary>Degradation 0-1 (reduces effective capacity over time).</summary>
        public float Degradation;

        /// <summary>For generators: hours of fuel remaining. -1 for batteries.</summary>
        public float FuelHours;

        /// <summary>Is currently discharging (no grid power).</summary>
        public bool IsDischarging;

        /// <summary>Transient fire intent written by effects job and consumed by effects system.</summary>
        public BackupPowerType PendingFireType;

        /// <summary>Sub-Wh charge remainder for precision accumulation (not serialized — ephemeral).</summary>
        public double ChargeRemainder;
        /// <summary>Sub-Wh discharge remainder for precision accumulation (not serialized — ephemeral).</summary>
        public double DischargeRemainder;

        public Entity GetBuildingEntity() => Building.ToEntity();

        private const byte SAVE_VERSION = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = SerializationGuard.BeginBlock(writer, SAVE_VERSION);
            try
            {
                KeyedSerializer.WriteBlockHeader(writer, 10);
                KeyedSerializer.WriteEntityField(writer, "bldg", Building.ToEntity());
                KeyedSerializer.WriteEnumByteField(writer, "type", (byte)Type);
                KeyedSerializer.WriteField(writer, "cap", CapacityWh);
                KeyedSerializer.WriteField(writer, "chg", CurrentChargeWh);
                KeyedSerializer.WriteField(writer, "chgR", ChargeRateW);
                KeyedSerializer.WriteField(writer, "disR", DischargeRateW);
                KeyedSerializer.WriteField(writer, "eff", Efficiency);
                KeyedSerializer.WriteField(writer, "deg", Degradation);
                KeyedSerializer.WriteField(writer, "fuel", FuelHours);
                KeyedSerializer.WriteField(writer, "disc", IsDischarging);
            }
            finally
            {
                SerializationGuard.EndBlock(writer, block);
            }
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            if (!SerializationGuard.TryBeginBlock(reader, SAVE_VERSION, out var version, out var block, nameof(BackupPower)))
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
                            case "type": Type = KeyedSerializer.ReadEnumByte<TReader, BackupPowerType>(reader, tag, "type", BackupPowerType.None); break;
                            case "cap": CapacityWh = KeyedSerializer.ReadBoundedInt(reader, tag, "cap", 0, 100000000, 0); break;
                            case "chg": CurrentChargeWh = KeyedSerializer.ReadBoundedInt(reader, tag, "chg", 0, 100000000, 0); break;
                            case "chgR": ChargeRateW = KeyedSerializer.ReadBoundedInt(reader, tag, "chgR", 0, 100000000, 0); break;
                            case "disR": DischargeRateW = KeyedSerializer.ReadBoundedInt(reader, tag, "disR", 0, 100000000, 0); break;
                            case "eff": Efficiency = KeyedSerializer.ReadSafeFloat(reader, tag, "eff", 0f, 1f, DEFAULT_EFFICIENCY); break;
                            case "deg": Degradation = KeyedSerializer.ReadSafeFloat(reader, tag, "deg", 0f, MAX_DEGRADATION, 0f); break;
                            case "fuel": FuelHours = KeyedSerializer.ReadSafeFloat(reader, tag, "fuel", -1f, 100000f, DEFAULT_BATTERY_FUEL_HOURS); break;
                            case "disc": IsDischarging = KeyedSerializer.ReadBool(reader, tag, "disc"); break;
                            default: KeyedSerializer.Skip(reader, tag); break;
                        }
                    }
                }
                // Cross-field invariant: charge cannot exceed effective post-degradation capacity.
                CurrentChargeWh = Unity.Mathematics.math.min(CurrentChargeWh, EffectiveCapacityWh(CapacityWh, Degradation));
            }
            catch (System.Exception ex)
            {
                BuildingRef preservedBuilding = Building;
                BackupPowerType preservedType = Type;
                BackupPowerLog.Log.Error($"Deserialize {nameof(BackupPower)} failed: {ex}");
                SetDefaults();
                Building = preservedBuilding;
                Type = preservedType;
            }
            finally
            {
                SerializationGuard.EndBlock(reader, block);
            }
        }

        public void SetDefaults()
        {
            this = default;
            Efficiency = DEFAULT_EFFICIENCY;
            FuelHours = DEFAULT_BATTERY_FUEL_HOURS;
        }

        /// <summary>
        /// Effective (degradation-adjusted) capacity in Watt-hours: single source of truth.
        /// Clamps degradation to [0, MAX_DEGRADATION] so out-of-range degradation can never
        /// inflate or invert capacity. Burst-safe; called from BackupPowerJob and BackupPowerStatsJob.
        /// </summary>
        public static int EffectiveCapacityWh(int capacityWh, float degradation)
        {
            if (capacityWh <= 0)
                return 0;

            float boundedDegradation = Unity.Mathematics.math.clamp(degradation, 0f, MAX_DEGRADATION);
            return (int)Unity.Mathematics.math.round(capacityWh * (1f - boundedDegradation));
        }
    }

    /// <summary>
    /// City-wide backup power statistics for UI.
    /// Pure data structure, no logic.
    /// </summary>
    public struct BackupPowerStats
    {
        public int ProtectedBuildings;
        public long TotalCapacityWh;
        public long TotalChargeWh;
        public int DischargingCount;
        public int GeneratorsRunning;
        public int GeneratorsTotal;
        public int GeneratorsFueled;

        /// <summary>
        /// Charge percentage (0-100). Uses double for precision with large values.
        /// BUG-E04 FIX: Prevents floating-point precision loss with very large cities.
        /// </summary>
        public float ChargePercent => TotalCapacityWh > 0
            ? Math.Clamp((float)((double)TotalChargeWh / TotalCapacityWh * 100.0), 0f, 100f)
            : 0f;
    }
}


