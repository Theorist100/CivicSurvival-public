// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/balance.contract.yaml
// SourceHash:       sha256:ec89b3f14a795ec42e45b560fa7c9689d103898fefc42d8941e2c2e35fd7b5db
// Generator:        scripts/generators/balance.py
// GeneratorVersion: 1.0.0
// ContractVersion:  2.6.3
// GeneratedAt:      2026-05-14T00:00:00Z

using System;
using System.Collections.Generic;
using CivicSurvival.Core.Infrastructure;

namespace CivicSurvival.Core.Types
{
    public sealed partial class RemoteBalanceConfig
    {
        public const int CURRENT_SCHEMA_REVISION = 1;

        public string Version { get; set; } = "2.9.7";

        public string UpdatedAt { get; set; } = "2026-06-29T00:00:00Z";

        public int SchemaRevision { get; set; } = 1;

        public ThreatsConfig Threats { get; set; } = new();

        public AirDefenseConfig AirDefense { get; set; } = new();

        public WavesConfig Waves { get; set; } = new();

        public SpotterConfig Spotter { get; set; } = new();

        public PenaltiesConfig Penalties { get; set; } = new();

        public RepairConfig Repair { get; set; } = new();

        public EngineeringConfig Engineering { get; set; } = new();

        public ConstructionConfig Construction { get; set; } = new();

        public GenerationSaturationConfig GenerationSaturation { get; set; } = new();

        public FuelCurveConfig FuelCurve { get; set; } = new();

        public CorruptionConfig Corruption { get; set; } = new();

        public CountermeasuresConfig Countermeasures { get; set; } = new();

        public DiplomacyConfig Diplomacy { get; set; } = new();

        public AttentionConfig Attention { get; set; } = new();

        public TrustConfig Trust { get; set; } = new();

        public AidConfig Aid { get; set; } = new();

        public EconomyConfig Economy { get; set; } = new();

        public IntelConfig Intel { get; set; } = new();

        public ShadowImportConfig ShadowImport { get; set; } = new();

        public PowerGridConfig PowerGrid { get; set; } = new();

        public GridStressConfig GridStress { get; set; } = new();

        public EquipmentWearConfig EquipmentWear { get; set; } = new();

        public BackupPowerConfig BackupPower { get; set; } = new();

        public NarrativeConfig Narrative { get; set; } = new();

        public NotificationsConfig Notifications { get; set; } = new();

        public NeighborEnvyConfig NeighborEnvy { get; set; } = new();

        public DistrictsConfig Districts { get; set; } = new();

        public EmergencyFundConfig EmergencyFund { get; set; } = new();

        public FuelSiphoningConfig FuelSiphoning { get; set; } = new();

        public ShadowProcurementConfig ShadowProcurement { get; set; } = new();

        public CorruptionEventsConfig CorruptionEvents { get; set; } = new();

        public ShadowReputationConfig ShadowReputation { get; set; } = new();

        public ProcurementConfig Procurement { get; set; } = new();

        public HumanitarianAidConfig HumanitarianAid { get; set; } = new();

        public ScenarioConfig Scenario { get; set; } = new();

        public FeatureGatesConfig FeatureGates { get; set; } = new();

        public InfrastructureRepairConfig InfrastructureRepair { get; set; } = new();

        public LoadSheddingConfig LoadShedding { get; set; } = new();

        public CognitiveConfig Cognitive { get; set; } = new();

        public MobilizationConfig Mobilization { get; set; } = new();

        public AAUnitsConfig AAUnits { get; set; } = new();

        public DebtConfig Debt { get; set; } = new();

        public CityStabilityConfig CityStability { get; set; } = new();

        public GridWarfareConfig GridWarfare { get; set; } = new();


        private const float MIN_POSITIVE_HOURS = 0.1f;
        private const float MIN_POSITIVE_SECONDS = 0.1f;
        private const float MIN_POSITIVE_EFFICIENCY = 0.01f;
        private const float MAX_GLOBAL_SHOCK_TIER = 99f;

