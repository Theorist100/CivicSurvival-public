/**
 * Debug bindings - balance metrics (development only)
 * These bindings only exist when C# is compiled with DEBUG flag
 */

import { bindCivicValue } from "../typedBinding.generated";
import { B } from "../bindingNames.generated";

// Core metrics
export const debugSeverityScore$ = bindCivicValue(B.Debug_SeverityScore, 0);
export const debugBlackoutPercent$ = bindCivicValue(B.Debug_BlackoutPercent, 0);
export const debugHappinessPenalty$ = bindCivicValue(B.Debug_HappinessPenalty, 0);
export const debugCommercePenalty$ = bindCivicValue(B.Debug_CommercePenalty, 0);
export const debugAffectedDistricts$ = bindCivicValue(B.Debug_AffectedDistricts, 0);
export const debugTotalDistricts$ = bindCivicValue(B.Debug_TotalDistricts, 0);

// Power data
export const debugPowerBalance$ = bindCivicValue(B.Debug_PowerBalance, 0);
export const debugProduction$ = bindCivicValue(B.Debug_Production, 0);
export const debugConsumption$ = bindCivicValue(B.Debug_Consumption, 0);
export const debugBlackoutedMW$ = bindCivicValue(B.Debug_BlackoutedMW, 0);

// City composition
export const debugResidentialMW$ = bindCivicValue(B.Debug_ResidentialMW, 0);
export const debugCommercialMW$ = bindCivicValue(B.Debug_CommercialMW, 0);
export const debugIndustrialMW$ = bindCivicValue(B.Debug_IndustrialMW, 0);
export const debugOfficeMW$ = bindCivicValue(B.Debug_OfficeMW, 0);
export const debugCityType$ = bindCivicValue(B.Debug_CityType, "Mixed");

// Blackout tracking
export const debugBuildingsInBlackout$ = bindCivicValue(B.Debug_BuildingsInBlackout, 0);
export const debugBlackoutDuration$ = bindCivicValue(B.Debug_BlackoutDuration, 0);

// History charts (JSON serialized)
export const debugPowerHistory$ = bindCivicValue(B.Debug_PowerHistory, "[]");
export const debugSeverityHistory$ = bindCivicValue(B.Debug_SeverityHistory, "[]");
export const debugCorruptionHistory$ = bindCivicValue(B.Debug_CorruptionHistory, "[]");

// Shadow Economy (Phase Unlock balance)
export const debugCurrentAct$ = bindCivicValue(B.Debug_CurrentAct, "PreWar");
export const debugWarDay$ = bindCivicValue(B.Debug_WarDay, 0);
export const debugShadowBalance$ = bindCivicValue(B.Debug_ShadowBalance, 0);
export const debugShadowDailyIncome$ = bindCivicValue(B.Debug_ShadowDailyIncome, 0);
export const debugGridWarfareUnlocked$ = bindCivicValue(B.Debug_GridWarfareUnlocked, false);

// Scenario testing
export const debugWaveNumber$ = bindCivicValue(B.Debug_WaveNumber, 0);
export const debugWaveInProgress$ = bindCivicValue(B.Debug_WaveInProgress, 0);
export const debugWavePhase$ = bindCivicValue(B.Debug_WavePhase, 0);
export const debugWavePhaseName$ = bindCivicValue(B.Debug_WavePhaseName, "Calm");
export const debugEnemyStance$ = bindCivicValue(B.Debug_EnemyStance, 0);
export const debugEnemyStanceName$ = bindCivicValue(B.Debug_EnemyStanceName, "IronDome");
export const debugEnemyPressure$ = bindCivicValue(B.Debug_EnemyPressure, 0);
export const debugStancePhase$ = bindCivicValue(B.Debug_StancePhase, 0);
export const debugStancePhaseName$ = bindCivicValue(B.Debug_StancePhaseName, "Active");
export const debugGridCollapsed$ = bindCivicValue(B.Debug_GridCollapsed, 0);
export const debugGridStressHours$ = bindCivicValue(B.Debug_GridStressHours, 0);
export const debugGridRecoveryHours$ = bindCivicValue(B.Debug_GridRecoveryHours, 0);
export const debugGridZone$ = bindCivicValue(B.Debug_GridZone, 0);
export const debugGridZoneName$ = bindCivicValue(B.Debug_GridZoneName, "Normal");
export const debugExodusRatePercentPerDay$ = bindCivicValue(B.Debug_ExodusRatePercentPerDay, 0);
export const debugTotalFled$ = bindCivicValue(B.Debug_TotalFled, 0);
export const debugExodusActive$ = bindCivicValue(B.Debug_ExodusActive, 0);
export const debugShockLevel$ = bindCivicValue(B.Debug_ShockLevel, 0);
export const debugShockTier$ = bindCivicValue(B.Debug_ShockTier, 0);
export const debugShockTierName$ = bindCivicValue(B.Debug_ShockTierName, "DeepConcern");
export const debugInfectionRate$ = bindCivicValue(B.Debug_InfectionRate, 0);
export const debugCityIntegrity$ = bindCivicValue(B.Debug_CityIntegrity, 1);
export const debugMediaTrust$ = bindCivicValue(B.Debug_MediaTrust, 0);
export const debugTelemarathonActive$ = bindCivicValue(B.Debug_TelemarathonActive, 0);
export const debugTrustLevel$ = bindCivicValue(B.Debug_TrustLevel, 0);
export const debugCorruptionHeat$ = bindCivicValue(B.Debug_CorruptionHeat, 0);
export const debugCorruptionScore$ = bindCivicValue(B.Debug_CorruptionScore, 0);
export const debugMoraleFactor$ = bindCivicValue(B.Debug_MoraleFactor, 1);
export const debugAaAmmo$ = bindCivicValue(B.Debug_AaAmmo, 0);

// Crisis sweep verdict (CrisisSweepDto JSON, "{}" until first sweep runs)
export const debugCrisisSweep$ = bindCivicValue(B.CrisisSweepState, "{}");
