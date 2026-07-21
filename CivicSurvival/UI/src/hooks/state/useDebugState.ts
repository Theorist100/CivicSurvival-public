/**
 * Hook for debug panel state.
 * Part of ISP refactor — focused on balance debug metrics.
 *
 * Uses useSafe* wrappers to handle Coherent UI race condition
 * where useValue() returns {} before C# bindings are ready.
 */

import { useMemo } from "react";
import { useNumberBinding, useSafeNumber, useSafeString, useSafeBoolean } from "../useSafeBinding";
import { safeJsonParse } from "../../utils/jsonParse";
import { isRecord } from "../../utils/typeGuards";
import { isCrisisSweepDto, type CrisisSweepDto } from "../../types/domainDtos.generated";
import { DEFAULT_CRISIS_SWEEP_DTO } from "../../types/domainDtos";
import {
    // Core
    debugSeverityScore$,
    debugBlackoutPercent$,
    debugHappinessPenalty$,
    debugCommercePenalty$,
    debugAffectedDistricts$,
    debugTotalDistricts$,
    // Power
    debugPowerBalance$,
    debugProduction$,
    debugConsumption$,
    debugBlackoutedMW$,
    // City
    debugResidentialMW$,
    debugCommercialMW$,
    debugIndustrialMW$,
    debugOfficeMW$,
    debugCityType$,
    // Blackout
    debugBuildingsInBlackout$,
    debugBlackoutDuration$,
    // History
    debugPowerHistory$,
    debugSeverityHistory$,
    debugCorruptionHistory$,
    // Shadow Economy
    debugCurrentAct$,
    debugWarDay$,
    debugShadowBalance$,
    debugShadowDailyIncome$,
    debugGridWarfareUnlocked$,
    // Scenario testing
    debugWaveNumber$,
    debugWaveInProgress$,
    debugWavePhase$,
    debugWavePhaseName$,
    debugEnemyStance$,
    debugEnemyStanceName$,
    debugEnemyPressure$,
    debugStancePhase$,
    debugStancePhaseName$,
    debugGridCollapsed$,
    debugGridStressHours$,
    debugGridRecoveryHours$,
    debugGridZone$,
    debugGridZoneName$,
    debugExodusRatePercentPerDay$,
    debugTotalFled$,
    debugExodusActive$,
    debugShockLevel$,
    debugShockTier$,
    debugShockTierName$,
    debugInfectionRate$,
    debugCityIntegrity$,
    debugMediaTrust$,
    debugTelemarathonActive$,
    debugTrustLevel$,
    debugCorruptionHeat$,
    debugCorruptionScore$,
    debugMoraleFactor$,
    debugAaAmmo$,
    // Crisis sweep
    debugCrisisSweep$,
} from "../bindings/debugBindings";

// ============ Types ============

export interface HistoryPoint {
    h: number; // gameHour
    v: number; // value
}

export interface PowerHistoryData {
    production: HistoryPoint[];
    consumption: HistoryPoint[];
}

export interface DebugState {
    status: "loading" | "ready";

    // Core metrics
    severityScore: number;
    blackoutPercent: number;
    happinessPenalty: number;
    commercePenalty: number;
    affectedDistricts: number;
    totalDistricts: number;

    // Power data
    powerBalance: number;
    production: number;
    consumption: number;
    blackoutedMW: number;

    // City composition
    residentialMW: number;
    commercialMW: number;
    industrialMW: number;
    officeMW: number;
    cityType: string;

    // Blackout tracking
    buildingsInBlackout: number;
    blackoutDuration: number;

    // History charts
    powerHistory: PowerHistoryData;
    severityHistory: HistoryPoint[];
    corruptionHistory: HistoryPoint[];

    // Shadow Economy
    currentAct: string;
    warDay: number;
    shadowBalance: number;
    shadowDailyIncome: number;
    gridWarfareUnlocked: boolean;