        /// <summary>
        /// Validate config values generated from balance.contract.yaml.
        /// Called after every config load (local file or server fetch).
        /// </summary>
        public void Validate()
        {
            float sum;

            AirDefense.BallisticIntercept = ClampUnitFinite(AirDefense.BallisticIntercept, 0.0f);
            AirDefense.AmmoRefillDurationSeconds = FiniteOr(AirDefense.AmmoRefillDurationSeconds, 720.0f);
            AirDefense.AmmoRefillDurationSeconds = Math.Max(AirDefense.AmmoRefillDurationSeconds, 1.0f);
            AirDefense.StartAmmoFraction = ClampUnitFinite(AirDefense.StartAmmoFraction, 0.25f);
            AirDefense.Cooldown = FiniteOr(AirDefense.Cooldown, 30.0f);
            AirDefense.Cooldown = Math.Max(AirDefense.Cooldown, 0.1f);
            AirDefense.EvasionPerShot = ClampUnitFinite(AirDefense.EvasionPerShot, 0.12f);
            AirDefense.EvasionMinChance = ClampUnitFinite(AirDefense.EvasionMinChance, 0.05f);
            AirDefense.RaycastEndpointEpsilon = FiniteOr(AirDefense.RaycastEndpointEpsilon, 0.02f);
            AirDefense.RaycastEndpointEpsilon = Math.Clamp(AirDefense.RaycastEndpointEpsilon, 0.0f, 0.5f);
            AirDefense.LOSAltitudeBypass = FiniteOr(AirDefense.LOSAltitudeBypass, 100.0f);
            AirDefense.LOSAltitudeBypass = Math.Max(AirDefense.LOSAltitudeBypass, 0.0f);
            AirDefense.BallisticSkipAltitude = FiniteOr(AirDefense.BallisticSkipAltitude, 200.0f);
            AirDefense.BallisticSkipAltitude = Math.Max(AirDefense.BallisticSkipAltitude, 0.0f);
            Waves.FrequencyMwDivisor = FiniteOr(Waves.FrequencyMwDivisor, 100.0f);
            Waves.FrequencyMwDivisor = Math.Max(Waves.FrequencyMwDivisor, 1.0f);
            Waves.WinterFrequencyMod = FiniteOr(Waves.WinterFrequencyMod, 0.6f);
            Waves.WinterFrequencyMod = Math.Max(Waves.WinterFrequencyMod, 0.0f);
            Waves.SizeFactorDiv = FiniteOr(Waves.SizeFactorDiv, 50.0f);
            Waves.SizeFactorDiv = Math.Max(Waves.SizeFactorDiv, 1.0f);
            Waves.RecoveredThreshold = FiniteOr(Waves.RecoveredThreshold, 0.1f);
            Waves.RecoveredThreshold = Math.Clamp(Waves.RecoveredThreshold, 0.0f, 1.0f);
            Waves.GraceFraction = FiniteOr(Waves.GraceFraction, 0.3f);
            Waves.GraceFraction = Math.Clamp(Waves.GraceFraction, 0.0f, 2.0f);
            Waves.NonPlantTargetWeightMW = Math.Max(Waves.NonPlantTargetWeightMW, 1);
            Waves.SurplusFreeThreshold = FiniteOr(Waves.SurplusFreeThreshold, 1.3f);
            Waves.SurplusFreeThreshold = Math.Max(Waves.SurplusFreeThreshold, 1.0f);
            Waves.SurplusFrequencyGain = FiniteOr(Waves.SurplusFrequencyGain, 1.0f);
            Waves.SurplusFrequencyGain = Math.Max(Waves.SurplusFrequencyGain, 0.0f);
            Waves.SurplusSizeGain = FiniteOr(Waves.SurplusSizeGain, 1.0f);
            Waves.SurplusSizeGain = Math.Max(Waves.SurplusSizeGain, 0.0f);
            Waves.SurplusMaxFreqMult = FiniteOr(Waves.SurplusMaxFreqMult, 2.0f);
            Waves.SurplusMaxFreqMult = Math.Max(Waves.SurplusMaxFreqMult, 1.0f);
            Waves.SurplusMaxSizeMult = FiniteOr(Waves.SurplusMaxSizeMult, 2.0f);
            Waves.SurplusMaxSizeMult = Math.Max(Waves.SurplusMaxSizeMult, 1.0f);
            Waves.DensityRefGuns = FiniteOr(Waves.DensityRefGuns, 6.0f);
            Waves.DensityRefGuns = Math.Max(Waves.DensityRefGuns, 1.0f);
            Waves.DensityFreeThreshold = FiniteOr(Waves.DensityFreeThreshold, 1.5f);
            Waves.DensityFreeThreshold = Math.Max(Waves.DensityFreeThreshold, 0.0f);
            Waves.DensitySizeGain = FiniteOr(Waves.DensitySizeGain, 0.2f);
            Waves.DensitySizeGain = Math.Max(Waves.DensitySizeGain, 0.0f);
            Waves.DensityMaxSizeMult = FiniteOr(Waves.DensityMaxSizeMult, 1.4f);
            Waves.DensityMaxSizeMult = Math.Max(Waves.DensityMaxSizeMult, 1.0f);
            Waves.SurchargeLethalFraction = FiniteOr(Waves.SurchargeLethalFraction, 0.4f);
            Waves.SurchargeLethalFraction = Math.Clamp(Waves.SurchargeLethalFraction, 0.001f, 1.0f);
            Repair.HitDamageMW = FiniteOr(Repair.HitDamageMW, 1200.0f);
            Repair.HitDamageMW = Math.Max(Repair.HitDamageMW, 1.0f);
            Repair.HitFleetSharePercent = FiniteOr(Repair.HitFleetSharePercent, 0.1f);
            Repair.HitFleetSharePercent = Math.Clamp(Repair.HitFleetSharePercent, 0.0f, 0.3f);
            Repair.MinHitLossPercent = ClampUnitFinite(Repair.MinHitLossPercent, 0.1f);
            Repair.MaxHitLossPercent = ClampUnitFinite(Repair.MaxHitLossPercent, 0.5f);
            Repair.DestructionThreshold = ClampUnitFinite(Repair.DestructionThreshold, 0.95f);
            Repair.FireThreshold = ClampUnitFinite(Repair.FireThreshold, 0.7f);
            Repair.RepairCostPerPercent = Math.Max(Repair.RepairCostPerPercent, 0);
            Engineering.WinterMultMid = FiniteOr(Engineering.WinterMultMid, 1.5f);
            Engineering.WinterMultMid = Math.Max(Engineering.WinterMultMid, 0.0f);
            Engineering.WinterMultMax = FiniteOr(Engineering.WinterMultMax, 3.0f);
            Engineering.WinterMultMax = Math.Max(Engineering.WinterMultMax, 0.0f);
            GenerationSaturation.UnitBufferCapMW = FiniteOr(GenerationSaturation.UnitBufferCapMW, 100.0f);
            GenerationSaturation.UnitBufferCapMW = Math.Max(GenerationSaturation.UnitBufferCapMW, 0.0f);
            Countermeasures.EventCooldownHours = FiniteOr(Countermeasures.EventCooldownHours, 2.0f);
            Countermeasures.EventCooldownHours = Math.Max(Countermeasures.EventCooldownHours, 0.0f);
            Countermeasures.HoursPerProgress = FiniteOr(Countermeasures.HoursPerProgress, 4.0f);
            Countermeasures.HoursPerProgress = Math.Max(Countermeasures.HoursPerProgress, 0.1f);
            Countermeasures.PoliceStartChance = ClampUnitFinite(Countermeasures.PoliceStartChance, 0.05f);
            Countermeasures.PoliceHonestChance = ClampUnitFinite(Countermeasures.PoliceHonestChance, 0.3f);
            Countermeasures.EvidenceDestroySuccess = ClampUnitFinite(Countermeasures.EvidenceDestroySuccess, 0.5f);
            Countermeasures.ProtestCooldownSeconds = FiniteOr(Countermeasures.ProtestCooldownSeconds, 600.0f);
            Countermeasures.ProtestCooldownSeconds = Math.Max(Countermeasures.ProtestCooldownSeconds, 0.0f);
            Countermeasures.ProtestDecaySeconds = FiniteOr(Countermeasures.ProtestDecaySeconds, 1800.0f);
            Countermeasures.ProtestDecaySeconds = Math.Max(Countermeasures.ProtestDecaySeconds, 1.0f);
            Diplomacy.SanctionTradePenalty = ClampUnitFinite(Diplomacy.SanctionTradePenalty, 0.2f);
            Diplomacy.SanctionDays = Math.Max(Diplomacy.SanctionDays, 0);
            Diplomacy.SanctionsBlackMarketMarkup = FiniteOr(Diplomacy.SanctionsBlackMarketMarkup, 1.5f);
            Diplomacy.SanctionsBlackMarketMarkup = Math.Max(Diplomacy.SanctionsBlackMarketMarkup, 0.0f);
            Diplomacy.ImportTrustPenaltyDecayPerDay = FiniteOr(Diplomacy.ImportTrustPenaltyDecayPerDay, 1.0f);
            Diplomacy.ImportTrustPenaltyDecayPerDay = Math.Max(Diplomacy.ImportTrustPenaltyDecayPerDay, 0.1f);
            ShadowImport.MaxImportPercent = ClampUnitFinite(ShadowImport.MaxImportPercent, 0.3f);
            ShadowImport.MinImportMw = Math.Max(ShadowImport.MinImportMw, 0);
            ShadowImport.AbsoluteMaxMw = Math.Max(ShadowImport.AbsoluteMaxMw, 0);
            BackupPower.FireRiskMultiplier = FiniteOr(BackupPower.FireRiskMultiplier, 0.01f);
            BackupPower.FireRiskMultiplier = Math.Max(BackupPower.FireRiskMultiplier, 0.0f);
            BackupPower.HomeBatteryEfficiency = FiniteOr(BackupPower.HomeBatteryEfficiency, 0.9f);
            BackupPower.HomeBatteryEfficiency = Math.Clamp(BackupPower.HomeBatteryEfficiency, 0.01f, 1.0f);
            BackupPower.FireRiskHomeBattery = ClampUnitFinite(BackupPower.FireRiskHomeBattery, 0.01f);
            BackupPower.FireRiskBusinessUps = ClampUnitFinite(BackupPower.FireRiskBusinessUps, 0.02f);
            BackupPower.FireRiskIndustrialBattery = ClampUnitFinite(BackupPower.FireRiskIndustrialBattery, 0.05f);
            BackupPower.FireRiskDieselGenerator = ClampUnitFinite(BackupPower.FireRiskDieselGenerator, 0.1f);
            Narrative.UpdateIntervalSeconds = FiniteOr(Narrative.UpdateIntervalSeconds, 3.0f);
            Narrative.UpdateIntervalSeconds = Math.Max(Narrative.UpdateIntervalSeconds, 0.1f);
            Narrative.BatchWindowSeconds = FiniteOr(Narrative.BatchWindowSeconds, 3.0f);
            Narrative.BatchWindowSeconds = Math.Max(Narrative.BatchWindowSeconds, 0.0f);
            Narrative.IdleMessageChance = ClampUnitFinite(Narrative.IdleMessageChance, 0.1f);
            Narrative.VipReactionChance = ClampUnitFinite(Narrative.VipReactionChance, 0.1f);
            Narrative.AngryDurationHours = FiniteOr(Narrative.AngryDurationHours, 2.0f);
            Narrative.AngryDurationHours = Math.Max(Narrative.AngryDurationHours, 0.0f);
            NeighborEnvy.EnvyRadius = FiniteOr(NeighborEnvy.EnvyRadius, 100.0f);
            NeighborEnvy.EnvyRadius = Math.Max(NeighborEnvy.EnvyRadius, 1.0f);
            ShadowProcurement.CounterfeitCapacityMult = FiniteOr(ShadowProcurement.CounterfeitCapacityMult, 0.5f);
            ShadowProcurement.CounterfeitCapacityMult = Math.Max(ShadowProcurement.CounterfeitCapacityMult, 0.0f);
            ShadowProcurement.CounterfeitDegradationMult = FiniteOr(ShadowProcurement.CounterfeitDegradationMult, 2.0f);
            ShadowProcurement.CounterfeitDegradationMult = Math.Max(ShadowProcurement.CounterfeitDegradationMult, 0.0f);
            ShadowProcurement.CounterfeitFireRiskMult = FiniteOr(ShadowProcurement.CounterfeitFireRiskMult, 5.0f);
            ShadowProcurement.CounterfeitFireRiskMult = Math.Max(ShadowProcurement.CounterfeitFireRiskMult, 0.0f);
            ShadowProcurement.FireMinDays = Math.Max(ShadowProcurement.FireMinDays, 1);
            ShadowProcurement.FireMaxDays = Math.Max(ShadowProcurement.FireMaxDays, 1);
            Scenario.IntegrationCheckIntervalHours = FiniteOr(Scenario.IntegrationCheckIntervalHours, 2.0f);
            Scenario.IntegrationCheckIntervalHours = Math.Max(Scenario.IntegrationCheckIntervalHours, 0.1f);
            Scenario.MigrationCheckIntervalHours = FiniteOr(Scenario.MigrationCheckIntervalHours, 1.0f);
            Scenario.MigrationCheckIntervalHours = Math.Max(Scenario.MigrationCheckIntervalHours, 0.1f);
            Scenario.RefugeeSupportIntervalHours = FiniteOr(Scenario.RefugeeSupportIntervalHours, 24.0f);
            Scenario.RefugeeSupportIntervalHours = Math.Max(Scenario.RefugeeSupportIntervalHours, 0.1f);
            Scenario.DefeatIntegrityThreshold = ClampUnitFinite(Scenario.DefeatIntegrityThreshold, 0.1f);
            Scenario.DefeatIntegrityHours = FiniteOr(Scenario.DefeatIntegrityHours, 48.0f);
            Scenario.DefeatIntegrityHours = Math.Max(Scenario.DefeatIntegrityHours, 0.1f);
            InfrastructureRepair.MunicipalRepairHours = FiniteOr(InfrastructureRepair.MunicipalRepairHours, 24.0f);
            InfrastructureRepair.MunicipalRepairHours = Math.Max(InfrastructureRepair.MunicipalRepairHours, 0.1f);
            InfrastructureRepair.ShadowOpsRepairHours = FiniteOr(InfrastructureRepair.ShadowOpsRepairHours, 2.0f);
            InfrastructureRepair.ShadowOpsRepairHours = Math.Max(InfrastructureRepair.ShadowOpsRepairHours, 0.1f);
            InfrastructureRepair.CivilianMunicipalRepairHours = FiniteOr(InfrastructureRepair.CivilianMunicipalRepairHours, 12.0f);
            InfrastructureRepair.CivilianMunicipalRepairHours = Math.Max(InfrastructureRepair.CivilianMunicipalRepairHours, 0.1f);
            InfrastructureRepair.CivilianShadowOpsRepairHours = FiniteOr(InfrastructureRepair.CivilianShadowOpsRepairHours, 1.0f);
            InfrastructureRepair.CivilianShadowOpsRepairHours = Math.Max(InfrastructureRepair.CivilianShadowOpsRepairHours, 0.1f);
            Cognitive.SkepticismFactor = ClampUnitFinite(Cognitive.SkepticismFactor, 0.3f);
            Cognitive.BlackoutVulnMaxBonus = ClampUnitFinite(Cognitive.BlackoutVulnMaxBonus, 0.3f);
            Cognitive.ResistanceStressReduction = ClampUnitFinite(Cognitive.ResistanceStressReduction, 0.5f);
            Cognitive.HeroInfectionReduction = ClampUnitFinite(Cognitive.HeroInfectionReduction, 0.5f);
            Cognitive.EnvyStress = ClampUnitFinite(Cognitive.EnvyStress, 0.1f);
            Cognitive.DefaultTrust = ClampUnitFinite(Cognitive.DefaultTrust, 0.7f);
            Cognitive.AlarmistStressRate = FiniteOr(Cognitive.AlarmistStressRate, 0.1f);
            Cognitive.AlarmistStressRate = Math.Max(Cognitive.AlarmistStressRate, 0.0f);
            AAUnits.PatriotStartAmmoFraction = ClampUnitFinite(AAUnits.PatriotStartAmmoFraction, 1.0f);
            AAUnits.AmmoScaleDiv = FiniteOr(AAUnits.AmmoScaleDiv, 50.0f);
            AAUnits.AmmoScaleDiv = Math.Max(AAUnits.AmmoScaleDiv, 1.0f);
            AAUnits.AmmoBaseMW = Math.Max(AAUnits.AmmoBaseMW, 50);
            AAUnits.AmmoScaleMult = FiniteOr(AAUnits.AmmoScaleMult, 0.3f);
            AAUnits.AmmoScaleMult = Math.Max(AAUnits.AmmoScaleMult, 0.0f);
            AAUnits.AmmoMaxScaleCap = FiniteOr(AAUnits.AmmoMaxScaleCap, 2.5f);
            AAUnits.AmmoMaxScaleCap = Math.Max(AAUnits.AmmoMaxScaleCap, 1.0f);
            Countermeasures.InvestigationMilestone25 = Math.Max(Countermeasures.InvestigationMilestone25, 0);
            Countermeasures.InvestigationMilestone50 = Math.Max(Countermeasures.InvestigationMilestone50, 0);
            Countermeasures.InvestigationMilestone75 = Math.Max(Countermeasures.InvestigationMilestone75, 0);
            EmergencyFund.CorruptionPer100k = Math.Max(EmergencyFund.CorruptionPer100k, 0.0f);
            HumanitarianAid.TonsPerDayAt100 = Math.Max(HumanitarianAid.TonsPerDayAt100, 0.0f);
            ShadowImport.GateLevel1Threshold = Math.Max(ShadowImport.GateLevel1Threshold, 0.0f);
            ShadowImport.GateLevel1Multiplier = Math.Max(ShadowImport.GateLevel1Multiplier, 0.0f);
            ShadowImport.GateLevel2Threshold = Math.Max(ShadowImport.GateLevel2Threshold, 0.0f);
            ShadowImport.GateLevel2Multiplier = Math.Max(ShadowImport.GateLevel2Multiplier, 0.0f);
            ShadowImport.GateLevel3Multiplier = Math.Max(ShadowImport.GateLevel3Multiplier, 0.0f);
            ShadowImport.RiskDay1 = Math.Max(ShadowImport.RiskDay1, 0.0f);
            ShadowImport.RiskDay2 = Math.Max(ShadowImport.RiskDay2, 0.0f);
            ShadowImport.RiskDay3 = Math.Max(ShadowImport.RiskDay3, 0.0f);
            ShadowImport.RiskDay4Plus = Math.Max(ShadowImport.RiskDay4Plus, 0.0f);
            GridWarfare.DroneCost = Math.Max(GridWarfare.DroneCost, 0L);
            GridWarfare.DronePrepareDuration = FiniteOr(GridWarfare.DronePrepareDuration, 45.0f);
            GridWarfare.DronePrepareDuration = Math.Max(GridWarfare.DronePrepareDuration, 0.1f);
            GridWarfare.DroneBaseDamage = FiniteOr(GridWarfare.DroneBaseDamage, 12.0f);
            GridWarfare.DroneBaseDamage = Math.Max(GridWarfare.DroneBaseDamage, 0.0f);
            GridWarfare.BlackoutCost = Math.Max(GridWarfare.BlackoutCost, 0L);
            GridWarfare.BlackoutPrepareDuration = FiniteOr(GridWarfare.BlackoutPrepareDuration, 30.0f);
            GridWarfare.BlackoutPrepareDuration = Math.Max(GridWarfare.BlackoutPrepareDuration, 0.1f);
            GridWarfare.BlackoutBaseDamage = FiniteOr(GridWarfare.BlackoutBaseDamage, 8.0f);
            GridWarfare.BlackoutBaseDamage = Math.Max(GridWarfare.BlackoutBaseDamage, 0.0f);
            GridWarfare.DisinfoCost = Math.Max(GridWarfare.DisinfoCost, 0L);
            GridWarfare.DisinfoPrepareDuration = FiniteOr(GridWarfare.DisinfoPrepareDuration, 20.0f);
            GridWarfare.DisinfoPrepareDuration = Math.Max(GridWarfare.DisinfoPrepareDuration, 0.1f);
            GridWarfare.DisinfoBaseDamage = FiniteOr(GridWarfare.DisinfoBaseDamage, 5.0f);
            GridWarfare.DisinfoBaseDamage = Math.Max(GridWarfare.DisinfoBaseDamage, 0.0f);
            GridWarfare.PressureFloor = FiniteOr(GridWarfare.PressureFloor, 20.0f);
            GridWarfare.PressureFloor = Math.Clamp(GridWarfare.PressureFloor, 0.0f, 100.0f);
            GridWarfare.PressureCap = FiniteOr(GridWarfare.PressureCap, 100.0f);
            GridWarfare.PressureCap = Math.Max(GridWarfare.PressureCap, 1.0f);
            GridWarfare.PressureRegenRatePerHour = FiniteOr(GridWarfare.PressureRegenRatePerHour, 5.0f);
            GridWarfare.PressureRegenRatePerHour = Math.Max(GridWarfare.PressureRegenRatePerHour, 0.0f);
            GridWarfare.MaxStabilityDiscount = FiniteOr(GridWarfare.MaxStabilityDiscount, 0.2f);
            GridWarfare.MaxStabilityDiscount = Math.Clamp(GridWarfare.MaxStabilityDiscount, 0.0f, 1.0f);
            GridWarfare.EnemyInterceptChance = FiniteOr(GridWarfare.EnemyInterceptChance, 0.25f);
            GridWarfare.EnemyInterceptChance = Math.Clamp(GridWarfare.EnemyInterceptChance, 0.0f, 1.0f);
            GridWarfare.ArsenalStockCap = Math.Max(GridWarfare.ArsenalStockCap, 1);
            GridWarfare.ArsenalDroneBaseCost = Math.Max(GridWarfare.ArsenalDroneBaseCost, 0);
            GridWarfare.ArsenalBallisticBaseCost = Math.Max(GridWarfare.ArsenalBallisticBaseCost, 0);
            GridWarfare.ArsenalMaxPurchaseCount = Math.Max(GridWarfare.ArsenalMaxPurchaseCount, 1);
            GridWarfare.DonorArsenalDroneGrant = Math.Max(GridWarfare.DonorArsenalDroneGrant, 0);
            GridWarfare.DonorArsenalBallisticGrant = Math.Max(GridWarfare.DonorArsenalBallisticGrant, 0);
            GridWarfare.ObjectiveAxisThreshold = FiniteOr(GridWarfare.ObjectiveAxisThreshold, 30.0f);
            GridWarfare.ObjectiveAxisThreshold = Math.Clamp(GridWarfare.ObjectiveAxisThreshold, 0.0f, 100.0f);
            GridWarfare.RespiteWindowHours = FiniteOr(GridWarfare.RespiteWindowHours, 12.0f);
            GridWarfare.RespiteWindowHours = Math.Max(GridWarfare.RespiteWindowHours, 0.0f);
            GridWarfare.RespiteWaveWeakenMultiplier = FiniteOr(GridWarfare.RespiteWaveWeakenMultiplier, 0.5f);
            GridWarfare.RespiteWaveWeakenMultiplier = Math.Clamp(GridWarfare.RespiteWaveWeakenMultiplier, 0.0f, 1.0f);
            GridWarfare.ObjectiveLootShadowCash = Math.Max(GridWarfare.ObjectiveLootShadowCash, 0);

            Threats.ShahedSpeed = PositiveFinite(Threats.ShahedSpeed, 40f);
            Threats.BallisticSpeed = PositiveFinite(Threats.BallisticSpeed, 400f);
            Threats.BallisticImpactRadius = NonNegativeFinite(Threats.BallisticImpactRadius, 200f);
            Threats.DirectHitRadiusBase = NonNegativeFinite(Threats.DirectHitRadiusBase, 50f);
            Threats.DestructionSeverity = NonNegativeFinite(Threats.DestructionSeverity, 0.5f);
            Threats.FireSeverity = NonNegativeFinite(Threats.FireSeverity, 0.2f);
            Threats.BallisticDestructionSeverity = NonNegativeFinite(Threats.BallisticDestructionSeverity, 0.8f);
            Threats.BallisticFireSeverity = NonNegativeFinite(Threats.BallisticFireSeverity, 0.3f);
            Threats.DebrisCheckRadius = NonNegativeFinite(Threats.DebrisCheckRadius, 100f);
            Threats.DebrisFallTime = NonNegativeFinite(Threats.DebrisFallTime, 5f);
            Waves.BallisticMwPerMissile = Math.Max(Waves.BallisticMwPerMissile, 1);
            Waves.BallisticMaxPerWave = Math.Max(Waves.BallisticMaxPerWave, 0);
            Waves.BallisticWaveBonus = NonNegativeFinite(Waves.BallisticWaveBonus, 0.5f);
            Waves.BallisticMinProductionMw = Math.Max(Waves.BallisticMinProductionMw, 0);
            Waves.BallisticStartWave = Math.Max(Waves.BallisticStartWave, 1);
            Waves.FrequencyMwDivisor = PositiveFinite(Waves.FrequencyMwDivisor, 100f, 1f);
            Waves.SizeFactorDiv = PositiveFinite(Waves.SizeFactorDiv, 50f, 1f);
            Waves.MassiveSizeMult = NonNegativeFinite(Waves.MassiveSizeMult, 3f);
            Waves.MassiveTimeMult = NonNegativeFinite(Waves.MassiveTimeMult, 1.5f);
            Waves.IntroStrengthMult = NonNegativeFinite(Waves.IntroStrengthMult, 1.2f);
            Waves.MinThreats = Math.Max(Waves.MinThreats, 0);
            Waves.MaxThreats = Math.Max(Waves.MaxThreats, Waves.MinThreats);
            Waves.MaxWaveInterceptFraction = ClampUnit(Waves.MaxWaveInterceptFraction);
            Countermeasures.ChargeDivisorNormal = Math.Max(Countermeasures.ChargeDivisorNormal, 1f);
            Countermeasures.ChargeDivisorPolice = Math.Max(Countermeasures.ChargeDivisorPolice, 1f);
            Countermeasures.ChargeDivisorBribeCaught = Math.Max(Countermeasures.ChargeDivisorBribeCaught, 1f);
            Countermeasures.HoursPerProgress = Math.Max(Countermeasures.HoursPerProgress, 0.1f);
            Countermeasures.EventCooldownHours = Math.Max(Countermeasures.EventCooldownHours, 0f);
            Countermeasures.HeatRefundOnResolve = Math.Clamp(Countermeasures.HeatRefundOnResolve, 0f, Countermeasures.HeatMax);
            Countermeasures.PoliceCooperateEvidenceThreshold = Math.Clamp(Countermeasures.PoliceCooperateEvidenceThreshold, 0f, 100f);
            Countermeasures.ProtestDecaySeconds = Math.Max(Countermeasures.ProtestDecaySeconds, 1f);
            Countermeasures.HeatGainTier1 = Math.Max(Countermeasures.HeatGainTier1, 0f);
            Countermeasures.HeatGainTier2 = Math.Max(Countermeasures.HeatGainTier2, Countermeasures.HeatGainTier1);
            Countermeasures.HeatGainTier3 = Math.Max(Countermeasures.HeatGainTier3, Countermeasures.HeatGainTier2);
            Countermeasures.HeatWarningThreshold = Math.Max(Countermeasures.HeatWarningThreshold, 0f);
            Countermeasures.HeatDangerThreshold = Math.Max(Countermeasures.HeatDangerThreshold, Countermeasures.HeatWarningThreshold);
            Countermeasures.HeatCriticalThreshold = Math.Max(Countermeasures.HeatCriticalThreshold, Countermeasures.HeatDangerThreshold);
            Countermeasures.InvestigationMilestone25 = Math.Max(Countermeasures.InvestigationMilestone25, 0);
            Countermeasures.InvestigationMilestone50 = Math.Max(Countermeasures.InvestigationMilestone50, Countermeasures.InvestigationMilestone25);
            Countermeasures.InvestigationMilestone75 = Math.Max(Countermeasures.InvestigationMilestone75, Countermeasures.InvestigationMilestone50);
            Countermeasures.PoliceHonestChance = ClampUnit(Countermeasures.PoliceHonestChance);
            Countermeasures.EvidenceDestroySuccess = ClampUnit(Countermeasures.EvidenceDestroySuccess);
            Countermeasures.PoliceStartChance = ClampUnit(Countermeasures.PoliceStartChance);
            AirDefense.Range = Math.Max(AirDefense.Range, 1f);
            AirDefense.Cooldown = Math.Max(AirDefense.Cooldown, MIN_POSITIVE_SECONDS);
            AirDefense.MaxAmmo = Math.Max(AirDefense.MaxAmmo, 1);
            AirDefense.BallisticIntercept = ClampUnit(AirDefense.BallisticIntercept);
            AirDefense.EvasionPerShot = ClampUnit(AirDefense.EvasionPerShot);
            AirDefense.EvasionMinChance = ClampUnit(AirDefense.EvasionMinChance);
            AAUnits.MwPerAA = Math.Max(AAUnits.MwPerAA, 1);
            AAUnits.HeritageMinCount = Math.Max(AAUnits.HeritageMinCount, 0);
            AAUnits.HeritageMaxCount = Math.Max(AAUnits.HeritageMaxCount, AAUnits.HeritageMinCount);
            Mobilization.ManpowerCoeff = Math.Max(Mobilization.ManpowerCoeff, 0.01f);
            Mobilization.ManpowerExponent = Math.Max(Mobilization.ManpowerExponent, 0.01f);
            HumanitarianAid.ProcurementIntervalHours = Math.Max(HumanitarianAid.ProcurementIntervalHours, 0.1f);
            Scenario.RefugeesPerHousehold = Math.Max(Scenario.RefugeesPerHousehold, 1);
            Scenario.ExodusMultiplierVillage = Math.Max(Scenario.ExodusMultiplierVillage, 0f);
            Scenario.ExodusMultiplierTown = Math.Max(Scenario.ExodusMultiplierTown, 0f);
            Scenario.ExodusMultiplierCity = Math.Max(Scenario.ExodusMultiplierCity, 0f);
            Scenario.VillageMaxPop = Math.Max(Scenario.VillageMaxPop, 0);
            Scenario.TownMaxPop = Math.Max(Scenario.TownMaxPop, Scenario.VillageMaxPop);
            Scenario.RefugeeTargetPercent = Math.Max(Scenario.RefugeeTargetPercent, 0f);
            Scenario.RefugeeInfluxDurationHours = Math.Max(Scenario.RefugeeInfluxDurationHours, 1);
            Scenario.RefugeeAidIntervalHours = Math.Max(Scenario.RefugeeAidIntervalHours, 1);
            Scenario.RefugeeWealthFloorMultiplier = Math.Max(Scenario.RefugeeWealthFloorMultiplier, 0f);
            Scenario.RefugeeStartMilestone = Math.Max(Scenario.RefugeeStartMilestone, 1);
            if (Scenario.WarStartMilestone <= Scenario.RefugeeStartMilestone)
                Scenario.WarStartMilestone = Scenario.RefugeeStartMilestone + 1;
            Scenario.IntegrationCheckIntervalHours = Math.Max(Scenario.IntegrationCheckIntervalHours, MIN_POSITIVE_HOURS);
            Scenario.MigrationCheckIntervalHours = Math.Max(Scenario.MigrationCheckIntervalHours, MIN_POSITIVE_HOURS);
            Scenario.RefugeeSupportIntervalHours = Math.Max(Scenario.RefugeeSupportIntervalHours, MIN_POSITIVE_HOURS);
            Scenario.RefugeeSupportPerHouseholdPerDay = Math.Max(Scenario.RefugeeSupportPerHouseholdPerDay, 0);
            Scenario.MigrationMaxPerUpdate = Math.Max(Scenario.MigrationMaxPerUpdate, 0);
            Scenario.VictoryMinPopulation = ClampUnit(Scenario.VictoryMinPopulation);
            Scenario.VictoryMaxCorruption = Math.Clamp(Scenario.VictoryMaxCorruption, 0f, 100f);
            Scenario.DefeatIntegrityThreshold = ClampUnit(Scenario.DefeatIntegrityThreshold);
            Scenario.DefeatIntegrityHours = Math.Max(Scenario.DefeatIntegrityHours, MIN_POSITIVE_HOURS);
            Attention.IntegrityMultLoyal = Math.Max(Attention.IntegrityMultLoyal, 0f);
            Attention.IntegrityMultAnxious = Math.Max(Attention.IntegrityMultAnxious, 0f);
            Attention.IntegrityMultRebellious = Math.Max(Attention.IntegrityMultRebellious, 0f);
            Attention.IntegrityMultBrainwashed = Math.Max(Attention.IntegrityMultBrainwashed, 0f);
            Attention.IntegrityMultZombie = Math.Max(Attention.IntegrityMultZombie, 0f);
            Attention.IntegrityThresholdLoyal = Math.Clamp(Attention.IntegrityThresholdLoyal, 0f, 1f);
            Attention.IntegrityThresholdAnxious = Math.Clamp(Attention.IntegrityThresholdAnxious, 0f, 1f);
            Attention.IntegrityThresholdRebellious = Math.Clamp(Attention.IntegrityThresholdRebellious, 0f, 1f);
            Attention.IntegrityThresholdBrainwashed = Math.Clamp(Attention.IntegrityThresholdBrainwashed, 0f, 1f);
            Attention.IntegrityThresholdAnxious = Math.Min(Attention.IntegrityThresholdAnxious, Attention.IntegrityThresholdLoyal);
            Attention.IntegrityThresholdRebellious = Math.Min(Attention.IntegrityThresholdRebellious, Attention.IntegrityThresholdAnxious);
            Attention.IntegrityThresholdBrainwashed = Math.Min(Attention.IntegrityThresholdBrainwashed, Attention.IntegrityThresholdRebellious);
            Attention.TierDeepConcern = Math.Max(Attention.TierDeepConcern, 0f);
            Attention.TierHeadlines = Math.Max(Attention.TierHeadlines, Attention.TierDeepConcern);
            Attention.TierGlobalShock = Math.Clamp(Math.Max(Attention.TierGlobalShock, Attention.TierHeadlines), Attention.TierHeadlines, MAX_GLOBAL_SHOCK_TIER);
            if (Attention.ExodusGlobalMin > Attention.ExodusGlobalMax)
                (Attention.ExodusGlobalMin, Attention.ExodusGlobalMax) = (Attention.ExodusGlobalMax, Attention.ExodusGlobalMin);
            CityStability.MaxDestroyedBuildings = Math.Max(CityStability.MaxDestroyedBuildings, 1);
            CityStability.MaxFires = Math.Max(CityStability.MaxFires, 1);
            CityStability.TotalDistricts = Math.Max(CityStability.TotalDistricts, 1);
            NeighborEnvy.EnvyRadius = Math.Max(NeighborEnvy.EnvyRadius, 1f);
            NeighborEnvy.CellSize = Math.Max(NeighborEnvy.CellSize, 1f);
            EquipmentWear.UpdateIntervalFrames = Math.Max(EquipmentWear.UpdateIntervalFrames, 1);
            EquipmentWear.MaxWearPercent = Math.Max(EquipmentWear.MaxWearPercent, 0f);
            Repair.MinHitLossPercent = ClampUnitFinite(Repair.MinHitLossPercent, 0.1f);
            Repair.MaxHitLossPercent = ClampUnitFinite(Repair.MaxHitLossPercent, 0.5f);
            if (Repair.MinHitLossPercent > Repair.MaxHitLossPercent)
                (Repair.MinHitLossPercent, Repair.MaxHitLossPercent) = (Repair.MaxHitLossPercent, Repair.MinHitLossPercent);
            Repair.FireThreshold = ClampUnitFinite(Repair.FireThreshold, 0.70f);
            Repair.DestructionThreshold = ClampUnitFinite(Repair.DestructionThreshold, 0.95f);
            Repair.FireThreshold = Math.Min(Repair.FireThreshold, Repair.DestructionThreshold);
            Repair.RepairCostPerPercent = Math.Max(Repair.RepairCostPerPercent, 0);
            BackupPower.HomeBatteryCapacityWh = Math.Max(BackupPower.HomeBatteryCapacityWh, 1);
            BackupPower.HomeBatteryChargeRateW = Math.Max(BackupPower.HomeBatteryChargeRateW, 1);
            BackupPower.HomeBatteryDischargeRateW = Math.Max(BackupPower.HomeBatteryDischargeRateW, 1);
            BackupPower.HomeBatteryEfficiency = Math.Max(BackupPower.HomeBatteryEfficiency, MIN_POSITIVE_EFFICIENCY);
            BackupPower.BusinessUpsCapacityWh = Math.Max(BackupPower.BusinessUpsCapacityWh, 1);
            BackupPower.BusinessUpsChargeRateW = Math.Max(BackupPower.BusinessUpsChargeRateW, 1);
            BackupPower.BusinessUpsDischargeRateW = Math.Max(BackupPower.BusinessUpsDischargeRateW, 1);
            BackupPower.BusinessUpsEfficiency = Math.Max(BackupPower.BusinessUpsEfficiency, MIN_POSITIVE_EFFICIENCY);
            BackupPower.IndustrialBatteryCapacityWh = Math.Max(BackupPower.IndustrialBatteryCapacityWh, 1);
            BackupPower.IndustrialBatteryChargeRateW = Math.Max(BackupPower.IndustrialBatteryChargeRateW, 1);
            BackupPower.IndustrialBatteryDischargeRateW = Math.Max(BackupPower.IndustrialBatteryDischargeRateW, 1);
            BackupPower.IndustrialBatteryEfficiency = Math.Max(BackupPower.IndustrialBatteryEfficiency, MIN_POSITIVE_EFFICIENCY);
            BackupPower.DieselGeneratorDischargeRateW = Math.Max(BackupPower.DieselGeneratorDischargeRateW, 1);
            BackupPower.DieselGeneratorEfficiency = Math.Max(BackupPower.DieselGeneratorEfficiency, MIN_POSITIVE_EFFICIENCY);
            BackupPower.DieselGeneratorFuelHours = Math.Max(BackupPower.DieselGeneratorFuelHours, MIN_POSITIVE_HOURS);
            Trust.FullAidMax = Math.Max(Trust.FullAidMax, 0f);
            Trust.PartialAidMax = Math.Max(Trust.PartialAidMax, Trust.FullAidMax);
            Trust.MinimalAidMax = Math.Max(Trust.MinimalAidMax, Trust.PartialAidMax);
            Intel.TensionLowMax = Math.Max(Intel.TensionLowMax, 0);
            Intel.TensionElevatedMax = Math.Max(Intel.TensionElevatedMax, Intel.TensionLowMax);
            Intel.TensionHighMax = Math.Max(Intel.TensionHighMax, Intel.TensionElevatedMax);
            Corruption.LevelClean = Math.Max(Corruption.LevelClean, 0f);
            Corruption.LevelMinor = Math.Max(Corruption.LevelMinor, Corruption.LevelClean);
            Corruption.LevelSuspicious = Math.Max(Corruption.LevelSuspicious, Corruption.LevelMinor);
            Corruption.LevelCorrupt = Math.Max(Corruption.LevelCorrupt, Corruption.LevelSuspicious);
            Corruption.OffshoreTier1 = Math.Max(Corruption.OffshoreTier1, 0d);
            Corruption.OffshoreTier2 = Math.Max(Corruption.OffshoreTier2, Corruption.OffshoreTier1);
            Corruption.OffshoreTier3 = Math.Max(Corruption.OffshoreTier3, Corruption.OffshoreTier2);
            ShadowReputation.FrozenThreshold = Math.Max(ShadowReputation.FrozenThreshold, 0f);
            ShadowReputation.InnerCircleThreshold = Math.Max(ShadowReputation.InnerCircleThreshold, ShadowReputation.FrozenThreshold);
            ShadowReputation.FrozenFrequency = Math.Max(ShadowReputation.FrozenFrequency, 0f);
            ShadowReputation.LowFrequency = Math.Max(ShadowReputation.LowFrequency, ShadowReputation.FrozenFrequency);
            ShadowReputation.MedFrequency = Math.Max(ShadowReputation.MedFrequency, ShadowReputation.LowFrequency);
            ShadowReputation.HighFrequency = Math.Max(ShadowReputation.HighFrequency, ShadowReputation.MedFrequency);
            Debt.WarningRatio = Math.Max(Debt.WarningRatio, 0f);
            Debt.RestructureRatio = Math.Max(Debt.RestructureRatio, Debt.WarningRatio);
            Debt.InterestRate = Math.Max(Debt.InterestRate, 0f);
            Debt.RestructuredRate = Math.Clamp(Debt.RestructuredRate, 0f, Debt.InterestRate);
            Debt.ReliefPercent = ClampUnit(Debt.ReliefPercent);
            GridStress.YellowZoneFrequency = Math.Min(GridStress.YellowZoneFrequency, GridStress.NormalFrequency);
            GridStress.RedZoneFrequency = Math.Min(GridStress.RedZoneFrequency, GridStress.YellowZoneFrequency);
            GridStress.CollapseFrequency = Math.Min(GridStress.CollapseFrequency, GridStress.RedZoneFrequency);
            GridStress.WarningThresholdYellow = ClampUnit(GridStress.WarningThresholdYellow);
            GridStress.WarningThresholdRed = Math.Clamp(GridStress.WarningThresholdRed, GridStress.WarningThresholdYellow, 1f);
            GridStress.GridGraceCoeff = Math.Max(GridStress.GridGraceCoeff, 0f);
            GridStress.GridGraceExponent = Math.Max(GridStress.GridGraceExponent, 0.01f);
            GridStress.GridGraceRefPop = Math.Max(GridStress.GridGraceRefPop, 1);
            GridStress.GridGraceMaxHours = Math.Max(GridStress.GridGraceMaxHours, GridStress.CollapseThresholdHours);
            GridStress.DeficitDeadZoneFraction = ClampUnit(GridStress.DeficitDeadZoneFraction);
            GridStress.DeficitDeadZoneMinKW = Math.Max(GridStress.DeficitDeadZoneMinKW, 0);
            GridStress.RecoveryStressThreshold = Math.Clamp(GridStress.RecoveryStressThreshold, 0f, GridStress.WarningThresholdYellow);
            GridStress.RecoveryHeadroomMinMW = Math.Max(GridStress.RecoveryHeadroomMinMW, 0);
            GridStress.RecoveryHeadroomFraction = ClampUnit(GridStress.RecoveryHeadroomFraction);
            PowerGrid.DefaultLegalExportMw = Math.Max(PowerGrid.DefaultLegalExportMw, 0);
            Engineering.SmallPlantCapacityKw = Math.Max(Engineering.SmallPlantCapacityKw, 1);
            Engineering.WinterMultMid = Math.Max(Engineering.WinterMultMid, 0f);
            Engineering.WinterMultMax = Math.Max(Engineering.WinterMultMax, Engineering.WinterMultMid);
            if (Waves.MinThreats > Waves.MaxThreats)
                (Waves.MinThreats, Waves.MaxThreats) = (Waves.MaxThreats, Waves.MinThreats);
            Waves.MaxAttackDuration = Math.Max(Waves.MaxAttackDuration, Waves.AttackMin);
            if (ShadowImport.MinImportMw > ShadowImport.AbsoluteMaxMw)
                (ShadowImport.MinImportMw, ShadowImport.AbsoluteMaxMw) = (ShadowImport.AbsoluteMaxMw, ShadowImport.MinImportMw);
            ShadowImport.MinImportMw = Math.Max(ShadowImport.MinImportMw, 0);
            ShadowImport.AbsoluteMaxMw = Math.Max(ShadowImport.AbsoluteMaxMw, ShadowImport.MinImportMw);
            ShadowImport.MaxImportPercent = ClampUnit(ShadowImport.MaxImportPercent);
            if (ShadowProcurement.FireMinDays > ShadowProcurement.FireMaxDays)
                (ShadowProcurement.FireMinDays, ShadowProcurement.FireMaxDays) = (ShadowProcurement.FireMaxDays, ShadowProcurement.FireMinDays);
            ShadowProcurement.FireMinDays = Math.Max(ShadowProcurement.FireMinDays, 1);
            ShadowProcurement.FireMaxDays = Math.Max(ShadowProcurement.FireMaxDays, ShadowProcurement.FireMinDays);
            if (ShadowProcurement.FireMaxDays <= ShadowProcurement.FireMinDays)
                ShadowProcurement.FireMaxDays = ShadowProcurement.FireMinDays + 1;
            if (Waves.DoubleTapMinSeconds > Waves.DoubleTapMaxSeconds)
                (Waves.DoubleTapMinSeconds, Waves.DoubleTapMaxSeconds) = (Waves.DoubleTapMaxSeconds, Waves.DoubleTapMinSeconds);
            if (Waves.CalmMin > Waves.CalmMax)
                (Waves.CalmMin, Waves.CalmMax) = (Waves.CalmMax, Waves.CalmMin);
            if (Waves.AlertMin > Waves.AlertMax)
                (Waves.AlertMin, Waves.AlertMax) = (Waves.AlertMax, Waves.AlertMin);
            if (Waves.AttackMin > Waves.AttackMax)
                (Waves.AttackMin, Waves.AttackMax) = (Waves.AttackMax, Waves.AttackMin);
            if (Waves.RecoveryMin > Waves.RecoveryMax)
                (Waves.RecoveryMin, Waves.RecoveryMax) = (Waves.RecoveryMax, Waves.RecoveryMin);
            if (Waves.FrequencyRngMin > Waves.FrequencyRngMax)
                (Waves.FrequencyRngMin, Waves.FrequencyRngMax) = (Waves.FrequencyRngMax, Waves.FrequencyRngMin);
            if (Cognitive.BlackoutVulnThresholdHours > Cognitive.BlackoutVulnMaxHours)
                (Cognitive.BlackoutVulnThresholdHours, Cognitive.BlackoutVulnMaxHours) = (Cognitive.BlackoutVulnMaxHours, Cognitive.BlackoutVulnThresholdHours);
            Cognitive.BlackoutVulnThresholdHours = Math.Max(Cognitive.BlackoutVulnThresholdHours, 0f);
            Cognitive.BlackoutVulnMaxHours = Math.Max(Cognitive.BlackoutVulnMaxHours, 0.001f);
            Cognitive.BlackoutVulnMaxBonus = Math.Clamp(Cognitive.BlackoutVulnMaxBonus, 0f, 1f);
            Cognitive.ModeBonusRealistic = Math.Max(Cognitive.ModeBonusRealistic, 0f);
            Cognitive.ModeBonusAlarmist = Math.Max(Cognitive.ModeBonusAlarmist, 0f);
            Cognitive.ModeBonusSoothing = Math.Max(Cognitive.ModeBonusSoothing, 0f);
            Cognitive.CounterOpsMultiplier = Math.Max(Cognitive.CounterOpsMultiplier, 0f);
            Cognitive.RecoveryRateBase = NonNegativeFinite(Cognitive.RecoveryRateBase, 0.01f);
            Cognitive.FirewallRecoveryMultiplier = NonNegativeFinite(Cognitive.FirewallRecoveryMultiplier, 0.5f);
            Cognitive.HeroRecoveryBonus = NonNegativeFinite(Cognitive.HeroRecoveryBonus, 0.5f);
            Cognitive.SkepticismFactor = Math.Clamp(Cognitive.SkepticismFactor, 0f, 1f);
            Cognitive.EnvyStress = Math.Clamp(Cognitive.EnvyStress, 0f, 1f);
            Cognitive.TraumaGainRate = Math.Max(Cognitive.TraumaGainRate, 0f);
            Cognitive.TraumaDecayRate = Math.Max(Cognitive.TraumaDecayRate, 0f);
            Cognitive.AlarmistStressRate = Math.Max(Cognitive.AlarmistStressRate, 0f);
            if (Spotter.SbuBaseCost > Spotter.SbuMaxCost)
                (Spotter.SbuBaseCost, Spotter.SbuMaxCost) = (Spotter.SbuMaxCost, Spotter.SbuBaseCost);
            Spotter.BaseDetectionChancePerHour = Math.Max(Spotter.BaseDetectionChancePerHour, 0f);
            Spotter.SpotterSilenceHours = Math.Max(Spotter.SpotterSilenceHours, 0f);
            Spotter.EvacReturnStaleDays = Math.Max(Spotter.EvacReturnStaleDays, 0f);
            if (Districts.MinPriority > Districts.MaxPriority)
                (Districts.MinPriority, Districts.MaxPriority) = (Districts.MaxPriority, Districts.MinPriority);
            Districts.DefaultPriority = Math.Clamp(Districts.DefaultPriority, Districts.MinPriority, Districts.MaxPriority);
            CityStability.PhysicalWeight = Math.Max(CityStability.PhysicalWeight, 0f);
            CityStability.DigitalWeight = Math.Max(CityStability.DigitalWeight, 0f);
            CityStability.SocialWeight = Math.Max(CityStability.SocialWeight, 0f);
            sum = CityStability.PhysicalWeight + CityStability.DigitalWeight + CityStability.SocialWeight;
            if (sum > 0f)
            {
                CityStability.PhysicalWeight /= sum;
                CityStability.DigitalWeight /= sum;
                CityStability.SocialWeight /= sum;
            }
            else
            {
                CityStability.PhysicalWeight = 1f / 3f;
                CityStability.DigitalWeight = 1f / 3f;
                CityStability.SocialWeight = 1f / 3f;
            }
            CityStability.BlackoutSubWeight = Math.Max(CityStability.BlackoutSubWeight, 0f);
            CityStability.DestroyedSubWeight = Math.Max(CityStability.DestroyedSubWeight, 0f);
            CityStability.FiresSubWeight = Math.Max(CityStability.FiresSubWeight, 0f);
            sum = CityStability.BlackoutSubWeight + CityStability.DestroyedSubWeight + CityStability.FiresSubWeight;
            if (sum > 0f)
            {
                CityStability.BlackoutSubWeight /= sum;
                CityStability.DestroyedSubWeight /= sum;
                CityStability.FiresSubWeight /= sum;
            }
            else
            {
                CityStability.BlackoutSubWeight = 1f / 3f;
                CityStability.DestroyedSubWeight = 1f / 3f;
                CityStability.FiresSubWeight = 1f / 3f;
            }
            CityStability.DeficitSubWeight = Math.Max(CityStability.DeficitSubWeight, 0f);
            CityStability.StressSubWeight = Math.Max(CityStability.StressSubWeight, 0f);
            sum = CityStability.DeficitSubWeight + CityStability.StressSubWeight;
            if (sum > 0f)
            {
                CityStability.DeficitSubWeight /= sum;
                CityStability.StressSubWeight /= sum;
            }
            else
            {
                CityStability.DeficitSubWeight = 0.5f;
                CityStability.StressSubWeight = 0.5f;
            }
            BackupPower.MitigationWeightHospital = Math.Max(BackupPower.MitigationWeightHospital, 0f);
            BackupPower.MitigationWeightSchool = Math.Max(BackupPower.MitigationWeightSchool, 0f);
            BackupPower.MitigationWeightPrivate = Math.Max(BackupPower.MitigationWeightPrivate, 0f);
            sum = BackupPower.MitigationWeightHospital + BackupPower.MitigationWeightSchool + BackupPower.MitigationWeightPrivate;
            if (sum > 0f)
            {
                BackupPower.MitigationWeightHospital /= sum;
                BackupPower.MitigationWeightSchool /= sum;
                BackupPower.MitigationWeightPrivate /= sum;
            }
            else
            {
                BackupPower.MitigationWeightHospital = 1f / 3f;
                BackupPower.MitigationWeightSchool = 1f / 3f;
                BackupPower.MitigationWeightPrivate = 1f / 3f;
            }
            AAUnits.HeritageRange = Math.Max(AAUnits.HeritageRange, 1f);
            AAUnits.HeritageMaxAmmo = Math.Max(AAUnits.HeritageMaxAmmo, 1);
            AAUnits.HeritageBurstRounds = Math.Max(AAUnits.HeritageBurstRounds, 1);
            AAUnits.HeritageCooldown = Math.Max(AAUnits.HeritageCooldown, MIN_POSITIVE_SECONDS);
            AAUnits.HeritageInterceptShahed = ClampUnit(AAUnits.HeritageInterceptShahed);
            AAUnits.HeritageInterceptBallistic = ClampUnit(AAUnits.HeritageInterceptBallistic);
            AAUnits.BoforsRange = Math.Max(AAUnits.BoforsRange, 1f);
            AAUnits.BoforsMaxAmmo = Math.Max(AAUnits.BoforsMaxAmmo, 1);
            AAUnits.BoforsBurstRounds = Math.Max(AAUnits.BoforsBurstRounds, 1);
            AAUnits.GepardBurstRounds = Math.Max(AAUnits.GepardBurstRounds, 1);
            AAUnits.GepardRange = Math.Max(AAUnits.GepardRange, 1f);
            AAUnits.GepardMaxAmmo = Math.Max(AAUnits.GepardMaxAmmo, 1);
            AAUnits.GepardCooldown = Math.Max(AAUnits.GepardCooldown, MIN_POSITIVE_SECONDS);
            AAUnits.GepardInterceptShahed = ClampUnit(AAUnits.GepardInterceptShahed);
            AAUnits.GepardInterceptBallistic = ClampUnit(AAUnits.GepardInterceptBallistic);
            AAUnits.BoforsCooldown = Math.Max(AAUnits.BoforsCooldown, MIN_POSITIVE_SECONDS);
            AAUnits.BoforsInterceptShahed = ClampUnit(AAUnits.BoforsInterceptShahed);
            AAUnits.BoforsInterceptBallistic = ClampUnit(AAUnits.BoforsInterceptBallistic);
            AAUnits.PatriotRange = Math.Max(AAUnits.PatriotRange, 1f);
            AAUnits.PatriotMaxAmmo = Math.Max(AAUnits.PatriotMaxAmmo, 1);
            AAUnits.PatriotBurstRounds = Math.Max(AAUnits.PatriotBurstRounds, 1);
            AAUnits.PatriotCooldown = Math.Max(AAUnits.PatriotCooldown, MIN_POSITIVE_SECONDS);
            AAUnits.PatriotInterceptShahed = ClampUnit(AAUnits.PatriotInterceptShahed);
            AAUnits.PatriotInterceptBallistic = ClampUnit(AAUnits.PatriotInterceptBallistic);
            if (GridWarfare.PressureFloor > GridWarfare.PressureCap)
                (GridWarfare.PressureFloor, GridWarfare.PressureCap) = (GridWarfare.PressureCap, GridWarfare.PressureFloor);
        }

