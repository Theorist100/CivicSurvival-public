/**
 * Corruption domain actions — export %, schemes, shadow import, investigation.
 * Extracted from viewModelActions.ts.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import {
    type InvestigationChoice,
    type PercentValue,
    type PoliceChoice,
} from "../types/semantic";

const IMPORT_PRESET_PAYLOAD_OFFSET = 1;

// ============ Export ============

export const setExportPercent = (percent: PercentValue): void =>
    triggerCivic(B.SetExportPercent, percent);

// ============ Schemes ============

export const setEmergencyFundWithdraw = (percent: PercentValue): void =>
    triggerCivic(B.SetEmergencyFundWithdraw, percent);

export const setFuelSiphonPercent = (percent: PercentValue): void =>
    triggerCivic(B.SetFuelSiphonPercent, percent);

export const setShadowImportPreset = (percent: PercentValue): void =>
    triggerCivic(B.SetShadowImportMW, -(Math.round(percent) + IMPORT_PRESET_PAYLOAD_OFFSET));

// ============ Investigation ============

export const makeInvestigationChoice = (choice: InvestigationChoice): void =>
    triggerCivic(B.MakeInvestigationChoice, choice);

export const makePoliceChoice = (choice: PoliceChoice): void =>
    triggerCivic(B.MakePoliceChoice, choice);

// Contract acceptance (acceptOfficialContract, acceptShadyContract, declineProcurement)
// requires paired entity index/version — use procurementBindings.ts as single source of truth.
