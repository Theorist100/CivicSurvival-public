/**
 * PsyDebriefingModal - Info-War (PSYOPS) After-Action Report
 *
 * The cognitive sibling of DebriefingModal. Shows after a wave's psy raids finish landing:
 * - Attack vectors (carriers that landed, targeted strata, peak intensity)
 * - Consequences (media trust, households affected, panic-buying run)
 * - Defense effectiveness (share of raids blunted by the deployed speaker)
 *
 * Data is a frozen event-time snapshot from CognitiveUISystem; the modal auto-dismisses after a
 * minimum display window. Uses the same shared modal components as the kinetic debriefing.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette, type ModalPalette } from "../../themes";
import { StatRow, StatSection, ProgressBar, Badge, defineModal, type BadgeVariant } from "../shared/modal";
import { bindingDataOrDefault, useCognitive } from "../../hooks/domain";
import { dismissPsyDebriefing } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale, type TranslationKey } from "../../locales";
import { DEFAULT_COGNITIVE_DTO } from "../../types/domainDtos";

// Defense outcome tier — a single classification of how well the narrative held (share of
// raids blunted). One source of truth drives BOTH the header accent colour and the status
// badge, so the border can never contradict the badge verdict (they used two independent
// thresholds before: accent graded at 70/40, badge flipped only at 100%).
type DefenseTier = "held" | "contested" | "breach";

const classifyDefense = (defenseEff: number): DefenseTier => {
    if (defenseEff >= 70) return "held";
    if (defenseEff >= 40) return "contested";
    return "breach";
};

const DEFENSE_TIER: Record<DefenseTier, {
    accent: keyof ModalPalette["accents"];
    badge: BadgeVariant;
    labelKey: TranslationKey;
}> = {
    held:      { accent: "info",    badge: "info",    labelKey: "UI_PSYDEBRIEF_DEFENDED" },
    contested: { accent: "warning", badge: "warning", labelKey: "UI_PSYDEBRIEF_CONTESTED" },
    breach:    { accent: "crisis",  badge: "danger",  labelKey: "UI_PSYDEBRIEF_BREACH" },
};

const PsyDebriefingModalView: React.FC = () => {
    const l = useLocale();
    const cog = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const show = cog.ShowPsyDebrief;
    const waveNumber = cog.PsyDebriefWave;
    const raids = cog.PsyDebriefRaidCount;
    const propaganda = cog.PsyDebriefPropaganda;
    const fakeVideo = cog.PsyDebriefFakeVideo;
    const rumor = cog.PsyDebriefRumor;
    const blunted = cog.PsyDebriefBlunted;
    const strataMask = cog.PsyDebriefStrataMask;
    const peakIntensity = cog.PsyDebriefPeakIntensity;
    const mediaTrust = cog.PsyDebriefMediaTrust;
    const householdsImpact = cog.PsyDebriefHouseholdsImpact;
    const foodRunActive = cog.PsyDebriefFoodRunActive;
    const infectedDelta = cog.PsyDebriefInfectedDelta;

    const m = useModalPalette();
    const defenseEff = raids > 0 ? Math.round((blunted / raids) * 100) : 0;
    const tier = DEFENSE_TIER[classifyDefense(defenseEff)];
    const accentColor = useMemo(() => m.accents[tier.accent], [tier, m]);

    const targetedStrata = useMemo(() => {
        const names: string[] = [];
        if (strataMask & 1) names.push(l.t("UI_OB_STRAT_POOR"));
        if (strataMask & 2) names.push(l.t("UI_OB_STRAT_MIDDLE"));
        if (strataMask & 4) names.push(l.t("UI_OB_STRAT_WEALTHY"));
        return names.join(", ");
    }, [strataMask, l]);

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
        width: "380rem",
    }), [accentColor, m]);
    const buttonContainerStyle = useMemo(() => ({
        ...base.buttonContainer,
        marginTop: "20rem",
    } as React.CSSProperties), [base]);

    if (!show) return null;

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <div style={customStyles.waveHeader}>{l.t("UI_PSYDEBRIEF_TITLE", waveNumber)}</div>
                    <h2 style={base.title}>{l.t("UI_PSYDEBRIEF_SUBTITLE")}</h2>
                    <div style={customStyles.badgeContainer}>
                        <Badge variant={tier.badge}>
                            {l.t(tier.labelKey)}
                        </Badge>
                    </div>
                </div>

                <div style={base.body}>
                    {/* Attack Vectors */}
                    <StatSection title={l.t("UI_PSYDEBRIEF_VECTORS")}>
                        <StatRow label={l.t("UI_PSYDEBRIEF_RAIDS")} value={raids} />
                        {propaganda > 0 && (
                            <StatRow label={l.t("UI_PSYDEBRIEF_PROPAGANDA")} value={propaganda} />
                        )}
                        {fakeVideo > 0 && (
                            <StatRow label={l.t("UI_PSYDEBRIEF_FAKEVIDEO")} value={fakeVideo} />
                        )}
                        {rumor > 0 && (
                            <StatRow label={l.t("UI_PSYDEBRIEF_RUMOR")} value={rumor} />
                        )}
                        {targetedStrata.length > 0 && (
                            <StatRow label={l.t("UI_PSYDEBRIEF_TARGETS")} value={targetedStrata} />
                        )}
                        <StatRow
                            label={l.t("UI_PSYDEBRIEF_INTENSITY")}
                            value={`${Math.round(peakIntensity * 100)}%`}
                            valueColor={peakIntensity >= 0.6 ? m.danger.text : peakIntensity >= 0.3 ? m.warning.text : undefined}
                        />
                    </StatSection>

                    {/* Consequences — the infected delta is the strike's measured dose: the report
                        waits for the wave's landed window to close, so this number is the damage. */}
                    <StatSection title={l.t("UI_PSYDEBRIEF_CONSEQUENCES")} showDivider>
                        <StatRow
                            label={l.t("UI_PSYDEBRIEF_INFECTED")}
                            value={infectedDelta > 0 ? `+${infectedDelta.toLocaleString()}` : "0"}
                            valueColor={infectedDelta > 0 ? m.danger.text : m.success.text}
                        />
                        <StatRow
                            label={l.t("UI_IW_MEDIA_TRUST")}
                            value={`${Math.round(mediaTrust * 100)}%`}
                            valueColor={mediaTrust < 0.4 ? m.danger.text : mediaTrust < 0.7 ? m.warning.text : m.success.text}
                        />
                        <StatRow
                            label={l.t("UI_PSYDEBRIEF_HOUSEHOLDS")}
                            value={householdsImpact}
                            valueColor={householdsImpact > 0 ? m.warning.text : m.success.text}
                        />
                        {foodRunActive && (
                            <StatRow
                                label={l.t("UI_PSYDEBRIEF_FOOD_RUN")}
                                value={l.t("UI_PSYDEBRIEF_ACTIVE")}
                                valueColor={m.danger.text}
                            />
                        )}
                    </StatSection>

                    {/* Defense Effectiveness */}
                    <ProgressBar
                        value={defenseEff}
                        label={l.t("UI_PSYDEBRIEF_DEFENSE")}
                        suffix={raids > 0 && blunted === 0 ? l.t("UI_PSYDEBRIEF_NONE_BLUNTED") : undefined}
                    />

                    {/* Dismiss Button */}
                    <div style={buttonContainerStyle}>
                        <button style={base.primaryButton} onClick={dismissPsyDebriefing}>
                            {l.t("UI_PSYDEBRIEF_ACK")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const PsyDebriefingModalDef = defineModal({
    id: "PsyDebriefing",
    render: () => <PsyDebriefingModalView />,
});
