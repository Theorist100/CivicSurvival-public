// GENERATED - DO NOT EDIT
// Source:           Docs/Contracts/request-lifecycle.contract.yaml
// SourceHash:       sha256:cfc0d169fd0de4d469917aec0edd40ffdbea785a043671e0bfe907ac19c82c45
// Generator:        scripts/generators/request_lifecycle.py
// GeneratorVersion: 1.2.0
// ContractVersion:  1.0.0
// GeneratedAt:      2026-05-14T00:00:00Z

import { useRequestAction } from "./actions/useRequestAction";
import type { RequestResult } from "../types/dtoSubTypes";

export type RequestKey = "EmergencyResupplyRequest" | "PlantRepairRequest" | "CivilianRepairRequest" | "DonorSelectionRequest" | "DonorDialogRequest" | "InsiderRequest" | "IntelUpgradeRequest" | "HeroActionRequest" | "LastChoiceRequestResult" | "ModernizationRequest" | "SpotterActionRequest" | "AirDefensePlacementRequest" | "BackupPolicyRequest" | "DefensePolicyRequest" | "PatriotDroneToggleRequest" | "CallToArmsRequest" | "ConscriptionToggleRequest" | "DistrictToggleRequest" | "DistrictInternetToggleRequest" | "CitySchedulePeriodRequest" | "InternetModeRequest" | "ProcurementLevelRequest" | "LastDistributeResult" | "CorruptionSchemeRequest" | "MaintenanceContractRequest" | "ShadowTradeImportRequest" | "ShadowTradeExportRequest" | "TelemarathonModeRequest" | "TelemarathonActiveRequest" | "AutoDispatchToggleRequest" | "OperationRequest" | "NicknameRequest" | "LocaleRequest" | "ArenaLastRefreshResult" | "OneMoreYearRequest" | "EndlessModeRequest" | "CrisisSweepRequest";
export type RequestResultMode = "simple" | "split" | "keyed" | "perField";
export type RequestResultDiscriminator = "none" | "districtIndex" | "offerKey" | "field" | "operationSlot";

export type RequestMetadata = {
    readonly kind: string;
    readonly owner: string;
    readonly resultMode: RequestResultMode;
    readonly discriminator: RequestResultDiscriminator;
};

export type NoRequestTarget = { readonly discriminator: "none" };
export type DistrictIndexRequestTarget = { readonly discriminator: "districtIndex"; readonly districtIndex: number | string };
export type OfferKeyRequestTarget = { readonly discriminator: "offerKey"; readonly offerKey: string };
export type FieldRequestTarget = { readonly discriminator: "field"; readonly field: string };
export type OperationSlotRequestTarget = { readonly discriminator: "operationSlot"; readonly operationSlot: string };
export type RequestTarget = NoRequestTarget | DistrictIndexRequestTarget | OfferKeyRequestTarget | FieldRequestTarget | OperationSlotRequestTarget;

export type RequestTargetByKey = {
    readonly "EmergencyResupplyRequest": NoRequestTarget;
    readonly "PlantRepairRequest": NoRequestTarget;
    readonly "CivilianRepairRequest": NoRequestTarget;
    readonly "DonorSelectionRequest": NoRequestTarget;
    readonly "DonorDialogRequest": NoRequestTarget;
    readonly "InsiderRequest": NoRequestTarget;
    readonly "IntelUpgradeRequest": NoRequestTarget;
    readonly "HeroActionRequest": NoRequestTarget;
    readonly "LastChoiceRequestResult": NoRequestTarget;
    readonly "ModernizationRequest": DistrictIndexRequestTarget;
    readonly "SpotterActionRequest": NoRequestTarget;
    readonly "AirDefensePlacementRequest": NoRequestTarget;
    readonly "BackupPolicyRequest": NoRequestTarget;
    readonly "DefensePolicyRequest": NoRequestTarget;
    readonly "PatriotDroneToggleRequest": NoRequestTarget;
    readonly "CallToArmsRequest": NoRequestTarget;
    readonly "ConscriptionToggleRequest": NoRequestTarget;
    readonly "DistrictToggleRequest": DistrictIndexRequestTarget;
    readonly "DistrictInternetToggleRequest": DistrictIndexRequestTarget;
    readonly "CitySchedulePeriodRequest": NoRequestTarget;
    readonly "InternetModeRequest": NoRequestTarget;
    readonly "ProcurementLevelRequest": NoRequestTarget;
    readonly "LastDistributeResult": NoRequestTarget;
    readonly "CorruptionSchemeRequest": NoRequestTarget;
    readonly "MaintenanceContractRequest": OfferKeyRequestTarget;
    readonly "ShadowTradeImportRequest": NoRequestTarget;
    readonly "ShadowTradeExportRequest": NoRequestTarget;
    readonly "TelemarathonModeRequest": NoRequestTarget;
    readonly "TelemarathonActiveRequest": NoRequestTarget;
    readonly "AutoDispatchToggleRequest": NoRequestTarget;
    readonly "OperationRequest": OperationSlotRequestTarget;
    readonly "NicknameRequest": NoRequestTarget;
    readonly "LocaleRequest": NoRequestTarget;
    readonly "ArenaLastRefreshResult": NoRequestTarget;
    readonly "OneMoreYearRequest": NoRequestTarget;
    readonly "EndlessModeRequest": NoRequestTarget;
    readonly "CrisisSweepRequest": NoRequestTarget;
};

