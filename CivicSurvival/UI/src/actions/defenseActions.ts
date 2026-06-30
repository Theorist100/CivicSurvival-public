/**
 * Defense domain actions — resupply, SBU, evacuation, counter-OSINT, policy.
 * Extracted from viewModelActions.ts.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import { type HeroStatusType, type InternetModeType, type NarrativeModeType } from "../hooks/domain/cognitiveLabels";
import { type DefensePolicyId, type AATypeId } from "../types/semantic";
import { type EntityRef } from "../types/entityRef";

export type AAPlacementMode = "Paid" | "Heritage" | "DonorCredit";

export type AAPlacementPayload = {
    prefab: "AA_40mm_Bofors" | "Gepard" | "MIM104_SAM";
    mode: AAPlacementMode;
};

// ============ Air Defense ============

export const emergencyResupply = (type: AATypeId): void =>
    triggerCivic(B.EmergencyResupply, type);

/** Sentinel id meaning "restock every gun type at once" — mirror of
 *  AAResupplyGroups.GunsResupplyTypeId (C#). Patriot keeps its own per-type id. */
const GUNS_RESUPPLY_ID = -1 as AATypeId;

/** Restock all gun types (Bofors/Gepard/Heritage) in one emergency batch. Delegates to
 *  emergencyResupply (the sole EmergencyResupply trigger wrapper) with the guns sentinel. */
export const emergencyResupplyGuns = (): void =>
    emergencyResupply(GUNS_RESUPPLY_ID);

export const sbuVisit = (): void =>
    triggerCivic(B.SbuVisit);

export const evacuation = (): void =>
    triggerCivic(B.Evacuation);

export const toggleCounterOSINT = (): void =>
    triggerCivic(B.ToggleCounterOSINT);

export const setDefensePolicy = (policyId: DefensePolicyId): void =>
    triggerCivic(B.SetDefensePolicy, policyId);

/** Idempotent SET: sends the target state, not a flip — robust to double-click/dupes. */
export const togglePatriotDroneIntercept = (enabled: boolean): void =>
    triggerCivic(B.TogglePatriotDroneIntercept, enabled);

/** Per-save AA rule: auto-buy ammo during calm. Idempotent SET (sends target state). */
export const toggleAutoResupply = (enabled: boolean): void =>
    triggerCivic(B.ToggleAutoResupply, enabled);

export const placeAABuilding = ({ prefab, mode }: AAPlacementPayload): void =>
    triggerCivic(B.PlaceAABuilding, `${prefab}|${mode}`);

// ============ Threats ============

export const focusThreat = (target: EntityRef): void =>
    triggerCivic(B.FocusThreat, target);

export const focusRadarThreat = (entity: EntityRef): void =>
    triggerCivic(B.FocusRadarThreat, entity);

export const dismissDebriefing = (): void =>
    triggerCivic(B.DismissDebriefing);

// ============ Mobilization ============

export const toggleConscription = (): void =>
    triggerCivic(B.ToggleConscription);

export const callToArms = (): void =>
    triggerCivic(B.CallToArms);

// ============ Cognitive Warfare ============

export const deployHero = (mode: HeroStatusType): void =>
    triggerCivic(B.DeployHero, mode);

export const recallHero = (): void =>
    triggerCivic(B.RecallHero);

export const setHeroMode = (mode: HeroStatusType): void =>
    triggerCivic(B.SetHeroMode, mode);

export const setNarrativeMode = (mode: NarrativeModeType): void =>
    triggerCivic(B.SetNarrativeMode, mode);

export const setTelemarathonActive = (active: boolean): void =>
    triggerCivic(B.SetTelemarathonActive, active);

export const setInternetMode = (mode: InternetModeType): void =>
    triggerCivic(B.SetInternetMode, mode);
