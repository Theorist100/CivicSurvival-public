/**
 * Pure transformation functions for ViewModel creation
 */

import { CATEGORIES, type BuildingCategoryKey } from "constants/categories";
import { type DistrictDto } from "types/domainDtos.generated";
import { asEntityIndex, asEntityVersion } from "types/semantic";
import {
    type DistrictViewModel,
    type CategoryViewModel,
    type BlackoutSource,
} from "types/models";

// ============================================================================
// District Transformations
// ============================================================================

const getCategoryOffState = (district: DistrictDto, key: BuildingCategoryKey): boolean => {
    switch (key) {
        case "residential": return district.ResidentialOff;
        case "commercial": return district.CommercialOff;
        case "industrial": return district.IndustrialOff;
        case "office": return district.OfficeOff;
        case "services": return district.ServicesOff ?? false;
        default: return false;
    }
};

const getCategoryMW = (district: DistrictDto, key: BuildingCategoryKey): number => {
    switch (key) {
        case "residential": return district.ResidentialMW;
        case "commercial": return district.CommercialMW;
        case "industrial": return district.IndustrialMW;
        case "office": return district.OfficeMW;
        case "services": return district.ServicesMW ?? 0;
        default: return 0;
    }
};

const isFullBlackout = (district: DistrictDto): boolean => {
    return (
        district.ResidentialOff &&
        district.CommercialOff &&
        district.IndustrialOff &&
        district.OfficeOff &&
        (district.ServicesOff ?? false)
    );
};

const toBlackoutSource = (raw: string | undefined): BlackoutSource => {
    if (raw === "auto" || raw === "schedule" || raw === "manual") return raw;
    return "none";
};

export const transformDistrict = (district: DistrictDto): DistrictViewModel => {
    // C# DistrictDto guarantees all fields are populated via the generated WriteTo.
    const scheduleId = district.Schedule;

    const categories: CategoryViewModel[] = CATEGORIES.map((config) => ({
        config,
        isOff: getCategoryOffState(district, config.key),
        mw: getCategoryMW(district, config.key),
    }));

    return {
        entity: asEntityIndex(district.EntityIndex),
        version: asEntityVersion(district.EntityVersion),
        name: district.Name,
        isUnzoned: district.IsUnzoned,
        isManualMode: scheduleId === 0,
        isFullBlackout: isFullBlackout(district),
        isScheduleActive: district.ScheduleActive,
        isAutoShedded: district.IsAutoShedded ?? false,
        blackoutSource: toBlackoutSource(district.BlackoutSource),
        scheduleId,
        scheduleName: district.ScheduleName ?? "Manual",
        categories,
        totalMW: district.TotalMW,
        deliveredMW: district.DeliveredMW ?? district.TotalMW,
        thresholdCutMW: district.ThresholdCutMW ?? 0,
        priority: district.Priority,
        isVIP: district.IsVIP,
        isVIPBypass: district.IsVIPBypass,
        internetDisabled: district.InternetDisabled,
        thresholdCutBuildings: district.ThresholdCutBuildings ?? 0,
        totalHappinessPenalty: district.TotalHappinessPenalty ?? 0,
        totalCommercePenalty: district.TotalCommercePenalty ?? 0,
    };
};
