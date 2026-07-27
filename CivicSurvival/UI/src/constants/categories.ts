/**
 * Building categories configuration
 * Matches C# BuildingCategory enum
 */

import { type TranslationKey } from "../locales";
import { asBuildingCategoryId, type BuildingCategoryId } from "../types/semantic";

export type BuildingCategoryKey = "residential" | "commercial" | "industrial" | "office" | "services";

export interface CategoryConfig {
    id: BuildingCategoryId;
    key: BuildingCategoryKey;
    /** Translation key for abbreviation (e.g. "UI_CAT_RESIDENTIAL_ABBREV" → "Res") */
    labelKey: TranslationKey;
    /** Translation key for full name (e.g. "UI_CAT_RESIDENTIAL" → "Residential") */
    nameKey: TranslationKey;
    color: string;
}

// IDs must match C# BuildingCategory enum values (not indices!)
// None=0, Residential=1, Commercial=2, Industrial=3, Office=4, Services=5
export const CATEGORIES: CategoryConfig[] = [
    { id: asBuildingCategoryId(1), key: "residential", labelKey: "UI_CAT_RESIDENTIAL_ABBREV", nameKey: "UI_CAT_RESIDENTIAL", color: "#4488ff" },
    { id: asBuildingCategoryId(2), key: "commercial", labelKey: "UI_CAT_COMMERCIAL_ABBREV", nameKey: "UI_CAT_COMMERCIAL", color: "#44ff88" },
    { id: asBuildingCategoryId(3), key: "industrial", labelKey: "UI_CAT_INDUSTRIAL_ABBREV", nameKey: "UI_CAT_INDUSTRIAL", color: "#ffaa44" },
    { id: asBuildingCategoryId(4), key: "office", labelKey: "UI_CAT_OFFICE_ABBREV", nameKey: "UI_CAT_OFFICE", color: "#aa88ff" },
    { id: asBuildingCategoryId(5), key: "services", labelKey: "UI_CAT_SERVICES_ABBREV", nameKey: "UI_CAT_SERVICES", color: "#888888" },
];
