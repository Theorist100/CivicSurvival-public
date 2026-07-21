import React from "react";
import { t } from "../../coherent";
import { useTheme, useAccents, hexToRgba } from "../../../themes";
import { SCHEDULE_CONFIG } from "../../shared/common/ModeDropdown";
import { HoverTipTarget } from "../../shared/common/HoverTip";
import { PanelSection, SectionTitle, StatRow } from "../../shared/ui";
import { useLocale } from "../../../locales";
import { type CityScheduleId, SATURATION_DISPLAY_EPS } from "../../../types/semantic";

type CityPassportStyles = {
    modeGrid: React.CSSProperties;
    modeButton: (isSelected: boolean, color: string) => React.CSSProperties;
    modeButtonDisabled: React.CSSProperties;
    modeLabel: React.CSSProperties;
    modeDesc: React.CSSProperties;
    toggleRow: React.CSSProperties;
    toggleLabel: React.CSSProperties;
    toggleButtons: React.CSSProperties;
    toggleButton: (isActive: boolean, color: string) => React.CSSProperties;
};

export interface CityPassportStats {
    totalMW: number;
    districtCount: number;
    blackoutCount: number;
    blackoutPercent: number;
    allVIP: boolean;
    someVIP: boolean;
    allWealthy: boolean;
    someWealthy: boolean;
    allInternetOff: boolean;
    someInternetOff: boolean;
    vipCount: number;
    wealthyCount: number;
    internetOffCount: number;
}

interface CityModeSectionProps {
    effectiveMode: CityScheduleId;
    disabled?: boolean;
    isScheduleDisabled?: (schedule: CityScheduleId) => boolean;
    lockedReasonId?: string;
    styles: CityPassportStyles;
    onScheduleClick: (e: React.MouseEvent<HTMLButtonElement>) => void;
}

interface CityMassControlsSectionProps {
    stats: CityPassportStats;
    autoDispatchEnabled: boolean;
    autoDispatchSheddedCount: number;
    autoDispatchBlockedByVip: boolean;
    internetLocked?: boolean;
    internetPending?: boolean;
    internetLockedText?: string;
    styles: CityPassportStyles;
    onToggleAllVIP: (enable: boolean) => void;
    onToggleAllWealthy: (enable: boolean) => void;
    onToggleAllInternet: (enable: boolean) => void;
    onToggleAutoDispatch: () => void;
}

interface CityStatsSectionProps {
    stats: CityPassportStats;
    fleetSaturation: number;
}


const CITY_MODE_SCHEDULES: ReadonlyArray<CityScheduleId> = [0, 1, 2, 3, 4, -1];

export const CityModeSection: React.FC<CityModeSectionProps> = ({
    effectiveMode,
    disabled = false,
    isScheduleDisabled,
    lockedReasonId = "",
    styles: s,
    onScheduleClick,
}) => {
    const l = useLocale();

    return (
        <PanelSection>
            <SectionTitle>{l.t("UI_CP_CITY_MODE")}</SectionTitle>
            <div style={s.modeGrid}>
                {CITY_MODE_SCHEDULES.map((sched) => {
                    const cfg = SCHEDULE_CONFIG[sched];
                    const isSelected = effectiveMode === sched;
                    const scheduleDisabled = disabled || isScheduleDisabled?.(sched) === true;
                    return (
                        <HoverTipTarget key={sched} text={scheduleDisabled && lockedReasonId ? l.tDynamic(lockedReasonId) : null}>
                            <button
                                style={{
                                    ...s.modeButton(isSelected, cfg.color),
                                    ...(scheduleDisabled || isSelected ? s.modeButtonDisabled : {}),
                                }}
                                data-sched={sched}
                                onClick={onScheduleClick}
                                disabled={scheduleDisabled || isSelected}
                            >
                                <span style={s.modeLabel}>{l.t(cfg.label)}</span>
                                <span style={s.modeDesc}>{l.t(cfg.desc)}</span>
                            </button>
                        </HoverTipTarget>
                    );
                })}
            </div>
        </PanelSection>
    );
};