        private static float ClampUnit(float value) => Math.Clamp(value, 0f, 1f);

        private static float ClampUnitFinite(float value, float fallback) =>
            ClampUnit(FiniteOr(value, fallback));

        private static float NonNegativeFinite(float value, float fallback) =>
            Math.Max(FiniteOr(value, fallback), 0f);

        private static float PositiveFinite(float value, float fallback, float min = MIN_POSITIVE_SECONDS) =>
            Math.Max(FiniteOr(value, fallback), min);

        private static float FiniteOr(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        /// <summary>
        /// Deep-clone this config. Replaces reflection-based JSON.Dump/Load round-trip.
        /// </summary>
        public RemoteBalanceConfig Clone()
        {
            var clone = new RemoteBalanceConfig
            {
                Version = Version,
                UpdatedAt = UpdatedAt,
                SchemaRevision = SchemaRevision,
                Threats = Threats.Clone(),
                AirDefense = AirDefense.Clone(),
                Waves = Waves.Clone(),
                Spotter = Spotter.Clone(),
                Penalties = Penalties.Clone(),
                Repair = Repair.Clone(),
                Engineering = Engineering.Clone(),
                Construction = Construction.Clone(),
                GenerationSaturation = GenerationSaturation.Clone(),
                FuelCurve = FuelCurve.Clone(),
                Corruption = Corruption.Clone(),
                Countermeasures = Countermeasures.Clone(),
                Diplomacy = Diplomacy.Clone(),
                Attention = Attention.Clone(),
                Trust = Trust.Clone(),
                Aid = Aid.Clone(),
                Economy = Economy.Clone(),
                Intel = Intel.Clone(),
                ShadowImport = ShadowImport.Clone(),
                PowerGrid = PowerGrid.Clone(),
                GridStress = GridStress.Clone(),
                EquipmentWear = EquipmentWear.Clone(),
                BackupPower = BackupPower.Clone(),
                Narrative = Narrative.Clone(),
                Notifications = Notifications.Clone(),
                NeighborEnvy = NeighborEnvy.Clone(),
                Districts = Districts.Clone(),
                EmergencyFund = EmergencyFund.Clone(),
                FuelSiphoning = FuelSiphoning.Clone(),
                ShadowProcurement = ShadowProcurement.Clone(),
                CorruptionEvents = CorruptionEvents.Clone(),
                ShadowReputation = ShadowReputation.Clone(),
                Procurement = Procurement.Clone(),
                HumanitarianAid = HumanitarianAid.Clone(),
                Scenario = Scenario.Clone(),
                FeatureGates = FeatureGates.Clone(),
                InfrastructureRepair = InfrastructureRepair.Clone(),
                LoadShedding = LoadShedding.Clone(),
                Cognitive = Cognitive.Clone(),
                Mobilization = Mobilization.Clone(),
                AAUnits = AAUnits.Clone(),
                Debt = Debt.Clone(),
                CityStability = CityStability.Clone(),
                GridWarfare = GridWarfare.Clone(),
            };
            return clone;
        }
    }

