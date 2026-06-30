/**
 * BackupReservesSection - Backup Reserves display
 * Shows: Battery, Generators, Protected, At risk, Discharge Policy
 *
 * Extracted from InfoSection to place in right panel (GridOpsSection)
 */

import React, { useCallback, useMemo, useRef } from "react";
import { HoverTip } from "../../shared/common/HoverTip";
import { Column, Row, t } from "../../coherent";
import { useTheme, useAccents } from "../../../themes";
import { bindingDataOrDefault, useBackupPower, usePowerGrid } from "@hooks/domain";
import { usePowerActions, useRequestAction } from "@hooks/actions";
import { createStyles } from "./SectionStyles";
import { IconAlert } from "../../shared/common/Icons";
import { HelpSection } from "../../shared/common/HelpSection";
import { SectionHeader } from "../../shared/ui";
import { useLocale } from "../../../locales";
import { isBackupPolicy, type BackupPolicy } from "../../../types/semantic";
import { DEFAULT_BACKUP_POWER_DTO, DEFAULT_POWER_GRID_DTO } from "../../../types/domainDtos";
import { DistrictModernizationSection } from "./DistrictModernizationSection";

const BACKUP_POLICY_OPTIONS: Array<{ value: BackupPolicy; labelKey: "UI_BACKUP_POLICY_RESERVE" | "UI_BACKUP_POLICY_CRITICAL" | "UI_BACKUP_POLICY_FULL"; descKey: "UI_BACKUP_POLICY_COLD_START" | "UI_BACKUP_POLICY_HOSP_SCHOOLS" | "UI_BACKUP_POLICY_ALL_LAYERS" }> = [
    { value: 0, labelKey: "UI_BACKUP_POLICY_RESERVE", descKey: "UI_BACKUP_POLICY_COLD_START" },
    { value: 1, labelKey: "UI_BACKUP_POLICY_CRITICAL", descKey: "UI_BACKUP_POLICY_HOSP_SCHOOLS" },
    { value: 2, labelKey: "UI_BACKUP_POLICY_FULL", descKey: "UI_BACKUP_POLICY_ALL_LAYERS" },
];