    // Scenario testing
    scWaveNumber: number;
    scWaveInProgress: number;
    scWavePhase: number;
    scWavePhaseName: string;
    scEnemyStance: number;
    scEnemyStanceName: string;
    scEnemyPressure: number;
    scStancePhase: number;
    scStancePhaseName: string;
    scGridCollapsed: number;
    scGridStressHours: number;
    scGridRecoveryHours: number;
    scGridZone: number;
    scGridZoneName: string;
    scExodusRatePercentPerDay: number;
    scTotalFled: number;
    scExodusActive: number;
    scShockLevel: number;
    scShockTier: number;
    scShockTierName: string;
    scInfectionRate: number;
    scCityIntegrity: number;
    scMediaTrust: number;
    scTelemarathonActive: number;
    scTrustLevel: number;
    scCorruptionHeat: number;
    scCorruptionScore: number;
    scMoraleFactor: number;
    scAaAmmo: number;

    // Crisis sweep verdict
    crisisSweep: CrisisSweepDto;
}

// ============ Helpers ============

const isHistoryPoint = (value: unknown): value is HistoryPoint =>
    isRecord(value)
    && typeof value.h === "number"
    && typeof value.v === "number";

const isPowerHistoryData = (value: unknown): value is PowerHistoryData => {
    if (!isRecord(value)) return false;
    return Array.isArray(value.production)
        && value.production.every(isHistoryPoint)
        && Array.isArray(value.consumption)
        && value.consumption.every(isHistoryPoint);
};

function parseHistoryArray(json: string): HistoryPoint[] {
    const parsed = safeJsonParse(json, Array.isArray);
    return parsed ? parsed.filter(isHistoryPoint) : [];
}

function parsePowerHistory(json: string): PowerHistoryData {
    return safeJsonParse(json, isPowerHistoryData) ?? { production: [], consumption: [] };
}

function parseCrisisSweep(json: string): CrisisSweepDto {
    return safeJsonParse(json, isCrisisSweepDto) ?? DEFAULT_CRISIS_SWEEP_DTO;
}

// ============ Hook ============