    public sealed partial class ThreatsConfig
    {
        public float ShahedSpeed { get; set; } = 40.0f;

        public float BallisticSpeed { get; set; } = 380.0f;

        public float BallisticImpactRadius { get; set; } = 150.0f;

        public float DirectHitRadiusBase { get; set; } = 35.0f;

        public float DestructionSeverity { get; set; } = 0.35f;

        public float FireSeverity { get; set; } = 0.14f;

        public float BallisticDestructionSeverity { get; set; } = 0.56f;

        public float BallisticFireSeverity { get; set; } = 0.4f;

        public float DebrisCheckRadius { get; set; } = 100.0f;

        public float DebrisFallTime { get; set; } = 5.0f;

        public int HospitalBaseCasualties { get; set; } = 40;

        public int SchoolBaseCasualties { get; set; } = 60;

        public int FireConditionDamage { get; set; } = 30;

        public int MinSignificantLossMw { get; set; } = 10;

        public float CivilianDurabilityMultiplier { get; set; } = 0.5f;

        public float ShahedCEP { get; set; } = 15.0f;

        public float FocusStrikeCEP { get; set; } = 8.0f;


        public ThreatsConfig Clone()
        {
            var clone = new ThreatsConfig
            {
                ShahedSpeed = ShahedSpeed,
                BallisticSpeed = BallisticSpeed,
                BallisticImpactRadius = BallisticImpactRadius,
                DirectHitRadiusBase = DirectHitRadiusBase,
                DestructionSeverity = DestructionSeverity,
                FireSeverity = FireSeverity,
                BallisticDestructionSeverity = BallisticDestructionSeverity,
                BallisticFireSeverity = BallisticFireSeverity,
                DebrisCheckRadius = DebrisCheckRadius,
                DebrisFallTime = DebrisFallTime,
                HospitalBaseCasualties = HospitalBaseCasualties,
                SchoolBaseCasualties = SchoolBaseCasualties,
                FireConditionDamage = FireConditionDamage,
                MinSignificantLossMw = MinSignificantLossMw,
                CivilianDurabilityMultiplier = CivilianDurabilityMultiplier,
                ShahedCEP = ShahedCEP,
                FocusStrikeCEP = FocusStrikeCEP,
            };
            return clone;
        }
    }

    public sealed partial class AirDefenseConfig
    {
        public float Range { get; set; } = 1200.0f;

        public float BallisticIntercept { get; set; } = 0.0f;

        public int MaxAmmo { get; set; } = 50;

        public float AmmoRefillDurationSeconds { get; set; } = 720.0f;

        public float StartAmmoFraction { get; set; } = 0.25f;

        public float Cooldown { get; set; } = 30.0f;

        public float EvasionPerShot { get; set; } = 0.12f;

        public float EvasionMinChance { get; set; } = 0.05f;

        public float RaycastEndpointEpsilon { get; set; } = 0.02f;

        public float EvasionLogThreshold { get; set; } = 0.2f;

        public float CriticalDistance { get; set; } = 500.0f;

        public float LOSAltitudeBypass { get; set; } = 100.0f;

        public float BallisticSkipAltitude { get; set; } = 200.0f;


        public AirDefenseConfig Clone()
        {
            var clone = new AirDefenseConfig
            {
                Range = Range,
                BallisticIntercept = BallisticIntercept,
                MaxAmmo = MaxAmmo,
                AmmoRefillDurationSeconds = AmmoRefillDurationSeconds,
                StartAmmoFraction = StartAmmoFraction,
                Cooldown = Cooldown,
                EvasionPerShot = EvasionPerShot,
                EvasionMinChance = EvasionMinChance,
                RaycastEndpointEpsilon = RaycastEndpointEpsilon,
                EvasionLogThreshold = EvasionLogThreshold,
                CriticalDistance = CriticalDistance,
                LOSAltitudeBypass = LOSAltitudeBypass,
                BallisticSkipAltitude = BallisticSkipAltitude,
            };
            return clone;
        }
    }

    public sealed partial class WavesConfig
    {
        public float CalmMin { get; set; } = 900.0f;

        public float CalmMax { get; set; } = 1200.0f;

        public float AlertMin { get; set; } = 120.0f;

        public float AlertMax { get; set; } = 300.0f;

        public float AttackMin { get; set; } = 300.0f;

        public float AttackMax { get; set; } = 900.0f;

        public float RecoveryMin { get; set; } = 900.0f;

        public float RecoveryMax { get; set; } = 1800.0f;

        public float MaxAttackDuration { get; set; } = 1800.0f;

        public float FrequencyBaseMinutes { get; set; } = 110.0f;

        public float FirstCalmBaseMinutes { get; set; } = 38.0f;

        public float FrequencyMwDivisor { get; set; } = 100.0f;

        public float FrequencyRngMin { get; set; } = 0.7f;

        public float FrequencyRngMax { get; set; } = 1.5f;

        public float DoubleTapChance { get; set; } = 0.05f;

        public float DoubleTapMinSeconds { get; set; } = 300.0f;

        public float DoubleTapMaxSeconds { get; set; } = 600.0f;

        public float WinterFrequencyMod { get; set; } = 0.6f;

        public float SummerFrequencyMod { get; set; } = 1.2f;

        public float DefaultFrequencyMod { get; set; } = 1.0f;

        public float MassiveStrikeBaseChance { get; set; } = 0.0f;

        public float MassiveStrikeWaveBonus { get; set; } = 0.01f;

        public float MassiveStrikeMaxChance { get; set; } = 0.15f;

        public float MassiveSizeMult { get; set; } = 2.0f;

        public float MassiveTimeMult { get; set; } = 1.2f;

        public float IntroStrengthMult { get; set; } = 1.2f;

        public float SizeFactorMult { get; set; } = 3.0f;

        public float SizeFactorDiv { get; set; } = 50.0f;

        public float TimeFactorPerWave { get; set; } = 0.25f;

        public int MinThreats { get; set; } = 3;

        public int MaxThreats { get; set; } = 40;

        public float MinCalmSeconds { get; set; } = 2700.0f;

        public float RecoveredThreshold { get; set; } = 0.1f;

        public float GraceFraction { get; set; } = 0.3f;

        public int MapBoundsRecacheWaves { get; set; } = 10;

        public float EscalationPerWave { get; set; } = 0.05f;

        public float MinCalmModifier { get; set; } = 0.5f;

        public float LightFrequency { get; set; } = 1.5f;

        public float LightIntensity { get; set; } = 0.5f;

        public float NormalFrequency { get; set; } = 1.0f;

        public float NormalIntensity { get; set; } = 1.0f;

        public float HeavyFrequency { get; set; } = 0.7f;

        public float HeavyIntensity { get; set; } = 1.5f;

        public float OverwhelmingFrequency { get; set; } = 0.5f;

        public float OverwhelmingIntensity { get; set; } = 2.0f;

        public float TargetEnergyRatio { get; set; } = 0.6f;

        public float TargetCriticalRatio { get; set; } = 0.15f;

        public float TargetServiceRatio { get; set; } = 0.15f;

        public float TargetCivilianRatio { get; set; } = 0.1f;

        public float MassiveEnergyRatio { get; set; } = 0.8f;

        public float MassiveCriticalRatio { get; set; } = 0.15f;

        public float MassiveServiceRatio { get; set; } = 0.05f;

        public float MassiveCivilianRatio { get; set; } = 0.0f;

        public float IntroEnergyRatio { get; set; } = 0.85f;

        public float IntroCriticalRatio { get; set; } = 0.1f;

        public float IntroServiceRatio { get; set; } = 0.05f;

        public float IntroCivilianRatio { get; set; } = 0.0f;

        public float TargetingVariance { get; set; } = 0.1f;

        public int MaxThreatsPerTarget { get; set; } = 3;

        public int NonPlantTargetWeightMW { get; set; } = 100;

        public float FocusFraction { get; set; } = 0.45f;

        public float FocusInterceptMultiplier { get; set; } = 0.5f;

        public float MaxWaveInterceptFraction { get; set; } = 0.75f;

        public int BallisticMinProductionMw { get; set; } = 300;

        public int BallisticStartWave { get; set; } = 1;

        public int BallisticMwPerMissile { get; set; } = 500;

        public int BallisticMaxPerWave { get; set; } = 6;

        public int BallisticMaxConcurrentInFlight { get; set; } = 8;

        public float BallisticWaveBonus { get; set; } = 0.5f;

        public bool SurplusStrikesEnabled { get; set; } = true;

        public float SurplusFreeThreshold { get; set; } = 1.3f;

        public float SurplusFrequencyGain { get; set; } = 1.0f;

        public float SurplusSizeGain { get; set; } = 1.0f;

        public float SurplusMaxFreqMult { get; set; } = 2.0f;

        public float SurplusMaxSizeMult { get; set; } = 2.0f;

        public bool DensityStrikesEnabled { get; set; } = true;

        public float DensityRefGuns { get; set; } = 6.0f;

        public float DensityFreeThreshold { get; set; } = 1.5f;

        public float DensitySizeGain { get; set; } = 0.2f;

        public float DensityMaxSizeMult { get; set; } = 1.4f;

        public float SurchargeLethalFraction { get; set; } = 0.4f;


        public WavesConfig Clone()
        {
            var clone = new WavesConfig
            {
                CalmMin = CalmMin,
                CalmMax = CalmMax,
                AlertMin = AlertMin,
                AlertMax = AlertMax,
                AttackMin = AttackMin,
                AttackMax = AttackMax,
                RecoveryMin = RecoveryMin,
                RecoveryMax = RecoveryMax,
                MaxAttackDuration = MaxAttackDuration,
                FrequencyBaseMinutes = FrequencyBaseMinutes,
                FirstCalmBaseMinutes = FirstCalmBaseMinutes,
                FrequencyMwDivisor = FrequencyMwDivisor,
                FrequencyRngMin = FrequencyRngMin,
                FrequencyRngMax = FrequencyRngMax,
                DoubleTapChance = DoubleTapChance,
                DoubleTapMinSeconds = DoubleTapMinSeconds,
                DoubleTapMaxSeconds = DoubleTapMaxSeconds,
                WinterFrequencyMod = WinterFrequencyMod,
                SummerFrequencyMod = SummerFrequencyMod,
                DefaultFrequencyMod = DefaultFrequencyMod,
                MassiveStrikeBaseChance = MassiveStrikeBaseChance,
                MassiveStrikeWaveBonus = MassiveStrikeWaveBonus,
                MassiveStrikeMaxChance = MassiveStrikeMaxChance,
                MassiveSizeMult = MassiveSizeMult,
                MassiveTimeMult = MassiveTimeMult,
                IntroStrengthMult = IntroStrengthMult,
                SizeFactorMult = SizeFactorMult,
                SizeFactorDiv = SizeFactorDiv,
                TimeFactorPerWave = TimeFactorPerWave,
                MinThreats = MinThreats,
                MaxThreats = MaxThreats,
                MinCalmSeconds = MinCalmSeconds,
                RecoveredThreshold = RecoveredThreshold,
                GraceFraction = GraceFraction,
                MapBoundsRecacheWaves = MapBoundsRecacheWaves,
                EscalationPerWave = EscalationPerWave,
                MinCalmModifier = MinCalmModifier,
                LightFrequency = LightFrequency,
                LightIntensity = LightIntensity,
                NormalFrequency = NormalFrequency,
                NormalIntensity = NormalIntensity,
                HeavyFrequency = HeavyFrequency,
                HeavyIntensity = HeavyIntensity,
                OverwhelmingFrequency = OverwhelmingFrequency,
                OverwhelmingIntensity = OverwhelmingIntensity,
                TargetEnergyRatio = TargetEnergyRatio,
                TargetCriticalRatio = TargetCriticalRatio,
                TargetServiceRatio = TargetServiceRatio,
                TargetCivilianRatio = TargetCivilianRatio,
                MassiveEnergyRatio = MassiveEnergyRatio,
                MassiveCriticalRatio = MassiveCriticalRatio,
                MassiveServiceRatio = MassiveServiceRatio,
                MassiveCivilianRatio = MassiveCivilianRatio,
                IntroEnergyRatio = IntroEnergyRatio,
                IntroCriticalRatio = IntroCriticalRatio,
                IntroServiceRatio = IntroServiceRatio,
                IntroCivilianRatio = IntroCivilianRatio,
                TargetingVariance = TargetingVariance,
                MaxThreatsPerTarget = MaxThreatsPerTarget,
                NonPlantTargetWeightMW = NonPlantTargetWeightMW,
                FocusFraction = FocusFraction,
                FocusInterceptMultiplier = FocusInterceptMultiplier,
                MaxWaveInterceptFraction = MaxWaveInterceptFraction,
                BallisticMinProductionMw = BallisticMinProductionMw,
                BallisticStartWave = BallisticStartWave,
                BallisticMwPerMissile = BallisticMwPerMissile,
                BallisticMaxPerWave = BallisticMaxPerWave,
                BallisticMaxConcurrentInFlight = BallisticMaxConcurrentInFlight,
                BallisticWaveBonus = BallisticWaveBonus,
                SurplusStrikesEnabled = SurplusStrikesEnabled,
                SurplusFreeThreshold = SurplusFreeThreshold,
                SurplusFrequencyGain = SurplusFrequencyGain,
                SurplusSizeGain = SurplusSizeGain,
                SurplusMaxFreqMult = SurplusMaxFreqMult,
                SurplusMaxSizeMult = SurplusMaxSizeMult,
                DensityStrikesEnabled = DensityStrikesEnabled,
                DensityRefGuns = DensityRefGuns,
                DensityFreeThreshold = DensityFreeThreshold,
                DensitySizeGain = DensitySizeGain,
                DensityMaxSizeMult = DensityMaxSizeMult,
                SurchargeLethalFraction = SurchargeLethalFraction,
            };
            return clone;
        }
    }

    public sealed partial class SpotterConfig
    {
        public float PenaltyPerSpotter { get; set; } = 0.02f;

        public float MaxGlobalPenalty { get; set; } = 0.2f;

        public int MaxSpotters { get; set; } = 10;

        public float BaseSpawnIntervalDays { get; set; } = 14.0f;

        public float SpawnOnImpactChance { get; set; } = 0.1f;

        public float SpawnOnBlackoutChance { get; set; } = 0.1f;

        public float SpawnOnVipChance { get; set; } = 0.15f;

        public int SbuBaseCost { get; set; } = 10000;

        public int SbuCostIncrement { get; set; } = 5000;

        public int SbuMaxCost { get; set; } = 25000;

        public float SbuSilenceDays { get; set; } = 7.0f;

        public float SbuArticleBaseChance { get; set; } = 0.2f;

        public float SbuArticleIncrement { get; set; } = 0.02f;

        public float SbuArticleMaxChance { get; set; } = 0.5f;

        public float InternetCommercePenalty { get; set; } = 0.2f;

        public int EvacuationCost { get; set; } = 50000;

        public float EvacuationReturnChance { get; set; } = 0.2f;

        public float EvacuationReturnDays { get; set; } = 30.0f;

        public int CounterOsintDailyCost { get; set; } = 5000;

        public float CounterOsintMultiplier { get; set; } = 0.5f;

        public float LongBlackoutThresholdHours { get; set; } = 4.0f;

        public float BaseDetectionChancePerHour { get; set; } = 0.005f;

        public float SpotterSilenceHours { get; set; } = 12.0f;

        public float EvacReturnStaleDays { get; set; } = 30.0f;


        public SpotterConfig Clone()
        {
            var clone = new SpotterConfig
            {
                PenaltyPerSpotter = PenaltyPerSpotter,
                MaxGlobalPenalty = MaxGlobalPenalty,
                MaxSpotters = MaxSpotters,
                BaseSpawnIntervalDays = BaseSpawnIntervalDays,
                SpawnOnImpactChance = SpawnOnImpactChance,
                SpawnOnBlackoutChance = SpawnOnBlackoutChance,
                SpawnOnVipChance = SpawnOnVipChance,
                SbuBaseCost = SbuBaseCost,
                SbuCostIncrement = SbuCostIncrement,
                SbuMaxCost = SbuMaxCost,
                SbuSilenceDays = SbuSilenceDays,
                SbuArticleBaseChance = SbuArticleBaseChance,
                SbuArticleIncrement = SbuArticleIncrement,
                SbuArticleMaxChance = SbuArticleMaxChance,
                InternetCommercePenalty = InternetCommercePenalty,
                EvacuationCost = EvacuationCost,
                EvacuationReturnChance = EvacuationReturnChance,
                EvacuationReturnDays = EvacuationReturnDays,
                CounterOsintDailyCost = CounterOsintDailyCost,
                CounterOsintMultiplier = CounterOsintMultiplier,
                LongBlackoutThresholdHours = LongBlackoutThresholdHours,
                BaseDetectionChancePerHour = BaseDetectionChancePerHour,
                SpotterSilenceHours = SpotterSilenceHours,
                EvacReturnStaleDays = EvacReturnStaleDays,
            };
            return clone;
        }
    }

