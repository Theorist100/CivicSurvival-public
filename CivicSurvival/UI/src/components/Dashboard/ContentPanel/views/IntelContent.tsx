/**
 * IntelContent - Tension, Forecast, Enemy Focus, Insider + OpSec + Counter-Measures
 * HYBRID OPS domain → INTEL view
 * Layout: Left (Tension + Enemy Focus + Attack Forecast) | Right (Insider + Intel & Security + Counter-Measures)
 */

import React, { memo, useMemo } from "react";
import { HoverTip } from "../../../shared/common/HoverTip";
import { HelpSection } from "../../../shared/common/HelpSection";
import { Column } from "@coherent";
import { useTheme, useAccents, formatCostArg } from "@themes";
import { bindingDataOrDefault, type ExportDto, type IntelDto, type SpotterDto, useExport, useIntel, useSpotters } from "@hooks/domain";
import { DEFAULT_INTEL_DTO, DEFAULT_SPOTTER_DTO } from "../../../../types/domainDtos";
import { currentAct$, Act, scenarioType$, ScenarioType } from "@hooks/bindings/scenarioDirectorBindings";
import { useIntelActions, useRequestAction, useSpotterActions } from "@hooks/actions";
import { useSafeNumber } from "@hooks/useSafeBinding";
import { optionalBindingData, useOptionalBinding } from "@hooks/useOptionalBinding";
import { useLocale } from "@locales";
import {
    TensionSection,
    EnemyFocusSection,
    AttackForecastSection,
    InsiderSection,
    PreWarStatusSection,
} from "../../../war/sections";
import { SectionHeader, StatRow } from "../../../shared/ui";
import { CounterSection } from "../../../shadow/sections";
import {
    createWarViewsStyles,
    getPenaltyColor,
} from "../WarViews.styles";

type IntelActions = ReturnType<typeof useIntelActions>;
type SpotterActions = ReturnType<typeof useSpotterActions>;

interface IntelInsiderProps {
    actions: IntelActions;
    intel: IntelDto;
    exportData: ExportDto | null;
}

interface OpSecPanelProps {
    actions: SpotterActions;
    spotters: SpotterDto;
}

