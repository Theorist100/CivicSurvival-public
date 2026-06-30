import type React from "react";
import { hexToRgba } from "@themes";
import { fractionToPercent, type ProgressFraction } from "../../../../../types/branded";
import { type PlantState, SATURATION_DISPLAY_EPS } from "../../../../../types/semantic";
import { IconAlert, IconClock, IconFuel, IconGear, IconLightning, IconReserve, IconTarget, IconWrench } from "@shared/common/Icons";

export const REPAIR_TYPE = {
    MUNICIPAL: 0,
    MUNICIPAL_WITH_KICKBACK: 1,
    SHADOW_OPS: 2,
} as const;

export type WearLevel = "normal" | "warning" | "critical" | "repairing" | "construction" | "damaged" | "disaster";

export const PLANT_STATE: Record<string, PlantState> = {
    Operational: 0,
    Worn: 1,
    Critical: 2,
    Repairing: 3,
    Exploded: 4,
    UnderConstruction: 5,
    DisabledByDisaster: 6,
};

export const getWearLevelFromState = (state: PlantState): WearLevel => {
    switch (state) {
        case PLANT_STATE.UnderConstruction: return "construction";
        case PLANT_STATE.Repairing: return "repairing";
        case PLANT_STATE.DisabledByDisaster: return "disaster";
        case PLANT_STATE.Exploded: return "damaged";
        case PLANT_STATE.Critical: return "critical";
        case PLANT_STATE.Worn: return "warning";
        case PLANT_STATE.Operational:
        default: return "normal";
    }
};

/** Fixed sub-column inside STATUS: same badge type always lands on the same vertical.
 *  "state" (construction/repair) is exclusive and spans the whole zone. */
export type BadgeSlot = "state" | "wear" | "damage" | "fuel" | "output";
export interface StatusBadge { text: string; color: string; bgColor: string; icon?: React.ComponentType<{ className?: string }>; title?: string; slot: BadgeSlot; }
export interface BadgeTheme { success: string; operations: string; warning: string; error: string; textMuted: string; }
// Chips show icon + compact value; the full wording (the long label) goes to the hover tip.
export interface BadgeLabels {
    building: (days: number) => string; buildingShort: (days: number) => string;
    repair: (hours: string) => string; repairShort: (hours: string) => string;
    wear: (pct: number) => string; wearLow: (pct: number) => string;
    hit: (hits: number, maxHits: number, pct: number) => string; disaster: (pct: number) => string;
    exploded: string; ok: string;
    saturationTip: (pct: number) => string;
    saturationRecovery: (hours: number) => string; fuel: (pct: number) => string;
}

const pctText = (n: number): string => `${n}%`;
const negPctText = (n: number): string => `-${n}%`;

