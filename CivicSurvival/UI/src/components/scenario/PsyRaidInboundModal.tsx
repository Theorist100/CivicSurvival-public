/**
 * PsyRaidInboundModal — an info-war raid is IN THE AIR.
 *
 * The counterpart of PsyDebriefingModal at the other end of the raid. The debrief tells the player
 * what happened; this one interrupts him while he can still change it — the hero's shield is decided
 * at the moment of landing, so advice delivered afterwards is worthless.
 *
 * It carries the counter-play, because the phase badge ("PSY ATTACK") names the event and explains
 * nothing: what the raid does, which speaker answers which carrier, and that the broadcast — unlike
 * the hero — does NOT blunt a landed contact.
 *
 * Everything shown respects the intel fog: an unread carrier/seam says "unknown — raise intel"
 * rather than guessing. The modal auto-closes when the raid lands (CognitiveUISystem drops it), so
 * it can never linger over a window that has already shut.
 */

import React, { useMemo } from "react";
import { createBaseModalStyles, useModalPalette } from "../../themes";
import { AlertBox, StatRow, StatSection, Badge, defineModal } from "../shared/modal";
import { bindingDataOrDefault, useCognitive } from "../../hooks/domain";
import { dismissPsyInbound } from "../../hooks/bindings/scenarioDirectorBindings";
import { useLocale } from "../../locales";
import { DEFAULT_COGNITIVE_DTO } from "../../types/domainDtos";
import { forecastTypeLabelKey, forecastStratumNameKey } from "../war/sections/cognitive/opinion/opinionData";

const PsyRaidInboundModalView: React.FC = () => {
    const l = useLocale();
    const cog = bindingDataOrDefault(useCognitive(), DEFAULT_COGNITIVE_DTO);
    const m = useModalPalette();

    const show = cog.ShowPsyInbound;
    // Same int→label helpers the forecast uses: null means the fog is still holding it back.
    const carrierKey = forecastTypeLabelKey(cog.PsyInboundType);
    const stratumKey = forecastStratumNameKey(cog.PsyInboundStratum);
    const counterReady = cog.PsyInboundCounterReady;

    // Crisis red while nothing answers this carrier; warning amber once a counter is on air —
    // the colour is the verdict, not decoration.
    const accentColor = counterReady ? m.accents.warning : m.accents.crisis;

    const base = useMemo(() => createBaseModalStyles(m, {
        accentColor,
        overlayOpacity: 0.85,
        width: "400rem",
    }), [accentColor, m]);

    const customStyles = useMemo(() => ({
        waveHeader: {
            fontSize: "13rem",
            color: m.textLabel,
            textTransform: "uppercase" as const,
            letterSpacing: "2rem",
            marginBottom: "8rem",
        } as React.CSSProperties,
        body: {
            fontSize: "13rem",
            lineHeight: 1.5,
            color: m.textBody,
            marginBottom: "14rem",
        } as React.CSSProperties,
        // The reference lines (who answers what, what the aid does) are lookup material, not
        // instructions — a bullet and a hanging indent keep them from reading as more orders.
        hint: {
            display: "flex",
            fontSize: "12rem",
            lineHeight: 1.45,
            color: m.textBody,
            marginBottom: "8rem",
        } as React.CSSProperties,
        bullet: {
            color: m.textLabel,
            marginRight: "8rem",
            flexShrink: 0,
        } as React.CSSProperties,
        badgeContainer: {
            marginTop: "12rem",
        } as React.CSSProperties,
    }), [m]);

    const buttonContainerStyle = useMemo(() => ({
        ...base.buttonContainer,
        marginTop: "18rem",
    } as React.CSSProperties), [base]);

    if (!show) return null;

    const etaText = cog.PsyInboundEtaHours >= 1
        ? l.t("UI_PSYINBOUND_ETA_HOURS", Math.round(cog.PsyInboundEtaHours))
        : l.t("UI_PSYINBOUND_ETA_NOW");

    return (
        <div style={base.overlay}>
            <div style={base.modal}>
                <div style={base.header}>
                    <div style={customStyles.waveHeader}>{l.t("UI_PSYINBOUND_WAVE", cog.PsyInboundWave)}</div>
                    <h2 style={base.title}>{l.t("UI_PSYINBOUND_TITLE")}</h2>
                    <div style={customStyles.badgeContainer}>
                        <Badge variant={counterReady ? "success" : "danger"}>
                            {counterReady ? l.t("UI_PSYINBOUND_READY") : l.t("UI_PSYINBOUND_NOT_READY")}
                        </Badge>
                    </div>
                </div>

                <div style={base.body}>
                    <div style={customStyles.body}>{l.t("UI_PSYINBOUND_WHAT")}</div>

                    {/* Titled for what it holds — the raid's read. It used to repeat the modal's own
                        headline word for word, two paragraphs below it. */}
                    <StatSection title={l.t("UI_PSYINBOUND_CONTACT")}>
                        <StatRow
                            label={l.t("UI_PSYINBOUND_CARRIER")}
                            value={carrierKey !== null ? l.t(carrierKey) : l.t("UI_PSYINBOUND_UNKNOWN")}
                            valueColor={carrierKey === null ? m.textLabel : undefined}
                        />
                        <StatRow
                            label={l.t("UI_PSYINBOUND_TARGET")}
                            value={stratumKey !== null ? l.t(stratumKey) : l.t("UI_PSYINBOUND_UNKNOWN")}
                            valueColor={stratumKey === null ? m.textLabel : undefined}
                        />
                        <StatRow
                            label={l.t("UI_PSYINBOUND_ETA")}
                            value={etaText}
                            valueColor={cog.PsyInboundEtaHours < 1 ? m.danger.text : m.warning.text}
                        />
                    </StatSection>

                    {/* Four lines of equal weight read as a wall, and the one ORDER among them drowns
                        in the reference material. No divider above: the last StatRow already rules a
                        line, and a second one left an empty band between them. */}
                    <StatSection title={l.t("UI_PSYINBOUND_HOW_TITLE")}>
                        <AlertBox
                            variant={counterReady ? "info" : "danger"}
                            title={l.t("UI_PSYINBOUND_HOW_NOW")}
                        >
                            {l.t("UI_PSYINBOUND_HOW_HERO")}
                        </AlertBox>

                        <div style={customStyles.hint}>
                            <span style={customStyles.bullet}>•</span>
                            <span>{l.t("UI_PSYINBOUND_HOW_RPS")}</span>
                        </div>
                        <div style={customStyles.hint}>
                            <span style={customStyles.bullet}>•</span>
                            <span>{l.t("UI_PSYINBOUND_HOW_AID")}</span>
                        </div>

                        {/* This line is a denial, not advice: on air the telemarathon looks like an
                            answer, and the player has to be told it is not one for THIS raid. */}
                        <AlertBox variant="warning" title={l.t("UI_PSYINBOUND_HOW_AIR_TITLE")}>
                            {l.t("UI_PSYINBOUND_HOW_AIR")}
                        </AlertBox>
                    </StatSection>

                    <div style={buttonContainerStyle}>
                        <button style={base.primaryButton} onClick={dismissPsyInbound}>
                            {l.t("UI_PSYINBOUND_ACK")}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const PsyRaidInboundModalDef = defineModal({
    id: "PsyRaidInbound",
    render: () => <PsyRaidInboundModalView />,
});