    public sealed partial class PenaltiesConfig
    {
        public float MaxHappinessPenalty { get; set; } = 0.5f;

        public float MaxCommercePenalty { get; set; } = 0.4f;

        public float WinterHappinessPenalty { get; set; } = 0.05f;

        public byte WellbeingDecayRate { get; set; } = (byte)2;

        public byte WellbeingRecoveryRate { get; set; } = (byte)1;

        public byte WellbeingBaseline { get; set; } = (byte)75;


        public PenaltiesConfig Clone()
        {
            var clone = new PenaltiesConfig
            {
                MaxHappinessPenalty = MaxHappinessPenalty,
                MaxCommercePenalty = MaxCommercePenalty,
                WinterHappinessPenalty = WinterHappinessPenalty,
                WellbeingDecayRate = WellbeingDecayRate,
                WellbeingRecoveryRate = WellbeingRecoveryRate,
                WellbeingBaseline = WellbeingBaseline,
            };
            return clone;
        }
    }

    public sealed partial class RepairConfig
    {
        public float HitDamageMW { get; set; } = 1200.0f;

        public float HitFleetSharePercent { get; set; } = 0.1f;

        public float MinHitLossPercent { get; set; } = 0.1f;

        public float MaxHitLossPercent { get; set; } = 0.5f;

        public float DestructionThreshold { get; set; } = 0.95f;

        public float FireThreshold { get; set; } = 0.7f;

        public int RepairCostPerPercent { get; set; } = 1000;


        public RepairConfig Clone()
        {
            var clone = new RepairConfig
            {
                HitDamageMW = HitDamageMW,
                HitFleetSharePercent = HitFleetSharePercent,
                MinHitLossPercent = MinHitLossPercent,
                MaxHitLossPercent = MaxHitLossPercent,
                DestructionThreshold = DestructionThreshold,
                FireThreshold = FireThreshold,
                RepairCostPerPercent = RepairCostPerPercent,
            };
            return clone;
        }
    }

    public sealed partial class EngineeringConfig
    {
        public float WinterMultMid { get; set; } = 1.5f;

        public float WinterMultMax { get; set; } = 3.0f;

        public float WinterCrisisThreshold { get; set; } = 2.0f;

        public float DisasterChancePerDay { get; set; } = 0.03f;

        public int SmallPlantCapacityKw { get; set; } = 10000;

        public float MultiplierChangeEpsilon { get; set; } = 0.05f;


        public EngineeringConfig Clone()
        {
            var clone = new EngineeringConfig
            {
                WinterMultMid = WinterMultMid,
                WinterMultMax = WinterMultMax,
                WinterCrisisThreshold = WinterCrisisThreshold,
                DisasterChancePerDay = DisasterChancePerDay,
                SmallPlantCapacityKw = SmallPlantCapacityKw,
                MultiplierChangeEpsilon = MultiplierChangeEpsilon,
            };
            return clone;
        }
    }

    public sealed partial class ConstructionConfig
    {
        public int WindDays { get; set; } = 1;

        public int SolarDays { get; set; } = 2;

        public int GeothermalDays { get; set; } = 3;

        public int GasDays { get; set; } = 3;

        public int HydroDays { get; set; } = 4;

        public int CoalDays { get; set; } = 5;

        public int NuclearDays { get; set; } = 12;

        public int GenericDays { get; set; } = 3;


        public ConstructionConfig Clone()
        {
            var clone = new ConstructionConfig
            {
                WindDays = WindDays,
                SolarDays = SolarDays,
                GeothermalDays = GeothermalDays,
                GasDays = GasDays,
                HydroDays = HydroDays,
                CoalDays = CoalDays,
                NuclearDays = NuclearDays,
                GenericDays = GenericDays,
            };
            return clone;
        }
    }

    public sealed partial class GenerationSaturationConfig
    {
        public bool Enabled { get; set; } = true;

        public float HeadroomBase { get; set; } = 1.3f;

        public float HeadroomPerType { get; set; } = 0.35f;

        public float SaturationSoftness { get; set; } = 0.9f;

        public float SaturationFloor { get; set; } = 0.35f;

        public float TauUpHours { get; set; } = 12.0f;

        public float Hysteresis { get; set; } = 0.03f;

        public float UnitBufferCapMW { get; set; } = 100.0f;

        public float PeakWindowHours { get; set; } = 24.0f;


        public GenerationSaturationConfig Clone()
        {
            var clone = new GenerationSaturationConfig
            {
                Enabled = Enabled,
                HeadroomBase = HeadroomBase,
                HeadroomPerType = HeadroomPerType,
                SaturationSoftness = SaturationSoftness,
                SaturationFloor = SaturationFloor,
                TauUpHours = TauUpHours,
                Hysteresis = Hysteresis,
                UnitBufferCapMW = UnitBufferCapMW,
                PeakWindowHours = PeakWindowHours,
            };
            return clone;
        }
    }

    public sealed partial class FuelCurveConfig
    {
        public bool Enabled { get; set; } = true;

        public float BufferThreshold { get; set; } = 0.2f;

        public float MinOutputAtZero { get; set; } = 0.0f;

        public float AnchorFrac { get; set; } = 0.05f;

        public float AnchorOutput { get; set; } = 0.3f;

        public float SteepnessLow { get; set; } = 1.6f;

        public float SteepnessHigh { get; set; } = 1.6f;


        public FuelCurveConfig Clone()
        {
            var clone = new FuelCurveConfig
            {
                Enabled = Enabled,
                BufferThreshold = BufferThreshold,
                MinOutputAtZero = MinOutputAtZero,
                AnchorFrac = AnchorFrac,
                AnchorOutput = AnchorOutput,
                SteepnessLow = SteepnessLow,
                SteepnessHigh = SteepnessHigh,
            };
            return clone;
        }
    }

    public sealed partial class CorruptionConfig
    {
        public float LevelClean { get; set; } = 10.0f;

        public float LevelMinor { get; set; } = 25.0f;

        public float LevelSuspicious { get; set; } = 50.0f;

        public float LevelCorrupt { get; set; } = 75.0f;

        public float ExportWeight { get; set; } = 0.6f;

        public float VipWeight { get; set; } = 5.0f;

        public float VipBypassWeight { get; set; } = 3.0f;

        public float ShadyContractWeight { get; set; } = 2.0f;

        public int WealthyThreshold { get; set; } = 100000;

        public double OffshoreTier1 { get; set; } = 200000.0d;

        public double OffshoreTier2 { get; set; } = 500000.0d;

        public double OffshoreTier3 { get; set; } = 1000000.0d;

        public float OffshorePointsPerTier { get; set; } = 12.0f;

        public float MaxChangePerDay { get; set; } = 8.0f;

        public float ExposureDecayPerDay { get; set; } = 2.0f;

        public float ExposureWeight { get; set; } = 1.0f;


        public CorruptionConfig Clone()
        {
            var clone = new CorruptionConfig
            {
                LevelClean = LevelClean,
                LevelMinor = LevelMinor,
                LevelSuspicious = LevelSuspicious,
                LevelCorrupt = LevelCorrupt,
                ExportWeight = ExportWeight,
                VipWeight = VipWeight,
                VipBypassWeight = VipBypassWeight,
                ShadyContractWeight = ShadyContractWeight,
                WealthyThreshold = WealthyThreshold,
                OffshoreTier1 = OffshoreTier1,
                OffshoreTier2 = OffshoreTier2,
                OffshoreTier3 = OffshoreTier3,
                OffshorePointsPerTier = OffshorePointsPerTier,
                MaxChangePerDay = MaxChangePerDay,
                ExposureDecayPerDay = ExposureDecayPerDay,
                ExposureWeight = ExposureWeight,
            };
            return clone;
        }
    }

    public sealed partial class CountermeasuresConfig
    {
        public float SuspicionThreshold { get; set; } = 25.0f;

        public float InvestigationThreshold { get; set; } = 50.0f;

        public float HeatGainTier1 { get; set; } = 2.0f;

        public float HeatGainTier2 { get; set; } = 5.0f;

        public float HeatGainTier3 { get; set; } = 10.0f;

        public float HeatDecayRate { get; set; } = 3.0f;

        public float HeatMax { get; set; } = 100.0f;

        public float HeatMinForInvestigation { get; set; } = 10.0f;

        public float HeatWarningThreshold { get; set; } = 20.0f;

        public float HeatDangerThreshold { get; set; } = 50.0f;

        public float HeatCriticalThreshold { get; set; } = 80.0f;

        public float ScandalHeatThreshold { get; set; } = 40.0f;

        public float ScandalTrustPenalty { get; set; } = 20.0f;

        public float ScandalCooldownDays { get; set; } = 14.0f;

        public float ScandalPenaltyDecayPerDay { get; set; } = 2.0f;

        public float InvestigationBaseChance { get; set; } = 0.02f;

        public float InvestigationChancePerPoint { get; set; } = 0.0016f;

        public float ProtestBaseChance { get; set; } = 0.02f;

        public float ProtestMaxBonus { get; set; } = 0.08f;

        public float EventCooldownHours { get; set; } = 2.0f;

        public int ChargesIncrement { get; set; } = 2;

        public float ShadyDisasterSuspicion { get; set; } = 5.0f;

        public float HoursPerProgress { get; set; } = 4.0f;

        public int InvestigationBaseFine { get; set; } = 50000;

        public int InvestigationFinePerPoint { get; set; } = 2000;

        public int JournalistBribeBase { get; set; } = 50000;

        public int JournalistBribePerProgress { get; set; } = 1500;

        public float JournalistBetrayalChance { get; set; } = 0.2f;

        public float InvestigationSpeedThreshold { get; set; } = 75.0f;

        public float InvestigationSpeedMultiplier { get; set; } = 1.5f;

        public float SuspicionExitThreshold { get; set; } = 20.0f;

        public float ArticleFadeThreshold { get; set; } = 30.0f;

        public float PoliceInvestigationHours { get; set; } = 24.0f;

        public int PoliceBribeCost { get; set; } = 200000;

        public float PoliceStartChance { get; set; } = 0.05f;

        public float PoliceHonestChance { get; set; } = 0.3f;

        public float EvidenceDestroySuccess { get; set; } = 0.5f;

        public float PoliceDropThreshold { get; set; } = 25.0f;

        public float ChargeDivisorNormal { get; set; } = 10.0f;

        public float ChargeDivisorPolice { get; set; } = 8.0f;

        public float ChargeDivisorBribeCaught { get; set; } = 4.0f;

        public int ChargeBriberyBonus { get; set; } = 3;

        public int ProtestMaxActive { get; set; } = 5;

        public int ProtestParticipantsMin { get; set; } = 50;

        public int ProtestParticipantsRange { get; set; } = 200;

        public float SbuSuspicionGain { get; set; } = 10.0f;

        public float ProtestCooldownSeconds { get; set; } = 600.0f;

        public float ProtestDecaySeconds { get; set; } = 1800.0f;

        public int SuspicionCooldownDays { get; set; } = 7;

        public float HeatRefundOnResolve { get; set; } = 25.0f;

        public float PoliceCooperateEvidenceThreshold { get; set; } = 50.0f;

        public float ScandalPenaltyCap { get; set; } = 80.0f;

        public float SanctionsCorruptionSuppression { get; set; } = 15.0f;

        public int InvestigationMilestone25 { get; set; } = 25;

        public int InvestigationMilestone50 { get; set; } = 50;

        public int InvestigationMilestone75 { get; set; } = 75;


        public CountermeasuresConfig Clone()
        {
            var clone = new CountermeasuresConfig
            {
                SuspicionThreshold = SuspicionThreshold,
                InvestigationThreshold = InvestigationThreshold,
                HeatGainTier1 = HeatGainTier1,
                HeatGainTier2 = HeatGainTier2,
                HeatGainTier3 = HeatGainTier3,
                HeatDecayRate = HeatDecayRate,
                HeatMax = HeatMax,
                HeatMinForInvestigation = HeatMinForInvestigation,
                HeatWarningThreshold = HeatWarningThreshold,
                HeatDangerThreshold = HeatDangerThreshold,
                HeatCriticalThreshold = HeatCriticalThreshold,
                ScandalHeatThreshold = ScandalHeatThreshold,
                ScandalTrustPenalty = ScandalTrustPenalty,
                ScandalCooldownDays = ScandalCooldownDays,
                ScandalPenaltyDecayPerDay = ScandalPenaltyDecayPerDay,
                InvestigationBaseChance = InvestigationBaseChance,
                InvestigationChancePerPoint = InvestigationChancePerPoint,
                ProtestBaseChance = ProtestBaseChance,
                ProtestMaxBonus = ProtestMaxBonus,
                EventCooldownHours = EventCooldownHours,
                ChargesIncrement = ChargesIncrement,
                ShadyDisasterSuspicion = ShadyDisasterSuspicion,
                HoursPerProgress = HoursPerProgress,
                InvestigationBaseFine = InvestigationBaseFine,
                InvestigationFinePerPoint = InvestigationFinePerPoint,
                JournalistBribeBase = JournalistBribeBase,
                JournalistBribePerProgress = JournalistBribePerProgress,
                JournalistBetrayalChance = JournalistBetrayalChance,
                InvestigationSpeedThreshold = InvestigationSpeedThreshold,
                InvestigationSpeedMultiplier = InvestigationSpeedMultiplier,
                SuspicionExitThreshold = SuspicionExitThreshold,
                ArticleFadeThreshold = ArticleFadeThreshold,
                PoliceInvestigationHours = PoliceInvestigationHours,
                PoliceBribeCost = PoliceBribeCost,
                PoliceStartChance = PoliceStartChance,
                PoliceHonestChance = PoliceHonestChance,
                EvidenceDestroySuccess = EvidenceDestroySuccess,
                PoliceDropThreshold = PoliceDropThreshold,
                ChargeDivisorNormal = ChargeDivisorNormal,
                ChargeDivisorPolice = ChargeDivisorPolice,
                ChargeDivisorBribeCaught = ChargeDivisorBribeCaught,
                ChargeBriberyBonus = ChargeBriberyBonus,
                ProtestMaxActive = ProtestMaxActive,
                ProtestParticipantsMin = ProtestParticipantsMin,
                ProtestParticipantsRange = ProtestParticipantsRange,
                SbuSuspicionGain = SbuSuspicionGain,
                ProtestCooldownSeconds = ProtestCooldownSeconds,
                ProtestDecaySeconds = ProtestDecaySeconds,
                SuspicionCooldownDays = SuspicionCooldownDays,
                HeatRefundOnResolve = HeatRefundOnResolve,
                PoliceCooperateEvidenceThreshold = PoliceCooperateEvidenceThreshold,
                ScandalPenaltyCap = ScandalPenaltyCap,
                SanctionsCorruptionSuppression = SanctionsCorruptionSuppression,
                InvestigationMilestone25 = InvestigationMilestone25,
                InvestigationMilestone50 = InvestigationMilestone50,
                InvestigationMilestone75 = InvestigationMilestone75,
            };
            return clone;
        }
    }

    public sealed partial class DiplomacyConfig
    {
        public int ConferenceMaxUses { get; set; } = 2;

        public float ConferenceCooldownDays { get; set; } = 30.0f;

        public float CrisisThreshold { get; set; } = 60.0f;

        public int MinGameDay { get; set; } = 30;

        public int FundsFull { get; set; } = 500000;

        public int GeneratorMw { get; set; } = 50;

        public int PatriotDays { get; set; } = 30;

        public float SanctionTradePenalty { get; set; } = 0.2f;

        public int SanctionDays { get; set; } = 60;

        public float SanctionsBlackMarketMarkup { get; set; } = 1.5f;

        public float GretaTrustBonus { get; set; } = 5.0f;

        public int GeneratorDecayIntervalDays { get; set; } = 14;

        public float ImportTrustPenaltyDecayPerDay { get; set; } = 1.0f;


        public DiplomacyConfig Clone()
        {
            var clone = new DiplomacyConfig
            {
                ConferenceMaxUses = ConferenceMaxUses,
                ConferenceCooldownDays = ConferenceCooldownDays,
                CrisisThreshold = CrisisThreshold,
                MinGameDay = MinGameDay,
                FundsFull = FundsFull,
                GeneratorMw = GeneratorMw,
                PatriotDays = PatriotDays,
                SanctionTradePenalty = SanctionTradePenalty,
                SanctionDays = SanctionDays,
                SanctionsBlackMarketMarkup = SanctionsBlackMarketMarkup,
                GretaTrustBonus = GretaTrustBonus,
                GeneratorDecayIntervalDays = GeneratorDecayIntervalDays,
                ImportTrustPenaltyDecayPerDay = ImportTrustPenaltyDecayPerDay,
            };
            return clone;
        }
    }

    public sealed partial class AttentionConfig
    {
        public float TierDeepConcern { get; set; } = 15.0f;

        public float TierHeadlines { get; set; } = 30.0f;

        public float TierGlobalShock { get; set; } = 60.0f;

        public float ShockPerCasualty { get; set; } = 1.0f;

        public float ShockPerBuilding { get; set; } = 5.0f;

        public float ShockMassCasualtyBonus { get; set; } = 10.0f;

        public float ShockCriticalHitBonus { get; set; } = 20.0f;

        public int MassCasualtyThreshold { get; set; } = 10;

        public float MultHospital { get; set; } = 2.0f;

        public float MultSchool { get; set; } = 2.0f;

        public float MultCriticalInfra { get; set; } = 1.5f;

        public float DecayPerDay { get; set; } = 5.0f;

        public float DecayPauseHours { get; set; } = 24.0f;

        public float ExodusDeepConcern { get; set; } = 0.1f;

        public float ExodusHeadlines { get; set; } = 0.5f;

        public float ExodusGlobalMin { get; set; } = 2.0f;

        public float ExodusGlobalMax { get; set; } = 4.0f;

        public int ExodusMilestoneInterval { get; set; } = 50;

        public int ExodusMilestoneMinThreshold { get; set; } = 100;

        public float IntegrityMultLoyal { get; set; } = 0.5f;

        public float IntegrityMultAnxious { get; set; } = 1.0f;

        public float IntegrityMultRebellious { get; set; } = 1.5f;

        public float IntegrityMultBrainwashed { get; set; } = 2.0f;

        public float IntegrityMultZombie { get; set; } = 3.0f;

        public float IntegrityThresholdLoyal { get; set; } = 0.8f;

        public float IntegrityThresholdAnxious { get; set; } = 0.5f;

        public float IntegrityThresholdRebellious { get; set; } = 0.3f;

        public float IntegrityThresholdBrainwashed { get; set; } = 0.1f;