export const getStatusBadges = (plant: { state: PlantState; wearProgress: ProgressFraction; repairHoursLeft: number; constructionDaysLeft?: number | undefined; operationalDamageProgress: ProgressFraction; operationalHitCount: number; operationalHitMax: number; disasterDamageProgress: ProgressFraction; hasExploded: boolean; saturationFactor: number; fuelAvailabilityPercent: number; fuelFactor: number; recoveryHours: number }, c: BadgeTheme, labels: BadgeLabels): StatusBadge[] => {
    const badges: StatusBadge[] = [];
    if (plant.state === PLANT_STATE.UnderConstruction) {
        const days = Math.ceil(plant.constructionDaysLeft ?? 0);
        badges.push({ text: labels.buildingShort(days), title: labels.building(days), icon: IconClock, color: c.success, bgColor: hexToRgba(c.success, 0.15), slot: "state" });
        return badges;
    }
    if (plant.state === PLANT_STATE.Repairing) {
        const hours = plant.repairHoursLeft.toFixed(1);
        badges.push({ text: labels.repairShort(hours), title: labels.repair(hours), icon: IconWrench, color: c.operations, bgColor: hexToRgba(c.operations, 0.15), slot: "state" });
        return badges;
    }
    const disasterPct = fractionToPercent(plant.disasterDamageProgress);
    if (disasterPct > 0) badges.push({ text: negPctText(disasterPct), title: labels.disaster(disasterPct), icon: IconAlert, color: c.error, bgColor: hexToRgba(c.error, 0.2), slot: "damage" });
    const wearPct = fractionToPercent(plant.wearProgress);
    if (plant.state === PLANT_STATE.Critical || plant.state === PLANT_STATE.Worn) {
        badges.push({ text: pctText(wearPct), title: plant.state === PLANT_STATE.Critical ? labels.wear(wearPct) : labels.wearLow(wearPct), icon: IconGear, color: c.warning, bgColor: hexToRgba(c.warning, plant.state === PLANT_STATE.Critical ? 0.15 : 0.1), slot: "wear" });
    }
    const dmgPct = fractionToPercent(plant.operationalDamageProgress);
    if (dmgPct > 0) badges.push({ text: negPctText(dmgPct), title: labels.hit(plant.operationalHitCount, plant.operationalHitMax, dmgPct), icon: IconTarget, color: c.error, bgColor: hexToRgba(c.error, 0.15), slot: "damage" });
    if (plant.hasExploded && dmgPct === 0) badges.push({ text: labels.exploded, icon: IconLightning, color: c.error, bgColor: hexToRgba(c.error, 0.15), slot: "damage" });
    // A plant knocked out entirely (exploded / disabled by disaster) outputs 0 — a reserve/fuel
    // badge next to EXPLODED would read as "still outputs N%". The persisted factors stay on the
    // C# side; the badges return once the plant is back.
    const isKnockedOut = plant.state === PLANT_STATE.Exploded || plant.state === PLANT_STATE.DisabledByDisaster;
    // Surplus saturation: shown as the OUTPUT share (spinning reserve), not the cut — framed as
    // a normal grid response to low demand, not a penalty. Neutral (muted) color, not warning.
    if (!isKnockedOut && plant.saturationFactor < 1 - SATURATION_DISPLAY_EPS) {
        const outputPct = Math.round(plant.saturationFactor * 100);
        // Inertia hint ("back to full in ~Nh") appended only while an up-ramp is pending
        // (RecoveryHours > 0). Ceil, not round: 0.3h must read "~1h", never "~0h".
        const tip = plant.recoveryHours > 0
            ? labels.saturationTip(outputPct) + labels.saturationRecovery(Math.ceil(plant.recoveryHours))
            : labels.saturationTip(outputPct);
        badges.push({
            text: pctText(outputPct),
            title: tip,
            color: c.textMuted,
            bgColor: hexToRgba(c.textMuted, 0.15),
            icon: IconReserve,
            slot: "output",
        });
    }
    // Fuel stockpile shortfall (thermal plants only; renewables read 1 = full). GATE on the
    // post-sigmoid output factor — the fuel curve's buffer forgives ordinary supply dips
    // (factor stays 1 above BufferThreshold), so gating on the raw stockpile fraction would
    // keep a warning badge on perfectly healthy plants. The TEXT still shows the stockpile %
    // (the number the player can act on via procurement).
    if (!isKnockedOut && plant.fuelFactor < 1 - SATURATION_DISPLAY_EPS) {
        const fuelPct = Math.round(plant.fuelAvailabilityPercent * 100);
        badges.push({
            text: pctText(fuelPct),
            title: labels.fuel(fuelPct),
            color: c.warning,
            bgColor: hexToRgba(c.warning, 0.15),
            icon: IconFuel,
            slot: "fuel",
        });
    }
    // OK lives in the output slot — keeps the right edge of the grid stable on healthy rows
    if (badges.length === 0) badges.push({ text: labels.ok, color: c.success, bgColor: hexToRgba(c.success, 0.1), slot: "output" });
    return badges;
};

