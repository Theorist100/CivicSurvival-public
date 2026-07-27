/**
 * Corruption domain hooks.
 * 6 granular hooks (export, import, schemes, countermeasures, reputation, maintenance)
 * + composite.
 */

import {
    exportState$, importState$, schemesState$, buckwheatState$,
    countermeasuresState$, reputationState$, maintenanceState$,
} from "../bindings/domainJsonBindings";
import { useDtoBinding } from "./useDtoBinding";
import {
    DEFAULT_BUCKWHEAT_DTO,
    DEFAULT_COUNTERMEASURES_DTO,
    DEFAULT_EXPORT_DTO,
    DEFAULT_IMPORT_DTO,
    DEFAULT_MAINTENANCE_DTO,
    DEFAULT_REPUTATION_DTO,
    DEFAULT_SCHEMES_DTO,
    isExportDto,
    isImportDto,
    isSchemesDto,
    isBuckwheatDto,
    isCountermeasuresDto,
    isReputationDto,
    isMaintenanceDto,
} from "../../types/domainDtos";

export const useExport = () =>
    useDtoBinding(exportState$, isExportDto, { debugName: "exportState", defaultValue: DEFAULT_EXPORT_DTO });

export const useImport = () =>
    useDtoBinding(importState$, isImportDto, { debugName: "importState", defaultValue: DEFAULT_IMPORT_DTO });

export const useSchemes = () =>
    useDtoBinding(schemesState$, isSchemesDto, { debugName: "schemesState", defaultValue: DEFAULT_SCHEMES_DTO });

export const useBuckwheat = () =>
    useDtoBinding(buckwheatState$, isBuckwheatDto, { debugName: "buckwheatState", defaultValue: DEFAULT_BUCKWHEAT_DTO });

export const useCountermeasures = () =>
    useDtoBinding(countermeasuresState$, isCountermeasuresDto, { debugName: "countermeasuresState", defaultValue: DEFAULT_COUNTERMEASURES_DTO });

export const useReputation = () =>
    useDtoBinding(reputationState$, isReputationDto, { debugName: "reputationState", defaultValue: DEFAULT_REPUTATION_DTO });

export const useMaintenance = () =>
    useDtoBinding(maintenanceState$, isMaintenanceDto, { debugName: "maintenanceState", defaultValue: DEFAULT_MAINTENANCE_DTO });