export type RequestTargetFor<K extends RequestKey> = RequestTargetByKey[K];

export const noRequestTarget = (): NoRequestTarget => ({ discriminator: "none" });
export const districtIndexTarget = (districtIndex: number | string): DistrictIndexRequestTarget => ({ discriminator: "districtIndex", districtIndex });
export const offerKeyTarget = (offerKey: string): OfferKeyRequestTarget => ({ discriminator: "offerKey", offerKey });
export const fieldTarget = (field: string): FieldRequestTarget => ({ discriminator: "field", field });
export const operationSlotTarget = (operationSlot: string): OperationSlotRequestTarget => ({ discriminator: "operationSlot", operationSlot });

export const REQUEST_KEYS: readonly RequestKey[] = [
    "EmergencyResupplyRequest",
    "PlantRepairRequest",
    "CivilianRepairRequest",
    "DonorSelectionRequest",
    "DonorDialogRequest",
    "InsiderRequest",
    "IntelUpgradeRequest",
    "HeroActionRequest",
    "LastChoiceRequestResult",
    "ModernizationRequest",
    "SpotterActionRequest",
    "AirDefensePlacementRequest",
    "BackupPolicyRequest",
    "DefensePolicyRequest",
    "PatriotDroneToggleRequest",
    "CallToArmsRequest",
    "ConscriptionToggleRequest",
    "DistrictToggleRequest",
    "DistrictInternetToggleRequest",
    "CitySchedulePeriodRequest",
    "InternetModeRequest",
    "ProcurementLevelRequest",
    "LastDistributeResult",
    "CorruptionSchemeRequest",
    "MaintenanceContractRequest",
    "ShadowTradeImportRequest",
    "ShadowTradeExportRequest",
    "TelemarathonModeRequest",
    "TelemarathonActiveRequest",
    "AutoDispatchToggleRequest",
    "OperationRequest",
    "NicknameRequest",
    "LocaleRequest",
    "ArenaLastRefreshResult",
    "OneMoreYearRequest",
    "EndlessModeRequest",
    "CrisisSweepRequest"
] as const;