export const BackupReservesSection: React.FC = () => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createStyles(theme), [theme]);
    const backupState = useBackupPower();
    const gridState = usePowerGrid();
    const powerActions = usePowerActions();
    const pendingPolicyRef = useRef<BackupPolicy>(0);
    const backup = bindingDataOrDefault(backupState, DEFAULT_BACKUP_POWER_DTO);
    const grid = bindingDataOrDefault(gridState, DEFAULT_POWER_GRID_DTO);
    const policyAction = useRequestAction(() => {
        powerActions.setBackupPolicy(pendingPolicyRef.current);
        return true;
    }, backup?.BackupPolicyRequest);

    const handlePolicyClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const value = Number(e.currentTarget.dataset.value);
        if (!isBackupPolicy(value)) return;
        pendingPolicyRef.current = value;
        policyAction.execute();
    }, [policyAction]);

    // ========================================================================
    // DERIVED VALUES
    // ========================================================================

    const chargePercent = Math.max(0, Math.min(100, backup.BackupCharge ?? 0));
    const generatorsRunning = backup.GeneratorsRunning ?? 0;
    const noiseLevel = backup.NoiseLevel ?? 0;
    const protectedBuildings = backup.ProtectedBuildings ?? 0;
    const backupPolicy = backup.BackupPolicy ?? 0;
    const hospitalsPowered = backup.HospitalsPowered ?? 0;
    const hospitalsTotal = backup.HospitalsTotal ?? 0;
    const schoolsPowered = backup.SchoolsPowered ?? 0;
    const schoolsTotal = backup.SchoolsTotal ?? 0;
    const policyLocked = backup.CanSetBackupPolicy === false;
    const policyLockedReason = backup.SetBackupPolicyLockedReasonId ? l.tDynamic(backup.SetBackupPolicyLockedReasonId) : "";
    const policyPending = policyAction.isPending || backup.BackupPolicyRequest.Status === "pending";
    const policyDisabled = policyPending || policyLocked;
    const policyError = backup.BackupPolicyRequest.Status === "failed" && backup.BackupPolicyRequest.ReasonId
        ? l.tDynamic(backup.BackupPolicyRequest.ReasonId)
        : policyLockedReason || "";

    const generationSources = Array.isArray(grid.GenerationSources) ? grid.GenerationSources : [];
    const plantsAtRisk = grid.AtRiskPlantCount;
    const plantsBuilding = generationSources.filter(
        (src) => src.IsUnderConstruction
    ).length;

    // ========================================================================
    // COLORS
    // ========================================================================

    const batteryColor = chargePercent < 20 ? accents.crisis.accent
        : chargePercent < 50 ? accents.resilience.accent
        : accents.schemes.accent;

    const coverageColor = (powered: number, total: number) =>
        total === 0 ? theme.colors.textMuted
        : powered === total ? accents.schemes.accent
        : powered > 0 ? accents.resilience.accent
        : accents.crisis.accent;

    // ========================================================================
    // RENDER
    // ========================================================================

    return (
        <div>
            <SectionHeader
                title={l.t("UI_BACKUP_TITLE")}
                titleStyle={s.sectionTitle}
                help={<HelpSection id="backup" title={l.t("UI_BACKUP_TITLE")}>{l.t("HELP_BACKUP")}</HelpSection>}
            />
            <Column gap={theme.spacing.xs} style={s.backup.container}>
                {/* Battery */}
                <Row justify="space-between" align="center">
                    <span style={s.backup.label}>{l.t("UI_BACKUP_BATTERY")}</span>
                    <div style={s.backup.batteryContainer}>
                        <div style={s.backup.batteryBar}>
                            <div style={s.backup.batteryFill(chargePercent, batteryColor)} />
                        </div>
                        <HoverTip text={l.t("TIP_BACKUP_BATTERY")} style={s.backup.value(batteryColor)}>
                            {chargePercent}%
                        </HoverTip>
                    </div>
                </Row>

                {/* Generators */}
                <Row justify="space-between" align="center">
                    <span style={s.backup.label}>{l.t("UI_BACKUP_GENERATORS")}</span>
                    <HoverTip text={l.t("TIP_GENERATORS")} style={s.backup.value(generatorsRunning > 0 ? accents.resilience.accent : theme.colors.textMuted)}>
                        {t`${generatorsRunning} running`}
                    </HoverTip>
                </Row>

                {/* Noise Level - only show when generators running */}
                {generatorsRunning > 0 && (
                    <Row justify="space-between" align="center">
                        <span style={s.backup.label}>{l.t("UI_BACKUP_NOISE")}</span>
                        <HoverTip text={l.t("TIP_NOISE")} style={s.backup.value(noiseLevel > 50 ? accents.crisis.accent : theme.colors.textSecondary)}>
                            {t`${noiseLevel} dB`}
                        </HoverTip>
                    </Row>
                )}

                {/* Protected */}
                <Row justify="space-between" align="center">
                    <span style={s.backup.label}>{l.t("UI_BACKUP_PROTECTED")}</span>
                    <HoverTip text={l.t("TIP_PROTECTED")} style={s.backup.value(theme.colors.textPrimary)}>
                        {t`${protectedBuildings} buildings`}
                    </HoverTip>
                </Row>

                {/* Hospitals coverage */}
                {hospitalsTotal > 0 && (
                    <Row justify="space-between" align="center">
                        <span style={s.backup.label}>{l.t("UI_BACKUP_HOSPITALS")}</span>
                        <HoverTip text={l.t("TIP_HOSPITALS")} style={s.backup.value(coverageColor(hospitalsPowered, hospitalsTotal))}>
                            {hospitalsPowered}/{hospitalsTotal}
                        </HoverTip>
                    </Row>
                )}

                {/* Schools coverage */}
                {schoolsTotal > 0 && (
                    <Row justify="space-between" align="center">
                        <span style={s.backup.label}>{l.t("UI_BACKUP_SCHOOLS")}</span>
                        <HoverTip text={l.t("TIP_SCHOOLS")} style={s.backup.value(coverageColor(schoolsPowered, schoolsTotal))}>
                            {schoolsPowered}/{schoolsTotal}
                        </HoverTip>
                    </Row>
                )}

                {/* Plants at risk */}
                <Row justify="space-between" align="center">
                    <span style={s.backup.label}>{l.t("UI_BACKUP_AT_RISK")}</span>
                    <HoverTip text={l.t("TIP_PLANTS_AT_RISK")} style={s.backup.value(plantsAtRisk > 0 ? accents.crisis.accent : theme.colors.textMuted)}>
                        {plantsAtRisk > 0 ? <><IconAlert /> {plantsAtRisk}</> : "0"}
                    </HoverTip>
                </Row>

                {/* Plants under construction */}
                {plantsBuilding > 0 && (
                    <Row justify="space-between" align="center">
                        <span style={s.backup.label}>{l.t("UI_BACKUP_BUILDING")}</span>
                        <HoverTip text={l.t("TIP_CONSTRUCTION_DELAY")} style={s.backup.value(accents.schemes.accent)}>
                            {plantsBuilding}
                        </HoverTip>
                    </Row>
                )}

                {/* Discharge Policy selector */}
                <div style={{ marginTop: theme.spacing.sm }}>
                    <div style={{
                        fontSize: theme.typography.sizeSM,
                        color: theme.colors.textSecondary,
                        marginBottom: theme.spacing.xs
                    }}>
                        {l.t("UI_BACKUP_DISCHARGE_POLICY")}
                    </div>
                    <Row gap={theme.spacing.xs}>
                        {BACKUP_POLICY_OPTIONS.map((opt) => (
                            <button
                                key={opt.value}
                                style={{
                                    flex: 1,
                                    padding: theme.spacing.xs,
                                    background: backupPolicy === opt.value
                                        ? accents.resilience.accent
                                        : theme.colors.paper,
                                    border: `2rem solid ${accents.resilience.accent}`,
                                    borderRadius: theme.layout.borderRadius,
                                    color: backupPolicy === opt.value
                                        ? theme.colors.white
                                        : theme.colors.textPrimary,
                                    fontSize: theme.typography.sizeXS,
                                    cursor: policyDisabled ? "not-allowed" : "pointer",
                                    opacity: policyDisabled ? 0.65 : 1,
                                    textAlign: "center" as const,
                                }}
                                data-value={opt.value}
                                disabled={policyDisabled}
                                onClick={!policyDisabled ? handlePolicyClick : undefined}
                            >
                                <div style={{ fontWeight: 700 }}>{policyPending ? l.t("UI_PROCESSING") : l.t(opt.labelKey)}</div>
                                <div style={{ opacity: 0.7, fontSize: "10rem" }}>{l.t(opt.descKey)}</div>
                            </button>
                        ))}
                    </Row>
                    {policyError && (
                        <div style={{
                            marginTop: theme.spacing.xs,
                            color: accents.crisis.accent,
                            fontSize: theme.typography.sizeXS,
                            fontWeight: 700,
                        }}>
                            {policyError}
                        </div>
                    )}
                </div>
            </Column>
            <DistrictModernizationSection />
        </div>
    );
};
