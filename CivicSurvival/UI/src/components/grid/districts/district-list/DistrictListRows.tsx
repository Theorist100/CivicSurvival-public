import React from "react";
import { useTheme, useAccents } from "../../../../themes";
import { type DistrictViewModel } from "../../../../types/models/PowerTypes";
import {
    IconAlert,
    IconGear,
    IconGlobe,
    IconMoney,
    IconPower,
    IconShield,
} from "../../../shared/common/Icons";
import { ModeDropdown, scheduleToMode, type DistrictMode } from "../../../shared/common/ModeDropdown";
import { HoverTipTarget } from "../../../shared/common/HoverTip";
import { useLocale } from "../../../../locales";
import { COLUMN_WIDTHS, hasPartialCategories, type FlagColors } from "../DistrictListView";
import { type CityScheduleId, type EntityIndex } from "../../../../types/semantic";

type DistrictListStyles = {
    row: (isOff: boolean, isHovered: boolean) => React.CSSProperties;
    cell: (width: string) => React.CSSProperties;
    districtName: (isOff: boolean) => React.CSSProperties;
    mwValue: (color: string) => React.CSSProperties;
    quickButton: (isOff: boolean) => React.CSSProperties;
    flagsContainer: React.CSSProperties;
    flagBadge: (color: string) => React.CSSProperties;
    editButton: React.CSSProperties;
    editButtonHover: React.CSSProperties;
};

interface CityRowProps {
    cityTotalMW: number;
    cityDeliveredMW: number;
    citySchedule: CityScheduleId;
    cityScheduleLocked?: boolean;
    districtsOverrideCity?: boolean;
    isHovered: boolean;
    styles: DistrictListStyles;
    onCityClick: () => void;
    onCityModeChange: (mode: DistrictMode) => void;
    onHoverChange: (isHovered: boolean) => void;
}

interface DistrictRowProps {
    district: DistrictViewModel;
    isHovered: boolean;
    flagColors: FlagColors;
    styles: DistrictListStyles;
    modeHandler: (mode: DistrictMode) => void;
    blackoutSourceInfo: {
        icon: React.FC<{ className?: string }> | null;
        color: string;
        title: string;
    };
    onSelectDistrict: (entity: EntityIndex) => void;
    onToggleBlackout: (entity: EntityIndex) => void;
    onHoverEntity: (entity: EntityIndex | null) => void;
}

const getDisplayName = (name: string, fallback: string): string => {
    if (name === "No District") return fallback;
    return name;
};

export const CityRow: React.FC<CityRowProps> = ({
    cityTotalMW,
    cityDeliveredMW,
    citySchedule,
    cityScheduleLocked = false,
    districtsOverrideCity = false,
    isHovered,
    styles: s,
    onCityClick,
    onCityModeChange,
    onHoverChange,
}) => {
    const theme = useTheme();
    const l = useLocale();

    // City-level supply status, mirroring the per-district DEL coloring:
    // nothing demanded → muted, no delivery → error, partial → brownout, full → success.
    const isUnpowered = cityDeliveredMW <= 0 && cityTotalMW > 0;
    const isBrownout = cityDeliveredMW > 0 && cityDeliveredMW < cityTotalMW * 0.9;
    const delColor = cityTotalMW <= 0
        ? theme.colors.textMuted
        : isUnpowered
            ? theme.colors.error
            : isBrownout
                ? theme.colors.warning
                : theme.colors.success;

    return (
        <div
            style={s.row(false, isHovered)}
            role="button"
            onClick={onCityClick}
            onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onCityClick();
                }
            }}
            tabIndex={0}
            onMouseEnter={() => onHoverChange(true)}
            onMouseLeave={() => onHoverChange(false)}
        >
            <div style={{ ...s.cell(COLUMN_WIDTHS.district), ...s.districtName(false), fontWeight: 700 }}>
                {l.t("UI_DL_CITY")}
            </div>
            <div style={{ ...s.cell(COLUMN_WIDTHS.del), ...s.mwValue(delColor) }}>
                <HoverTipTarget text={l.t("UI_DL_CITY_DEL_TIP")}>
                    <span>{cityDeliveredMW}</span>
                </HoverTipTarget>
            </div>
            <div style={{ ...s.cell(COLUMN_WIDTHS.max), ...s.mwValue(theme.colors.textMuted) }}>
                <HoverTipTarget text={l.t("UI_DL_CITY_MAX_TIP")}>
                    <span>{cityTotalMW}</span>
                </HoverTipTarget>
            </div>
            <div style={s.cell(COLUMN_WIDTHS.mode)}>
                <ModeDropdown
                    mode={scheduleToMode(citySchedule, citySchedule === -1)}
                    onChange={onCityModeChange}
                    minWidth="65rem"
                    fontSize="9rem"
                    disabled={cityScheduleLocked}
                    hasOverride={districtsOverrideCity}
                />
            </div>
            <div style={s.cell(COLUMN_WIDTHS.quick)} />
            <div style={{ ...s.cell(COLUMN_WIDTHS.flags), ...s.flagsContainer }} />
            <div style={s.cell(COLUMN_WIDTHS.edit)}>
                <HoverTipTarget text={l.t("UI_DL_CITY_SETTINGS")}>
                    <button
                        style={{
                            ...s.editButton,
                            ...(isHovered ? s.editButtonHover : {}),
                        }}
                        onClick={(e) => {
                            e.stopPropagation();
                            onCityClick();
                        }}
                    >
                        <IconGear />
                    </button>
                </HoverTipTarget>
            </div>
        </div>
    );
};

