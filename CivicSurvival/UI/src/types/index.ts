/**
 * Types for Systems Critical UI
 */

// Re-export domain types
export type { StressZone } from "./models/PowerTypes";
export type { PlantWearData } from "./domainDtos.generated";

/**
 * District data: re-exported generated contract DTO. The hand-written
 * DistrictData camelCase interface that lived here drifted from the C#
 * producer in DistrictDtoFactory; the C4b2 migration made DistrictDto a
 * generated subtype with PascalCase wire keys. Consumers should import
 * DistrictDto from ./domainDtos.generated (or this re-export) and run
 * the transformDistrict mapper if they need the camelCase ViewModel.
 */
export type { DistrictDto } from "./domainDtos.generated";
