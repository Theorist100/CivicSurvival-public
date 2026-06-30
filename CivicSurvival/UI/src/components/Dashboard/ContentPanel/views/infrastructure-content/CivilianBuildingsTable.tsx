import React, { useMemo } from "react";
import { hexToRgba, useAccents, useTheme } from "@themes";
import { useLocale } from "@locales";
import { DataTable, StatusBadge, type DataTableColumn } from "@shared/ui";
import { type CivilianDamageData } from "../../../../../types/domainDtos.generated";
import { type EntityRef } from "../../../../../types/entityRef";
import { type RepairType } from "../../../../../types/semantic";
import { RepairDropdown, REPAIR_TYPE } from "./RepairDropdown";

const dtoBuildingKey = (building: CivilianDamageData["Building"]): string =>
    `${building.Index}:${building.Version}`;

const dtoBuildingToRef = (building: CivilianDamageData["Building"]): EntityRef => ({
    index: building.Index,
    version: building.Version,
});

/**
 * Display-only grouping of damaged buildings. Damaged rows that share the exact
 * same tier (name + HitCount/MaxHits) collapse into one row with a ×N count;
 * repair still acts on a single representative building via the existing
 * per-building request. Repairing buildings never merge — their RepairHoursLeft
 * differs, so the progress badge would lie if collapsed.
 */
interface CivilianBuildingGroup {
    key: string;
    rep: CivilianDamageData;
    count: number;
}

const groupCivilianBuildings = (buildings: CivilianDamageData[]): CivilianBuildingGroup[] => {
    const groups = new Map<string, CivilianBuildingGroup>();
    for (const bld of buildings) {
        const tierKey = bld.IsRepairing
            ? `R:${dtoBuildingKey(bld.Building)}`
            : `D:${bld.Name}:${bld.HitCount}:${bld.MaxHits}`;
        const existing = groups.get(tierKey);
        if (!existing) {
            groups.set(tierKey, { key: tierKey, rep: bld, count: 1 });
            continue;
        }
        existing.count++;
        // Stable representative: lowest building index, so the open dropdown /
        // kickback toggle stay anchored to the same building across refreshes.
        if (bld.Building.Index < existing.rep.Building.Index) existing.rep = bld;
    }
    return [...groups.values()];
};

interface CivilianGridConfig {
    CivilianMunicipalRepairHours: number;
    CivilianShadowOpsRepairHours: number;
}

interface CivilianBuildingsTableProps {
    buildings: CivilianDamageData[];
    grid: CivilianGridConfig;
    openDropdown: string | null;
    kickbackEnabled: Record<string, boolean>;
    onToggleDropdown: (buildingKey: string | null) => void;
    onMunicipalRepair: (building: EntityRef) => void;
    onRepair: (building: EntityRef, repairType: RepairType) => void;
    onToggleKickback: (buildingKey: string) => void;
    repairPending: boolean;
    repairErrorKey: string;
}

const CIV_COL = { name: "auto", status: "120rem", repair: "100rem" };