const OpSecPanel = memo(({ actions, spotters }: OpSecPanelProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarViewsStyles(theme, accents), [theme, accents]);
    const penaltyColor = getPenaltyColor(spotters.SpotterPenaltyPercent ?? 0, accents, theme);
    // Act overlay: Can* from the DTO is local-facts-only; act-lock (currentAct < Crisis)
    // is applied here in the frontend. Click is also gated by AddScenarioTrigger and the
    // backend command system is the authoritative act guard.
    const currentAct = useSafeNumber(currentAct$, Act.PreWar, "currentAct");
    const isPreWar = currentAct < Act.Crisis;
    const sbuAction = useRequestAction(() => {
        actions.sbuVisit();
        return true;
    }, spotters.SpotterActionRequest);
    const evacuationAction = useRequestAction(() => {
        actions.evacuation();
        return true;
    }, spotters.SpotterActionRequest);
    const counterOsintAction = useRequestAction(() => {
        actions.toggleCounterOSINT();
        return true;
    }, spotters.SpotterActionRequest);
    const sbuLockedReason = spotters.SbuVisitLockedReasonId ? l.tDynamic(spotters.SbuVisitLockedReasonId) : l.t("OPSEC_SBU_TIP");
    const evacuationLockedReason = spotters.EvacuationRunLockedReasonId ? l.tDynamic(spotters.EvacuationRunLockedReasonId) : l.t("OPSEC_EVAC_TIP");
    const counterLockedReason = spotters.CounterOSINTLockedReasonId ? l.tDynamic(spotters.CounterOSINTLockedReasonId) : "";
    const opsecPending =
        sbuAction.isPending ||
        evacuationAction.isPending ||
        counterOsintAction.isPending ||
        spotters.SpotterActionRequest.Status === "pending";

    return (
        <>
            <SectionHeader
                title={l.t("INTEL_PANEL_TITLE")}
                titleAs="span"
                titleStyle={s.panelHeader(accents.schemes.accent)}
                extra={spotters.SpotterCount !== 0 && (
                    <span style={s.statusBadge(accents.crisis.accent, true)}>
                        {l.t("OPSEC_COMPROMISED")}
                    </span>
                )}
                help={<HelpSection id="intel" title={l.t("INTEL_PANEL_TITLE")}>{l.t("HELP_INTEL")}</HelpSection>}
            />

            <StatRow
                label={l.t("OPSEC_SPOTTERS")}
                value={spotters.SpotterCount}
                color={spotters.SpotterCount !== 0 ? accents.crisis.accent : accents.schemes.accent}
            />
            <StatRow
                label={l.t("OPSEC_PENALTY")}
                value={`-${spotters.SpotterPenaltyPercent ?? 0}%`}
                color={penaltyColor}
            />

            {spotters.SpotterCount !== 0 ? (
                <div style={s.note}>{l.t("OPSEC_VALERA_NOTE")}</div>
            ) : (
                <div style={s.clear}>{l.t("OPSEC_CLEAR")}</div>
            )}
            <div style={{ display: "flex", marginTop: "8rem" }}>
                <HoverTip text={spotters.CanSbuVisit ? l.t("OPSEC_SBU_TIP") : sbuLockedReason} style={{ flex: 1, marginRight: "8rem" }}>
                    <button
                        style={{ ...s.button(accents.operations.accent), width: "100%" }}
                        onClick={sbuAction.execute}
                        disabled={opsecPending || isPreWar || !spotters.CanSbuVisit}
                    >
                        {sbuAction.isPending ? l.t("UI_PROCESSING") : l.t("OPSEC_SBU_BTN", formatCostArg(spotters.SbuVisitCost))}
                    </button>
                </HoverTip>
                <HoverTip text={spotters.CanEvacuationRun ? l.t("OPSEC_EVAC_TIP") : evacuationLockedReason} style={{ flex: 1 }}>
                    <button
                        style={{ ...s.button(accents.crisis.accent), width: "100%" }}
                        onClick={evacuationAction.execute}
                        disabled={opsecPending || isPreWar || !spotters.CanEvacuationRun}
                    >
                        {evacuationAction.isPending ? l.t("UI_PROCESSING") : l.t("OPSEC_EVAC_BTN", formatCostArg(spotters.EvacuationCost))}
                    </button>
                </HoverTip>
            </div>
            <div style={s.stats}>
                {l.t("OPSEC_STATS", spotters.TotalSBUVisits, spotters.TotalEvacuations)}
            </div>

            <div style={s.divider} />
            <CounterSection />
            <div style={s.divider} />
            <div style={s.subsectionTitle(accents.schemes.accent)}>
                {l.t("COUNTERMEASURES_SUBTITLE")}
            </div>

            <StatRow
                label={l.t("COSINT_LABEL")}
                value={spotters.CounterOSINTActive ? l.t("COSINT_ACTIVE") : l.t("COSINT_OFF")}
                color={spotters.CounterOSINTActive ? accents.schemes.accent : theme.colors.textMuted}
            />

            <div style={s.note}>{l.t("COSINT_NOTE")}</div>

            <HoverTip text={spotters.CanToggleCounterOSINT ? l.t("COSINT_NOTE") : counterLockedReason}>
                <button
                    style={s.buttonFull(spotters.CounterOSINTActive ? accents.crisis.accent : accents.schemes.accent)}
                    onClick={counterOsintAction.execute}
                    disabled={opsecPending || isPreWar || !spotters.CanToggleCounterOSINT}
                >
                    {counterOsintAction.isPending
                        ? l.t("UI_PROCESSING")
                        : spotters.CounterOSINTActive
                        ? l.t("COSINT_DISABLE", formatCostArg(spotters.CounterOSINTDailyCost))
                        : l.t("COSINT_ENABLE", formatCostArg(spotters.CounterOSINTDailyCost))}
                </button>
            </HoverTip>
        </>
    );
});
OpSecPanel.displayName = "OpSecPanel";