export const CityMassControlsSection: React.FC<CityMassControlsSectionProps> = ({
    stats,
    autoDispatchEnabled,
    autoDispatchSheddedCount,
    autoDispatchBlockedByVip,
    internetLocked = false,
    internetPending = false,
    internetLockedText = "",
    styles: s,
    onToggleAllVIP,
    onToggleAllWealthy,
    onToggleAllInternet,
    onToggleAutoDispatch,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const disabledButtonStyle: React.CSSProperties = {
        opacity: 0.45,
        cursor: "not-allowed",
    };
    const vipOffDisabled = !stats.someVIP;
    const vipOnDisabled = stats.allVIP;
    const wealthyOffDisabled = !stats.someWealthy;
    const wealthyOnDisabled = stats.allWealthy;
    const internetDisabled = internetLocked || internetPending;
    const internetOffDisabled = internetDisabled || stats.allInternetOff;
    const internetOnDisabled = internetDisabled || !stats.someInternetOff;

    return (
        <PanelSection>
            <SectionTitle>{l.t("UI_CP_MASS_CONTROLS")}</SectionTitle>

            <div style={s.toggleRow}>
                <span style={s.toggleLabel}>
                    {l.t("UI_CP_VIP_PROTECTION")} {stats.someVIP && !stats.allVIP && l.t("UI_CP_PARTIAL")}
                </span>
                <div style={s.toggleButtons}>
                    <button
                        style={{ ...s.toggleButton(!stats.someVIP, theme.colors.textMuted), ...(vipOffDisabled ? disabledButtonStyle : {}) }}
                        onClick={() => onToggleAllVIP(false)}
                        disabled={vipOffDisabled}
                    >
                        {l.t("UI_CP_ALL_OFF")}
                    </button>
                    <button
                        style={{ ...s.toggleButton(stats.allVIP, accents.vip.accent), ...(vipOnDisabled ? disabledButtonStyle : {}) }}
                        onClick={() => onToggleAllVIP(true)}
                        disabled={vipOnDisabled}
                    >
                        {l.t("UI_CP_ALL_ON")}
                    </button>
                </div>
            </div>

            <div style={s.toggleRow}>
                <span style={s.toggleLabel}>
                    {l.t("UI_CP_WEALTHY_BYPASS")} {stats.someWealthy && !stats.allWealthy && l.t("UI_CP_PARTIAL")}
                </span>
                <div style={s.toggleButtons}>
                    <button
                        style={{ ...s.toggleButton(!stats.someWealthy, theme.colors.textMuted), ...(wealthyOffDisabled ? disabledButtonStyle : {}) }}
                        onClick={() => onToggleAllWealthy(false)}
                        disabled={wealthyOffDisabled}
                    >
                        {l.t("UI_CP_ALL_OFF")}
                    </button>
                    <button
                        style={{ ...s.toggleButton(stats.allWealthy, accents.schemes.accent), ...(wealthyOnDisabled ? disabledButtonStyle : {}) }}
                        onClick={() => onToggleAllWealthy(true)}
                        disabled={wealthyOnDisabled}
                    >
                        {l.t("UI_CP_ALL_ON")}
                    </button>
                </div>
            </div>

            <div style={s.toggleRow}>
                <span style={s.toggleLabel}>
                    {l.t("UI_CP_INTERNET")} {stats.someInternetOff && !stats.allInternetOff && l.t("UI_CP_PARTIAL")}
                </span>
                <div style={s.toggleButtons}>
                    <HoverTipTarget text={internetLocked ? internetLockedText : null}>
                        <button
                            style={{ ...s.toggleButton(stats.allInternetOff, theme.colors.zoneRed), ...(internetOffDisabled ? disabledButtonStyle : {}) }}
                            onClick={() => onToggleAllInternet(false)}
                            disabled={internetOffDisabled}
                        >
                            {l.t("UI_CP_ALL_OFF")}
                        </button>
                    </HoverTipTarget>
                    <HoverTipTarget text={internetLocked ? internetLockedText : null}>
                        <button
                            style={{ ...s.toggleButton(!stats.someInternetOff, theme.colors.success), ...(internetOnDisabled ? disabledButtonStyle : {}) }}
                            onClick={() => onToggleAllInternet(true)}
                            disabled={internetOnDisabled}
                        >
                            {l.t("UI_CP_ALL_ON")}
                        </button>
                    </HoverTipTarget>
                </div>
            </div>

            <div style={s.toggleRow}>
                <span style={s.toggleLabel}>
                    {autoDispatchSheddedCount > 0
                        ? l.t("UI_CP_AUTO_DISPATCH_SHEDDED", autoDispatchSheddedCount)
                        : l.t("UI_CP_AUTO_DISPATCH")}
                </span>
                <div style={s.toggleButtons}>
                    <button
                        style={s.toggleButton(!autoDispatchEnabled, theme.colors.textMuted)}
                        onClick={() => autoDispatchEnabled && onToggleAutoDispatch()}
                    >
                        {l.t("UI_CP_OFF")}
                    </button>
                    <button
                        style={s.toggleButton(autoDispatchEnabled, "#f59e0b")}
                        onClick={() => !autoDispatchEnabled && onToggleAutoDispatch()}
                    >
                        {l.t("UI_CP_AUTO")}
                    </button>
                </div>
            </div>

            {autoDispatchBlockedByVip && (
                <div style={{
                    marginTop: "8rem",
                    padding: "8rem 12rem",
                    backgroundColor: hexToRgba(theme.colors.errorBright, 0.2),
                    border: `1rem solid ${theme.colors.error}`,
                    borderRadius: "4rem",
                    color: theme.colors.error,
                    fontSize: "11rem",
                }}>
                    {l.t("UI_CP_VIP_BLOCKED")}
                </div>
            )}
        </PanelSection>
    );
};

export const CityStatsSection: React.FC<CityStatsSectionProps> = ({ stats, fleetSaturation }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    return (
        <PanelSection>
            <SectionTitle>{l.t("UI_CP_STATISTICS")}</SectionTitle>
            {/* Neutral on purpose, mirroring the per-plant badge: spinning reserve is a normal
                grid response, not a crisis. Crisis red is reserved for things demanding action
                (blackouts, deficit) — a permanent red row the player cannot act on teaches them
                to ignore red exactly where it matters. */}
            {fleetSaturation < 1 - SATURATION_DISPLAY_EPS && (
                <StatRow
                    label={l.t("UI_GRID_FLEET_EFFICIENCY")}
                    value={t`${Math.round(fleetSaturation * 100)}%`}
                    color={theme.colors.textMuted}
                />
            )}
            <StatRow
                label={l.t("UI_CP_DISTRICTS_IN_BLACKOUT")}
                value={t`${stats.blackoutCount} / ${stats.districtCount}`}
                color={stats.blackoutCount > 0 ? accents.crisis.accent : theme.colors.success}
            />
            <StatRow
                label={l.t("UI_CP_VIP_DISTRICTS")}
                value={stats.vipCount}
                color={stats.someVIP ? accents.vip.accent : theme.colors.textMuted}
            />
            <StatRow
                label={l.t("UI_CP_WEALTHY_DISTRICTS")}
                value={stats.wealthyCount}
                color={stats.someWealthy ? accents.schemes.accent : theme.colors.textMuted}
            />
            <StatRow
                label={l.t("UI_CP_INTERNET_DISABLED")}
                value={stats.internetOffCount}
                color={stats.someInternetOff ? theme.colors.zoneRed : theme.colors.textMuted}
            />
        </PanelSection>
    );
};