        public AttentionConfig Clone()
        {
            var clone = new AttentionConfig
            {
                TierDeepConcern = TierDeepConcern,
                TierHeadlines = TierHeadlines,
                TierGlobalShock = TierGlobalShock,
                ShockPerCasualty = ShockPerCasualty,
                ShockPerBuilding = ShockPerBuilding,
                ShockMassCasualtyBonus = ShockMassCasualtyBonus,
                ShockCriticalHitBonus = ShockCriticalHitBonus,
                MassCasualtyThreshold = MassCasualtyThreshold,
                MultHospital = MultHospital,
                MultSchool = MultSchool,
                MultCriticalInfra = MultCriticalInfra,
                DecayPerDay = DecayPerDay,
                DecayPauseHours = DecayPauseHours,
                ExodusDeepConcern = ExodusDeepConcern,
                ExodusHeadlines = ExodusHeadlines,
                ExodusGlobalMin = ExodusGlobalMin,
                ExodusGlobalMax = ExodusGlobalMax,
                ExodusMilestoneInterval = ExodusMilestoneInterval,
                ExodusMilestoneMinThreshold = ExodusMilestoneMinThreshold,
                IntegrityMultLoyal = IntegrityMultLoyal,
                IntegrityMultAnxious = IntegrityMultAnxious,
                IntegrityMultRebellious = IntegrityMultRebellious,
                IntegrityMultBrainwashed = IntegrityMultBrainwashed,
                IntegrityMultZombie = IntegrityMultZombie,
                IntegrityThresholdLoyal = IntegrityThresholdLoyal,
                IntegrityThresholdAnxious = IntegrityThresholdAnxious,
                IntegrityThresholdRebellious = IntegrityThresholdRebellious,
                IntegrityThresholdBrainwashed = IntegrityThresholdBrainwashed,
            };
            return clone;
        }
    }

    public sealed partial class TrustConfig
    {
        public float FullAidMax { get; set; } = 35.0f;

        public float PartialAidMax { get; set; } = 60.0f;

        public float MinimalAidMax { get; set; } = 80.0f;

        public float PartialDelivery { get; set; } = 0.7f;

        public float MinimalDelivery { get; set; } = 0.4f;

        public float InvestigationThreshold { get; set; } = 50.0f;


        public TrustConfig Clone()
        {
            var clone = new TrustConfig
            {
                FullAidMax = FullAidMax,
                PartialAidMax = PartialAidMax,
                MinimalAidMax = MinimalAidMax,
                PartialDelivery = PartialDelivery,
                MinimalDelivery = MinimalDelivery,
                InvestigationThreshold = InvestigationThreshold,
            };
            return clone;
        }
    }

    public sealed partial class AidConfig
    {
        public int DeepConcernFunds { get; set; } = 100000;

        public float DeepConcernAmmoPercent { get; set; } = 50.0f;

        public int HeadlinesFunds { get; set; } = 1000000;

        public int HeadlinesGenerators { get; set; } = 3;

        public int HeadlinesGeneratorMw { get; set; } = 50;

        public int GlobalShockFunds { get; set; } = 10000000;

        public int GlobalShockGenerators { get; set; } = 10;

        public int GlobalShockPatriotDays { get; set; } = 30;


        public AidConfig Clone()
        {
            var clone = new AidConfig
            {
                DeepConcernFunds = DeepConcernFunds,
                DeepConcernAmmoPercent = DeepConcernAmmoPercent,
                HeadlinesFunds = HeadlinesFunds,
                HeadlinesGenerators = HeadlinesGenerators,
                HeadlinesGeneratorMw = HeadlinesGeneratorMw,
                GlobalShockFunds = GlobalShockFunds,
                GlobalShockGenerators = GlobalShockGenerators,
                GlobalShockPatriotDays = GlobalShockPatriotDays,
            };
            return clone;
        }
    }

    public sealed partial class EconomyConfig
    {
        public int AmmoCostPerRound { get; set; } = 2000;

        public int PatriotResupplyCost { get; set; } = 60000;

        public int BoforsResupplyCost { get; set; } = 8000;

        public int HeritageResupplyCost { get; set; } = 4000;

        public int GepardResupplyCost { get; set; } = 12000;

        public float PatriotResupplyCooldownHours { get; set; } = 0.0f;

        public int PatriotResupplyCooldownWaves { get; set; } = 1;

        public float BoforsResupplyCooldownHours { get; set; } = 0.0f;

        public float HeritageResupplyCooldownHours { get; set; } = 0.0f;

        public float GepardResupplyCooldownHours { get; set; } = 0.0f;

        public float ShadowPricePerMwDay { get; set; } = 400.0f;

        public int InsiderCost { get; set; } = 25000;


        public EconomyConfig Clone()
        {
            var clone = new EconomyConfig
            {
                AmmoCostPerRound = AmmoCostPerRound,
                PatriotResupplyCost = PatriotResupplyCost,
                BoforsResupplyCost = BoforsResupplyCost,
                HeritageResupplyCost = HeritageResupplyCost,
                GepardResupplyCost = GepardResupplyCost,
                PatriotResupplyCooldownHours = PatriotResupplyCooldownHours,
                PatriotResupplyCooldownWaves = PatriotResupplyCooldownWaves,
                BoforsResupplyCooldownHours = BoforsResupplyCooldownHours,
                HeritageResupplyCooldownHours = HeritageResupplyCooldownHours,
                GepardResupplyCooldownHours = GepardResupplyCooldownHours,
                ShadowPricePerMwDay = ShadowPricePerMwDay,
                InsiderCost = InsiderCost,
            };
            return clone;
        }
    }

    public sealed partial class IntelConfig
    {
        public float NoisePercent { get; set; } = 0.3f;

        public float TimeNoiseMin { get; set; } = 0.5f;

        public float TimeNoiseMax { get; set; } = 2.0f;

        public int TensionLowMax { get; set; } = 30;

        public int TensionElevatedMax { get; set; } = 50;

        public int TensionHighMax { get; set; } = 75;

        public float PriceMultLow { get; set; } = 1.0f;

        public float PriceMultElevated { get; set; } = 1.15f;

        public float PriceMultHigh { get; set; } = 1.35f;

        public float PriceMultCritical { get; set; } = 2.0f;

        public float InsiderNoisePercent { get; set; } = 0.1f;

        public long IntelUpgradeCostPerLevel { get; set; } = 200000L;


        public IntelConfig Clone()
        {
            var clone = new IntelConfig
            {
                NoisePercent = NoisePercent,
                TimeNoiseMin = TimeNoiseMin,
                TimeNoiseMax = TimeNoiseMax,
                TensionLowMax = TensionLowMax,
                TensionElevatedMax = TensionElevatedMax,
                TensionHighMax = TensionHighMax,
                PriceMultLow = PriceMultLow,
                PriceMultElevated = PriceMultElevated,
                PriceMultHigh = PriceMultHigh,
                PriceMultCritical = PriceMultCritical,
                InsiderNoisePercent = InsiderNoisePercent,
                IntelUpgradeCostPerLevel = IntelUpgradeCostPerLevel,
            };
            return clone;
        }
    }

    public sealed partial class ShadowImportConfig
    {
        public float PricePerMwDay { get; set; } = 600.0f;

        public float PriceMultiplier { get; set; } = 3.0f;

        public float MaxImportPercent { get; set; } = 0.3f;

        public int MinImportMw { get; set; } = 10;

        public int AbsoluteMaxMw { get; set; } = 500;

        public float RiskDecayPerDay { get; set; } = 0.05f;

        public int SanctionDurationDays { get; set; } = 7;

        public float AttentionIncrease { get; set; } = 20.0f;

        public float DonorTrustDecrease { get; set; } = 10.0f;

        public float CorruptionOnActivate { get; set; } = 5.0f;

        public float CorruptionPerDayActive { get; set; } = 1.0f;

        public float GateLevel1Threshold { get; set; } = 25.0f;

        public float GateLevel1Multiplier { get; set; } = 0.17f;

        public float GateLevel2Threshold { get; set; } = 50.0f;

        public float GateLevel2Multiplier { get; set; } = 0.67f;

        public float GateLevel3Multiplier { get; set; } = 1.0f;

        public float RiskDay1 { get; set; } = 0.01f;

        public float RiskDay2 { get; set; } = 0.05f;

        public float RiskDay3 { get; set; } = 0.2f;

        public float RiskDay4Plus { get; set; } = 0.5f;


        public ShadowImportConfig Clone()
        {
            var clone = new ShadowImportConfig
            {
                PricePerMwDay = PricePerMwDay,
                PriceMultiplier = PriceMultiplier,
                MaxImportPercent = MaxImportPercent,
                MinImportMw = MinImportMw,
                AbsoluteMaxMw = AbsoluteMaxMw,
                RiskDecayPerDay = RiskDecayPerDay,
                SanctionDurationDays = SanctionDurationDays,
                AttentionIncrease = AttentionIncrease,
                DonorTrustDecrease = DonorTrustDecrease,
                CorruptionOnActivate = CorruptionOnActivate,
                CorruptionPerDayActive = CorruptionPerDayActive,
                GateLevel1Threshold = GateLevel1Threshold,
                GateLevel1Multiplier = GateLevel1Multiplier,
                GateLevel2Threshold = GateLevel2Threshold,
                GateLevel2Multiplier = GateLevel2Multiplier,
                GateLevel3Multiplier = GateLevel3Multiplier,
                RiskDay1 = RiskDay1,
                RiskDay2 = RiskDay2,
                RiskDay3 = RiskDay3,
                RiskDay4Plus = RiskDay4Plus,
            };
            return clone;
        }
    }

    public sealed partial class PowerGridConfig
    {
        public float GridPowerThreshold { get; set; } = 0.9f;

        public int CriticalDeficitThreshold { get; set; } = -100000;

        public int SurplusThreshold { get; set; } = 50;

        public int DefaultLegalImportMw { get; set; } = 100;

        public int DefaultLegalExportMw { get; set; } = 0;


        public PowerGridConfig Clone()
        {
            var clone = new PowerGridConfig
            {
                GridPowerThreshold = GridPowerThreshold,
                CriticalDeficitThreshold = CriticalDeficitThreshold,
                SurplusThreshold = SurplusThreshold,
                DefaultLegalImportMw = DefaultLegalImportMw,
                DefaultLegalExportMw = DefaultLegalExportMw,
            };
            return clone;
        }
    }

    public sealed partial class GridStressConfig
    {
        public float NormalFrequency { get; set; } = 50.0f;

        public float YellowZoneFrequency { get; set; } = 49.5f;

        public float RedZoneFrequency { get; set; } = 49.0f;

        public float CollapseFrequency { get; set; } = 48.5f;

        public float CollapseThresholdHours { get; set; } = 2.0f;

        public float RecoveryDurationHours { get; set; } = 24.0f;

        public float StressDecayRate { get; set; } = 2.0f;

        public float GridGraceCoeff { get; set; } = 10.0f;

        public float GridGraceExponent { get; set; } = 0.5f;

        public float GridGraceRefPop { get; set; } = 1000.0f;

        public float GridGraceMaxHours { get; set; } = 16.0f;

        public float WarningThresholdYellow { get; set; } = 0.25f;

        public float WarningThresholdRed { get; set; } = 0.5f;

        public int UpdateIntervalFrames { get; set; } = 60;

        public float DeficitDeadZoneFraction { get; set; } = 0.02f;

        public int DeficitDeadZoneMinKW { get; set; } = 250;

        public float RecoveryStressThreshold { get; set; } = 0.1f;

        public int RecoveryHeadroomMinMW { get; set; } = 5;

        public float RecoveryHeadroomFraction { get; set; } = 0.1f;


        public GridStressConfig Clone()
        {
            var clone = new GridStressConfig
            {
                NormalFrequency = NormalFrequency,
                YellowZoneFrequency = YellowZoneFrequency,
                RedZoneFrequency = RedZoneFrequency,
                CollapseFrequency = CollapseFrequency,
                CollapseThresholdHours = CollapseThresholdHours,
                RecoveryDurationHours = RecoveryDurationHours,
                StressDecayRate = StressDecayRate,
                GridGraceCoeff = GridGraceCoeff,
                GridGraceExponent = GridGraceExponent,
                GridGraceRefPop = GridGraceRefPop,
                GridGraceMaxHours = GridGraceMaxHours,
                WarningThresholdYellow = WarningThresholdYellow,
                WarningThresholdRed = WarningThresholdRed,
                UpdateIntervalFrames = UpdateIntervalFrames,
                DeficitDeadZoneFraction = DeficitDeadZoneFraction,
                DeficitDeadZoneMinKW = DeficitDeadZoneMinKW,
                RecoveryStressThreshold = RecoveryStressThreshold,
                RecoveryHeadroomMinMW = RecoveryHeadroomMinMW,
                RecoveryHeadroomFraction = RecoveryHeadroomFraction,
            };
            return clone;
        }
    }

    public sealed partial class EquipmentWearConfig
    {
        public float BaseWearRate { get; set; } = 0.001f;

        public float OverloadMultiplier { get; set; } = 3.0f;

        public float HighLoadThreshold { get; set; } = 0.9f;

        public float OverloadThreshold { get; set; } = 1.0f;

        public float ExplosionThreshold { get; set; } = 0.5f;

        public float ExplosionChanceMax { get; set; } = 0.01f;

        public float ExplosionDamage { get; set; } = 0.8f;

        public int RepairCostPerPercent { get; set; } = 5000;

        public float MaxWearPercent { get; set; } = 1.0f;

        public int UpdateIntervalFrames { get; set; } = 120;


        public EquipmentWearConfig Clone()
        {
            var clone = new EquipmentWearConfig
            {
                BaseWearRate = BaseWearRate,
                OverloadMultiplier = OverloadMultiplier,
                HighLoadThreshold = HighLoadThreshold,
                OverloadThreshold = OverloadThreshold,
                ExplosionThreshold = ExplosionThreshold,
                ExplosionChanceMax = ExplosionChanceMax,
                ExplosionDamage = ExplosionDamage,
                RepairCostPerPercent = RepairCostPerPercent,
                MaxWearPercent = MaxWearPercent,
                UpdateIntervalFrames = UpdateIntervalFrames,
            };
            return clone;
        }
    }

    public sealed partial class BackupPowerConfig
    {
        public float DegradationPerHour { get; set; } = 0.0001f;

        public float FireRiskMultiplier { get; set; } = 0.01f;

        public int HomeBatteryCapacityWh { get; set; } = 2000;

        public int HomeBatteryChargeRateW { get; set; } = 500;

        public int HomeBatteryDischargeRateW { get; set; } = 500;

        public float HomeBatteryEfficiency { get; set; } = 0.9f;

        public int BusinessUpsCapacityWh { get; set; } = 10000;

        public int BusinessUpsChargeRateW { get; set; } = 2000;

        public int BusinessUpsDischargeRateW { get; set; } = 5000;

        public float BusinessUpsEfficiency { get; set; } = 0.92f;

        public int IndustrialBatteryCapacityWh { get; set; } = 100000;

        public int IndustrialBatteryChargeRateW { get; set; } = 20000;

        public int IndustrialBatteryDischargeRateW { get; set; } = 25000;

        public float IndustrialBatteryEfficiency { get; set; } = 0.88f;

        public int DieselGeneratorDischargeRateW { get; set; } = 10000;

        public float DieselGeneratorEfficiency { get; set; } = 0.35f;

        public float DieselGeneratorFuelHours { get; set; } = 24.0f;

        public int NoiseLevelSilent { get; set; } = 0;

        public int NoiseLevelDiesel { get; set; } = 70;

        public float FireRiskHomeBattery { get; set; } = 0.01f;

        public float FireRiskBusinessUps { get; set; } = 0.02f;

        public float FireRiskIndustrialBattery { get; set; } = 0.05f;

        public float FireRiskDieselGenerator { get; set; } = 0.1f;

        public float IdleDegradationFraction { get; set; } = 0.01f;

        public float CounterfeitIdlePenalty { get; set; } = 10.0f;

        public float DegradationWarningThreshold { get; set; } = 0.2f;

        public float MitigationWeightHospital { get; set; } = 0.4f;

        public float MitigationWeightSchool { get; set; } = 0.3f;

        public float MitigationWeightPrivate { get; set; } = 0.3f;

        public float MitigationMin { get; set; } = 0.1f;

        public float ChargeRateMultLoyal { get; set; } = 1.0f;

        public float ChargeRateMultAnxious { get; set; } = 0.8f;

        public float ChargeRateMultRebellious { get; set; } = 0.5f;

        public float ChargeRateMultBrainwashed { get; set; } = 0.2f;

        public float ChargeRateMultZombie { get; set; } = 0.15f;


        public BackupPowerConfig Clone()
        {
            var clone = new BackupPowerConfig
            {
                DegradationPerHour = DegradationPerHour,
                FireRiskMultiplier = FireRiskMultiplier,
                HomeBatteryCapacityWh = HomeBatteryCapacityWh,
                HomeBatteryChargeRateW = HomeBatteryChargeRateW,
                HomeBatteryDischargeRateW = HomeBatteryDischargeRateW,
                HomeBatteryEfficiency = HomeBatteryEfficiency,
                BusinessUpsCapacityWh = BusinessUpsCapacityWh,
                BusinessUpsChargeRateW = BusinessUpsChargeRateW,
                BusinessUpsDischargeRateW = BusinessUpsDischargeRateW,
                BusinessUpsEfficiency = BusinessUpsEfficiency,
                IndustrialBatteryCapacityWh = IndustrialBatteryCapacityWh,
                IndustrialBatteryChargeRateW = IndustrialBatteryChargeRateW,
                IndustrialBatteryDischargeRateW = IndustrialBatteryDischargeRateW,
                IndustrialBatteryEfficiency = IndustrialBatteryEfficiency,
                DieselGeneratorDischargeRateW = DieselGeneratorDischargeRateW,
                DieselGeneratorEfficiency = DieselGeneratorEfficiency,
                DieselGeneratorFuelHours = DieselGeneratorFuelHours,
                NoiseLevelSilent = NoiseLevelSilent,
                NoiseLevelDiesel = NoiseLevelDiesel,
                FireRiskHomeBattery = FireRiskHomeBattery,
                FireRiskBusinessUps = FireRiskBusinessUps,
                FireRiskIndustrialBattery = FireRiskIndustrialBattery,
                FireRiskDieselGenerator = FireRiskDieselGenerator,
                IdleDegradationFraction = IdleDegradationFraction,
                CounterfeitIdlePenalty = CounterfeitIdlePenalty,
                DegradationWarningThreshold = DegradationWarningThreshold,
                MitigationWeightHospital = MitigationWeightHospital,
                MitigationWeightSchool = MitigationWeightSchool,
                MitigationWeightPrivate = MitigationWeightPrivate,
                MitigationMin = MitigationMin,
                ChargeRateMultLoyal = ChargeRateMultLoyal,
                ChargeRateMultAnxious = ChargeRateMultAnxious,
                ChargeRateMultRebellious = ChargeRateMultRebellious,
                ChargeRateMultBrainwashed = ChargeRateMultBrainwashed,
                ChargeRateMultZombie = ChargeRateMultZombie,
            };
            return clone;
        }
    }

    public sealed partial class NarrativeConfig
    {
        public float UpdateIntervalSeconds { get; set; } = 3.0f;

        public float BatchWindowSeconds { get; set; } = 3.0f;

        public float RelationshipPenaltyMajor { get; set; } = 30.0f;

        public int BlackoutExtremeDays { get; set; } = 5;

        public int BlackoutLongDays { get; set; } = 3;

        public float IdleMessageChance { get; set; } = 0.1f;

        public float VipReactionChance { get; set; } = 0.1f;

        public float AngryDurationHours { get; set; } = 2.0f;


