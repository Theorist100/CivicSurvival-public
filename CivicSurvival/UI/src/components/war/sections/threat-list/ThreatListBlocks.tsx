import React from "react";
import { Column, Row } from "@coherent";
import { useLocale, type TranslationKey } from "@locales";
import { ProgressBar } from "../../../shared/ui";
import { type WavePhase } from "../../../../types/domainDtos";

type ThreatGroup = [TranslationKey, { count: number; minEta: number }];

type ThreatListStyles = {
    waveBox: React.CSSProperties;
    phaseIndicator: React.CSSProperties;
    phaseName: React.CSSProperties;
    waveLabel: React.CSSProperties;
    countdownLabel: React.CSSProperties;
    countdownValue: React.CSSProperties;
    elapsed: React.CSSProperties;
    legendBox: React.CSSProperties;
    legendTitle: React.CSSProperties;
    typeName: React.CSSProperties;
    typeCount: React.CSSProperties;
    noThreats: React.CSSProperties;
    identifyBox: (confirmed: boolean) => React.CSSProperties;
    identifyLabel: React.CSSProperties;
    priorityBadge: React.CSSProperties;
    identifyTrack: React.CSSProperties;
    identifyFill: React.CSSProperties;
};

interface WaveStatusBlockProps {
    phase: WavePhase;
    phaseName: string;
    timeInPhase: number;
    phaseEndTime: number;
    waveNumber: number;
    scenarioStarted: boolean;
    waitingForLaunchWindow: boolean;
    progressValue: number;
    progressColor: string;
    styles: ThreatListStyles;
}

interface ThreatLegendBlockProps {
    threatGroups: ThreatGroup[];
    threatGroupStyles: Partial<Record<TranslationKey, { group: React.CSSProperties; indicator: React.CSSProperties }>>;
    etaStyles: {
        urgent: React.CSSProperties;
        normal: React.CSSProperties;
    };
    identifyConfirmed: boolean;
    identifyFocusActive: boolean;
    identifyTrackedEntity: number;
    styles: ThreatListStyles;
}

const formatTime = (seconds: number): string => {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, "0")}`;
};

export const WaveStatusBlock: React.FC<WaveStatusBlockProps> = ({
    phase,
    phaseName,
    timeInPhase,
    phaseEndTime,
    waveNumber,
    scenarioStarted,
    waitingForLaunchWindow,
    progressValue,
    progressColor,
    styles: s,
}) => {
    const l = useLocale();

    return (
        <Column gap="6rem" style={s.waveBox}>
            <Row justify="space-between" align="center">
                <Row align="center">
                    <div style={s.phaseIndicator} />
                    <span style={s.phaseName}>{phaseName}</span>
                </Row>
                <span style={s.waveLabel}>
                    {`${l.t("UI_WAVE")} #${waveNumber}`}
                </span>
            </Row>

            {phase === "calm" || phase === "recovery" ? (
                <Column align="center" gap="4rem">
                    <span style={s.countdownLabel}>
                        {!scenarioStarted ? l.t("THREAT_STATUS") : phase === "calm" ? l.t("THREAT_NEXT_WAVE") : l.t("THREAT_RECOVERY_ENDS")}
                    </span>
                    <span style={s.countdownValue}>
                        {!scenarioStarted
                            ? l.t("THREAT_STANDBY")
                            : waitingForLaunchWindow
                                ? l.t("THREAT_AWAITING_WINDOW")
                                : formatTime(Math.max(0, phaseEndTime - timeInPhase))}
                    </span>
                </Column>
            ) : (
                <>
                    <ProgressBar value={progressValue} color={progressColor} height="6rem" />
                    <Row justify="center" style={s.elapsed}>
                        <span>{`${formatTime(timeInPhase)} ${l.t("UI_STAT_TIME")}`}</span>
                    </Row>
                </>
            )}
        </Column>
    );
};

export const ThreatLegendBlock: React.FC<ThreatLegendBlockProps> = ({
    threatGroups,
    threatGroupStyles,
    etaStyles,
    identifyConfirmed,
    identifyFocusActive,
    identifyTrackedEntity,
    styles: s,
}) => {
    const l = useLocale();

    return (
        <Column gap="4rem" style={s.legendBox}>
            <div style={s.legendTitle}>
                {l.t("THREAT_LEGEND")}
            </div>

            {threatGroups.length > 0 ? (
                threatGroups.map(([typeKey, data]) => (
                    // Two lines: name + count on top, ETA below — the narrow panel clips a
                    // single-line "name (count)   eta" layout (e.g. "Drone Type-A (24) 66s").
                    <Column key={typeKey} gap="2rem" style={threatGroupStyles[typeKey]?.group}>
                        <Row align="center" gap="6rem">
                            <span style={s.typeName}>
                                {l.t(typeKey)}
                            </span>
                            <span style={s.typeCount}>
                                ({data.count})
                            </span>
                        </Row>
                        <span style={data.minEta < 30 ? etaStyles.urgent : etaStyles.normal}>
                            {`${l.t("UI_STAT_TIME")} ${Math.round(data.minEta)}s`}
                        </span>
                    </Column>
                ))
            ) : (
                <div style={s.noThreats}>
                    {l.t("NO_ACTIVE_THREATS")}
                </div>
            )}

            {identifyTrackedEntity !== -1 && (
                <Column gap="4rem" style={s.identifyBox(identifyConfirmed)}>
                    <Row justify="space-between" align="center">
                        <span style={s.identifyLabel}>
                            {identifyConfirmed ? l.t("THREAT_IDENTIFY_CONFIRMED") : l.t("THREAT_IDENTIFYING")}
                        </span>
                        {identifyFocusActive && (
                            <span style={s.priorityBadge}>
                                {l.t("THREAT_PRIORITY")}
                            </span>
                        )}
                    </Row>
                    {!identifyConfirmed && (
                        <div style={s.identifyTrack}>
                            <div style={s.identifyFill} />
                        </div>
                    )}
                </Column>
            )}
        </Column>
    );
};
