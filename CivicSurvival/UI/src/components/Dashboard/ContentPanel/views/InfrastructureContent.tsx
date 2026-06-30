/**
 * InfrastructureContent - Power Plants + Civilian Buildings with Two-Lane Repair
 * GRID domain → INFRA view
 *
 * Two sections:
 * 1. Power Plants (grid table pattern from PlantsContent)
 * 2. Civilian Buildings (same grid table pattern)
 *
 * Grid pattern: COL widths → headerCell/cell helpers → consistent alignment
 */

import React, { memo, useState, useEffect, useMemo, useRef } from "react";
import { Column } from "@coherent";
import { getPanelStyles, useTheme } from "@themes";
import { bindingDataOrDefault, type AttentionDto, usePowerGrid, useAttention } from "@hooks/domain";
import { HelpSection } from "@shared/common/HelpSection";
import { SectionHeader } from "@shared/ui";
import { usePowerActions, useRequestAction } from "@hooks/actions";
import { useLocale } from "@locales";
import { DEFAULT_ATTENTION_DTO, DEFAULT_POWER_GRID_DTO, type PowerGridDto } from "../../../../types/domainDtos";
import { type CivilianDamageData } from "../../../../types/domainDtos.generated";
import { type EntityRef } from "../../../../types/entityRef";
import { type PlantId, type RepairType } from "../../../../types/semantic";
import { REPAIR_TYPE } from "./infrastructure-content/helpers";
import { CivilianSummary, PowerPlantSummary } from "./infrastructure-content/InfrastructureSummaryCards";
import { CivilianBuildingsTable } from "./infrastructure-content/CivilianBuildingsTable";
import { PowerPlantTable } from "./infrastructure-content/PowerPlantTable";
import { DestroyedGroup, type DestroyedRow } from "./infrastructure-content/DestroyedGroup";

const civBuildingKey = (building: CivilianDamageData["Building"]): string =>
    `${building.Index}:${building.Version}`;

interface InfrastructureContentReadyProps {
    grid: PowerGridDto;
    attention: AttentionDto;
}

