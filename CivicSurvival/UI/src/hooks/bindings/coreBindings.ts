/**
 * Core bindings - individual bindValue subscriptions.
 * Only bindings that are NOT in a JSON DTO and are actively used remain here.
 * Scalar fields in PowerGridDto are accessed via usePowerGrid() hook.
 */

import { bindCivicValue } from "../typedBinding.generated";
import { triggerCivic } from "@hooks/typedTrigger";
import { type DistrictIndex } from "../../types/semantic";
import {
    isCognitiveDistrictEntry,
    isDistrictDto,
    type CognitiveDistrictEntry,
} from "../../types/domainDtos.generated";
import { B } from "../bindingNames.generated";

// Districts - JSON array string (DistrictUISystem), parsed via useSafeJsonArray
export const districts$ = bindCivicValue(B.Districts, "[]");

// UI settings (separate C# system)
export const uiTheme$ = bindCivicValue(B.UiTheme, 0);

// Social feed (separate C# system, already JSON string)
export const socialFeed$ = bindCivicValue(B.SocialFeed, "[]");

// Official NEWS feed (separate from Chipper/social satire)
export const newsFeed$ = bindCivicValue(B.NewsFeed, "[]");

// Cognitive districts - JSON array string (CognitiveUISystem), parsed via useSafeJsonArray.
// Type re-exported from the generated subtype; historical CognitiveDistrictDto name kept as alias.
export type CognitiveDistrictDto = CognitiveDistrictEntry;

export const cognitiveDistricts$ = bindCivicValue(B.CognitiveDistricts, "[]");

export const toggleDistrictInternet = (districtIndex: DistrictIndex) => triggerCivic(B.ToggleInternet, districtIndex);

// Re-export the generated DistrictDto type guard under the historical
// isDistrictData name so consumers don't need to track the rename.
export const isDistrictData = isDistrictDto;

// Re-export the generated guard under the historical isCognitiveDistrictDto name.
export const isCognitiveDistrictDto = isCognitiveDistrictEntry;
