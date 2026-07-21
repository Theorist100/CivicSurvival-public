import React, { memo } from "react";
import { HoverTip } from "../../shared/common/HoverTip";
import { useTheme, useAccents } from "../../../themes";
import { type createGlobalStatusStyles } from "./GlobalStatus.styles";
import { useNumberBinding, useBooleanBinding } from "../../../hooks/useSafeBinding";
import { bindingDataOrDefault, isBindingLive, usePowerGrid, useThreat, useBackupPower } from "@hooks/domain";
import { combineBindingStates } from "../../../hooks/domain/combineBindingStates";
import {
    shockActActive$,
    taxMultiplier$,
    loansAvailable$,
    crisisDayNumber$,
} from "../../../hooks/bindings/shockActBindings";
import { useLocale } from "../../../locales";
import { DEFAULT_POWER_GRID_DTO, DEFAULT_BACKUP_POWER_DTO, DEFAULT_THREAT_DTO, type WavePhase } from "../../../types/domainDtos";

if (typeof document !== "undefined" && !document.querySelector("[data-civicsurvival-threat-pulse]")) {
    const style = document.createElement("style");
    style.setAttribute("data-civicsurvival-threat-pulse", "");
    style.textContent = `
@keyframes threat-pulse {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.5; transform: scale(1.3); }
}`;
    document.head.appendChild(style);
}

interface BadgeProps {
    styles: ReturnType<typeof createGlobalStatusStyles>;
}

export const FrequencyGauge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const gridState = usePowerGrid();
    const grid = bindingDataOrDefault(gridState, DEFAULT_POWER_GRID_DTO);
    // Default GridFrequency=0 reads as a dead grid; until the feed delivers,
    // show a muted "—" gauge instead of a fabricated frequency (class rule:
    // never present DEFAULT_* values as a live verdict — see isBindingLive).
    const live = isBindingLive(gridState);
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const frequency = grid.GridFrequency ?? 50;
    const stressZone = grid.StressZone ?? "normal";

    const color = !live ? theme.colors.textMuted
        : stressZone === "collapsed" ? theme.colors.errorBright
        : stressZone === "red" ? accents.crisis.accent
        : stressZone === "yellow" ? accents.resilience.accent
        : accents.schemes.accent;

    const percent = !live ? 0 : Math.max(0, Math.min(100, ((frequency - 48) / 2) * 100));

    return (
        <div style={s.frequencyGauge}>
            <div style={s.frequencyBar}>
                <div style={s.frequencyFill(percent, color)} />
            </div>
            <HoverTip text={l.t("TIP_FREQUENCY")} style={s.frequencyValue(color)}>
                {live ? l.t("UI_UNIT_HZ", frequency.toFixed(1)) : "—"}
            </HoverTip>
        </div>
    );
});
FrequencyGauge.displayName = "FrequencyGauge";

export const BalanceBadge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const gridState = usePowerGrid();
    const grid = bindingDataOrDefault(gridState, DEFAULT_POWER_GRID_DTO);
    // Default headroom 0 reads as a green "+0 MW" all-clear; stay muted until live.
    const live = isBindingLive(gridState);
    const theme = useTheme();
    const l = useLocale();

    const headroom = grid.CapacityHeadroomMW ?? 0;
    // Production = 0 with real demand is a grid failure: the potential is intact but
    // nothing is delivered. headroom is capacity, not flow — without this signal the
    // badge would stay green "+N MW" during a blackout.
    const gridFailure = (grid.Production ?? 0) <= 0 && (grid.Demand ?? 0) > 0;
    const isPositive = headroom >= 0 && !gridFailure;

    const awaitingBadge: React.CSSProperties = {
        ...s.balanceBadge(true),
        background: "transparent",
        border: `2rem solid ${theme.colors.border}`,
    };
    const awaitingValue: React.CSSProperties = {
        ...s.balanceValue(true),
        color: theme.colors.textMuted,
    };

    return (
        <div style={live ? s.balanceBadge(isPositive) : awaitingBadge}>
            <HoverTip text={l.t("TIP_POWER_BALANCE")} style={live ? s.balanceValue(isPositive) : awaitingValue}>
                {live ? l.t("UI_POWER_BALANCE_VALUE", (headroom >= 0 ? "+" : "") + headroom) : "—"}
            </HoverTip>
        </div>
    );
});
BalanceBadge.displayName = "BalanceBadge";

export const BatteryBadge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const backupState = useBackupPower();
    const backup = bindingDataOrDefault(backupState, DEFAULT_BACKUP_POWER_DTO);
    // Default BackupCharge=0 reads as a crisis-red empty battery; muted until live.
    const live = isBindingLive(backupState);
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const charge = backup.BackupCharge;

    const color = !live ? theme.colors.textMuted
        : charge < 20 ? accents.crisis.accent
        : charge < 50 ? accents.resilience.accent
        : accents.schemes.accent;

    return (
        <div style={s.batteryContainer}>
            <div style={s.batteryBar}>
                <div style={s.batteryFill(live ? charge : 0, color)} />
            </div>
            <HoverTip text={l.t("TIP_BATTERY")} style={s.batteryValue(color)}>
                {live ? `${charge}%` : "—"}
            </HoverTip>
        </div>
    );
});
BatteryBadge.displayName = "BatteryBadge";

