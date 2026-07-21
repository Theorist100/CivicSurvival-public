/**
 * DebriefingModal - Post-Wave Report
 *
 * Shows after each wave ends (Recovery phase):
 * - Casualties & Damage cost
 * - AA Performance (shots fired, intercepted, efficiency)
 *
 * Auto-dismisses when next wave starts (Alert phase).
 *
 * Uses shared modal components for consistent styling.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette, type ModalPalette } from "../../themes";
import { StatRow, StatSection, ProgressBar, Badge, defineModal } from "../shared/modal";
import { bindingDataOrDefault, useThreat } from "../../hooks/domain";
import { DEFAULT_GLOBAL_NEWS_STATE, useGlobalNews } from "../../hooks/state/useGlobalNews";
import { useBetaWave } from "../../hooks/useBetaWave";
import { dismissDebriefing } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";
import { DEFAULT_THREAT_DTO } from "../../types/domainDtos";

// Mirror the server-side per-wave clamps (CivicServer ArenaRepository) so the
// shown points never exceed what the ladder will actually credit.
const MAX_INTERCEPTED_PER_WAVE = 500;
const MAX_WAVE_NUMBER = 200;

// Accent color based on performance
const getAccentColor = (efficiency: number, accents: ModalPalette["accents"]): string => {
    if (efficiency >= 70) return accents.info;      // Good: blue
    if (efficiency >= 40) return accents.warning;   // Okay: orange
    return accents.crisis;                          // Bad: red
};

const DebriefingModalView: React.FC = () => {
    const l = useLocale();
    const threat = bindingDataOrDefault(useThreat(), DEFAULT_THREAT_DTO);
    const showDebriefing = threat.ShowDebriefing;
    const waveNumber = threat.DebriefingWave;
    const intercepted = threat.DebriefingIntercepted;
    const shotsFired = threat.DebriefingShotsFired;
    const casualties = threat.DebriefingCasualties;
    const damageCost = threat.DebriefingDamageCost;
    const infraDamageCost = threat.DebriefingInfraDamageCost;
    const totalThreats = threat.DebriefingTotalThreats;
    const efficiency = threat.DebriefingEfficiency;

    const m = useModalPalette();
    const accentColor = useMemo(() => getAccentColor(efficiency, m.accents), [efficiency, m]);

    // Ladder feedback: instant "+N pts" when the wave result will reach the
    // server (arena open + Online on), an opt-in nudge when Online is off.
    const arenaOpen = useBetaWave("ArenaUI").status === "open";
    const news = bindingDataOrDefault(useGlobalNews(), DEFAULT_GLOBAL_NEWS_STATE);
    const onlineEnabled = news.networkConnectionEnabled;
    const arenaPoints = Math.min(Math.max(intercepted, 0), MAX_INTERCEPTED_PER_WAVE)
        * Math.min(Math.max(waveNumber, 1), MAX_WAVE_NUMBER);

    const offlineNudgeStyle = useMemo((): React.CSSProperties => ({
        marginTop: "8rem",
        fontSize: "11rem",
        color: m.textLabel,
        fontStyle: "italic" as const,
    }), [m.textLabel]);

    const customStyles = useMemo(() => ({
        waveHeader: {
            fontSize: "13rem",
            color: m.textLabel,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            marginBottom: "8rem",
        } as React.CSSProperties,
        badgeContainer: {
            marginTop: "12rem",
        } as React.CSSProperties,
    }), [m]);

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor,
        overlayOpacity: 0.85,
        width: "380rem",    }), [accentColor, m]);
    const buttonContainerStyle = useMemo(() => ({
        ...base.buttonContainer,
        marginTop: "20rem",
    } as React.CSSProperties), [base]);

    if (!showDebriefing) return null;

    const isGoodResult = casualties === 0 && damageCost === 0 && infraDamageCost === 0;
    const interceptRate = totalThreats > 0 ? Math.round((intercepted / totalThreats) * 100) : 0;
    const missedShots = Math.max(0, shotsFired - intercepted);

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <div style={customStyles.waveHeader}>{l.t("UI_DEBRIEF_TITLE", waveNumber)}</div>
                    <h2 style={base.title}>{l.t("UI_DEBRIEF_SUBTITLE")}</h2>
                    <div style={customStyles.badgeContainer}>
                        <Badge variant={isGoodResult ? "success" : "danger"}>
                            {isGoodResult ? l.t("UI_DEBRIEF_ALL_CLEAR") : l.t("UI_DEBRIEF_DAMAGE")}
                        </Badge>
                    </div>
                </div>

                <div style={base.body}>
                    {/* Impact Assessment */}
                    <StatSection title={l.t("UI_DEBRIEF_IMPACT")}>
                        <StatRow
                            label={l.t("UI_DEBRIEF_CASUALTIES")}
                            value={casualties}
                            valueColor={casualties > 0 ? m.danger.text : m.success.text}
                        />
                        <StatRow
                            label={l.t("UI_DEBRIEF_DAMAGE_COST")}
                            value={`$${damageCost.toLocaleString("en-US")}`}
                            valueColor={damageCost > 0 ? m.warning.text : m.success.text}
                        />
                        {infraDamageCost > 0 && (
                            <StatRow
                                label={l.t("UI_DEBRIEF_INFRA_DAMAGE")}
                                value={`$${infraDamageCost.toLocaleString("en-US")}`}
                                valueColor={m.warning.text}
                            />
                        )}
                    </StatSection>

                    {/* AA Performance */}
                    <StatSection title={l.t("UI_DEBRIEF_AA_PERF")} showDivider>
                        <StatRow
                            label={l.t("UI_DEBRIEF_INTERCEPTED")}
                            value={`${intercepted} / ${totalThreats}`}
                        />
                        <StatRow
                            label={l.t("UI_DEBRIEF_INTERCEPT_RATE")}
                            value={`${interceptRate}%`}
                            valueColor={accentColor}
                        />
                        <StatRow label={l.t("UI_DEBRIEF_SHOTS_FIRED")} value={shotsFired} />
                        <StatRow
                            label={l.t("UI_DEBRIEF_MISSED_SHOTS")}
                            value={missedShots}
                            valueColor={missedShots > shotsFired * 0.5 ? m.warning.text : undefined}
                        />
                        {arenaOpen && onlineEnabled && arenaPoints > 0 && (
                            <StatRow
                                label={l.t("UI_ARENA_PTS_EARNED")}
                                value={l.t("UI_ARENA_PTS_VALUE", arenaPoints)}
                                valueColor={m.success.text}
                            />
                        )}
                    </StatSection>
                    {arenaOpen && !onlineEnabled && (
                        <div style={offlineNudgeStyle}>{l.t("UI_ARENA_PTS_OFFLINE")}</div>
                    )}

                    {/* Efficiency Progress Bar */}
                    <ProgressBar
                        value={efficiency}
                        label={l.t("UI_DEBRIEF_EFFICIENCY")}
                        suffix={efficiency < 40 ? l.t("UI_DEBRIEF_HIGH_EVASION") : undefined}
                    />

                    {/* Dismiss Button */}
                    <div style={buttonContainerStyle}>
                        <button style={base.primaryButton} onClick={dismissDebriefing}>
                            {l.t("UI_DEBRIEF_ACK")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const DebriefingModalDef = defineModal({
    id: "Debriefing",
    render: () => <DebriefingModalView />,
});
