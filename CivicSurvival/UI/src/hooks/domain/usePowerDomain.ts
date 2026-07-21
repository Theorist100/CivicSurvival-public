/**
 * Power domain hooks.
 * Granular hooks for grid, backup, + composite.
 */

import { gridState$, backupState$ } from "../bindings/domainJsonBindings";
import { bindingDataOrDefault, useDtoBinding } from "./useDtoBinding";
import {
    DEFAULT_BACKUP_POWER_DTO,
    DEFAULT_POWER_GRID_DTO,
    isPowerGridDto,
    isBackupPowerDto,
} from "../../types/domainDtos";

export const usePowerGrid = () =>
    useDtoBinding(gridState$, isPowerGridDto, { debugName: "gridState", defaultValue: DEFAULT_POWER_GRID_DTO });

export const useBackupPower = () =>
    useDtoBinding(backupState$, isBackupPowerDto, { debugName: "backupState", defaultValue: DEFAULT_BACKUP_POWER_DTO });

export const useShadowPrograms = () =>
    bindingDataOrDefault(useBackupPower(), DEFAULT_BACKUP_POWER_DTO).ShadowProgramsJson;

export const usePowerGridState = usePowerGrid;

export const useBackupPowerState = useBackupPower;
