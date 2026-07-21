import React from "react";
import { t } from "../../coherent";
import { useTheme, useAccents } from "../../../themes";
import { type DistrictViewModel } from "../../../types/models/PowerTypes";
import { PanelSection, SectionTitle, StatRow } from "../../shared/ui";
import { HoverTipTarget } from "../../shared/common/HoverTip";
import { useLocale } from "../../../locales";

type DistrictPassportStyles = {
    categoryRow: (isOff: boolean) => React.CSSProperties;
    categoryAbbrev: (color: string) => React.CSSProperties;
    categoryName: React.CSSProperties;
    categoryMW: React.CSSProperties;
    categoryToggle: (isOff: boolean, color: string, disabled?: boolean) => React.CSSProperties;
    protectionRow: React.CSSProperties;
    protectionButton: (isOn: boolean, color: string, disabled?: boolean) => React.CSSProperties;
    protectionLabel: React.CSSProperties;
    protectionStatus: (isOn: boolean) => React.CSSProperties;
    penaltyHint: React.CSSProperties;
};

interface DistrictCategoriesSectionProps {
    district: DistrictViewModel;
    styles: DistrictPassportStyles;
    onCategoryToggle: (e: React.MouseEvent<HTMLButtonElement>) => void;
}

interface DistrictProtectionSectionProps {
    district: DistrictViewModel;
    styles: DistrictPassportStyles;
    onToggleVIP: () => void;
    onToggleVIPBypass: () => void;
    onToggleInternet: () => void;
    internetLocked?: boolean;
    internetPending?: boolean;
    internetLockedText?: string;
}

interface DistrictPenaltiesSectionProps {
    district: DistrictViewModel;
    styles: DistrictPassportStyles;
}

export const DistrictCategoriesSection: React.FC<DistrictCategoriesSectionProps> = ({
    district,
    styles: s,
    onCategoryToggle,
}) => {
    const l = useLocale();

    return (
        <PanelSection>
            <SectionTitle>{l.t("UI_DP_CATEGORIES")}</SectionTitle>
            {district.categories.map((cat) => (
                <div key={cat.config.id} style={s.categoryRow(cat.isOff)}>
                    <span style={s.categoryAbbrev(cat.config.color)}>[{l.t(cat.config.labelKey)}]</span>
                    <span style={s.categoryName}>{l.t(cat.config.nameKey)}</span>
                    <span style={s.categoryMW}>{t`${cat.mw} MW`}</span>
                    <button
                        style={s.categoryToggle(cat.isOff, cat.config.color, !district.isManualMode)}
                        data-category={cat.config.id}
                        onClick={onCategoryToggle}
                        disabled={!district.isManualMode}
                    >
                        {cat.isOff ? l.t("UI_DP_OFF") : l.t("UI_DP_ON")}
                    </button>
                </div>
            ))}
        </PanelSection>
    );
};

export const DistrictProtectionSection: React.FC<DistrictProtectionSectionProps> = ({
    district,
    styles: s,
    onToggleVIP,
    onToggleVIPBypass,
    onToggleInternet,
    internetLocked = false,
    internetPending = false,
    internetLockedText = "",
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();

    const internetDisabled = internetLocked || internetPending;

    return (
        <PanelSection>
            <SectionTitle>{l.t("UI_DP_PROTECTION_NETWORK")}</SectionTitle>
            <div style={s.protectionRow}>
                <button
                    style={s.protectionButton(district.isVIP, accents.vip.accent)}
                    onClick={onToggleVIP}
                >
                    <span style={s.protectionLabel}>{l.t("UI_DP_VIP")}</span>
                    <span style={s.protectionStatus(district.isVIP)}>
                        {district.isVIP ? l.t("UI_DP_ON") : l.t("UI_DP_OFF")}
                    </span>
                </button>
                <button
                    style={s.protectionButton(district.isVIPBypass, accents.schemes.accent)}
                    onClick={onToggleVIPBypass}
                >
                    <span style={s.protectionLabel}>{l.t("UI_DP_WEALTHY")}</span>
                    <span style={s.protectionStatus(district.isVIPBypass)}>
                        {district.isVIPBypass ? l.t("UI_DP_ON") : l.t("UI_DP_OFF")}
                    </span>
                </button>
                <HoverTipTarget text={internetLocked ? internetLockedText : null}>
                    <button
                        style={s.protectionButton(!district.internetDisabled, theme.colors.success, internetDisabled)}
                        onClick={onToggleInternet}
                        disabled={internetDisabled}
                    >
                        <span style={s.protectionLabel}>{l.t("UI_DP_INTERNET")}</span>
                        <span style={s.protectionStatus(!district.internetDisabled)}>
                            {district.internetDisabled ? l.t("UI_DP_OFF") : l.t("UI_DP_ON")}
                        </span>
                    </button>
                </HoverTipTarget>
            </div>
        </PanelSection>
    );
};

export const DistrictPenaltiesSection: React.FC<DistrictPenaltiesSectionProps> = ({
    district,
    styles: s,
}) => {
    const accents = useAccents();
    const l = useLocale();
    const hasPenalties = district.totalHappinessPenalty > 0 || district.totalCommercePenalty > 0;

    if (!hasPenalties) return null;

    return (
        <PanelSection>
            <SectionTitle>{l.t("UI_DP_PENALTIES")}</SectionTitle>
            {district.totalHappinessPenalty > 0 && (
                <StatRow
                    label={l.t("UI_DP_HAPPINESS")}
                    value={t`-${Math.round(district.totalHappinessPenalty * 100)}%`}
                    color={accents.crisis.accent}
                />
            )}
            {district.totalCommercePenalty > 0 && (
                <StatRow
                    label={l.t("UI_DP_COMMERCE")}
                    value={t`-${Math.round(district.totalCommercePenalty * 100)}%`}
                    color={accents.crisis.accent}
                />
            )}
            <div style={s.penaltyHint}>
                {`${district.isScheduleActive ? l.t("UI_DP_PENALTY_SCHEDULE") : l.t("UI_DP_PENALTY_BLACKOUT")}${district.internetDisabled ? l.t("UI_DP_NO_INTERNET") : ""}`}
            </div>
        </PanelSection>
    );
};
