/**
 * CounterSection - Countermeasures status and Exposure Risk
 */

import React, { memo, useCallback, useMemo, useRef } from "react";
import { useTheme, useAccents } from "../../../themes";
import { bindingDataOrDefault, useCountermeasures } from "@hooks/domain";
import { useLocale, type TranslationKey } from "../../../locales";
import {
    createSectionStyles,
    getPhaseColor,
    getProgressColor,
    getRiskColor,
    getHeatColor,
} from "./SectionStyles";
import { IconArrowDown } from "../../shared/common/Icons";
import { GlassCase, InlineWarning, ProgressBar, SectionTitle, StatRow } from "../../shared/ui";
import { type InvestigationChoice, type PoliceChoice } from "../../../types/semantic";
import { DEFAULT_COUNTERMEASURES_DTO, type BribeRiskWarning, type CounterChoiceType, type CounterHeatLevel, type CounterPhase } from "../../../types/domainDtos";
import { useCorruptionActions, useRequestAction } from "@hooks/actions";

type CorruptionActions = ReturnType<typeof useCorruptionActions>;

const PHASE_KEYS: Record<CounterPhase, TranslationKey> = {
    Clear: "UI_COUNTER_PHASE_IDLE",
    Idle: "UI_COUNTER_PHASE_IDLE",
    Suspicion: "UI_COUNTER_PHASE_SUSPICION",
    Investigation: "UI_COUNTER_PHASE_INVESTIGATION",
    "Waiting for Decision": "UI_COUNTER_PHASE_WAITING_DECISION",
    "Article Published": "UI_COUNTER_PHASE_ARTICLE_PUBLISHED",
    "Police Investigation": "UI_COUNTER_PHASE_POLICE_DECISION",
    "Under Investigation": "UI_COUNTER_PHASE_UNDER_INVESTIGATION",
    Arrested: "UI_COUNTER_PHASE_ARRESTED",
    Unknown: "UI_COUNTER_PHASE_UNKNOWN",
    UI_COUNTER_PHASE_IDLE: "UI_COUNTER_PHASE_IDLE",
    UI_COUNTER_PHASE_SUSPICION: "UI_COUNTER_PHASE_SUSPICION",
    UI_COUNTER_PHASE_INVESTIGATION: "UI_COUNTER_PHASE_INVESTIGATION",
    UI_COUNTER_PHASE_WAITING_DECISION: "UI_COUNTER_PHASE_WAITING_DECISION",
    UI_COUNTER_PHASE_ARTICLE_PUBLISHED: "UI_COUNTER_PHASE_ARTICLE_PUBLISHED",
    UI_COUNTER_PHASE_POLICE_DECISION: "UI_COUNTER_PHASE_POLICE_DECISION",
    UI_COUNTER_PHASE_UNDER_INVESTIGATION: "UI_COUNTER_PHASE_UNDER_INVESTIGATION",
    UI_COUNTER_PHASE_ARRESTED: "UI_COUNTER_PHASE_ARRESTED",
    UI_COUNTER_PHASE_UNKNOWN: "UI_COUNTER_PHASE_UNKNOWN",
};

const HEAT_KEYS: Record<CounterHeatLevel, TranslationKey> = {
    Safe: "UI_COUNTER_HEAT_LEVEL_SAFE",
    Warning: "UI_COUNTER_HEAT_LEVEL_WARNING",
    Danger: "UI_COUNTER_HEAT_LEVEL_DANGER",
    Critical: "UI_COUNTER_HEAT_LEVEL_CRITICAL",
    UI_COUNTER_HEAT_LEVEL_SAFE: "UI_COUNTER_HEAT_LEVEL_SAFE",
    UI_COUNTER_HEAT_LEVEL_WARNING: "UI_COUNTER_HEAT_LEVEL_WARNING",
    UI_COUNTER_HEAT_LEVEL_DANGER: "UI_COUNTER_HEAT_LEVEL_DANGER",
    UI_COUNTER_HEAT_LEVEL_CRITICAL: "UI_COUNTER_HEAT_LEVEL_CRITICAL",
};

const RISK_WARNING_KEYS: Partial<Record<BribeRiskWarning, TranslationKey>> = {
    RISK_BRIBE_INVESTIGATION_WARNING: "RISK_BRIBE_INVESTIGATION_WARNING",
    RISK_BRIBE_POLICE_WARNING: "RISK_BRIBE_POLICE_WARNING",
};

