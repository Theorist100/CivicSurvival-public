/**
 * Defense domain actions — resupply, SBU, evacuation, counter-OSINT, policy.
 * Extracted from viewModelActions.ts.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import { type HeroStatusType, type InternetModeType, type NarrativeModeType, type HeroArchetypeType } from "../hooks/domain/cognitiveLabels";
import { type DefensePolicyId, type AATypeId } from "../types/semantic";
import { type EntityRef } from "../types/entityRef";

export type AAPlacementMode = "Paid" | "Heritage" | "DonorCredit" | "BlackMarket";

export type AAPlacementPayload = {
    /** Prefab id straight from the backend roster — not a hand-kept union, or a new AA could not
     *  be placed until this line was edited too. */
    prefab: string;
    mode: AAPlacementMode;
};

// ============ Air Defense ============

export const emergencyResupply = (type: AATypeId): void =>
    triggerCivic(B.EmergencyResupply, type);

/** Sentinel id meaning "restock every gun type at once" — mirror of
 *  AAResupplyGroups.GunsResupplyTypeId (C#). */
const GUNS_RESUPPLY_ID = -1 as AATypeId;

/** Sentinel id meaning "restock every missile type at once" (Patriot + Hawk) — mirror of
 *  AAResupplyGroups.RocketsResupplyTypeId (C#). The rocket-kind mirror of the guns sentinel. */
const ROCKETS_RESUPPLY_ID = -2 as AATypeId;

/** Restock all gun types (Bofors/Gepard/Heritage) in one emergency batch. Delegates to
 *  emergencyResupply (the sole EmergencyResupply trigger wrapper) with the guns sentinel. */
export const emergencyResupplyGuns = (): void =>
    emergencyResupply(GUNS_RESUPPLY_ID);

/** Restock all missile types (Patriot/Hawk) that are off cooldown, in one emergency batch. */
export const emergencyResupplyRockets = (): void =>
    emergencyResupply(ROCKETS_RESUPPLY_ID);

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

/** Put the placement tool away (vanilla second-click convention). Sync, pause-safe;
 *  the open placement request completes server-side via the same path Esc takes. */
export const cancelAAPlacement = (): void =>
    triggerCivic(B.CancelAAPlacement);

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

/** Switch the selected speaker archetype (Voice/Arestovych/Patriot). Phase 04 backend
 *  (HeroActionType.SetArchetype): free while inactive, cost + cooldown while deployed. */
export const setHeroArchetype = (archetype: HeroArchetypeType): void =>
    triggerCivic(B.SetHeroArchetype, archetype);

/** Split the Propaganda Center's household coverage ceiling across Poor/Middle/Wealthy. Args are
 *  normalized weights (0..1, sum 1); sent as permille ints, the backend re-normalizes. Pause-safe
 *  sync host-direct command (PropagandaCenterSystem.SetBroadcastAllocation) — no request lifecycle. */
export const setBroadcastAllocation = (poor: number, middle: number, wealthy: number): void =>
    triggerCivic(
        B.SetBroadcastAllocation,
        Math.round(poor * 1000),
        Math.round(middle * 1000),
        Math.round(wealthy * 1000),
    );

export const setNarrativeMode = (mode: NarrativeModeType): void =>
    triggerCivic(B.SetNarrativeMode, mode);

export const setTelemarathonActive = (active: boolean): void =>
    triggerCivic(B.SetTelemarathonActive, active);

export const setInternetMode = (mode: InternetModeType): void =>
    triggerCivic(B.SetInternetMode, mode);
