/**
 * SchemesSection - Emergency Fund Raid + Fuel Siphoning + Construction Kickback
 */

import React, { memo, useMemo, useCallback, useRef } from "react";
import { HoverTip } from "../../shared/common/HoverTip";
import { HelpSection } from "../../shared/common/HelpSection";
import { useTheme, useAccents, formatCostArg } from "../../../themes";
import { bindingDataOrDefault, useSchemes } from "@hooks/domain";
import { useCorruptionActions, useRequestAction } from "@hooks/actions";
import { DEFAULT_SCHEMES_DTO } from "../../../types/domainDtos";
import { GlassCase } from "../../shared/ui";
import { createSectionStyles, formatMoney } from "./SectionStyles";
import { useLocale } from "../../../locales";
import { asPercentValue } from "../../../types/semantic";

type CorruptionActions = ReturnType<typeof useCorruptionActions>;

const EMERGENCY_FUND_LEVELS = [0, 25, 50, 75, 100];
const FUEL_SIPHON_LEVELS = [0, 15, 30, 50];
const CONSTRUCTION_KICKBACK_LEVELS = [0, 5, 10, 20];

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
    const requestPending = schemeAction.isPending;
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

    const handleConstructionKickbackClick = useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
        const percent = Number(e.currentTarget.dataset.percent);
        if (!Number.isNaN(percent)) {
            schemeActionRef.current = () => {
                actions.setConstructionKickbackPercent(asPercentValue(percent));
                return true;
            };
            schemeAction.execute();
        }
    }, [actions, schemeAction]);

    const emergencyFundAvailability = schemes.EmergencyFundAvailability;
    const fuelSiphonAvailability = schemes.FuelSiphonAvailability;
    const constructionKickbackAvailability = schemes.ConstructionKickbackAvailability;
    const isEmergencyFundLocked = (percent: number): boolean =>
        percent > 0 && !emergencyFundAvailability.CanRun;
    const isFuelSiphonLocked = (percent: number): boolean =>
        percent > 0 && !fuelSiphonAvailability.CanRun;
    const isConstructionKickbackLocked = (percent: number): boolean =>
        percent > 0 && !constructionKickbackAvailability.CanRun;
    const emergencyFundTip = (percent: number): string => {
        if (percent === 0) return l.t("UI_SCHEME_NO_WITHDRAWAL");
        if (!emergencyFundAvailability.CanRun) return l.tDynamic(emergencyFundAvailability.LockedReasonId);
        return l.t("UI_SCHEME_WITHDRAW_PCT", percent);
    };
    const fuelSiphonTip = (percent: number): string => {
        if (percent === 0) return l.t("UI_SCHEME_NO_SIPHONING");
        if (!fuelSiphonAvailability.CanRun) return l.tDynamic(fuelSiphonAvailability.LockedReasonId);
        return l.t("UI_SCHEME_SIPHON_PCT", percent, percent * 0.5);
    };
    const constructionKickbackTip = (percent: number): string => {
        if (percent === 0) return l.t("UI_SCHEME_NO_KICKBACK");
        if (!constructionKickbackAvailability.CanRun) return l.tDynamic(constructionKickbackAvailability.LockedReasonId);
        return l.t("UI_SCHEME_KICKBACK_PCT", percent);
    };

    const renderPresetButton = (
        percent: number,
        selected: number,
        locked: boolean,
        tip: string,
        onClick: (e: React.MouseEvent<HTMLButtonElement>) => void
    ) => (
        <div key={percent} style={s.presetButtonGroupChild}>
            <HoverTip text={tip}>
                <button
                    style={s.presetButton(selected === percent, accents.schemes.accent, theme.colors.border)}
                    data-percent={percent}
                    disabled={requestPending || locked}
                    onClick={!requestPending ? onClick : undefined}
                >
                    {requestPending ? l.t("UI_PROCESSING") : `${percent}%`}
                </button>
            </HoverTip>
        </div>
    );

    return (
        <>
            {/* Emergency Fund Raid — compact row: title + presets in one wrap line */}
            <div style={s.section}>
                <div style={s.schemeRow}>
                    <span style={s.schemeRowTitle}>{l.t("UI_SCHEME_EMERGENCY_FUND")}</span>
                    <span style={s.schemeRowHelp}>
                        <HelpSection id="schemes" title={l.t("UI_SCHEME_EMERGENCY_FUND")}>{l.t("HELP_SCHEMES")}</HelpSection>
                    </span>
                    {EMERGENCY_FUND_LEVELS.map((percent) => renderPresetButton(
                        percent,
                        schemes.EmergencyFundWithdraw ?? 0,
                        isEmergencyFundLocked(percent),
                        emergencyFundTip(percent),
                        handleEmergencyFundClick
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
                <div style={s.schemeRow}>
                    <span style={s.schemeRowTitle}>{l.t("UI_SCHEME_FUEL_SIPHONING")}</span>
                    {FUEL_SIPHON_LEVELS.map((percent) => renderPresetButton(
                        percent,
                        schemes.FuelSiphonPercent ?? 0,
                        isFuelSiphonLocked(percent),
                        fuelSiphonTip(percent),
                        handleFuelSiphonClick
                    ))}
                </div>
                {schemes.FuelSiphonPercent > 0 && (
                    <div style={s.schemeInfo}>
                        {l.t("UI_SCHEME_FUEL_PROFIT", Math.round(schemes.FuelSiphonPercent * 1.3))}
                    </div>
                )}
            </div>

            {/* Construction Kickback */}
            <div style={s.section}>
                <div style={s.schemeRow}>
                    <span style={s.schemeRowTitle}>{l.t("UI_SCHEME_CONSTRUCTION_KICKBACK")}</span>
                    {CONSTRUCTION_KICKBACK_LEVELS.map((percent) => renderPresetButton(
                        percent,
                        schemes.ConstructionKickbackPercent ?? 0,
                        isConstructionKickbackLocked(percent),
                        constructionKickbackTip(percent),
                        handleConstructionKickbackClick
                    ))}
                </div>
                {schemes.ConstructionKickbackPending > 0 && (
                    <div style={s.schemeInfo}>
                        {/* formatMoney, not formatCostArg: pending is in raw dollars and can
                            be well under $1k — the /1000 "{0}K" formatter rounds it to 0. */}
                        {l.t("UI_SCHEME_KICKBACK_PENDING", formatMoney(schemes.ConstructionKickbackPending ?? 0))}
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
            description="Emergency Fund withdrawals, fuel siphoning and construction kickbacks let you funnel cash from official budgets into the shadow wallet at the cost of corruption heat and investigation risk."
        >
            <SchemesSectionContent actions={actions} />
        </GlassCase>
    );
});
SchemesSection.displayName = "SchemesSection";