const InfrastructureContentReady = memo(({ grid, attention }: InfrastructureContentReadyProps) => {
    const theme = useTheme();
    const panelStyles = getPanelStyles(theme);
    const l = useLocale();
    const powerActions = usePowerActions();
    const [openDropdown, setOpenDropdown] = useState<PlantId | null>(null);
    const [kickbackEnabled, setKickbackEnabled] = useState<Record<number, boolean>>({});
    const [civOpenDropdown, setCivOpenDropdown] = useState<string | null>(null);
    const [civKickbackEnabled, setCivKickbackEnabled] = useState<Record<string, boolean>>({});
    const plantRepairRef = useRef<{ plantId: PlantId; repairType: RepairType } | null>(null);
    const civRepairRef = useRef<{ building: EntityRef; repairType: RepairType } | null>(null);
    const plantRepairAction = useRequestAction(() => {
        const request = plantRepairRef.current;
        if (!request) return false;
        powerActions.repairPlant({ plantId: request.plantId, mode: request.repairType });
        return true;
    }, grid.PlantRepairRequest);
    const civRepairAction = useRequestAction(() => {
        const request = civRepairRef.current;
        if (!request) return false;
        powerActions.repairCivilian({ building: request.building, repairType: request.repairType });
        return true;
    }, grid.CivilianRepairRequest);

    const plants = useMemo(() => Array.isArray(grid.GenerationSources) ? grid.GenerationSources : [], [grid.GenerationSources]);
    const civilianBuildings = useMemo(() => Array.isArray(grid.CivilianDamage) ? grid.CivilianDamage : [], [grid.CivilianDamage]) as CivilianDamageData[];

    // Split on the authoritative IsDestroyed flag (vanilla Destroyed on the building), NOT a
    // damage-percent proxy: a plant burned down at 70%+ is Destroyed while its operational damage
    // is well under 100%, so the old `>= 1` test leaked ruins into the active table where REPAIR
    // resolved as NOT_FOUND. Destroyed plants fold into a collapsed group instead.
    const { activePlants, destroyedPlantRows, destroyedCount, destroyedCapacityMW } = useMemo(() => {
        let dCapacity = 0;
        const active: typeof plants = [];
        const drows: DestroyedRow[] = [];
        for (const p of plants) {
            if (p.IsDestroyed) {
                drows.push({ key: String(p.PlantId), name: p.Name, detail: `${p.CapacityMW} MW` });
                dCapacity += p.CapacityMW;
            } else { active.push(p); }
        }
        return { activePlants: active, destroyedPlantRows: drows, destroyedCount: drows.length, destroyedCapacityMW: dCapacity };
    }, [plants]);

    // Civilian destroyed buildings are NOT listed per-building: they are deleted on destruction
    // (keeping a sidecar per ruin would make the damage-map rebuild grow with cumulative destruction).
    // The destroyed total is shown by the attention-driven DESTROYED summary tile instead.
    useEffect(() => { setOpenDropdown((prev) => { if (prev === null) return null; return activePlants.some((p) => p.PlantId === prev) ? prev : null; }); }, [activePlants]);
    useEffect(() => { setCivOpenDropdown((prev) => { if (prev === null) return null; return civilianBuildings.some((b) => civBuildingKey(b.Building) === prev) ? prev : null; }); }, [civilianBuildings]);

    const totalCapacity = activePlants.reduce((sum, p) => sum + p.CapacityMW, 0);
    const currentOutput = activePlants.reduce((sum, p) => sum + (p.CurrentOutputMW ?? 0), 0);
    const atRiskCount = grid.AtRiskPlantCount;

    const handleRepair = (plantId: PlantId, repairType: RepairType) => {
        plantRepairRef.current = { plantId, repairType };
        plantRepairAction.execute();
    };
    const handleMunicipalRepair = (plantId: PlantId) => { handleRepair(plantId, (kickbackEnabled[plantId] ?? false) ? REPAIR_TYPE.MUNICIPAL_WITH_KICKBACK : REPAIR_TYPE.MUNICIPAL); };
    const toggleKickback = (plantId: PlantId) => { setKickbackEnabled(prev => ({ ...prev, [plantId]: !prev[plantId] })); };
    const handleCivRepair = (building: EntityRef, repairType: RepairType) => {
        civRepairRef.current = { building, repairType };
        civRepairAction.execute();
    };
    const handleCivMunicipalRepair = (building: EntityRef) => {
        const key = `${building.index}:${building.version}`;
        handleCivRepair(building, (civKickbackEnabled[key] ?? false) ? REPAIR_TYPE.MUNICIPAL_WITH_KICKBACK : REPAIR_TYPE.MUNICIPAL);
    };
    const toggleCivKickback = (buildingKey: string) => { setCivKickbackEnabled(prev => ({ ...prev, [buildingKey]: !prev[buildingKey] })); };

    const civDamagedCount = civilianBuildings.filter(b => !b.IsRepairing).length;
    const civRepairingCount = civilianBuildings.filter(b => b.IsRepairing).length;
    const civDestroyedTotal = attention.TotalCivilianBuildingsDestroyed ?? 0;

    // ════════════════════════════════════════════════════════════
    // Tables use shared DataTable columns and action dropdowns.
    // ════════════════════════════════════════════════════════════

    const containerStyle: React.CSSProperties = { padding: theme.spacing.md, height: "100%", overflowY: "auto" };
    const containerChildStyle: React.CSSProperties = { marginBottom: theme.spacing.md };
    const headerStyle: React.CSSProperties = { ...panelStyles.card, padding: theme.spacing.sm };
    const sectionHeading = panelStyles.sectionTitle();
    return (
        <Column style={containerStyle}>
            {/* ══════════ POWER PLANTS ══════════ */}
            {/* No separate "INFRASTRUCTURE" panel title: it duplicates the INFRA tab label
                directly above. The infrastructure help "?" lives on the first section instead. */}
            <SectionHeader
                title={l.t("INFRA_PP_SECTION")}
                titleAs="span"
                titleStyle={sectionHeading}
                help={<HelpSection id="engineering" title={l.t("INFRA_HEADER")}>{l.t("HELP_ENGINEERING")}</HelpSection>}
            />
            <PowerPlantSummary
                activeCount={activePlants.length}
                currentOutput={currentOutput}
                totalCapacity={totalCapacity}
                atRiskCount={atRiskCount}
                destroyedCount={destroyedCount}
                destroyedCapacityMW={destroyedCapacityMW}
                headerStyle={headerStyle}
                childStyle={containerChildStyle}
            />

            <PowerPlantTable
                plants={activePlants}
                openDropdown={openDropdown}
                kickbackEnabled={kickbackEnabled}
                onToggleDropdown={setOpenDropdown}
                onMunicipalRepair={handleMunicipalRepair}
                onRepair={handleRepair}
                onToggleKickback={toggleKickback}
                repairPending={plantRepairAction.isPending}
                repairErrorKey={grid.PlantRepairRequest.Status === "failed" ? grid.PlantRepairRequest.ReasonId : ""}
                municipalRepairHours={grid.PlantMunicipalRepairHours}
                shadowOpsRepairHours={grid.PlantShadowOpsRepairHours}
            />

            <DestroyedGroup rows={destroyedPlantRows} />

            {/* ══════════ CIVILIAN BUILDINGS ══════════ */}
            <div style={{ borderTop: `2rem solid ${theme.colors.border}`, margin: `${theme.spacing.md} 0` }} />
            <div style={sectionHeading}>{l.t("INFRA_CIV_SECTION")}</div>

            <CivilianSummary
                damagedCount={civDamagedCount}
                repairingCount={civRepairingCount}
                destroyedTotal={civDestroyedTotal}
                headerStyle={headerStyle}
                childStyle={containerChildStyle}
            />

            <CivilianBuildingsTable
                buildings={civilianBuildings}
                grid={grid}
                openDropdown={civOpenDropdown}
                kickbackEnabled={civKickbackEnabled}
                onToggleDropdown={setCivOpenDropdown}
                onMunicipalRepair={handleCivMunicipalRepair}
                onRepair={handleCivRepair}
                onToggleKickback={toggleCivKickback}
                repairPending={civRepairAction.isPending}
                repairErrorKey={grid.CivilianRepairRequest.Status === "failed" ? grid.CivilianRepairRequest.ReasonId : ""}
            />
        </Column>
    );
});
InfrastructureContentReady.displayName = "InfrastructureContentReady";

export const InfrastructureContent = memo(() => {
    const grid = usePowerGrid();
    const attention = useAttention();
    const gridData = bindingDataOrDefault(grid, DEFAULT_POWER_GRID_DTO);
    const attentionData = bindingDataOrDefault(attention, DEFAULT_ATTENTION_DTO);

    return (
        <InfrastructureContentReady
            grid={gridData}
            attention={attentionData}
        />
    );
});
InfrastructureContent.displayName = "InfrastructureContent";