        public NarrativeConfig Clone()
        {
            var clone = new NarrativeConfig
            {
                UpdateIntervalSeconds = UpdateIntervalSeconds,
                BatchWindowSeconds = BatchWindowSeconds,
                RelationshipPenaltyMajor = RelationshipPenaltyMajor,
                BlackoutExtremeDays = BlackoutExtremeDays,
                BlackoutLongDays = BlackoutLongDays,
                IdleMessageChance = IdleMessageChance,
                VipReactionChance = VipReactionChance,
                AngryDurationHours = AngryDurationHours,
            };
            return clone;
        }
    }

    public sealed partial class NotificationsConfig
    {
        public float CooldownSystemAlert { get; set; } = 30.0f;

        public float CooldownSocialPost { get; set; } = 60.0f;

        public int MaxSocialPosts { get; set; } = 20;

        public float DuplicateWindowSocialPost { get; set; } = 60.0f;


        public NotificationsConfig Clone()
        {
            var clone = new NotificationsConfig
            {
                CooldownSystemAlert = CooldownSystemAlert,
                CooldownSocialPost = CooldownSocialPost,
                MaxSocialPosts = MaxSocialPosts,
                DuplicateWindowSocialPost = DuplicateWindowSocialPost,
            };
            return clone;
        }
    }

    public sealed partial class NeighborEnvyConfig
    {
        public float EnvyRadius { get; set; } = 100.0f;

        public float CellSize { get; set; } = 100.0f;


        public NeighborEnvyConfig Clone()
        {
            var clone = new NeighborEnvyConfig
            {
                EnvyRadius = EnvyRadius,
                CellSize = CellSize,
            };
            return clone;
        }
    }

    public sealed partial class DistrictsConfig
    {
        public int DefaultPriority { get; set; } = 3;

        public int MinPriority { get; set; } = 1;

        public int MaxPriority { get; set; } = 5;


        public DistrictsConfig Clone()
        {
            var clone = new DistrictsConfig
            {
                DefaultPriority = DefaultPriority,
                MinPriority = MinPriority,
                MaxPriority = MaxPriority,
            };
            return clone;
        }
    }

    public sealed partial class EmergencyFundConfig
    {
        public double InitialBalance { get; set; } = 500000.0d;

        public float NoFundPenaltyMult { get; set; } = 2.0f;

        public float LowFundThreshold { get; set; } = 0.25f;

        public float LowFundPenaltyMult { get; set; } = 1.5f;

        public float DailyWithdrawRate { get; set; } = 0.5f;

        public float CorruptionPer100k { get; set; } = 1.0f;


        public EmergencyFundConfig Clone()
        {
            var clone = new EmergencyFundConfig
            {
                InitialBalance = InitialBalance,
                NoFundPenaltyMult = NoFundPenaltyMult,
                LowFundThreshold = LowFundThreshold,
                LowFundPenaltyMult = LowFundPenaltyMult,
                DailyWithdrawRate = DailyWithdrawRate,
                CorruptionPer100k = CorruptionPer100k,
            };
            return clone;
        }
    }

    public sealed partial class FuelSiphoningConfig
    {
        public float ConsumptionMultPerPercent { get; set; } = 0.02f;

        public float CorruptionPerPercent { get; set; } = 0.5f;

        public float IncomePerPercentDay { get; set; } = 500.0f;


        public FuelSiphoningConfig Clone()
        {
            var clone = new FuelSiphoningConfig
            {
                ConsumptionMultPerPercent = ConsumptionMultPerPercent,
                CorruptionPerPercent = CorruptionPerPercent,
                IncomePerPercentDay = IncomePerPercentDay,
            };
            return clone;
        }
    }

    public sealed partial class ShadowProcurementConfig
    {
        public float CostPerBuilding { get; set; } = 250.0f;

        public float CorruptKickbackPercent { get; set; } = 0.8f;

        public int CooldownDays { get; set; } = 30;

        public float CounterfeitCapacityMult { get; set; } = 0.5f;

        public float CounterfeitDegradationMult { get; set; } = 2.0f;

        public float CounterfeitFireRiskMult { get; set; } = 5.0f;

        public int FireMinDays { get; set; } = 60;

        public int FireMaxDays { get; set; } = 90;

        public float InvestigationBaseRisk { get; set; } = 0.05f;

        public float InvestigationPerDistrict { get; set; } = 0.1f;

        public float InvestigationFineMultiplier { get; set; } = 5.0f;

        public float ReputationHonest { get; set; } = 10.0f;

        public float ReputationCorrupt { get; set; } = -5.0f;

        public int InvestigationBaseFinePerDistrict { get; set; } = 10000;


        public ShadowProcurementConfig Clone()
        {
            var clone = new ShadowProcurementConfig
            {
                CostPerBuilding = CostPerBuilding,
                CorruptKickbackPercent = CorruptKickbackPercent,
                CooldownDays = CooldownDays,
                CounterfeitCapacityMult = CounterfeitCapacityMult,
                CounterfeitDegradationMult = CounterfeitDegradationMult,
                CounterfeitFireRiskMult = CounterfeitFireRiskMult,
                FireMinDays = FireMinDays,
                FireMaxDays = FireMaxDays,
                InvestigationBaseRisk = InvestigationBaseRisk,
                InvestigationPerDistrict = InvestigationPerDistrict,
                InvestigationFineMultiplier = InvestigationFineMultiplier,
                ReputationHonest = ReputationHonest,
                ReputationCorrupt = ReputationCorrupt,
                InvestigationBaseFinePerDistrict = InvestigationBaseFinePerDistrict,
            };
            return clone;
        }
    }

    public sealed partial class CorruptionEventsConfig
    {
        public float MinDaysBetweenOffers { get; set; } = 0.5f;

        public float MinDaysBetweenAnyPopup { get; set; } = 0.25f;

        public float ProcurementOfferChancePerDay { get; set; } = 0.05f;


        public CorruptionEventsConfig Clone()
        {
            var clone = new CorruptionEventsConfig
            {
                MinDaysBetweenOffers = MinDaysBetweenOffers,
                MinDaysBetweenAnyPopup = MinDaysBetweenAnyPopup,
                ProcurementOfferChancePerDay = ProcurementOfferChancePerDay,
            };
            return clone;
        }
    }

    public sealed partial class ShadowReputationConfig
    {
        public float InitialTrust { get; set; } = 50.0f;

        public float TrustAcceptOffer { get; set; } = 10.0f;

        public float TrustRejectOffer { get; set; } = -5.0f;

        public float TrustSuccessfulScheme { get; set; } = 5.0f;

        public float TrustGetCaught { get; set; } = -20.0f;

        public float FrozenThreshold { get; set; } = 25.0f;

        public float FreezeDurationDays { get; set; } = 30.0f;

        public float InnerCircleThreshold { get; set; } = 75.0f;

        public float FrozenFrequency { get; set; } = 0.0f;

        public float LowFrequency { get; set; } = 1.0f;

        public float MedFrequency { get; set; } = 1.5f;

        public float HighFrequency { get; set; } = 2.0f;

        public float FrozenPassiveTrustRecovery { get; set; } = 1.0f;


        public ShadowReputationConfig Clone()
        {
            var clone = new ShadowReputationConfig
            {
                InitialTrust = InitialTrust,
                TrustAcceptOffer = TrustAcceptOffer,
                TrustRejectOffer = TrustRejectOffer,
                TrustSuccessfulScheme = TrustSuccessfulScheme,
                TrustGetCaught = TrustGetCaught,
                FrozenThreshold = FrozenThreshold,
                FreezeDurationDays = FreezeDurationDays,
                InnerCircleThreshold = InnerCircleThreshold,
                FrozenFrequency = FrozenFrequency,
                LowFrequency = LowFrequency,
                MedFrequency = MedFrequency,
                HighFrequency = HighFrequency,
                FrozenPassiveTrustRecovery = FrozenPassiveTrustRecovery,
            };
            return clone;
        }
    }

    public sealed partial class ProcurementConfig
    {
        public float KickbackPercent { get; set; } = 0.2f;

        public int ContractDurationDays { get; set; } = 365;

        public float BaseMaintenanceCost { get; set; } = 50000.0f;

        public float MaintenanceVsSupplyRatio { get; set; } = 0.5f;


        public ProcurementConfig Clone()
        {
            var clone = new ProcurementConfig
            {
                KickbackPercent = KickbackPercent,
                ContractDurationDays = ContractDurationDays,
                BaseMaintenanceCost = BaseMaintenanceCost,
                MaintenanceVsSupplyRatio = MaintenanceVsSupplyRatio,
            };
            return clone;
        }
    }

    public sealed partial class HumanitarianAidConfig
    {
        public int CostPerTon { get; set; } = 5000;

        public float HappinessBonus { get; set; } = 0.15f;

        public float TrustBonus { get; set; } = 5.0f;

        public float EffectDurationHours { get; set; } = 24.0f;

        public float TonsPerDistribution { get; set; } = 1.0f;

        public float DistributionCooldownHours { get; set; } = 4.0f;

        public float ProcurementIntervalHours { get; set; } = 6.0f;

        public float TonsPerDayAt100 { get; set; } = 2.0f;


        public HumanitarianAidConfig Clone()
        {
            var clone = new HumanitarianAidConfig
            {
                CostPerTon = CostPerTon,
                HappinessBonus = HappinessBonus,
                TrustBonus = TrustBonus,
                EffectDurationHours = EffectDurationHours,
                TonsPerDistribution = TonsPerDistribution,
                DistributionCooldownHours = DistributionCooldownHours,
                ProcurementIntervalHours = ProcurementIntervalHours,
                TonsPerDayAt100 = TonsPerDayAt100,
            };
            return clone;
        }
    }

    public sealed partial class ScenarioConfig
    {
        public int VillageMaxPop { get; set; } = 1000;

        public int TownMaxPop { get; set; } = 10000;

        public float RefugeeTargetPercent { get; set; } = 0.2f;

        public int RefugeeInfluxDurationHours { get; set; } = 24;

        public int RefugeeAidIntervalHours { get; set; } = 6;

        public float RefugeeWealthFloorMultiplier { get; set; } = 1.2f;

        public float ShockExodusRate { get; set; } = 0.04f;

        public int ShockDaysVillage { get; set; } = 3;

        public int ShockDaysTown { get; set; } = 5;

        public int ShockDaysCity { get; set; } = 7;

        public int VictoryDays { get; set; } = 365;

        public float VictoryMinPopulation { get; set; } = 0.5f;

        public float VictoryMaxCorruption { get; set; } = 50.0f;

        public int WarFatigueDay { get; set; } = 180;

        public float ShockTaxMultiplier { get; set; } = 0.2f;

        public float ShockCommerceMultiplier { get; set; } = 0.3f;

        public float ShockTourismMultiplier { get; set; } = 0.05f;

        public float ExodusWarningThreshold { get; set; } = 0.02f;

        public int RefugeeRateVillage { get; set; } = 500;

        public int RefugeeRateTown { get; set; } = 200;

        public int RefugeeRateCity { get; set; } = 50;

        public float RefugeeGrowthCap { get; set; } = 0.004f;

        public float ExodusMultiplierVillage { get; set; } = 0.05f;

        public float ExodusMultiplierTown { get; set; } = 0.02f;

        public float ExodusMultiplierCity { get; set; } = 0.04f;

        public float ExodusActThreshold { get; set; } = 0.15f;

        public int AdaptationTriggerDays { get; set; } = 7;

        public int AdaptationWavesRequired { get; set; } = 3;

        public float IntegrationCheckIntervalHours { get; set; } = 2.0f;

        public float IntegrationRate { get; set; } = 0.1f;

        public float MigrationCheckIntervalHours { get; set; } = 1.0f;

        public int MigrationMaxPerUpdate { get; set; } = 50;

        public float CollapsePopulationRatio { get; set; } = 5.0f;

        public int RefugeeStartMilestone { get; set; } = 3;

        public int WarStartMilestone { get; set; } = 4;

        public float NagIntervalHours { get; set; } = 2.0f;

        public int RefugeesPerHousehold { get; set; } = 2;

        public float IntroModalDelay { get; set; } = 1.0f;

        public float IntroConsentBeat { get; set; } = 1.5f;

        public float IntroSilenceDuration { get; set; } = 8.0f;

        public float IntroSirenDelay { get; set; } = 12.0f;

        public float IntroAttackDelay { get; set; } = 5.0f;

        public float IntroRevealDelay { get; set; } = 5.0f;

        public int VictoryMinWaves { get; set; } = 10;

        public float RefugeePrewarMultiplier { get; set; } = 0.3f;

        public int RefugeeSupportPerHouseholdPerDay { get; set; } = 500;

        public float RefugeeSupportIntervalHours { get; set; } = 24.0f;

        public long CrisisEmergencyInjection { get; set; } = 200000L;

        public int DefeatPopulationThreshold { get; set; } = 100;

        public float DefeatIntegrityThreshold { get; set; } = 0.1f;

        public float DefeatIntegrityHours { get; set; } = 48.0f;


        public ScenarioConfig Clone()
        {
            var clone = new ScenarioConfig
            {
                VillageMaxPop = VillageMaxPop,
                TownMaxPop = TownMaxPop,
                RefugeeTargetPercent = RefugeeTargetPercent,
                RefugeeInfluxDurationHours = RefugeeInfluxDurationHours,
                RefugeeAidIntervalHours = RefugeeAidIntervalHours,
                RefugeeWealthFloorMultiplier = RefugeeWealthFloorMultiplier,
                ShockExodusRate = ShockExodusRate,
                ShockDaysVillage = ShockDaysVillage,
                ShockDaysTown = ShockDaysTown,
                ShockDaysCity = ShockDaysCity,
                VictoryDays = VictoryDays,
                VictoryMinPopulation = VictoryMinPopulation,
                VictoryMaxCorruption = VictoryMaxCorruption,
                WarFatigueDay = WarFatigueDay,
                ShockTaxMultiplier = ShockTaxMultiplier,
                ShockCommerceMultiplier = ShockCommerceMultiplier,
                ShockTourismMultiplier = ShockTourismMultiplier,
                ExodusWarningThreshold = ExodusWarningThreshold,
                RefugeeRateVillage = RefugeeRateVillage,
                RefugeeRateTown = RefugeeRateTown,
                RefugeeRateCity = RefugeeRateCity,
                RefugeeGrowthCap = RefugeeGrowthCap,
                ExodusMultiplierVillage = ExodusMultiplierVillage,
                ExodusMultiplierTown = ExodusMultiplierTown,
                ExodusMultiplierCity = ExodusMultiplierCity,
                ExodusActThreshold = ExodusActThreshold,
                AdaptationTriggerDays = AdaptationTriggerDays,
                AdaptationWavesRequired = AdaptationWavesRequired,
                IntegrationCheckIntervalHours = IntegrationCheckIntervalHours,
                IntegrationRate = IntegrationRate,
                MigrationCheckIntervalHours = MigrationCheckIntervalHours,
                MigrationMaxPerUpdate = MigrationMaxPerUpdate,
                CollapsePopulationRatio = CollapsePopulationRatio,
                RefugeeStartMilestone = RefugeeStartMilestone,
                WarStartMilestone = WarStartMilestone,
                NagIntervalHours = NagIntervalHours,
                RefugeesPerHousehold = RefugeesPerHousehold,
                IntroModalDelay = IntroModalDelay,
                IntroConsentBeat = IntroConsentBeat,
                IntroSilenceDuration = IntroSilenceDuration,
                IntroSirenDelay = IntroSirenDelay,
                IntroAttackDelay = IntroAttackDelay,
                IntroRevealDelay = IntroRevealDelay,
                VictoryMinWaves = VictoryMinWaves,
                RefugeePrewarMultiplier = RefugeePrewarMultiplier,
                RefugeeSupportPerHouseholdPerDay = RefugeeSupportPerHouseholdPerDay,
                RefugeeSupportIntervalHours = RefugeeSupportIntervalHours,
                CrisisEmergencyInjection = CrisisEmergencyInjection,
                DefeatPopulationThreshold = DefeatPopulationThreshold,
                DefeatIntegrityThreshold = DefeatIntegrityThreshold,
                DefeatIntegrityHours = DefeatIntegrityHours,
            };
            return clone;
        }
    }

    public sealed partial class InfrastructureRepairConfig
    {
        public float MunicipalRepairHours { get; set; } = 24.0f;

        public int MunicipalBaseCostPerPercent { get; set; } = 5000;

        public float MunicipalKickbackPercent { get; set; } = 0.1f;

        public float MunicipalCostMultiplierWithKickback { get; set; } = 2.0f;

        public float ShadowOpsRepairHours { get; set; } = 2.0f;

        public int ShadowOpsBaseCostPerPercent { get; set; } = 2500;

        public float KickbackCorruptionExposure { get; set; } = 5.0f;

        public float CivilianMunicipalRepairHours { get; set; } = 12.0f;

        public float CivilianShadowOpsRepairHours { get; set; } = 1.0f;

        public int CivilianMunicipalCostPerHit { get; set; } = 3000;

        public int CivilianShadowOpsCostPerHit { get; set; } = 1500;


        public InfrastructureRepairConfig Clone()
        {
            var clone = new InfrastructureRepairConfig
            {
                MunicipalRepairHours = MunicipalRepairHours,
                MunicipalBaseCostPerPercent = MunicipalBaseCostPerPercent,
                MunicipalKickbackPercent = MunicipalKickbackPercent,
                MunicipalCostMultiplierWithKickback = MunicipalCostMultiplierWithKickback,
                ShadowOpsRepairHours = ShadowOpsRepairHours,
                ShadowOpsBaseCostPerPercent = ShadowOpsBaseCostPerPercent,
                KickbackCorruptionExposure = KickbackCorruptionExposure,
                CivilianMunicipalRepairHours = CivilianMunicipalRepairHours,
                CivilianShadowOpsRepairHours = CivilianShadowOpsRepairHours,
                CivilianMunicipalCostPerHit = CivilianMunicipalCostPerHit,
                CivilianShadowOpsCostPerHit = CivilianShadowOpsCostPerHit,
            };
            return clone;
        }
    }

    public sealed partial class LoadSheddingConfig
    {
        public int MildOnHours { get; set; } = 4;

        public int MildOffHours { get; set; } = 2;

        public int BalancedOnHours { get; set; } = 4;

        public int BalancedOffHours { get; set; } = 4;

        public int SevereOnHours { get; set; } = 2;

        public int SevereOffHours { get; set; } = 4;

        public int DayshiftStartHour { get; set; } = 8;

        public int DayshiftEndHour { get; set; } = 20;

        public int DayshiftPhaseSpread { get; set; } = 12;

        public int MaxPhaseOffsets { get; set; } = 24;


        public LoadSheddingConfig Clone()
        {
            var clone = new LoadSheddingConfig
            {
                MildOnHours = MildOnHours,
                MildOffHours = MildOffHours,
                BalancedOnHours = BalancedOnHours,
                BalancedOffHours = BalancedOffHours,
                SevereOnHours = SevereOnHours,
                SevereOffHours = SevereOffHours,
                DayshiftStartHour = DayshiftStartHour,
                DayshiftEndHour = DayshiftEndHour,
                DayshiftPhaseSpread = DayshiftPhaseSpread,
                MaxPhaseOffsets = MaxPhaseOffsets,
            };
            return clone;
        }
    }

    public sealed partial class CognitiveConfig
    {
        public float TraumaGainRate { get; set; } = 0.5f;

        public float TraumaDecayRate { get; set; } = 0.02f;

        public float InertiaGainRate { get; set; } = 0.15f;

