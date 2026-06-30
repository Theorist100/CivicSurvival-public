/**
 * Scenario Director UI Bindings
 *
 * Connects UI to ScenarioDirectorSystem C# backend.
 * Provides access to scenario state, statistics, and milestone modals.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../bindingNames.generated";
import {
    Act,
    DefeatCause,
    ScenarioType,
    type ActValue,
    type DefeatCauseValue,
    type ScenarioTypeValue,
} from "../../types/sharedEnums.generated";

// ===== Scenario Type Constants =====

export { Act, ScenarioType };

// ===== Defeat Cause Constants =====

export const DefeatCauseType = DefeatCause;
export type { ActValue, DefeatCauseValue, ScenarioTypeValue };

// ===== Core State Bindings =====

/** Current scenario type (Village/Town/City) */
export const scenarioType$ = bindCivicValue(B.ScenarioType, ScenarioType.None);

/** Current narrative act */
export const currentAct$ = bindCivicValue(B.CurrentAct, Act.PreWar);

// Note: warDay$ is in mobilizationBindings.ts (single source of truth)

/** Total refugees received (Village scenario) */
export const totalRefugees$ = bindCivicValue(B.TotalRefugees, 0);

// totalExodus$ is in attentionBindings.ts (single source of truth)

/** Current population as percent of peak (0-1) */
export const populationPercent$ = bindCivicValue(B.PopulationPercent, 1);

// ===== Statistics Bindings =====

/** Total attack waves defended */
export const wavesDefended$ = bindCivicValue(B.WavesDefended, 0);

/** Total missiles intercepted */
export const missilesIntercepted$ = bindCivicValue(B.MissilesIntercepted, 0);

/** Total blackout recoveries */
export const blackoutRecoveries$ = bindCivicValue(B.BlackoutRecoveries, 0);

/** Total buildings damaged by threats */
export const buildingsDamaged$ = bindCivicValue(B.BuildingsDamaged, 0);

/** Last One More Year request result */
export const oneMoreYearRequest$ = bindCivicValue(B.OneMoreYearRequest, "{\"RequestId\":0,\"Status\":\"idle\",\"ReasonId\":\"\",\"CanonicalEcho\":\"\",\"DiscriminatorKind\":\"none\",\"DiscriminatorValue\":\"\"}");

/** Last Endless Mode request result */
export const endlessModeRequest$ = bindCivicValue(B.EndlessModeRequest, "{\"RequestId\":0,\"Status\":\"idle\",\"ReasonId\":\"\",\"CanonicalEcho\":\"\",\"DiscriminatorKind\":\"none\",\"DiscriminatorValue\":\"\"}");

/** Defeat cause (0=None, 1=PopulationCollapse, 2=LostControl, 3=Arrested) */
export const defeatCause$ = bindCivicValue(B.DefeatCause, DefeatCauseType.None);

/** Days survived when defeat occurred */
export const daysSurvived$ = bindCivicValue(B.DaysSurvived, 0);

// ===== Triggers =====

/** Dismiss War Fatigue modal */
export function dismissWarFatigue(): void {
    triggerCivic(B.DismissWarFatigue);
}

/** Dismiss defeat modal */
export function dismissDefeat(): void {
    triggerCivic(B.DismissDefeat);
}

/** Dismiss grid collapse modal */
export function dismissGridCollapse(): void {
    triggerCivic(B.DismissGridCollapse);
}

/** Dismiss grid critical (pre-collapse warning) modal */
export function dismissGridCritical(): void {
    triggerCivic(B.DismissGridCritical);
}

/** Dismiss wave debriefing modal */
export function dismissDebriefing(): void {
    triggerCivic(B.DismissDebriefing);
}

/** Choose One More Year after victory (+365 days) */
export function oneMoreYear(): void {
    triggerCivic(B.OneMoreYear);
}

/** Choose Endless Mode after victory (no more victory/defeat checks) */
export function endlessMode(): void {
    triggerCivic(B.EndlessMode);
}


