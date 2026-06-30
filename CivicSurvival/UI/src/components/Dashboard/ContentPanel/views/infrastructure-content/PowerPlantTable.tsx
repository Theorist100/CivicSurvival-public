import React from "react";
import { t } from "@coherent";
import { hexToRgba, useAccents, useTheme } from "@themes";
import { useLocale } from "@locales";
import { DataTable, StatusBadge, type DataTableColumn } from "@shared/ui";
import { HoverTip } from "@shared/common/HoverTip";
import { type PlantWearData } from "../../../../../types/domainDtos.generated";
import { type PlantId, type RepairType } from "../../../../../types/semantic";
import { asProgressFraction } from "../../../../../types/branded";
import {
    getStatusBadges,
    getWearLevelFromState,
    type BadgeLabels,
    type BadgeSlot,
    type BadgeTheme,
    type StatusBadge as StatusBadgeData,
    type WearLevel,
} from "./helpers";
import { RepairDropdown, REPAIR_TYPE } from "./RepairDropdown";

export type PlantRowData = PlantWearData;

interface PowerPlantTableProps {
    plants: PlantRowData[];
    openDropdown: PlantId | null;
    kickbackEnabled: Record<number, boolean>;
    onToggleDropdown: (plantId: PlantId | null) => void;
    onMunicipalRepair: (plantId: PlantId) => void;
    onRepair: (plantId: PlantId, repairType: RepairType) => void;
    onToggleKickback: (plantId: PlantId) => void;
    repairPending: boolean;
    repairErrorKey: string;
    municipalRepairHours: number;
    shadowOpsRepairHours: number;
}

const PP_COL = { name: "auto", capacity: "80rem", status: "320rem", repair: "100rem" };

// Fixed sub-columns inside STATUS — the same badge type always lands on the same
// vertical across rows (wear · damage · fuel · output, output anchored next to REPAIR).
// Chips are centered in their slot, so a chip wider than the slot spills into the
// neighbour and the borders touch. The slot must therefore fit the WORST-CASE chip:
// icon (12) + gap (4) + "-100%" (~35) + padding (12) + border (4) + margins (4) ≈ 71rem.
// 80rem keeps a visible gap even between two worst-case neighbours.
const BADGE_SLOTS: readonly BadgeSlot[] = ["wear", "damage", "fuel", "output"];
const SLOT_WIDTH = "80rem";
const slotStyle: React.CSSProperties = { width: SLOT_WIDTH, display: "flex", justifyContent: "center", flexShrink: 0 };