const IntelForecastColumn = memo(({ intel }: { intel: IntelDto }) => {
    const theme = useTheme();
    // In Pre-War the tension/enemy-focus/forecast widgets have no data yet. Replace them
    // with a plain "the war starts on its own" explainer so the silence reads as an expected
    // phase — the Village path has no cold-open at all, so this is its only on-screen cue.
    const currentAct = useSafeNumber(currentAct$, Act.PreWar, "currentAct");
    const scenarioType = useSafeNumber(scenarioType$, ScenarioType.None, "scenarioType");
    const isPreWar = currentAct < Act.Crisis;

    return (
        <Column style={{
            width: "280rem",
            minWidth: "280rem",
            maxWidth: "280rem",
            borderRight: `2rem solid ${theme.colors.border}`,
            padding: theme.spacing.md,
            overflowY: "auto" as const,
            overflowX: "hidden" as const,
            flexShrink: 0,
        }}>
            {isPreWar ? (
                <PreWarStatusSection isVillage={scenarioType === ScenarioType.Village} />
            ) : (
                <>
                    <div style={{ marginBottom: "8rem" }}>
                        <TensionSection
                            tensionLevel={intel.TensionLevel ?? 0}
                            tensionStatus={intel.TensionStatus ?? "LOW"}
                        />
                    </div>
                    <EnemyFocusSection
                        energyFocusRange={intel.EnergyFocusRange ?? null}
                        infraFocusRange={intel.InfraFocusRange ?? null}
                        residentialFocusRange={intel.ResidentialFocusRange ?? null}
                    />

                    <div style={{ marginTop: "8rem" }}>
                        <AttackForecastSection
                            waveTypePrediction={intel.WaveTypePrediction ?? null}
                            isMassiveStrike={intel.IsMassiveStrike ?? false}
                            timeEstimate={intel.TimeEstimate ?? null}
                            threatComposition={intel.ThreatComposition ?? null}
                            estimatedShaheds={intel.EstimatedShaheds ?? 0}
                            estimatedBallistics={intel.EstimatedBallistics ?? 0}
                            hasInsiderInfo={intel.HasInsider ?? false}
                        />
                    </div>
                </>
            )}
        </Column>
    );
});
IntelForecastColumn.displayName = "IntelForecastColumn";

const IntelInsiderBlock = memo(({ actions, intel, exportData }: IntelInsiderProps) => {
    const currentAct = useSafeNumber(currentAct$, Act.PreWar, "currentAct");
    const isPreWar = currentAct < Act.Crisis;

    if (isPreWar) return null;

    return (
        <InsiderSection
            actions={actions}
            hasInsiderInfo={intel.HasInsider ?? false}
            insiderCost={intel.InsiderCost ?? 0}
            canBuyInsider={intel.CanBuyInsider}
            insiderLockedReasonId={intel.InsiderLockedReasonId}
            offshoreBalance={exportData?.OffshoreBalance ?? 0}
            priceModifier={intel.TensionPriceModifierPercent ?? 0}
            isFrozen={exportData?.IsFrozen ?? false}
            insiderRequest={intel.InsiderRequest}
        />
    );
});
IntelInsiderBlock.displayName = "IntelInsiderBlock";

const IntelContentComponent: React.FC = () => {
    const theme = useTheme();
    const exportData = useOptionalBinding(useExport());
    const readyExportData = optionalBindingData(exportData);
    const intelState = useIntel();
    const intelActions = useIntelActions();
    const spotterState = useSpotters();
    const spotterActions = useSpotterActions();
    const intelReady = bindingDataOrDefault(intelState, DEFAULT_INTEL_DTO);
    const spotterReady = bindingDataOrDefault(spotterState, DEFAULT_SPOTTER_DTO);

    return (
        <div style={{
            display: "flex",
            height: "100%",
            overflow: "hidden" as const,
            position: "relative" as const,
        }}>
            <IntelForecastColumn intel={intelReady} />

            <Column style={{
                flex: 1,
                padding: theme.spacing.md,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                minWidth: 0,
            }}>
                <OpSecPanel actions={spotterActions} spotters={spotterReady} />

                <IntelInsiderBlock actions={intelActions} intel={intelReady} exportData={readyExportData} />
            </Column>
        </div>
    );
};

export const IntelContent = memo(IntelContentComponent);

IntelContent.displayName = "IntelContent";