export const ThresholdBadge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const grid = bindingDataOrDefault(usePowerGrid(), DEFAULT_POWER_GRID_DTO);
    const l = useLocale();
    const isActive = grid.ThresholdActive ?? false;
    const cutCount = grid.BuildingsCutCount ?? 0;

    if (!isActive) return null;

    return (
        <>
            <div style={s.separator} />
            <div style={s.thresholdBadge(isActive)}>
                <HoverTip text={l.t("TIP_THRESHOLD")} style={s.thresholdValue(isActive)}>
                    {l.t("UI_STATUS_CUT", cutCount)}
                </HoverTip>
            </div>
        </>
    );
});
ThresholdBadge.displayName = "ThresholdBadge";

export const ThreatBadge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const threatState = useThreat();
    const threatData = bindingDataOrDefault(threatState, DEFAULT_THREAT_DTO);
    const grid = bindingDataOrDefault(usePowerGrid(), DEFAULT_POWER_GRID_DTO);
    // Default WavePhase "calm" claims a quiet sky — on a save loaded mid-attack
    // that is a lie for the first publishes. Muted "—" chip until the feed is live.
    const live = isBindingLive(threatState);
    const theme = useTheme();
    const l = useLocale();

    const phase = threatData.WavePhase || "calm";

    // Wave phase and grid status are independent state: the wave can be over (calm)
    // while the grid is still collapsed from earlier damage. Surface the relationship
    // instead of showing a bare "CALM" next to a "COLLAPSED" grid.
    const isAftermath = phase === "calm" && (grid.StressZone ?? "normal") === "collapsed";

    const phaseLabels: Record<WavePhase, string> = {
        calm: l.t("UI_STATUS_CALM"),
        alert: l.t("UI_STATUS_ALERT"),
        attack: l.t("UI_STATUS_ATTACK"),
        psyattack: l.t("UI_STATUS_PSYATTACK"),
        recovery: l.t("UI_STATUS_RECOVERY"),
    };

    if (!live) {
        return (
            <div style={{ ...s.threatBadge(phase), background: "transparent", border: `2rem solid ${theme.colors.border}` }}>
                <HoverTip text={l.t("TIP_THREAT_PHASE")} style={{ ...s.threatText(phase), color: theme.colors.textMuted }}>
                    {"—"}
                </HoverTip>
            </div>
        );
    }

    return (
        <div style={s.threatBadge(phase)}>
            <HoverTip text={isAftermath ? l.t("TIP_AFTERMATH") : l.t("TIP_THREAT_PHASE")} style={s.threatText(phase)}>
                {isAftermath ? l.t("UI_STATUS_AFTERMATH") : phaseLabels[phase]}
            </HoverTip>
        </div>
    );
});
ThreatBadge.displayName = "ThreatBadge";

export const CrisisEconomyBadge: React.FC<BadgeProps> = memo(({ styles: s }) => {
    const isActiveState = useBooleanBinding(shockActActive$, "shockActActive");
    const taxMultState = useNumberBinding(taxMultiplier$, "taxMultiplier");
    const loansOkState = useBooleanBinding(loansAvailable$, "loansAvailable");
    const crisisDayState = useNumberBinding(crisisDayNumber$, "crisisDayNumber");
    const crisisState = combineBindingStates({
        isActive: isActiveState,
        taxMult: taxMultState,
        loansOk: loansOkState,
        crisisDay: crisisDayState,
    }, (values) => values);
    const l = useLocale();
    if (crisisState.status !== "ready") return null;

    const { isActive, taxMult, loansOk, crisisDay } = crisisState.data;

    if (!isActive && taxMult >= 1 && loansOk) return null;

    const taxReduction = Math.round((1 - taxMult) * 100);

    return (
        <>
            <div style={s.crisisEconomyBadge}>
                <span style={s.crisisEconomyLabel}>{l.t("UI_STATUS_CRISIS")}</span>
                {crisisDay > 0 && (
                    <span style={s.crisisEconomyItem}>
                        {l.t("UI_STATUS_CRISIS_DAY", crisisDay)}
                    </span>
                )}
                {taxMult < 1 && (
                    <span style={s.crisisEconomyItem}>
                        {l.t("UI_STATUS_TAX_REDUCTION", taxReduction)}
                    </span>
                )}
                {!loansOk && (
                    <span style={s.crisisEconomyItemLast}>
                        {l.t("UI_STATUS_NO_LOANS")}
                    </span>
                )}
            </div>
            <div style={s.separator} />
        </>
    );
});
CrisisEconomyBadge.displayName = "CrisisEconomyBadge";