export const PowerPlantTable: React.FC<PowerPlantTableProps> = ({
    plants,
    openDropdown,
    kickbackEnabled,
    onToggleDropdown,
    onMunicipalRepair,
    onRepair,
    onToggleKickback,
    repairPending,
    repairErrorKey,
    municipalRepairHours,
    shadowOpsRepairHours,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const badgeTheme: BadgeTheme = {
        success: theme.colors.success,
        operations: accents.operations.accent,
        warning: theme.colors.warning,
        error: theme.colors.error,
        textMuted: theme.colors.textMuted,
    };
    const badgeLabels: BadgeLabels = {
        building: (days: number) => l.t("PLANTS_BADGE_BUILDING", days),
        buildingShort: (days: number) => l.t("PLANTS_BADGE_BUILDING_SHORT", days),
        repair: (hours: string) => l.t("PLANTS_BADGE_REPAIR", hours),
        repairShort: (hours: string) => l.t("PLANTS_BADGE_REPAIR_SHORT", hours),
        wear: (pct: number) => l.t("PLANTS_BADGE_WEAR", pct),
        wearLow: (pct: number) => l.t("PLANTS_BADGE_WEAR_LOW", pct),
        hit: (hits: number, maxHits: number, pct: number) => l.t("PLANTS_BADGE_HIT", hits, maxHits, pct),
        disaster: (pct: number) => l.t("PLANTS_BADGE_DISASTER", pct),
        exploded: l.t("PLANTS_BADGE_EXPLODED"),
        ok: l.t("PLANTS_BADGE_OK"),
        saturationTip: (pct: number) => l.t("PLANTS_BADGE_SATURATION_TIP", pct),
        saturationRecovery: (hours: number) => l.t("PLANTS_BADGE_SATURATION_RECOVERY", hours),
        fuel: (pct: number) => l.t("PLANTS_BADGE_FUEL", pct),
    };
    // Thin separators; accent color is reserved for rows in real trouble — wear-warning rows
    // keep a neutral line (the amber chip already carries the signal), otherwise a fleet with
    // ordinary wear turns into a wall of orange.
    const plantRowTheme: Record<WearLevel, { bg: string; border: string }> = {
        disaster: { bg: hexToRgba(accents.crisis.accent, 0.1), border: `1rem solid ${accents.crisis.accent}` },
        critical: { bg: hexToRgba(accents.crisis.accent, 0.08), border: `1rem solid ${accents.crisis.accent}` },
        damaged: { bg: hexToRgba(accents.crisis.accent, 0.06), border: `1rem solid ${accents.crisis.accent}` },
        warning: { bg: hexToRgba(accents.resilience.accent, 0.08), border: `1rem solid ${theme.colors.border}` },
        repairing: { bg: hexToRgba(accents.operations.accent, 0.06), border: `1rem solid ${theme.colors.border}` },
        construction: { bg: hexToRgba(accents.schemes.accent, 0.06), border: `1rem solid ${accents.schemes.accent}` },
        normal: { bg: theme.colors.paper, border: `1rem solid ${theme.colors.border}` },
    };
    const ppRowStyle = (level: WearLevel): React.CSSProperties => ({
        display: "flex",
        alignItems: "center",
        background: plantRowTheme[level].bg,
        borderBottom: plantRowTheme[level].border,
    });

    const columns: DataTableColumn<PlantRowData>[] = [
        {
            id: "name",
            label: l.t("PLANTS_COL_NAME"),
            width: PP_COL.name,
            align: "left",
            cellStyle: { fontWeight: 600, color: theme.colors.textPrimary, fontSize: "13rem" },
            render: (plant) => (
                <>{plant.IsRepairing && "~ "}{plant.Name}</>
            ),
        },
        {
            id: "capacity",
            label: l.t("PLANTS_COL_CAPACITY"),
            width: PP_COL.capacity,
            align: "right",
            cellStyle: { color: theme.colors.textMuted, fontFamily: theme.typography.fontFamilyMono },
            render: (plant) => t`${plant.CapacityMW} MW`,
        },
        {
            id: "status",
            label: (
                <span style={{ display: "flex" }}>
                    {/* Abbreviated slot headers carry the mechanic primers as tooltips */}
                    <HoverTip text={l.t("PLANTS_SLOT_WEAR_TIP")} style={{ ...slotStyle, justifyContent: "center" }}>
                        {l.t("PLANTS_SLOT_WEAR")}
                    </HoverTip>
                    <HoverTip text={l.t("PLANTS_SLOT_DAMAGE_TIP")} style={{ ...slotStyle, justifyContent: "center" }}>
                        {l.t("PLANTS_SLOT_DAMAGE")}
                    </HoverTip>
                    <HoverTip text={l.t("PLANTS_SLOT_FUEL_TIP")} style={{ ...slotStyle, justifyContent: "center" }}>
                        {l.t("PLANTS_SLOT_FUEL")}
                    </HoverTip>
                    <HoverTip text={l.t("PLANTS_SLOT_OUTPUT_TIP")} style={{ ...slotStyle, justifyContent: "center" }}>
                        {l.t("PLANTS_SLOT_OUTPUT")}
                    </HoverTip>
                </span>
            ),
            width: PP_COL.status,
            align: "right",
            render: (plant) => {
                const badges = getStatusBadges({
                    state: plant.State,
                    wearProgress: plant.WearPercent,
                    repairHoursLeft: plant.RepairHoursLeft,
                    constructionDaysLeft: plant.ConstructionDaysLeft,
                    operationalDamageProgress: asProgressFraction(plant.OperationalDamagePercent ?? 0),
                    operationalHitCount: plant.OperationalHitCount,
                    operationalHitMax: plant.OperationalHitMax,
                    disasterDamageProgress: asProgressFraction(plant.DisasterDamagePercent ?? 0),
                    hasExploded: plant.HasExploded,
                    saturationFactor: plant.SaturationFactor ?? 1,
                    fuelAvailabilityPercent: plant.FuelAvailabilityPercent ?? 1,
                    fuelFactor: plant.FuelFactor ?? 1,
                    recoveryHours: plant.RecoveryHours ?? 0,
                }, badgeTheme, badgeLabels);
                const chip = (b: StatusBadgeData, key: React.Key) => (
                    <StatusBadge key={key} color={b.color} bgColor={b.bgColor} title={b.title} style={{ margin: "0 2rem" }}>
                        {/* Coherent has no flex gap — spacing via margin (see Flex.tsx);
                            fontSize sizes the cs2 Icon (it scales with em) */}
                        {b.icon && <span style={{ display: "flex", marginRight: "4rem", fontSize: "12rem" }}><b.icon /></span>}{b.text}
                    </StatusBadge>
                );
                // Construction/repair are exclusive states; their chip rides the OUTPUT slot
                // so every row's rightmost chip shares the same vertical (otherwise the
                // column's right-align flushes it past the slot grid and the rail wobbles)
                const stateBadge = badges.find((b) => b.slot === "state");
                return BADGE_SLOTS.map((slot) => (
                    <span key={slot} style={slotStyle}>
                        {stateBadge
                            ? (slot === "output" ? chip(stateBadge, "state") : null)
                            : badges.filter((b) => b.slot === slot).map((b, i) => chip(b, i))}
                    </span>
                ));
            },
        },
        {
            id: "repair",
            label: l.t("PLANTS_COL_REPAIR"),
            width: PP_COL.repair,
            align: "center",
            cellStyle: { position: "relative" },
            render: (plant) => {
                const showRepair = plant.IsRepairable;
                const isKickbackEnabled = kickbackEnabled[plant.PlantId] ?? false;

                if (plant.IsRepairing) {
                    return <span style={{ fontSize: "10rem", color: accents.operations.accent }}>{l.t("PLANTS_REPAIR_PROGRESS")}</span>;
                }

                if (showRepair) {
                    return (
                        <RepairDropdown
                            open={openDropdown === plant.PlantId}
                            onOpenChange={(nextOpen) => onToggleDropdown(nextOpen ? plant.PlantId : null)}
                            isKickbackEnabled={isKickbackEnabled}
                            municipal={{
                                canRun: plant.CanMunicipalRepair,
                                lockedReasonId: plant.MunicipalRepairLockedReasonId,
                                cost: plant.MunicipalRepairCharge,
                            }}
                            municipalKickback={{
                                canRun: plant.CanKickbackRepair,
                                lockedReasonId: plant.KickbackRepairLockedReasonId,
                                cost: plant.MunicipalKickbackRepairCharge,
                            }}
                            shadow={{
                                canRun: plant.CanShadowRepair,
                                lockedReasonId: plant.ShadowRepairLockedReasonId,
                                cost: plant.ShadowOpsRepairCharge,
                            }}
                            kickbackAmount={plant.KickbackRepairAmount}
                            onMunicipalRepair={() => onMunicipalRepair(plant.PlantId)}
                            onShadowRepair={() => onRepair(plant.PlantId, REPAIR_TYPE.SHADOW_OPS)}
                            onToggleKickback={() => onToggleKickback(plant.PlantId)}
                            isPending={repairPending}
                            errorText={repairErrorKey ? l.tDynamic(repairErrorKey) : ""}
                            labels={{
                                repairBtn: l.t("PLANTS_REPAIR_BTN"),
                                municipal: l.t("PLANTS_MUNICIPAL"),
                                municipalNote: l.t("PLANTS_MUNICIPAL_NOTE"),
                                shadowOps: l.t("PLANTS_SHADOW_OPS"),
                                shadowNote: l.t("PLANTS_SHADOW_NOTE"),
                                kickbackLabel: (amount) => l.t("PLANTS_KICKBACK_LABEL", amount),
                                municipalDuration: `${municipalRepairHours}h`,
                                shadowDuration: `${shadowOpsRepairHours}h`,
                                insufficientFundsFallback: "UI_INSUFFICIENT_FUNDS",
                                shadowInsufficientFallback: "PLANTS_SHADOW_INSUFFICIENT",
                            }}
                        />
                    );
                }

                return null;
            },
        },
    ];

    return (
        <DataTable
            columns={columns}
            rows={plants}
            getKey={(plant) => plant.PlantId}
            empty={l.t("PLANTS_EMPTY")}
            style={{ overflow: "visible" }}
            rowStyle={(plant) => {
                // Operational (missile) damage has no PlantState of its own, so a missile-hit
                // but otherwise-intact plant resolves to "normal". Promote it to "damaged" so the
                // row reads as damaged — symmetric with the civilian table.
                let level = getWearLevelFromState(plant.State);
                if (level === "normal" && (plant.OperationalDamagePercent ?? 0) > 0) level = "damaged";
                return ppRowStyle(level);
            }}
        />
    );
};