export function useDebugState(): DebugState {
    // Core metrics
    const severityScore = useSafeNumber(debugSeverityScore$, 0, "severityScore");
    const blackoutPercent = useSafeNumber(debugBlackoutPercent$, 0, "blackoutPercent");
    const happinessPenalty = useSafeNumber(debugHappinessPenalty$, 0, "happinessPenalty");
    const commercePenalty = useSafeNumber(debugCommercePenalty$, 0, "commercePenalty");
    const affectedDistricts = useSafeNumber(debugAffectedDistricts$, 0, "affectedDistricts");
    const totalDistricts = useSafeNumber(debugTotalDistricts$, 0, "totalDistricts");

    // Power data
    const powerBalance = useSafeNumber(debugPowerBalance$, 0, "powerBalance");
    const production = useSafeNumber(debugProduction$, 0, "production");
    const consumption = useSafeNumber(debugConsumption$, 0, "consumption");
    const blackoutedMW = useSafeNumber(debugBlackoutedMW$, 0, "blackoutedMW");

    // City composition
    const residentialMW = useSafeNumber(debugResidentialMW$, 0, "residentialMW");
    const commercialMW = useSafeNumber(debugCommercialMW$, 0, "commercialMW");
    const industrialMW = useSafeNumber(debugIndustrialMW$, 0, "industrialMW");
    const officeMW = useSafeNumber(debugOfficeMW$, 0, "officeMW");
    const cityType = useSafeString(debugCityType$, "Mixed", "cityType");

    // Blackout tracking
    const buildingsInBlackout = useSafeNumber(debugBuildingsInBlackout$, 0, "buildingsInBlackout");
    const blackoutDuration = useSafeNumber(debugBlackoutDuration$, 0, "blackoutDuration");

    // History (JSON strings)
    const powerHistoryJson = useSafeString(debugPowerHistory$, "[]", "powerHistory");
    const severityHistoryJson = useSafeString(debugSeverityHistory$, "[]", "severityHistory");
    const corruptionHistoryJson = useSafeString(debugCorruptionHistory$, "[]", "corruptionHistory");

    // Parse history with memoization
    const powerHistory = useMemo(() => parsePowerHistory(powerHistoryJson), [powerHistoryJson]);
    const severityHistory = useMemo(() => parseHistoryArray(severityHistoryJson), [severityHistoryJson]);
    const corruptionHistory = useMemo(() => parseHistoryArray(corruptionHistoryJson), [corruptionHistoryJson]);

    // Shadow Economy
    const currentAct = useSafeString(debugCurrentAct$, "PreWar", "currentAct");
    const warDay = useSafeNumber(debugWarDay$, 0, "warDay");
    const shadowBalance = useSafeNumber(debugShadowBalance$, 0, "shadowBalance");
    const shadowDailyIncome = useSafeNumber(debugShadowDailyIncome$, 0, "shadowDailyIncome");
    const gridWarfareUnlocked = useSafeBoolean(debugGridWarfareUnlocked$, false, "gridWarfareUnlocked");

    // Scenario testing
    const scWaveNumber = useSafeNumber(debugWaveNumber$, 0, "scWaveNumber");
    const scWaveInProgress = useSafeNumber(debugWaveInProgress$, 0, "scWaveInProgress");
    const scWavePhase = useSafeNumber(debugWavePhase$, 0, "scWavePhase");
    const scWavePhaseName = useSafeString(debugWavePhaseName$, "Calm", "scWavePhaseName");
    const scEnemyStance = useSafeNumber(debugEnemyStance$, 0, "scEnemyStance");
    const scEnemyStanceName = useSafeString(debugEnemyStanceName$, "IronDome", "scEnemyStanceName");
    const scEnemyPressureState = useNumberBinding(debugEnemyPressure$, "scEnemyPressure");
    const scEnemyPressure = scEnemyPressureState.status === "ready" ? scEnemyPressureState.data : 0;
    const scStancePhase = useSafeNumber(debugStancePhase$, 0, "scStancePhase");
    const scStancePhaseName = useSafeString(debugStancePhaseName$, "Active", "scStancePhaseName");
    const scGridCollapsed = useSafeNumber(debugGridCollapsed$, 0, "scGridCollapsed");
    const scGridStressHoursState = useNumberBinding(debugGridStressHours$, "scGridStressHours");
    const scGridStressHours = scGridStressHoursState.status === "ready" ? scGridStressHoursState.data : 0;
    const scGridRecoveryHours = useSafeNumber(debugGridRecoveryHours$, 0, "scGridRecoveryHours");
    const scGridZone = useSafeNumber(debugGridZone$, 0, "scGridZone");
    const scGridZoneName = useSafeString(debugGridZoneName$, "Normal", "scGridZoneName");
    const scExodusRatePercentPerDay = useSafeNumber(debugExodusRatePercentPerDay$, 0, "scExodusRatePercentPerDay");
    const scTotalFled = useSafeNumber(debugTotalFled$, 0, "scTotalFled");
    const scExodusActive = useSafeNumber(debugExodusActive$, 0, "scExodusActive");
    const scShockLevelState = useNumberBinding(debugShockLevel$, "scShockLevel");
    const scShockLevel = scShockLevelState.status === "ready" ? scShockLevelState.data : 0;
    const scShockTier = useSafeNumber(debugShockTier$, 0, "scShockTier");
    const scShockTierName = useSafeString(debugShockTierName$, "DeepConcern", "scShockTierName");
    const scInfectionRate = useSafeNumber(debugInfectionRate$, 0, "scInfectionRate");
    const scCityIntegrityState = useNumberBinding(debugCityIntegrity$, "scCityIntegrity");
    const scCityIntegrity = scCityIntegrityState.status === "ready" ? scCityIntegrityState.data : 1;
    const scMediaTrust = useSafeNumber(debugMediaTrust$, 0, "scMediaTrust");
    const scTelemarathonActive = useSafeNumber(debugTelemarathonActive$, 0, "scTelemarathonActive");
    const scTrustLevelState = useNumberBinding(debugTrustLevel$, "scTrustLevel");
    const scTrustLevel = scTrustLevelState.status === "ready" ? scTrustLevelState.data : 0;
    const scCorruptionHeatState = useNumberBinding(debugCorruptionHeat$, "scCorruptionHeat");
    const scCorruptionHeat = scCorruptionHeatState.status === "ready" ? scCorruptionHeatState.data : 0;
    const scCorruptionScoreState = useNumberBinding(debugCorruptionScore$, "scCorruptionScore");
    const scCorruptionScore = scCorruptionScoreState.status === "ready" ? scCorruptionScoreState.data : 0;
    const scMoraleFactorState = useNumberBinding(debugMoraleFactor$, "scMoraleFactor");
    const scMoraleFactor = scMoraleFactorState.status === "ready" ? scMoraleFactorState.data : 1;
    const scAaAmmo = useSafeNumber(debugAaAmmo$, 0, "scAaAmmo");

    // Crisis sweep verdict (JSON string)
    const crisisSweepJson = useSafeString(debugCrisisSweep$, "{}", "crisisSweep");
    const crisisSweep = useMemo(() => parseCrisisSweep(crisisSweepJson), [crisisSweepJson]);

    const status = [
        scEnemyPressureState,
        scGridStressHoursState,
        scShockLevelState,
        scCityIntegrityState,
        scTrustLevelState,
        scCorruptionHeatState,
        scCorruptionScoreState,
        scMoraleFactorState,
    ].every((state) => state.status === "ready") ? "ready" : "loading";

    return useMemo(() => ({
        status,

        // Core metrics
        severityScore,
        blackoutPercent,
        happinessPenalty,
        commercePenalty,
        affectedDistricts,
        totalDistricts,

        // Power data
        powerBalance,
        production,
        consumption,
        blackoutedMW,

        // City composition
        residentialMW,
        commercialMW,
        industrialMW,
        officeMW,
        cityType,

        // Blackout tracking
        buildingsInBlackout,
        blackoutDuration,

        // History charts
        powerHistory,
        severityHistory,
        corruptionHistory,

        // Shadow Economy
        currentAct,
        warDay,
        shadowBalance,
        shadowDailyIncome,
        gridWarfareUnlocked,

        // Scenario testing
        scWaveNumber,
        scWaveInProgress,
        scWavePhase,
        scWavePhaseName,
        scEnemyStance,
        scEnemyStanceName,
        scEnemyPressure,
        scStancePhase,
        scStancePhaseName,
        scGridCollapsed,
        scGridStressHours,
        scGridRecoveryHours,
        scGridZone,
        scGridZoneName,
        scExodusRatePercentPerDay,
        scTotalFled,
        scExodusActive,
        scShockLevel,
        scShockTier,
        scShockTierName,
        scInfectionRate,
        scCityIntegrity,
        scMediaTrust,
        scTelemarathonActive,
        scTrustLevel,
        scCorruptionHeat,
        scCorruptionScore,
        scMoraleFactor,
        scAaAmmo,

        // Crisis sweep verdict
        crisisSweep,
    }), [
        status, severityScore, blackoutPercent, happinessPenalty, commercePenalty, affectedDistricts,
        totalDistricts, powerBalance, production, consumption, blackoutedMW, residentialMW,
        commercialMW, industrialMW, officeMW, cityType, buildingsInBlackout, blackoutDuration,
        powerHistory, severityHistory, corruptionHistory, currentAct, warDay, shadowBalance,
        shadowDailyIncome, gridWarfareUnlocked,
        scWaveNumber, scWaveInProgress, scWavePhase, scWavePhaseName, scEnemyStance,
        scEnemyStanceName, scEnemyPressure, scStancePhase, scStancePhaseName,
        scGridCollapsed, scGridStressHours, scGridRecoveryHours, scGridZone, scGridZoneName, scExodusRatePercentPerDay,
        scTotalFled, scExodusActive, scShockLevel, scShockTier, scShockTierName, scInfectionRate,
        scCityIntegrity, scMediaTrust, scTelemarathonActive, scTrustLevel, scCorruptionHeat,
        scCorruptionScore, scMoraleFactor, scAaAmmo, crisisSweep,
    ]);
}
