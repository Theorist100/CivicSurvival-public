type Brand<T, Name extends string> = T & { readonly __brand: Name };

export type EntityIndex = Brand<number, "EntityIndex">;
export type EntityVersion = Brand<number, "EntityVersion">;
export type DistrictIndex = Brand<number, "DistrictIndex">;
export type BuildingIndex = Brand<number, "BuildingIndex">;
export type PlantId = Brand<number, "PlantId">;
export type BuildingCategoryId = Brand<number, "BuildingCategoryId">;
export type ScheduleId = Brand<number, "ScheduleId">;
export type ThreatIndex = Brand<number, "ThreatIndex">;
export type Megawatts = Brand<number, "Megawatts">;
export type PercentValue = Brand<number, "PercentValue">;
export type ToastId = Brand<number, "ToastId">;

export const FEATURE_IDS = [
    "AirDefense",
    "Arena",
    "ArenaUI",
    "Attention",
    "Blackout",
    "Cognitive",
    "Corruption",
    "Countermeasures",
    "DamageAccounting",
    "Diplomacy",
    "Economy",
    "Effects",
    "Efficiency",
    "Engineering",
    "Finance",
    "GridWarfare",
    "Intel",
    "Mobilization",
    "Narrative",
    "NeighborEnvy",
    "Network",
    "Notifications",
    "PowerBackup",
    "PowerGrid",
    "Refugees",
    "Scenario",
    "ShadowEconomy",
    "Spotters",
    "ThreatDamage",
    "ThreatFlight",
    "ThreatsAirDefense",
    "ThreatUI",
    "Tutorial",
    "UI",
    "Waves",
    "WarView",
    "Wellbeing",
] as const;

export type FeatureId = typeof FEATURE_IDS[number];

export const isFeatureId = (value: string): value is FeatureId =>
    (FEATURE_IDS as readonly string[]).includes(value);

export type BackupPolicy = 0 | 1 | 2;
export type ContractorType = 0 | 1;
export type RepairType = 0 | 1 | 2;
export type ProcurementLevel = 0 | 25 | 50 | 75 | 100;
export type InvestigationChoice = 1 | 2 | 3 | 4;
export type PoliceChoice = 1 | 2 | 3;
export type DefensePolicyId = 0 | 1;

/**
 * AA type ids — must mirror the C# AAType enum (Core/Types/ThreatTypes.cs). The emergency
 * resupply trigger carries this id so the request refills only that type's installations at
 * that type's own cost.
 */
export const AA_TYPE = {
    HeritageBofors: 0,
    Bofors40mm: 1,
    Gepard: 2,
    PatriotSAM: 3,
} as const;

export type AATypeId = (typeof AA_TYPE)[keyof typeof AA_TYPE];
export type PlantState = 0 | 1 | 2 | 3 | 4 | 5 | 6;
export type SchedulePresetId = 0 | 1 | 2 | 3 | 4;
export type CityScheduleId = -1 | SchedulePresetId;
export type UIThemeId = 0 | 1 | 2;
export type DifficultyPresetId = 0 | 1 | 2 | 3;
export type ModLanguageId = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7;

// Float jitter guard shared by every "factor ≈ 1 → hide" display gate (per-plant
// saturation/fuel badges, city-passport fleet row): a factor within 1% of 1 renders nothing.
export const SATURATION_DISPLAY_EPS = 0.01;

export const isCityScheduleId = (value: number): value is CityScheduleId =>
    value === -1 || value === 0 || value === 1 || value === 2 || value === 3 || value === 4;

export const isUIThemeId = (value: number): value is UIThemeId =>
    value === 0 || value === 1 || value === 2;

export const isDifficultyPresetId = (value: number): value is DifficultyPresetId =>
    value === 0 || value === 1 || value === 2 || value === 3;

export const isModLanguageId = (value: number): value is ModLanguageId =>
    value >= 0 && value <= 7 && Number.isInteger(value);

export const isBackupPolicy = (value: number): value is BackupPolicy =>
    value === 0 || value === 1 || value === 2;

export const isProcurementLevel = (value: number): value is ProcurementLevel =>
    value === 0 || value === 25 || value === 50 || value === 75 || value === 100;

export const asEntityIndex = (value: number): EntityIndex => value as EntityIndex;
export const asEntityVersion = (value: number): EntityVersion => value as EntityVersion;
export const asDistrictIndex = (value: number): DistrictIndex => value as DistrictIndex;
export const asBuildingIndex = (value: number): BuildingIndex => value as BuildingIndex;
export const asPlantId = (value: number): PlantId => value as PlantId;
export const asBuildingCategoryId = (value: number): BuildingCategoryId => value as BuildingCategoryId;
export const asScheduleId = (value: number): ScheduleId => value as ScheduleId;
export const asThreatIndex = (value: number): ThreatIndex => value as ThreatIndex;
export const asMegawatts = (value: number): Megawatts => value as Megawatts;
export const asPercentValue = (value: number): PercentValue => value as PercentValue;
export const asToastId = (value: number): ToastId => value as ToastId;