const INVESTIGATION_CHOICES: Array<{ key: TranslationKey; value: InvestigationChoice }> = [
    { key: "UI_COUNTER_CHOICE_BRIBE", value: 1 },
    { key: "UI_COUNTER_CHOICE_CENSOR", value: 2 },
    { key: "UI_COUNTER_CHOICE_WAIT", value: 3 },
    { key: "UI_COUNTER_CHOICE_CONFESS", value: 4 },
];

const POLICE_CHOICES: Array<{ key: TranslationKey; value: PoliceChoice }> = [
    { key: "UI_COUNTER_CHOICE_COOPERATE", value: 1 },
    { key: "UI_COUNTER_CHOICE_DESTROY", value: 2 },
    { key: "UI_COUNTER_CHOICE_BRIBE", value: 3 },
];

const ChoiceType: Record<"None" | "Investigation" | "Police", CounterChoiceType> = {
    None: 0,
    Investigation: 1,
    Police: 2,
};

interface CounterSectionContentProps {
    corruptionActions: CorruptionActions;
}

const CounterSectionContent: React.FC<CounterSectionContentProps> = ({ corruptionActions }) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createSectionStyles(theme, accents), [theme, accents]);
    const cmState = useCountermeasures();
    const cm = bindingDataOrDefault(cmState, DEFAULT_COUNTERMEASURES_DTO);
    const phaseKey = cm ? PHASE_KEYS[cm.CountermeasuresPhase] : "UI_COUNTER_PHASE_IDLE";
    const heatKey = cm ? HEAT_KEYS[cm.HeatLevel] : "UI_COUNTER_HEAT_LEVEL_SAFE";
    const warningKey = cm ? RISK_WARNING_KEYS[cm.BribeRiskWarning] : undefined;
    const heatColor = getHeatColor(heatKey, accents, theme);
    const selectedChoiceRef = useRef<InvestigationChoice | PoliceChoice>(1);

    // Colors
    const phaseColor = getPhaseColor(phaseKey, accents);
    const investigationColor = getProgressColor(cm?.InvestigationProgress ?? 0, accents);
    const riskColor = getRiskColor(cm?.CorruptionScore ?? 0, accents, theme);
    const showInvestigation =
        phaseKey !== "UI_COUNTER_PHASE_IDLE";
    const choiceButtons = cm?.ChoiceType === ChoiceType.Police ? POLICE_CHOICES : INVESTIGATION_CHOICES;
    const showChoiceControls =
        cm?.ChoiceRequired &&
        (cm.ChoiceType === ChoiceType.Investigation || cm.ChoiceType === ChoiceType.Police);
    const bribeAvailability = cm?.BribeAvailability;
    const bribeCost = bribeAvailability?.EffectiveCost && bribeAvailability.EffectiveCost > 0
        ? bribeAvailability.EffectiveCost
        : cm?.BribeCost ?? 0;
    const bribeLockedReason = bribeAvailability?.LockedReasonId ? l.tDynamic(bribeAvailability.LockedReasonId) : "";

    const choiceAction = useRequestAction(() => {
        const choice = selectedChoiceRef.current;
        if (cm?.ChoiceType === ChoiceType.Police) {
            corruptionActions.makePoliceChoice(choice as PoliceChoice);
        } else {
            corruptionActions.makeInvestigationChoice(choice as InvestigationChoice);
        }
        return true;
    }, cm?.LastChoiceRequestResult);
    const requestPending = choiceAction.isPending || cm?.LastChoiceRequestResult.Status === "pending";

    const submitChoice = useCallback((choice: InvestigationChoice | PoliceChoice) => {
        if (requestPending) return;
        selectedChoiceRef.current = choice;
        choiceAction.execute();
    }, [choiceAction, requestPending]);

    return (
        <>
            {/* Countermeasures Status */}
            <div style={s.section}>
                <SectionTitle>{l.t("UI_COUNTER_TITLE")}</SectionTitle>
                <StatRow label={l.t("UI_COUNTER_PHASE")} value={l.t(phaseKey)} color={phaseColor} emphasis="title" />

                {showInvestigation && (
                    <>
                        <StatRow label={l.t("UI_COUNTER_INVESTIGATION")} value={`${cm.InvestigationProgress ?? 0}%`} color={investigationColor} />
                        <ProgressBar value={cm.InvestigationProgress ?? 0} color={investigationColor} height="4rem" />
                    </>
                )}

                {showChoiceControls && (
                    <div style={s.choicePanel}>
                        <div style={s.choiceTitle}>{l.t("UI_COUNTER_CHOICE_TITLE")}</div>
                        {bribeCost > 0 && (
                            <div style={s.choiceMeta}>
                                {l.t("RISK_BRIBE_COST", `$${bribeCost.toLocaleString()}`)}
                            </div>
                        )}
                        {warningKey && (
                            <InlineWarning accent={accents.resilience.accent}>{l.t(warningKey)}</InlineWarning>
                        )}
                        {bribeLockedReason && !cm.BribeAvailability.CanRun && (
                            <InlineWarning accent={accents.crisis.accent}>{bribeLockedReason}</InlineWarning>
                        )}
                        <div style={s.choiceGrid}>
                            {choiceButtons.map((choice) => {
                                const isBribeChoice = cm.ChoiceType === ChoiceType.Police
                                    ? choice.value === 3
                                    : choice.value === 1;
                                const disabled = requestPending || (isBribeChoice && !cm.BribeAvailability.CanRun);
                                return (
                                    <div key={choice.value} style={s.choiceGridChild}>
                                        <button
                                            type="button"
                                            style={s.choiceButton(disabled, accents.crisis.accent)}
                                            disabled={disabled}
                                            onClick={() => submitChoice(choice.value)}
                                        >
                                            {l.t(choice.key)}
                                        </button>
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                )}

                {cm.LastChoiceResult && (
                    <div style={s.choiceMeta}>{cm.LastChoiceResult}</div>
                )}
            </div>

            {/* Corruption Score (Threshold to game over) */}
            <div style={s.section}>
                <SectionTitle>{l.t("UI_COUNTER_CORRUPTION")}</SectionTitle>
                <StatRow label={l.t("UI_COUNTER_LEVEL")} value={`${cm.CorruptionScore ?? 0}%`} color={riskColor} emphasis="title" />
                <ProgressBar value={cm.CorruptionScore ?? 0} color={riskColor} height="4rem" />
                {cm.SanctionsSuppressingCorruption && (
                    <div style={{ marginTop: theme.spacing.xs, fontSize: theme.typography.sizeXS, color: accents.resilience.accent, display: "flex", alignItems: "center" }}>
                        <span style={{ marginRight: theme.spacing.xs }}><IconArrowDown /></span>
                        {l.t("UI_COUNTER_SANCTIONS_SUPPRESSION")}
                    </div>
                )}
            </div>

            {/* Heat Level (Investigation Risk) */}
            <div style={s.section}>
                <SectionTitle>{l.t("UI_COUNTER_HEAT")}</SectionTitle>
                <StatRow label={l.t("UI_COUNTER_LEVEL")} value={`${cm.Heat ?? 0}% (${l.t(heatKey)})`} color={heatColor} emphasis="title" />
                <ProgressBar value={cm.Heat ?? 0} color={heatColor} height="4rem" />

                {heatKey === "UI_COUNTER_HEAT_LEVEL_CRITICAL" ? (
                    <InlineWarning accent={theme.colors.errorBright} variant="critical">{l.t("UI_COUNTER_HEAT_CRITICAL")}</InlineWarning>
                ) : null}
                {heatKey === "UI_COUNTER_HEAT_LEVEL_DANGER" ? (
                    <InlineWarning accent={accents.crisis.accent}>{l.t("UI_COUNTER_HEAT_DANGER")}</InlineWarning>
                ) : null}
                {heatKey === "UI_COUNTER_HEAT_LEVEL_WARNING" ? (
                    <InlineWarning accent={accents.resilience.accent}>{l.t("UI_COUNTER_HEAT_WARNING")}</InlineWarning>
                ) : null}

                {/* International scrutiny warning (scandal threshold ~40) */}
                {(cm.Heat ?? 0) >= 40 && (
                    <InlineWarning accent={accents.operations.accent}>{l.t("UI_COUNTER_HEAT_INTL")}</InlineWarning>
                )}
            </div>
        </>
    );
};

export const CounterSection = memo(() => {
    const actions = useCorruptionActions();
    return (
        <GlassCase
            feature="Countermeasures"
            name="Counter-Investigation"
            description="Investigative reporters and police can open cases on corrupt deals. Choose how to respond: bribe, censor, wait, or confess — each path has its own corruption-score and heat consequences."
        >
            <CounterSectionContent corruptionActions={actions} />
        </GlassCase>
    );
});
CounterSection.displayName = "CounterSection";