export const CivilianBuildingsTable: React.FC<CivilianBuildingsTableProps> = ({
    buildings,
    grid,
    openDropdown,
    kickbackEnabled,
    onToggleDropdown,
    onMunicipalRepair,
    onRepair,
    onToggleKickback,
    repairPending,
    repairErrorKey,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const groups = useMemo(() => groupCivilianBuildings(buildings), [buildings]);

    const columns: DataTableColumn<CivilianBuildingGroup>[] = [
        {
            id: "name",
            label: l.t("INFRA_CIV_COL_NAME"),
            width: CIV_COL.name,
            align: "left",
            cellStyle: { fontWeight: 600, color: theme.colors.textPrimary },
            render: ({ rep, count }) => (
                <>
                    {rep.IsRepairing ? "~ " : ""}{rep.Name}
                    {count > 1 && (
                        <span style={{ marginLeft: "6rem", color: theme.colors.textMuted, fontWeight: 700 }}>
                            ×{count}
                        </span>
                    )}
                </>
            ),
        },
        {
            id: "status",
            label: l.t("INFRA_CIV_COL_STATUS"),
            width: CIV_COL.status,
            align: "right",
            render: ({ rep }) => rep.IsRepairing ? (
                <StatusBadge color={accents.operations.accent} bgColor={hexToRgba(accents.operations.accent, 0.15)}>
                    {l.t("INFRA_CIV_BADGE_REPAIRING", rep.RepairHoursLeft.toFixed(1))}
                </StatusBadge>
            ) : (
                <StatusBadge color={accents.crisis.accent} bgColor={hexToRgba(accents.crisis.accent, 0.15)}>
                    {l.t("INFRA_CIV_BADGE_HITS", rep.HitCount, rep.MaxHits)}
                </StatusBadge>
            ),
        },
        {
            id: "repair",
            label: l.t("INFRA_CIV_COL_REPAIR"),
            width: CIV_COL.repair,
            align: "center",
            cellStyle: { position: "relative" },
            render: ({ rep }) => {
                const key = dtoBuildingKey(rep.Building);
                const isKickback = kickbackEnabled[key] ?? false;

                if (rep.IsRepairing) {
                    return <span style={{ fontSize: "10rem", color: accents.operations.accent }}>{l.t("INFRA_CIV_REPAIR_PROGRESS")}</span>;
                }

                return (
                    <RepairDropdown
                        open={openDropdown === key}
                        onOpenChange={(nextOpen) => onToggleDropdown(nextOpen ? key : null)}
                        isKickbackEnabled={isKickback}
                        municipal={{
                            canRun: rep.CanMunicipalRepair,
                            lockedReasonId: rep.MunicipalRepairLockedReasonId,
                            cost: rep.MunicipalRepairCharge,
                        }}
                        municipalKickback={{
                            canRun: rep.CanKickbackRepair,
                            lockedReasonId: rep.KickbackRepairLockedReasonId,
                            cost: rep.MunicipalKickbackRepairCharge,
                        }}
                        shadow={{
                            canRun: rep.CanShadowRepair,
                            lockedReasonId: rep.ShadowRepairLockedReasonId,
                            cost: rep.ShadowOpsRepairCharge,
                        }}
                        kickbackAmount={rep.KickbackRepairAmount}
                        onMunicipalRepair={() => onMunicipalRepair(dtoBuildingToRef(rep.Building))}
                        onShadowRepair={() => onRepair(dtoBuildingToRef(rep.Building), REPAIR_TYPE.SHADOW_OPS)}
                        onToggleKickback={() => onToggleKickback(key)}
                        isPending={repairPending}
                        errorText={repairErrorKey ? l.tDynamic(repairErrorKey) : ""}
                        labels={{
                            repairBtn: l.t("INFRA_CIV_REPAIR_BTN"),
                            municipal: l.t("INFRA_CIV_MUNICIPAL"),
                            municipalNote: l.t("INFRA_CIV_MUNICIPAL_NOTE"),
                            shadowOps: l.t("INFRA_CIV_SHADOW_OPS"),
                            shadowNote: l.t("INFRA_CIV_SHADOW_NOTE"),
                            kickbackLabel: (amount) => l.t("INFRA_CIV_KICKBACK_LABEL", amount),
                            municipalDuration: `${grid.CivilianMunicipalRepairHours}h`,
                            shadowDuration: `${grid.CivilianShadowOpsRepairHours}h`,
                            insufficientFundsFallback: "UI_INSUFFICIENT_FUNDS",
                            shadowInsufficientFallback: "INFRA_CIV_SHADOW_INSUFFICIENT",
                        }}
                    />
                );
            },
        },
    ];

    return (
        <DataTable
            columns={columns}
            rows={groups}
            getKey={(group) => group.key}
            empty={l.t("INFRA_CIV_EMPTY")}
            compact
            style={{ overflow: "visible" }}
            rowStyle={({ rep }) => {
                const isRepairing = rep.IsRepairing;
                const rowBg = isRepairing ? hexToRgba(accents.operations.accent, 0.06) : hexToRgba(accents.crisis.accent, 0.04 + rep.DamagePercent * 0.06);
                const rowBorder = isRepairing ? `2rem solid ${theme.colors.border}` : `2rem solid ${hexToRgba(accents.crisis.accent, 0.3 + rep.DamagePercent * 0.3)}`;
                return { background: rowBg, borderBottom: rowBorder };
            }}
        />
    );
};
