/**
 * SchemesSection - Emergency Fund Raid + Fuel Siphoning
 */

import React, { memo, useMemo, useCallback, useRef } from "react";
import { HoverTip } from "../../shared/common/HoverTip";
import { HelpSection } from "../../shared/common/HelpSection";
import { useTheme, useAccents, formatCostArg } from "../../../themes";
import { bindingDataOrDefault, useSchemes } from "@hooks/domain";
import { useCorruptionActions, useRequestAction } from "@hooks/actions";
import { DEFAULT_SCHEMES_DTO } from "../../../types/domainDtos";
import { GlassCase, SectionHeader, StatRow } from "../../shared/ui";
import { createSectionStyles } from "./SectionStyles";
import { useLocale } from "../../../locales";
import { asPercentValue } from "../../../types/semantic";

type CorruptionActions = ReturnType<typeof useCorruptionActions>;

const EMERGENCY_FUND_LEVELS = [0, 25, 50, 75, 100];
const FUEL_SIPHON_LEVELS = [0, 15, 30, 50];

interface SchemesSectionContentProps {
    actions: CorruptionActions;
}

const SchemesSectionContent: React.FC<SchemesSectionContentProps> = ({ actions }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createSectionStyles(theme, accents), [theme, accents]);
    const schemesState = useSchemes();
    const schemeActionRef = useRef<() => boolean>(() => false);
    const schemeAction = useRequestAction(
        () => schemeActionRef.current(),
        schemesState.status === "ready" ? schemesState.data.CorruptionSchemeRequest : undefined
    );
    const schemes = bindingDataOrDefault(schemesState, DEFAULT_SCHEMES_DTO);
    const requestPending = schemeAction.isPending || schemes.CorruptionSchemeRequest.Status === "pending";
    const requestError = schemes.CorruptionSchemeRequest.Status === "failed" && schemes.CorruptionSchemeRequest.ReasonId
        ? l.tDynamic(schemes.CorruptionSchemeRequest.ReasonId)
        : "";

    // Stable handlers using data attributes to avoid inline closures in map
    const handleEmergencyFundClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const percent = Number(e.currentTarget.dataset.percent);
        if (!Number.isNaN(percent)) {
            schemeActionRef.current = () => {
                actions.setEmergencyFundWithdraw(asPercentValue(percent));
                return true;
            };
            schemeAction.execute();
        }
    }, [actions, schemeAction]);

    const handleFuelSiphonClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const percent = Number(e.currentTarget.dataset.percent);
        if (!Number.isNaN(percent)) {
            schemeActionRef.current = () => {
                actions.setFuelSiphonPercent(asPercentValue(percent));
                return true;
            };
            schemeAction.execute();
        }
    }, [actions, schemeAction]);

    const corruptionWindowActive = schemes.CorruptionWindowActive;
    const emergencyFundAvailability = schemes.EmergencyFundAvailability;
    const fuelSiphonAvailability = schemes.FuelSiphonAvailability;
    const isEmergencyFundLocked = (percent: number): boolean =>
        percent > 0 && (!emergencyFundAvailability.CanRun || !corruptionWindowActive);
    const isFuelSiphonLocked = (percent: number): boolean =>
        percent > 0 && (!fuelSiphonAvailability.CanRun || !corruptionWindowActive);
    const emergencyFundTip = (percent: number): string => {
        if (percent === 0) return l.t("UI_SCHEME_NO_WITHDRAWAL");
        if (!emergencyFundAvailability.CanRun) return l.tDynamic(emergencyFundAvailability.LockedReasonId);
        if (!corruptionWindowActive) return l.t("UI_CORRUPTION_WINDOW_CLOSED");
        return l.t("UI_SCHEME_WITHDRAW_PCT", percent);
    };
    const fuelSiphonTip = (percent: number): string => {
        if (percent === 0) return l.t("UI_SCHEME_NO_SIPHONING");
        if (!fuelSiphonAvailability.CanRun) return l.tDynamic(fuelSiphonAvailability.LockedReasonId);
        if (!corruptionWindowActive) return l.t("UI_CORRUPTION_WINDOW_CLOSED");
        return l.t("UI_SCHEME_SIPHON_PCT", percent, percent * 0.5);
    };

    return (
        <>
            {/* Emergency Fund Raid */}
            <div style={s.section}>
                <SectionHeader
                    title={l.t("UI_SCHEME_EMERGENCY_FUND")}
                    titleStyle={s.sectionTitle}
                    help={<HelpSection id="schemes" title={l.t("UI_SCHEME_EMERGENCY_FUND")}>{l.t("HELP_SCHEMES")}</HelpSection>}
                />
                <StatRow
                    label={l.t("UI_SCHEME_WITHDRAWAL_RATE")}
                    value={`${schemes.EmergencyFundWithdraw ?? 0}%`}
                    color={(schemes.EmergencyFundWithdraw ?? 0) > 0 ? accents.schemes.accent : theme.colors.textMuted}
                    emphasis="title"
                />
                <div style={s.presetButtonGroup}>
                    {EMERGENCY_FUND_LEVELS.map((percent) => (
                        <div key={percent} style={s.presetButtonGroupChild}>
                            <HoverTip
                                text={emergencyFundTip(percent)}
                            >
                                <button
                                    style={s.presetButton((schemes.EmergencyFundWithdraw ?? 0) === percent, accents.schemes.accent, theme.colors.border)}
                                    data-percent={percent}
                                    disabled={requestPending || isEmergencyFundLocked(percent)}
                                    onClick={!requestPending ? handleEmergencyFundClick : undefined}
                                >
                                    {requestPending ? l.t("UI_PROCESSING") : `${percent}%`}
                                </button>
                            </HoverTip>
                        </div>
                    ))}
                </div>
                {schemes.EmergencyFundWithdraw > 0 && (
                    <div style={s.schemeInfo}>
                        {l.t("UI_SCHEME_BALANCE", formatCostArg(schemes.EmergencyFundBalance ?? 0))}
                    </div>
                )}
            </div>

            {/* Fuel Siphoning */}
            <div style={s.section}>
                <div style={s.sectionTitle}>{l.t("UI_SCHEME_FUEL_SIPHONING")}</div>
                <StatRow
                    label={l.t("UI_SCHEME_SIPHON_RATE")}
                    value={`${schemes.FuelSiphonPercent ?? 0}%`}
                    color={(schemes.FuelSiphonPercent ?? 0) > 0 ? accents.schemes.accent : theme.colors.textMuted}
                    emphasis="title"
                />
                <div style={s.presetButtonGroup}>
                    {FUEL_SIPHON_LEVELS.map((percent) => (
                        <div key={percent} style={s.presetButtonGroupChild}>
                            <HoverTip
                                text={fuelSiphonTip(percent)}
                            >
                                <button
                                    style={s.presetButton((schemes.FuelSiphonPercent ?? 0) === percent, accents.schemes.accent, theme.colors.border)}
                                    data-percent={percent}
                                    disabled={requestPending || isFuelSiphonLocked(percent)}
                                    onClick={!requestPending ? handleFuelSiphonClick : undefined}
                                >
                                    {requestPending ? l.t("UI_PROCESSING") : `${percent}%`}
                                </button>
                            </HoverTip>
                        </div>
                    ))}
                </div>
                {schemes.FuelSiphonPercent > 0 && (
                    <div style={s.schemeInfo}>
                        {l.t("UI_SCHEME_FUEL_PROFIT", Math.round(schemes.FuelSiphonPercent * 1.3))}
                    </div>
                )}
                {requestError && (
                    <div style={{ ...s.schemeInfo, color: accents.crisis.accent, fontWeight: 700 }}>
                        {requestError}
                    </div>
                )}
            </div>
        </>
    );
};

export const SchemesSection = memo(() => {
    const actions = useCorruptionActions();
    return (
        <GlassCase
            feature="Corruption"
            name="Corruption Schemes"
            description="Emergency Fund withdrawals and fuel siphoning let you funnel cash from official budgets into the shadow wallet at the cost of corruption heat and investigation risk."
        >
            <SchemesSectionContent actions={actions} />
        </GlassCase>
    );
});
SchemesSection.displayName = "SchemesSection";