        public float InertiaDecayRate { get; set; } = 0.1f;

        public float ImpactDistantFactor { get; set; } = 0.1f;

        public float StressToWellbeing { get; set; } = 10.0f;

        public float ShockDurationHours { get; set; } = 2.0f;

        public float ShockTrustPenalty { get; set; } = 0.3f;

        public float ShockCooldownHours { get; set; } = 6.0f;

        public float FatigueRatePerHour { get; set; } = 0.02f;

        public float FatigueDecayOnSwitch { get; set; } = 0.3f;

        public float ModeBonusRealistic { get; set; } = 1.0f;

        public float ModeBonusAlarmist { get; set; } = 0.7f;

        public float ModeBonusSoothing { get; set; } = 0.4f;

        public float InfectionRateBase { get; set; } = 0.02f;

        public float RecoveryRateBase { get; set; } = 0.01f;

        public float StressThreshold { get; set; } = 0.01f;

        public float InfectionThreshold { get; set; } = 0.1f;

        public float EnemyInternetWeight { get; set; } = 0.8f;

        public float EnemyIpsoWeight { get; set; } = 1.2f;

        public float CounterOpsMultiplier { get; set; } = 2.0f;

        public float SkepticismFactor { get; set; } = 0.3f;

        public float BlackoutVulnThresholdHours { get; set; } = 4.0f;

        public float BlackoutVulnMaxHours { get; set; } = 24.0f;

        public float BlackoutVulnMaxBonus { get; set; } = 0.3f;

        public float InfectionStressWeight { get; set; } = 0.5f;

        public float ResistanceStressReduction { get; set; } = 0.5f;

        public float CompromiseThreshold { get; set; } = 0.5f;

        public float CriticalThreshold { get; set; } = 0.3f;

        public float CriticalRecoveryMultiplier { get; set; } = 1.5f;

        public int HeroDeployCost { get; set; } = 50000;

        public float HeroInfectionReduction { get; set; } = 0.5f;

        public float HeroRecoveryBonus { get; set; } = 0.5f;

        public float FirewallRecoveryMultiplier { get; set; } = 0.5f;

        public float EnvyStress { get; set; } = 0.1f;

        public float FirewallInfectionMultiplier { get; set; } = 0.3f;

        public float FirewallCommercePenalty { get; set; } = 0.1f;

        public float BlackoutCommercePenalty { get; set; } = 0.25f;

        public float DefaultTrust { get; set; } = 0.7f;

        public float SoothingTrustDecay { get; set; } = 0.05f;

        public float AlarmistTrustDecay { get; set; } = 0.02f;

        public float RealisticTrustRecovery { get; set; } = 0.03f;

        public float AlarmistSpotterBonus { get; set; } = 0.3f;

        public float AlarmistStressRate { get; set; } = 0.1f;

        public float FatigueMaxReduction { get; set; } = 0.75f;


        public CognitiveConfig Clone()
        {
            var clone = new CognitiveConfig
            {
                TraumaGainRate = TraumaGainRate,
                TraumaDecayRate = TraumaDecayRate,
                InertiaGainRate = InertiaGainRate,
                InertiaDecayRate = InertiaDecayRate,
                ImpactDistantFactor = ImpactDistantFactor,
                StressToWellbeing = StressToWellbeing,
                ShockDurationHours = ShockDurationHours,
                ShockTrustPenalty = ShockTrustPenalty,
                ShockCooldownHours = ShockCooldownHours,
                FatigueRatePerHour = FatigueRatePerHour,
                FatigueDecayOnSwitch = FatigueDecayOnSwitch,
                ModeBonusRealistic = ModeBonusRealistic,
                ModeBonusAlarmist = ModeBonusAlarmist,
                ModeBonusSoothing = ModeBonusSoothing,
                InfectionRateBase = InfectionRateBase,
                RecoveryRateBase = RecoveryRateBase,
                StressThreshold = StressThreshold,
                InfectionThreshold = InfectionThreshold,
                EnemyInternetWeight = EnemyInternetWeight,
                EnemyIpsoWeight = EnemyIpsoWeight,
                CounterOpsMultiplier = CounterOpsMultiplier,
                SkepticismFactor = SkepticismFactor,
                BlackoutVulnThresholdHours = BlackoutVulnThresholdHours,
                BlackoutVulnMaxHours = BlackoutVulnMaxHours,
                BlackoutVulnMaxBonus = BlackoutVulnMaxBonus,
                InfectionStressWeight = InfectionStressWeight,
                ResistanceStressReduction = ResistanceStressReduction,
                CompromiseThreshold = CompromiseThreshold,
                CriticalThreshold = CriticalThreshold,
                CriticalRecoveryMultiplier = CriticalRecoveryMultiplier,
                HeroDeployCost = HeroDeployCost,
                HeroInfectionReduction = HeroInfectionReduction,
                HeroRecoveryBonus = HeroRecoveryBonus,
                FirewallRecoveryMultiplier = FirewallRecoveryMultiplier,
                EnvyStress = EnvyStress,
                FirewallInfectionMultiplier = FirewallInfectionMultiplier,
                FirewallCommercePenalty = FirewallCommercePenalty,
                BlackoutCommercePenalty = BlackoutCommercePenalty,
                DefaultTrust = DefaultTrust,
                SoothingTrustDecay = SoothingTrustDecay,
                AlarmistTrustDecay = AlarmistTrustDecay,
                RealisticTrustRecovery = RealisticTrustRecovery,
                AlarmistSpotterBonus = AlarmistSpotterBonus,
                AlarmistStressRate = AlarmistStressRate,
                FatigueMaxReduction = FatigueMaxReduction,
            };
            return clone;
        }
    }

    public sealed partial class MobilizationConfig
    {
        public int BoforsCrew { get; set; } = 4;

        public int GepardCrew { get; set; } = 5;

        public int PatriotCrew { get; set; } = 14;

        public int WarFatigueDay { get; set; } = 30;

        public float FatiguePenalty { get; set; } = 0.15f;

        public float DeepFatiguePenalty { get; set; } = 0.3f;

        public float ConscriptionBonus { get; set; } = 0.5f;

        public float ConscriptionReputation { get; set; } = -10.0f;

        public float CriticalThreshold { get; set; } = 0.2f;

        public float ManpowerCoeff { get; set; } = 1.0f;

        public float ManpowerExponent { get; set; } = 0.33f;

        public float CorruptionImpact { get; set; } = 0.5f;

        public float HappinessImpact { get; set; } = 0.5f;

        public float CasualtySurvivalRate { get; set; } = 0.5f;

        public int CallToArmsRecovery { get; set; } = 20;

        public float CallToArmsReputation { get; set; } = -5.0f;

        public float CallToArmsCooldownHours { get; set; } = 24.0f;

        public float ConscriptionCooldownHours { get; set; } = 24.0f;


        public MobilizationConfig Clone()
        {
            var clone = new MobilizationConfig
            {
                BoforsCrew = BoforsCrew,
                GepardCrew = GepardCrew,
                PatriotCrew = PatriotCrew,
                WarFatigueDay = WarFatigueDay,
                FatiguePenalty = FatiguePenalty,
                DeepFatiguePenalty = DeepFatiguePenalty,
                ConscriptionBonus = ConscriptionBonus,
                ConscriptionReputation = ConscriptionReputation,
                CriticalThreshold = CriticalThreshold,
                ManpowerCoeff = ManpowerCoeff,
                ManpowerExponent = ManpowerExponent,
                CorruptionImpact = CorruptionImpact,
                HappinessImpact = HappinessImpact,
                CasualtySurvivalRate = CasualtySurvivalRate,
                CallToArmsRecovery = CallToArmsRecovery,
                CallToArmsReputation = CallToArmsReputation,
                CallToArmsCooldownHours = CallToArmsCooldownHours,
                ConscriptionCooldownHours = ConscriptionCooldownHours,
            };
            return clone;
        }
    }

    public sealed partial class AAUnitsConfig
    {
        public int HeritageBaseCount { get; set; } = 2;

        public int MwPerAA { get; set; } = 400;

        public int HeritageMinCount { get; set; } = 2;

        public int HeritageMaxCount { get; set; } = 5;

        public float HeritageRange { get; set; } = 500.0f;

        public float HeritageInterceptShahed { get; set; } = 0.16f;

        public float HeritageInterceptBallistic { get; set; } = 0.0f;

        public int HeritageMaxAmmo { get; set; } = 400;

        public int HeritageBurstRounds { get; set; } = 4;

        public float HeritageCooldown { get; set; } = 10.0f;

        public float BoforsRange { get; set; } = 700.0f;

        public float BoforsInterceptShahed { get; set; } = 0.24f;

        public float BoforsInterceptBallistic { get; set; } = 0.0f;

        public int BoforsMaxAmmo { get; set; } = 1200;

        public int BoforsBurstRounds { get; set; } = 6;

        public int GepardBurstRounds { get; set; } = 8;

        public float GepardRange { get; set; } = 900.0f;

        public float GepardInterceptShahed { get; set; } = 0.46f;

        public float GepardInterceptBallistic { get; set; } = 0.1f;

        public int GepardMaxAmmo { get; set; } = 1600;

        public float GepardCooldown { get; set; } = 2.0f;

        public float BoforsCooldown { get; set; } = 2.5f;

        public float PatriotRange { get; set; } = 4000.0f;

        public float PatriotInterceptShahed { get; set; } = 0.3f;

        public float PatriotInterceptBallistic { get; set; } = 0.4f;

        public int PatriotMaxAmmo { get; set; } = 4;

        public int PatriotBurstRounds { get; set; } = 1;

        public float PatriotCooldown { get; set; } = 8.0f;

        public float PatriotStartAmmoFraction { get; set; } = 1.0f;

        public float AmmoScaleDiv { get; set; } = 50.0f;

        public int AmmoBaseMW { get; set; } = 100;

        public float AmmoScaleMult { get; set; } = 0.3f;

        public float AmmoMaxScaleCap { get; set; } = 2.5f;

        public int HeritageCrewRequired { get; set; } = 2;

        public int BoforsPrice { get; set; } = 10000;

        public int GepardPrice { get; set; } = 25000;

        public int PatriotPrice { get; set; } = 100000;

        public float RefundPercent1 { get; set; } = 0.75f;

        public float RefundPercent2 { get; set; } = 0.4f;

        public float RefundPercent3 { get; set; } = 0.15f;

        public float RefundWindowDays1 { get; set; } = 1.0f;

        public float RefundWindowDays2 { get; set; } = 3.0f;

        public float RefundWindowDays3 { get; set; } = 7.0f;


        public AAUnitsConfig Clone()
        {
            var clone = new AAUnitsConfig
            {
                HeritageBaseCount = HeritageBaseCount,
                MwPerAA = MwPerAA,
                HeritageMinCount = HeritageMinCount,
                HeritageMaxCount = HeritageMaxCount,
                HeritageRange = HeritageRange,
                HeritageInterceptShahed = HeritageInterceptShahed,
                HeritageInterceptBallistic = HeritageInterceptBallistic,
                HeritageMaxAmmo = HeritageMaxAmmo,
                HeritageBurstRounds = HeritageBurstRounds,
                HeritageCooldown = HeritageCooldown,
                BoforsRange = BoforsRange,
                BoforsInterceptShahed = BoforsInterceptShahed,
                BoforsInterceptBallistic = BoforsInterceptBallistic,
                BoforsMaxAmmo = BoforsMaxAmmo,
                BoforsBurstRounds = BoforsBurstRounds,
                GepardBurstRounds = GepardBurstRounds,
                GepardRange = GepardRange,
                GepardInterceptShahed = GepardInterceptShahed,
                GepardInterceptBallistic = GepardInterceptBallistic,
                GepardMaxAmmo = GepardMaxAmmo,
                GepardCooldown = GepardCooldown,
                BoforsCooldown = BoforsCooldown,
                PatriotRange = PatriotRange,
                PatriotInterceptShahed = PatriotInterceptShahed,
                PatriotInterceptBallistic = PatriotInterceptBallistic,
                PatriotMaxAmmo = PatriotMaxAmmo,
                PatriotBurstRounds = PatriotBurstRounds,
                PatriotCooldown = PatriotCooldown,
                PatriotStartAmmoFraction = PatriotStartAmmoFraction,
                AmmoScaleDiv = AmmoScaleDiv,
                AmmoBaseMW = AmmoBaseMW,
                AmmoScaleMult = AmmoScaleMult,
                AmmoMaxScaleCap = AmmoMaxScaleCap,
                HeritageCrewRequired = HeritageCrewRequired,
                BoforsPrice = BoforsPrice,
                GepardPrice = GepardPrice,
                PatriotPrice = PatriotPrice,
                RefundPercent1 = RefundPercent1,
                RefundPercent2 = RefundPercent2,
                RefundPercent3 = RefundPercent3,
                RefundWindowDays1 = RefundWindowDays1,
                RefundWindowDays2 = RefundWindowDays2,
                RefundWindowDays3 = RefundWindowDays3,
            };
            return clone;
        }
    }

    public sealed partial class DebtConfig
    {
        public float MonthlyRate { get; set; } = 0.1f;

        public long MinimumPayment { get; set; } = 1000L;

        public float InterestRate { get; set; } = 0.05f;

        public float WarningRatio { get; set; } = 3.0f;

        public float RestructureRatio { get; set; } = 5.0f;

        public float RestructuredRate { get; set; } = 0.02f;

        public float ReliefShockThreshold { get; set; } = 70.0f;

        public float ReliefPercent { get; set; } = 0.3f;


        public DebtConfig Clone()
        {
            var clone = new DebtConfig
            {
                MonthlyRate = MonthlyRate,
                MinimumPayment = MinimumPayment,
                InterestRate = InterestRate,
                WarningRatio = WarningRatio,
                RestructureRatio = RestructureRatio,
                RestructuredRate = RestructuredRate,
                ReliefShockThreshold = ReliefShockThreshold,
                ReliefPercent = ReliefPercent,
            };
            return clone;
        }
    }

    public sealed partial class CityStabilityConfig
    {
        public float PhysicalWeight { get; set; } = 0.4f;

        public float DigitalWeight { get; set; } = 0.3f;

        public float SocialWeight { get; set; } = 0.3f;

        public int MaxDestroyedBuildings { get; set; } = 50;

        public int MaxFires { get; set; } = 20;

        public int TotalDistricts { get; set; } = 10;

        public float MaxDiscount { get; set; } = 0.2f;

        public float BlackoutSubWeight { get; set; } = 0.5f;

        public float DestroyedSubWeight { get; set; } = 0.375f;

        public float FiresSubWeight { get; set; } = 0.125f;

        public float DeficitSubWeight { get; set; } = 0.5f;

        public float StressSubWeight { get; set; } = 0.5f;


        public CityStabilityConfig Clone()
        {
            var clone = new CityStabilityConfig
            {
                PhysicalWeight = PhysicalWeight,
                DigitalWeight = DigitalWeight,
                SocialWeight = SocialWeight,
                MaxDestroyedBuildings = MaxDestroyedBuildings,
                MaxFires = MaxFires,
                TotalDistricts = TotalDistricts,
                MaxDiscount = MaxDiscount,
                BlackoutSubWeight = BlackoutSubWeight,
                DestroyedSubWeight = DestroyedSubWeight,
                FiresSubWeight = FiresSubWeight,
                DeficitSubWeight = DeficitSubWeight,
                StressSubWeight = StressSubWeight,
            };
            return clone;
        }
    }

    public sealed partial class GridWarfareConfig
    {
        public long DroneCost { get; set; } = 500000L;

        public float DronePrepareDuration { get; set; } = 45.0f;

        public float DroneBaseDamage { get; set; } = 12.0f;

        public long BlackoutCost { get; set; } = 300000L;

        public float BlackoutPrepareDuration { get; set; } = 30.0f;

        public float BlackoutBaseDamage { get; set; } = 8.0f;

        public long DisinfoCost { get; set; } = 100000L;

        public float DisinfoPrepareDuration { get; set; } = 20.0f;

        public float DisinfoBaseDamage { get; set; } = 5.0f;

        public float PressureFloor { get; set; } = 20.0f;

        public float PressureCap { get; set; } = 100.0f;

        public float PressureRegenRatePerHour { get; set; } = 5.0f;

        public float MaxStabilityDiscount { get; set; } = 0.2f;

        public float EnemyInterceptChance { get; set; } = 0.25f;

        public int ArsenalStockCap { get; set; } = 100000;

        public int ArsenalDroneBaseCost { get; set; } = 8000;

        public int ArsenalBallisticBaseCost { get; set; } = 25000;

        public int ArsenalMaxPurchaseCount { get; set; } = 50;

        public int DonorArsenalDroneGrant { get; set; } = 3;

        public int DonorArsenalBallisticGrant { get; set; } = 1;

        public float ObjectiveAxisThreshold { get; set; } = 30.0f;

        public float RespiteWindowHours { get; set; } = 12.0f;

        public float RespiteWaveWeakenMultiplier { get; set; } = 0.5f;

        public int ObjectiveLootShadowCash { get; set; } = 15000;


        public GridWarfareConfig Clone()
        {
            var clone = new GridWarfareConfig
            {
                DroneCost = DroneCost,
                DronePrepareDuration = DronePrepareDuration,
                DroneBaseDamage = DroneBaseDamage,
                BlackoutCost = BlackoutCost,
                BlackoutPrepareDuration = BlackoutPrepareDuration,
                BlackoutBaseDamage = BlackoutBaseDamage,
                DisinfoCost = DisinfoCost,
                DisinfoPrepareDuration = DisinfoPrepareDuration,
                DisinfoBaseDamage = DisinfoBaseDamage,
                PressureFloor = PressureFloor,
                PressureCap = PressureCap,
                PressureRegenRatePerHour = PressureRegenRatePerHour,
                MaxStabilityDiscount = MaxStabilityDiscount,
                EnemyInterceptChance = EnemyInterceptChance,
                ArsenalStockCap = ArsenalStockCap,
                ArsenalDroneBaseCost = ArsenalDroneBaseCost,
                ArsenalBallisticBaseCost = ArsenalBallisticBaseCost,
                ArsenalMaxPurchaseCount = ArsenalMaxPurchaseCount,
                DonorArsenalDroneGrant = DonorArsenalDroneGrant,
                DonorArsenalBallisticGrant = DonorArsenalBallisticGrant,
                ObjectiveAxisThreshold = ObjectiveAxisThreshold,
                RespiteWindowHours = RespiteWindowHours,
                RespiteWaveWeakenMultiplier = RespiteWaveWeakenMultiplier,
                ObjectiveLootShadowCash = ObjectiveLootShadowCash,
            };
            return clone;
        }
    }

}

namespace CivicSurvival.Core.Infrastructure
{
    public sealed partial class FeatureGatesConfig
    {
        public int CurrentWave { get; set; } = 1;

        public Dictionary<string, int> Waves { get; set; } = new()
        {
            ["Arena"] = 99,
            ["ArenaUI"] = 99,
            ["Cognitive"] = 2,
            ["Corruption"] = 2,
            ["Countermeasures"] = 2,
            ["Diplomacy"] = 2,
            ["GridWarfare"] = 99,
            ["Narrative"] = 1,
            ["NeighborEnvy"] = 2,
            ["Network"] = 1,
            ["Refugees"] = 1,
            ["ShadowEconomy"] = 2,
        };


        public FeatureGatesConfig Clone()
        {
            var clone = new FeatureGatesConfig
            {
                CurrentWave = CurrentWave,
                Waves = new Dictionary<string, int>(Waves),
            };
            return clone;
        }
    }
}