export const REQUEST_METADATA: Readonly<Record<RequestKey, RequestMetadata>> = {
    "EmergencyResupplyRequest": {
        "kind": "EmergencyResupply",
        "owner": "defense.resupply",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "PlantRepairRequest": {
        "kind": "PlantRepair",
        "owner": "infrastructure.repair",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "CivilianRepairRequest": {
        "kind": "CivilianRepair",
        "owner": "threat_damage.civilian_repair",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "DonorSelectionRequest": {
        "kind": "DonorSelection",
        "owner": "diplomacy.donor",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "DonorDialogRequest": {
        "kind": "DonorDialog",
        "owner": "diplomacy.donor",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "InsiderRequest": {
        "kind": "IntelPurchase",
        "owner": "intel.purchase",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "IntelUpgradeRequest": {
        "kind": "IntelUpgrade",
        "owner": "intel.upgrade",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "HeroActionRequest": {
        "kind": "HeroAction",
        "owner": "cognitive.hero",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "LastChoiceRequestResult": {
        "kind": "CountermeasureChoice",
        "owner": "countermeasures.choice",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "ModernizationRequest": {
        "kind": "Modernization",
        "owner": "corruption.modernization",
        "resultMode": "keyed",
        "discriminator": "districtIndex"
    },
    "SpotterActionRequest": {
        "kind": "SpotterAction",
        "owner": "spotters.action",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "AirDefensePlacementRequest": {
        "kind": "AirDefensePlacement",
        "owner": "air_defense.placement",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "BackupPolicyRequest": {
        "kind": "BackupPolicy",
        "owner": "settings.backup_policy",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "DefensePolicyRequest": {
        "kind": "DefensePolicy",
        "owner": "defense.policy",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "PatriotDroneToggleRequest": {
        "kind": "PatriotDroneToggle",
        "owner": "defense.patriot_drone_toggle",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "CallToArmsRequest": {
        "kind": "Mobilization",
        "owner": "mobilization.call_to_arms",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "ConscriptionToggleRequest": {
        "kind": "ConscriptionToggle",
        "owner": "mobilization.conscription",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "DistrictToggleRequest": {
        "kind": "DistrictToggle",
        "owner": "power_grid.district_toggle",
        "resultMode": "keyed",
        "discriminator": "districtIndex"
    },
    "DistrictInternetToggleRequest": {
        "kind": "DistrictInternetToggle",
        "owner": "power_grid.internet_toggle",
        "resultMode": "keyed",
        "discriminator": "districtIndex"
    },
    "CitySchedulePeriodRequest": {
        "kind": "CitySchedule",
        "owner": "power_grid.schedule",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "InternetModeRequest": {
        "kind": "InternetMode",
        "owner": "cognitive.internet_mode",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "ProcurementLevelRequest": {
        "kind": "ProcurementLevel",
        "owner": "relief.procurement",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "LastDistributeResult": {
        "kind": "AidDistribution",
        "owner": "relief.distribution",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "CorruptionSchemeRequest": {
        "kind": "CorruptionScheme",
        "owner": "corruption.scheme",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "MaintenanceContractRequest": {
        "kind": "MaintenanceContract",
        "owner": "corruption.maintenance_contract",
        "resultMode": "keyed",
        "discriminator": "offerKey"
    },
    "ShadowTradeImportRequest": {
        "kind": "ShadowTradeImport",
        "owner": "shadow.import",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "ShadowTradeExportRequest": {
        "kind": "ShadowTradeExport",
        "owner": "shadow.export",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "TelemarathonModeRequest": {
        "kind": "TelemarathonMode",
        "owner": "cognitive.telemarathon",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "TelemarathonActiveRequest": {
        "kind": "TelemarathonActive",
        "owner": "cognitive.telemarathon",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "AutoDispatchToggleRequest": {
        "kind": "AutoDispatchToggle",
        "owner": "settings.auto_dispatch",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "OperationRequest": {
        "kind": "OperationLaunch",
        "owner": "gridwarfare.operation",
        "resultMode": "keyed",
        "discriminator": "operationSlot"
    },
    "NicknameRequest": {
        "kind": "NicknameUpdate",
        "owner": "player.nickname",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "LocaleRequest": {
        "kind": "LocaleChange",
        "owner": "settings.locale",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "ArenaLastRefreshResult": {
        "kind": "ArenaRefresh",
        "owner": "arena.leaderboard",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "OneMoreYearRequest": {
        "kind": "OneMoreYear",
        "owner": "scenario.victory",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "EndlessModeRequest": {
        "kind": "EndlessMode",
        "owner": "scenario.victory",
        "resultMode": "simple",
        "discriminator": "none"
    },
    "CrisisSweepRequest": {
        "kind": "CrisisSweep",
        "owner": "diagnostics.crisis_sweep",
        "resultMode": "simple",
        "discriminator": "none"
    }
} as const;

export function isRequestKey(value: string): value is RequestKey {
    return (REQUEST_KEYS as readonly string[]).includes(value);
}

function requestTargetValue(target: RequestTarget): string {
    switch (target.discriminator) {
        case "none": return "";
        case "districtIndex": return String(target.districtIndex);
        case "offerKey": return target.offerKey;
        case "field": return target.field;
        case "operationSlot": return target.operationSlot;
    }
}

export function requestResultMatchesTarget<K extends RequestKey>(
    key: K,
    result: RequestResult | undefined,
    target: RequestTargetFor<K>
): boolean {
    if (!result) return false;
    const metadata = REQUEST_METADATA[key];
    if (metadata.discriminator !== target.discriminator) return false;
    return result.DiscriminatorKind === target.discriminator
        && result.DiscriminatorValue === requestTargetValue(target);
}

export function requestResultForTarget<K extends RequestKey>(
    key: K,
    result: RequestResult | undefined,
    target: RequestTargetFor<K>
): RequestResult | undefined {
    return requestResultMatchesTarget(key, result, target) ? result : undefined;
}

export function useRequest(action: () => boolean, result: RequestResult | undefined) {
    return useRequestAction(action, result);
}
