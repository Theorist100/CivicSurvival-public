/**
 * Power domain actions — district toggles, backup policy, auto-dispatch.
 * Extracted from viewModelActions.ts.
 */

/** @internal Raw trigger wrappers. Import only from hooks/actions feature action hooks. */
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";
import {
    type BackupPolicy,
    type BuildingCategoryId,
    type ContractorType,
    type DistrictIndex,
    type EntityIndex,
    type PlantId,
    type RepairType,
    type ScheduleId,
    asScheduleId,
} from "../types/semantic";
import { type EntityRef } from "../types/entityRef";
import { type DistrictMode } from "../components/shared/common/ModeDropdown";

export type RepairPlantPayload = {
    plantId: PlantId;
    mode: RepairType;
};

export type RepairCivilianPayload = {
    building: EntityRef;
    repairType: RepairType;
};

export type DistrictModePayload = {
    entity: EntityIndex;
    mode: DistrictMode;
};

// ============ District Controls ============

export const toggleDistrictBlackout = (districtIndex: EntityIndex): void =>
    triggerCivic(B.ToggleDistrictBlackout, districtIndex);

export const setDistrictBlackout = (districtIndex: EntityIndex, blackedOut: boolean): void =>
    triggerCivic(B.SetDistrictBlackout, districtIndex, blackedOut ? 1 : 0);

// Composite key encoding: districtIndex * 100 + subId
export const toggleDistrictCategory = (districtIndex: EntityIndex, categoryId: BuildingCategoryId): void =>
    triggerCivic(B.ToggleDistrictCategory, districtIndex * 100 + categoryId);

export const setDistrictSchedule = (districtIndex: EntityIndex, scheduleId: ScheduleId): void =>
    triggerCivic(B.SetDistrictSchedule, districtIndex, scheduleId);

export const setDistrictMode = ({ entity, mode }: DistrictModePayload): void => {
    if (mode === "off") {
        setDistrictBlackout(entity, true);
        return;
    }
    setDistrictBlackout(entity, false);
    if (mode === "on") {
        setDistrictSchedule(entity, asScheduleId(0)); // explicit always-on district override
        return;
    }
    const scheduleId = mode === "q1" ? 1 : mode === "q2" ? 2 : mode === "q3" ? 3 : mode === "dayshift" ? 4 : 0;
    setDistrictSchedule(entity, asScheduleId(scheduleId));
};

export const setCitySchedule = (scheduleId: ScheduleId): void =>
    triggerCivic(B.SetCitySchedule, scheduleId);

export const toggleVIP = (districtIndex: EntityIndex): void =>
    triggerCivic(B.ToggleVIP, districtIndex);

export const toggleVIPBypass = (districtIndex: EntityIndex): void =>
    triggerCivic(B.ToggleVIPBypass, districtIndex);

export const toggleInternet = (districtIndex: EntityIndex): void =>
    triggerCivic(B.ToggleInternet, districtIndex);

// ============ Auto-Dispatch ============

export const toggleAutoDispatch = (): void =>
    triggerCivic(B.ToggleAutoDispatch);

// ============ Backup Policy ============

/** 0=Reserve, 1=CriticalOnly, 2=FullDischarge */
export const setBackupPolicy = (policy: BackupPolicy): void =>
    triggerCivic(B.SetBackupPolicy, policy);

// ============ District Modernization ============

/** Encoded: districtIndex * 10 + contractorType (0=Honest, 1=YourGuy) */
export const launchDistrictModernization = (districtIndex: DistrictIndex, contractorType: ContractorType): void =>
    triggerCivic(B.LaunchDistrictModernization, districtIndex * 10 + contractorType);

// ============ Plant Repair ============

export const repairPlant = ({ plantId, mode }: RepairPlantPayload): void =>
    triggerCivic(B.RepairPlant, plantId, mode);

// ============ Civilian Repair ============

export const repairCivilian = ({ building, repairType }: RepairCivilianPayload): void =>
    triggerCivic(B.RepairCivilian, building, repairType);
