/**
 * Enriched debug panel data.
 *
 * Keeps repeated severity/color/display derivations out of debug tab JSX while
 * leaving useDebugState as the low-level binding hook.
 */

import { useMemo } from "react";
import { useAccents, useTheme } from "../../themes";
import { useDurationFormatter } from "../format";
import { useDebugState } from "./useDebugState";

function thresholdColor(
    value: number,
    low: number,
    high: number,
    successColor: string,
    warningColor: string,
    errorColor: string,
): string {
    if (value < low) return successColor;
    if (value < high) return warningColor;
    return errorColor;
}

function balanceColor(
    balance: number,
    successColor: string,
    warningColor: string,
    errorColor: string,
): string {
    if (balance > 50) return successColor;
    if (balance > 0) return warningColor;
    return errorColor;
}

function mwShare(value: number, total: number): string {
    const percent = total > 0 ? ((value / total) * 100).toFixed(0) : "0";
    return `${value} MW (${percent}%)`;
}

export function useDebugData() {
    const raw = useDebugState();
    const theme = useTheme();
    const accents = useAccents();
    const format = useDurationFormatter();

    return useMemo(() => {
        const successColor = theme.colors.success;
        const warningColor = accents.resilience.accent;
        const errorColor = accents.crisis.accent;
        const totalCityMW =
            raw.residentialMW + raw.commercialMW + raw.industrialMW + raw.officeMW;
        const isUnderAttack = raw.scWaveInProgress !== 0;
        const isGridCollapsed = raw.scGridCollapsed !== 0;
        const isExodusActive = raw.scExodusActive !== 0;
        const isTelemarathonActive = raw.scTelemarathonActive !== 0;

        return {
            status: raw.status,
            raw,
            colors: {
                success: successColor,
                warning: warningColor,
                error: errorColor,
            },
            severityDisplay: format.decimal(raw.severityScore, 1),
            severityColor: thresholdColor(raw.severityScore, 20, 50, successColor, warningColor, errorColor),
            powerBalanceDisplay: `${raw.powerBalance} MW`,
            powerBalanceColor: balanceColor(raw.powerBalance, successColor, warningColor, errorColor),
            productionConsumptionDisplay: `${raw.production} / ${raw.consumption}`,
            blackoutedMWDisplay: `${raw.blackoutedMW} MW`,
            blackoutedMWColor: thresholdColor(raw.blackoutedMW, 1, 50, successColor, warningColor, errorColor),
            blackoutPercentDisplay: format.percent(raw.blackoutPercent, 1),
            blackoutPercentColor: thresholdColor(raw.blackoutPercent, 10, 30, successColor, warningColor, errorColor),
            happinessPenaltyDisplay: `-${format.percent(raw.happinessPenalty, 0, "ratio")}`,
            happinessPenaltyColor: thresholdColor(raw.happinessPenalty * 100, 10, 25, successColor, warningColor, errorColor),
            commercePenaltyDisplay: `-${format.percent(raw.commercePenalty, 0, "ratio")}`,
            commercePenaltyColor: thresholdColor(raw.commercePenalty * 100, 10, 20, successColor, warningColor, errorColor),
            affectedDistrictsDisplay: `${raw.affectedDistricts}/${raw.totalDistricts}`,
            affectedDistrictsColor: thresholdColor(raw.affectedDistricts, 1, 3, successColor, warningColor, errorColor),
            cityComposition: {
                totalMW: totalCityMW,
                residentialDisplay: mwShare(raw.residentialMW, totalCityMW),
                commercialDisplay: mwShare(raw.commercialMW, totalCityMW),
                industrialDisplay: mwShare(raw.industrialMW, totalCityMW),
                officeDisplay: mwShare(raw.officeMW, totalCityMW),
            },
            buildingsInBlackoutColor: thresholdColor(raw.buildingsInBlackout, 10, 50, successColor, warningColor, errorColor),
            blackoutDurationDisplay: `${format.decimal(raw.blackoutDuration)} min`,
            blackoutDurationColor: thresholdColor(raw.blackoutDuration, 30, 120, successColor, warningColor, errorColor),
            gridWarfareDisplay: raw.gridWarfareUnlocked ? "UNLOCKED" : "LOCKED",
            gridWarfareColor: raw.gridWarfareUnlocked ? successColor : errorColor,
            currentActColor: raw.gridWarfareUnlocked ? successColor : warningColor,
            shadowBalanceDisplay: `$${raw.shadowBalance.toLocaleString()}`,
            shadowDailyIncomeDisplay: `+$${raw.shadowDailyIncome.toLocaleString()}/day`,
            scenario: {
                waveDisplay: `#${raw.scWaveNumber} (${raw.scWavePhaseName})`,
                underAttackDisplay: isUnderAttack ? "YES" : "NO",
                underAttackColor: isUnderAttack ? errorColor : successColor,
                stanceDisplay: `${raw.scEnemyStanceName} (${raw.scStancePhaseName})`,
                enemyPressureDisplay: format.percent(raw.scEnemyPressure),
                enemyPressureColor: thresholdColor(raw.scEnemyPressure, 40, 70, successColor, warningColor, errorColor),
                aaAmmoColor: raw.scAaAmmo > 0 ? successColor : errorColor,
                gridCollapsedDisplay: isGridCollapsed ? "YES" : "NO",
                gridCollapsedColor: isGridCollapsed ? errorColor : successColor,
                gridStressDisplay: format.hours(raw.scGridStressHours, 1),
                gridZoneColor: raw.scGridZone >= 3 ? errorColor : raw.scGridZone >= 2 ? warningColor : successColor,
                gridRecoveryDisplay: format.hours(raw.scGridRecoveryHours, 1),
                shockDisplay: `${format.percent(raw.scShockLevel, 1)} (${raw.scShockTierName})`,
                shockColor: thresholdColor(raw.scShockLevel, 30, 60, successColor, warningColor, errorColor),
                exodusDisplay: `${isExodusActive ? "ACTIVE" : "inactive"} (${raw.scTotalFled} fled, ${raw.scExodusRatePercentPerDay.toFixed(1)}%/d)`,
                exodusColor: isExodusActive ? errorColor : successColor,
                infectionRateDisplay: `${format.percent(raw.scInfectionRate, 1, "ratio")}/h`,
                infectionRateColor: raw.scInfectionRate > 0 ? warningColor : successColor,
                cityIntegrityDisplay: format.percent(raw.scCityIntegrity, 0, "ratio"),
                mediaTrustDisplay: format.percent(raw.scMediaTrust, 0, "ratio"),
                mediaTrustColor: thresholdColor(100 - raw.scMediaTrust * 100, 30, 60, successColor, warningColor, errorColor),
                telemarathonDisplay: isTelemarathonActive ? "ON" : "OFF",
                telemarathonColor: isTelemarathonActive ? successColor : theme.colors.textMuted,
                trustDisplay: format.decimal(raw.scTrustLevel),
                trustColor: thresholdColor(100 - raw.scTrustLevel, 30, 60, successColor, warningColor, errorColor),
                corruptionScoreDisplay: format.decimal(raw.scCorruptionScore, 1),
                corruptionHeatDisplay: format.decimal(raw.scCorruptionHeat, 1),
                corruptionHeatColor: thresholdColor(raw.scCorruptionHeat, 30, 60, successColor, warningColor, errorColor),
            },
        };
    }, [raw, theme, accents, format]);
}
