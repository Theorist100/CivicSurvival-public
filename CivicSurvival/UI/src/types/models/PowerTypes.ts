/**
 * Power Grid domain types
 */

import { type CategoryConfig } from "constants/categories";
import { type EntityIndex, type EntityVersion, type SchedulePresetId } from "../semantic";

/** Grid stress zone - maps to C# GridStressZone enum */
export type StressZone = "normal" | "yellow" | "red" | "collapsed";

/** Blackout source - why the district is blacked out */
export type BlackoutSource = "none" | "manual" | "auto" | "schedule";

export interface CategoryViewModel {
    config: CategoryConfig;
    isOff: boolean;
    mw: number;
}

export interface DistrictViewModel {
    entity: EntityIndex;
    version: EntityVersion;
    name: string;
    isUnzoned: boolean;
    isManualMode: boolean;
    isFullBlackout: boolean;
    isScheduleActive: boolean;
    isAutoShedded: boolean;
    blackoutSource: BlackoutSource;
    scheduleId: SchedulePresetId;
    scheduleName: string;
    categories: CategoryViewModel[];
    totalMW: number;
    /** Actually delivered to the district after vanilla flow distribution and threshold cuts. */
    deliveredMW: number;
    /** MW lost in this district to threshold cuts (buildings receiving < 90% zeroed). */
    thresholdCutMW: number;
    priority: number;
    isVIP: boolean;
    isVIPBypass: boolean;
    internetDisabled: boolean;
    thresholdCutBuildings: number;
    totalHappinessPenalty: number;
    totalCommercePenalty: number;
}