export const DistrictRow: React.FC<DistrictRowProps> = ({
    district,
    isHovered,
    flagColors,
    styles: s,
    modeHandler,
    blackoutSourceInfo,
    onSelectDistrict,
    onToggleBlackout,
    onHoverEntity,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    // MODE dropdown shows the schedule preset name only; the full-blackout toggle
    // is handled by the QUICK power-icon button, so the dropdown ignores isFullBlackout
    // to avoid two controls competing for the same ON/OFF lexicon.
    const mode = scheduleToMode(district.scheduleId, false);
    const isOff = district.isFullBlackout;
    const totalMW = district.totalMW;
    const deliveredMW = district.deliveredMW;
    const isUnpowered = !isOff && !district.isScheduleActive && deliveredMW <= 0 && totalMW > 0;
    const isBrownout = !isOff && !district.isScheduleActive && deliveredMW > 0 && deliveredMW < totalMW * 0.9;
    const mwColor = isOff
        ? theme.colors.textMuted
        : district.isScheduleActive
            ? accents.resilience.accent
            : isUnpowered
                ? theme.colors.error
                : isBrownout
                    ? theme.colors.warning
                    : theme.colors.success;

    const BlackoutSourceIcon = blackoutSourceInfo.icon;

    return (
        <div
            style={s.row(isOff, isHovered)}
            role="button"
            onClick={() => onSelectDistrict(district.entity)}
            onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onSelectDistrict(district.entity);
                }
            }}
            tabIndex={0}
            onMouseEnter={() => onHoverEntity(district.entity)}
            onMouseLeave={() => onHoverEntity(null)}
        >
            <div style={{ ...s.cell(COLUMN_WIDTHS.district), ...s.districtName(isOff) }}>
                {getDisplayName(district.name, l.t("UI_DL_NO_DISTRICT"))}
            </div>

            <div style={{ ...s.cell(COLUMN_WIDTHS.del), ...s.mwValue(mwColor) }}>
                {deliveredMW}
            </div>
            <div style={{ ...s.cell(COLUMN_WIDTHS.max), ...s.mwValue(theme.colors.textMuted) }}>
                {totalMW}
            </div>

            <div style={s.cell(COLUMN_WIDTHS.mode)}>
                <ModeDropdown
                    mode={mode}
                    onChange={modeHandler}
                />
            </div>

            <div style={s.cell(COLUMN_WIDTHS.quick)}>
                <HoverTipTarget text={l.t(isOff ? "UI_DL_RESTORE_TIP" : "UI_DL_CUT_TIP")}>
                    <button
                        style={s.quickButton(isOff)}
                        onClick={(e) => {
                            e.stopPropagation();
                            onToggleBlackout(district.entity);
                        }}
                        aria-label={l.t(isOff ? "UI_DL_RESTORE_TIP" : "UI_DL_CUT_TIP")}
                    >
                        <IconPower />
                    </button>
                </HoverTipTarget>
            </div>

            <div style={{ ...s.cell(COLUMN_WIDTHS.flags), ...s.flagsContainer }}>
                {BlackoutSourceIcon && (
                    <HoverTipTarget text={blackoutSourceInfo.title}>
                        <div style={s.flagBadge(blackoutSourceInfo.color)}>
                            <BlackoutSourceIcon />
                        </div>
                    </HoverTipTarget>
                )}
                {hasPartialCategories(district) && (
                    <HoverTipTarget text={l.t("UI_DL_FLAG_PARTIAL")}>
                        <div style={s.flagBadge(flagColors.partial)}>
                            <IconAlert />
                        </div>
                    </HoverTipTarget>
                )}
                {district.isVIP && (
                    <HoverTipTarget text={l.t("UI_DL_FLAG_VIP")}>
                        <div style={s.flagBadge(flagColors.vip)}>
                            <IconShield />
                        </div>
                    </HoverTipTarget>
                )}
                {district.isVIPBypass && (
                    <HoverTipTarget text={l.t("UI_DL_FLAG_WEALTHY")}>
                        <div style={s.flagBadge(flagColors.wealthy)}>
                            <IconMoney />
                        </div>
                    </HoverTipTarget>
                )}
                {district.internetDisabled && (
                    <HoverTipTarget text={l.t("UI_DL_FLAG_NO_INTERNET")}>
                        <div style={s.flagBadge(flagColors.noInternet)}>
                            <IconGlobe />
                        </div>
                    </HoverTipTarget>
                )}
            </div>

            <div style={s.cell(COLUMN_WIDTHS.edit)}>
                <HoverTipTarget text={l.t("UI_DL_DISTRICT_SETTINGS")}>
                    <button
                        style={{
                            ...s.editButton,
                            ...(isHovered ? s.editButtonHover : {}),
                        }}
                        onClick={(e) => {
                            e.stopPropagation();
                            onSelectDistrict(district.entity);
                        }}
                    >
                        <IconGear />
                    </button>
                </HoverTipTarget>
            </div>
        </div>
    );
};
