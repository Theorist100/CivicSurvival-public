using CivicSurvival.Core.Config;
using CivicSurvival.Core.Components.Domain.Power;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Backup power factory for each type.
    /// Level-based distribution - higher level = richer = more likely to have backup.
    /// Creates "class inequality" visualization: poor areas dark, rich areas lit.
    /// </summary>
    public static class BackupPowerFactory
    {
        // Level-based chances (Building Level 1-5)
        // Lvl 1 = Poor tenements, Lvl 5 = Elite penthouses
        private static readonly float[] ResidentialChanceByLevel = { 0.00f, 0.15f, 0.30f, 0.60f, 0.90f };
        private static readonly float[] CommercialChanceByLevel = { 0.05f, 0.20f, 0.40f, 0.70f, 1.00f };
        private static readonly float[] IndustrialChanceByLevel = { 0.05f, 0.10f, 0.20f, 0.40f, 0.70f };
        private static readonly float[] OfficeChanceByLevel = { 0.10f, 0.30f, 0.50f, 0.80f, 1.00f };

        /// <summary>
        /// Get backup power chance for Residential buildings by level.
        /// Lvl 1: 0% (poor can't afford), Lvl 5: 90% (rich almost always have)
        /// </summary>
        public static float GetResidentialChance(int level) =>
            GetChance(ResidentialChanceByLevel, level);

        /// <summary>
        /// Get backup power chance for Commercial buildings by level.
        /// Lvl 1: 5% (small kiosk), Lvl 5: 100% (luxury boutique)
        /// </summary>
        public static float GetCommercialChance(int level) =>
            GetChance(CommercialChanceByLevel, level);

        /// <summary>
        /// Get backup power chance for Industrial buildings by level.
        /// Lvl 1: 5% (garage workshop), Lvl 5: 70% (modern factory)
        /// </summary>
        public static float GetIndustrialChance(int level) =>
            GetChance(IndustrialChanceByLevel, level);

        /// <summary>
        /// Get backup power chance for Office buildings by level.
        /// Lvl 1: 10% (small office), Lvl 5: 100% (corporate HQ)
        /// </summary>
        public static float GetOfficeChance(int level) =>
            GetChance(OfficeChanceByLevel, level);

        // Factory methods for distribution
        public static BackupPower CreateHomeBattery() => Create(BackupPowerType.HomeBattery);
        public static BackupPower CreateBusinessUPS() => Create(BackupPowerType.BusinessUPS);
        public static BackupPower CreateIndustrialBattery() => Create(BackupPowerType.IndustrialBattery);
        public static BackupPower CreateDieselGenerator() => Create(BackupPowerType.DieselGenerator);

        public static BackupPower Create(BackupPowerType type, BuildingRef building)
        {
            var bp = Create(type);
            bp.Building = building;
            return bp;
        }

        public static BackupPower Create(BackupPowerType type)
        {
            var bp = BalanceConfig.Current.BackupPower;
            return type switch
            {
                BackupPowerType.HomeBattery => new BackupPower
                {
                    Type = type,
                    CapacityWh = bp.HomeBatteryCapacityWh,
                    CurrentChargeWh = bp.HomeBatteryCapacityWh,
                    ChargeRateW = bp.HomeBatteryChargeRateW,
                    DischargeRateW = bp.HomeBatteryDischargeRateW,
                    Efficiency = bp.HomeBatteryEfficiency,
                    Degradation = 0f,
                    FuelHours = -1f,
                    IsDischarging = false
                },
                BackupPowerType.BusinessUPS => new BackupPower
                {
                    Type = type,
                    CapacityWh = bp.BusinessUpsCapacityWh,
                    CurrentChargeWh = bp.BusinessUpsCapacityWh,
                    ChargeRateW = bp.BusinessUpsChargeRateW,
                    DischargeRateW = bp.BusinessUpsDischargeRateW,
                    Efficiency = bp.BusinessUpsEfficiency,
                    Degradation = 0f,
                    FuelHours = -1f,
                    IsDischarging = false
                },
                BackupPowerType.IndustrialBattery => new BackupPower
                {
                    Type = type,
                    CapacityWh = bp.IndustrialBatteryCapacityWh,
                    CurrentChargeWh = bp.IndustrialBatteryCapacityWh,
                    ChargeRateW = bp.IndustrialBatteryChargeRateW,
                    DischargeRateW = bp.IndustrialBatteryDischargeRateW,
                    Efficiency = bp.IndustrialBatteryEfficiency,
                    Degradation = 0f,
                    FuelHours = -1f,
                    IsDischarging = false
                },
                BackupPowerType.DieselGenerator => new BackupPower
                {
                    Type = type,
                    CapacityWh = 0,      // Fuel-only source; capacity/charge are intentionally unused.
                    CurrentChargeWh = 0,
                    ChargeRateW = 0,
                    DischargeRateW = bp.DieselGeneratorDischargeRateW,
                    Efficiency = bp.DieselGeneratorEfficiency,
                    Degradation = 0f,
                    FuelHours = bp.DieselGeneratorFuelHours,
                    IsDischarging = false
                },
                BackupPowerType.None => new BackupPower { Type = BackupPowerType.None },
                _ => throw new System.ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        private static float GetChance(float[] chances, int level)
        {
            if (level < 1 || level > chances.Length)
                return 0f;

            int index = level - 1;
            return chances[index];
        }

        /// <summary>
        /// Get noise level for backup power type (0 = silent, 100 = very loud).
        /// </summary>
        public static int GetNoiseLevel(BackupPowerType type)
        {
            var bp = BalanceConfig.Current.BackupPower;
            return type switch
            {
                BackupPowerType.HomeBattery => bp.NoiseLevelSilent,
                BackupPowerType.BusinessUPS => bp.NoiseLevelSilent,
                BackupPowerType.IndustrialBattery => bp.NoiseLevelSilent,
                BackupPowerType.DieselGenerator => bp.NoiseLevelDiesel,
                BackupPowerType.None => 0,
                _ => throw new System.ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        /// <summary>
        /// Get fire risk percentage for backup power type.
        /// </summary>
        public static float GetFireRisk(BackupPowerType type)
        {
            var bp = BalanceConfig.Current.BackupPower;
            return type switch
            {
                BackupPowerType.HomeBattery => bp.FireRiskHomeBattery,
                BackupPowerType.BusinessUPS => bp.FireRiskBusinessUps,
                BackupPowerType.IndustrialBattery => bp.FireRiskIndustrialBattery,
                BackupPowerType.DieselGenerator => bp.FireRiskDieselGenerator,
                BackupPowerType.None => 0f,
                _ => throw new System.ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }
    }
}
