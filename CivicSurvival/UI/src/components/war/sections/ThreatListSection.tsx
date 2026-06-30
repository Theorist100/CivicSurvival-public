/**
 * ThreatListSection - Wave status, threat legend, stats
 * WAR domain → Left column
 */

import React, { memo, useMemo } from "react";
import { Column } from "@coherent";
import { useTheme, useAccents, hexToRgba } from "@themes";
import { radarThemes } from "@themes/radar";
import { type TranslationKey } from "@locales";
import { type ThreatType, type WaveDataStatus, type WavePhase } from "../../../types/domainDtos";
import { ThreatStatsGrid } from "./threat-list/ThreatStatsGrid";
import { ThreatLegendBlock, WaveStatusBlock } from "./threat-list/ThreatListBlocks";

// Command-post palette shared with the radar, so the left panel chrome reads as the
// same tactical display (navy boxes + cyan borders). Danger stays red/orange on the
// actual threat indicators and values, not the chrome.
const cmd = radarThemes.command;

const isBallistic = (typeKey: TranslationKey) => typeKey === "THREAT_TYPE_BALLISTIC";

export interface ThreatListSectionProps {
    threats: Array<{ type: ThreatType; eta: number }>;
    phase: WavePhase;
    phaseColor: string;
    phaseName: string;
    timeInPhase: number;
    phaseEndTime: number;
    waveNumber: number;
    spawned: number;
    active: number;
    intercepted: number;
    hits: number;
    crashed: number;
    interceptRate: number;
    scenarioStarted: boolean;
    waveDataStatus: WaveDataStatus;
    waitingForLaunchWindow: boolean;
    identifyProgress: number;
    identifyConfirmed: boolean;
    identifyFocusActive: boolean;
    identifyTrackedEntity: number;
}

const THREAT_TYPE_MAP: Record<ThreatType, TranslationKey> = {
    ballistic: "THREAT_TYPE_BALLISTIC",
    shahed: "THREAT_TYPE_DRONE_A",
};

const DEFAULT_THREAT_KEY: TranslationKey = "THREAT_TYPE_DRONE_A";

export const ThreatListSection: React.FC<ThreatListSectionProps> = memo((props) => {
    const {
        threats,
        phase,
        phaseColor,
        phaseName,
        timeInPhase,
        phaseEndTime,
        waveNumber,
        scenarioStarted,
        waitingForLaunchWindow,
        identifyProgress,
        identifyConfirmed,
        identifyFocusActive,
        identifyTrackedEntity,
    } = props;

    const theme = useTheme();
    const accents = useAccents();

    const phaseProgress = phaseEndTime > 0 ? Math.min(100, (timeInPhase / phaseEndTime) * 100) : 0;

    // Group threats by type and count
    const threatGroups = useMemo(() => {
        const groups: Record<TranslationKey, { count: number; minEta: number }> = {} as Record<TranslationKey, { count: number; minEta: number }>;
        for (const threat of threats) {
            const typeKey = THREAT_TYPE_MAP[threat.type] ?? DEFAULT_THREAT_KEY;
            if (!groups[typeKey]) {
                groups[typeKey] = { count: 0, minEta: Infinity };
            }
            groups[typeKey].count++;
            groups[typeKey].minEta = Math.min(groups[typeKey].minEta, threat.eta);
        }
        return (Object.entries(groups) as Array<[TranslationKey, { count: number; minEta: number }]>)
            .sort((a, b) => a[1].minEta - b[1].minEta);
    }, [threats]);

    // Memoized styles for threat groups
    const threatGroupStyles = useMemo(() => {
        const map: Partial<Record<TranslationKey, { group: React.CSSProperties; indicator: React.CSSProperties }>> = {};
        for (const [typeKey] of threatGroups) {
            const ballistic = isBallistic(typeKey);
            const color = ballistic ? accents.crisis.accent : accents.resilience.accent;
            map[typeKey] = {
                group: {
                    padding: "8rem 10rem",
                    background: ballistic ? hexToRgba(color, 0.08) : hexToRgba(color, 0.06),
                    borderRadius: "4rem",
                    borderLeft: `4rem solid ${color}`,
                },
                indicator: {
                    width: "10rem",
                    height: "10rem",
                    borderRadius: ballistic ? "2rem" : "50%",
                    background: color,
                    marginRight: "8rem",
                },
            };
        }
        return map;
    }, [threatGroups, accents.crisis.accent, accents.resilience.accent]);

    // Memoized static styles
    const s = useMemo(() => ({
        waveBox: {
            padding: theme.spacing.md,
            background: hexToRgba(phaseColor, 0.06),
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${hexToRgba(phaseColor, 0.19)}`,
            marginBottom: theme.spacing.md,
        } as React.CSSProperties,
        phaseIndicator: {
            width: "10rem",
            height: "10rem",
            borderRadius: "50%",
            background: phaseColor,
            boxShadow: phase === "attack" ? `0 0 8rem ${phaseColor}` : "none",
            marginRight: "8rem",
        } as React.CSSProperties,
        phaseName: {
            color: phaseColor,
            fontWeight: 700,
            fontSize: "14rem",
            textTransform: "uppercase" as const,
        } as React.CSSProperties,
        waveLabel: {
            color: theme.colors.textPrimary,
            fontWeight: 600,
            fontSize: "12rem",
            fontFamily: theme.typography.fontFamilyMono,
        } as React.CSSProperties,
        countdownLabel: {
            fontSize: "10rem",
            color: theme.colors.textMuted,
            textTransform: "uppercase" as const,
        } as React.CSSProperties,
        countdownValue: {
            fontSize: "22rem",
            fontWeight: 700,
            fontFamily: theme.typography.fontFamilyMono,
            color: !scenarioStarted ? theme.colors.textMuted : phaseColor,
        } as React.CSSProperties,
        elapsed: {
            fontSize: "12rem",
            color: theme.colors.textSecondary,
            fontFamily: theme.typography.fontFamilyMono,
        } as React.CSSProperties,
        legendBox: {
            padding: theme.spacing.md,
            background: hexToRgba(cmd.ring, 0.18),
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${hexToRgba(cmd.sweep, 0.3)}`,
            minHeight: "120rem",
            marginBottom: theme.spacing.md,
        } as React.CSSProperties,
        legendTitle: {
            fontSize: "11rem",
            fontWeight: 700,
            textTransform: "uppercase" as const,
            color: cmd.sweep,
            letterSpacing: "0.5rem",
        } as React.CSSProperties,
        typeName: {
            color: theme.colors.textPrimary,
            fontSize: "13rem",
            fontWeight: 600,
            marginRight: "8rem",
        } as React.CSSProperties,
        typeCount: {
            color: theme.colors.textSecondary,
            fontSize: "12rem",
            fontFamily: theme.typography.fontFamilyMono,
        } as React.CSSProperties,
        noThreats: {
            color: theme.colors.textMuted,
            fontSize: "12rem",
            textAlign: "center" as const,
            padding: "30rem 10rem",
            fontStyle: "italic" as const,
        } as React.CSSProperties,
        identifyBox: (confirmed: boolean) => ({
            marginTop: "6rem",
            padding: "6rem 8rem",
            background: confirmed ? hexToRgba(accents.schemes.accent, 0.08) : hexToRgba(accents.resilience.accent, 0.06),
            borderRadius: "4rem",
            borderLeft: `4rem solid ${confirmed ? accents.schemes.accent : accents.resilience.accent}`,
        } as React.CSSProperties),
        identifyLabel: {
            fontSize: "11rem",
            fontWeight: 700,
            textTransform: "uppercase" as const,
            color: identifyConfirmed ? accents.schemes.accent : accents.resilience.accent,
        } as React.CSSProperties,
        priorityBadge: {
            fontSize: "10rem",
            fontWeight: 700,
            padding: "1rem 4rem",
            borderRadius: "2rem",
            color: theme.colors.textPrimary,
            background: accents.crisis.accent,
            marginLeft: "8rem",
            flexShrink: 0,
        } as React.CSSProperties,
        identifyTrack: {
            height: "4rem",
            background: theme.colors.border,
            borderRadius: "2rem",
            overflow: "hidden" as const,
        } as React.CSSProperties,
        identifyFill: {
            width: `${Math.min(100, Math.round(identifyProgress * 100))}%`,
            height: "100%",
            background: accents.resilience.accent,
            transition: "width 0.1s linear",
        } as React.CSSProperties,
        statCard: (marginSide: "left" | "right") => ({
            flex: 1,
            display: "flex" as const,
            flexDirection: "column" as const,
            alignItems: "center" as const,
            padding: "10rem",
            background: hexToRgba(cmd.ring, 0.18),
            borderRadius: theme.layout.borderRadius,
            border: `2rem solid ${hexToRgba(cmd.sweep, 0.3)}`,
            ...(marginSide === "left" ? { marginLeft: "4rem" } : { marginRight: "4rem" }),
        } as React.CSSProperties),
    }), [theme, accents, phaseColor, phase, scenarioStarted, identifyConfirmed, identifyProgress]);

    const etaStyles = useMemo(() => {
        const base: React.CSSProperties = { fontSize: "13rem", fontFamily: theme.typography.fontFamilyMono };
        return {
            urgent: { ...base, color: accents.crisis.accent, fontWeight: 700 } as React.CSSProperties,
            normal: { ...base, color: theme.colors.textSecondary, fontWeight: 500 } as React.CSSProperties,
        };
    }, [accents.crisis.accent, theme.colors.textSecondary, theme.typography.fontFamilyMono]);

    return (
        <Column>
            <WaveStatusBlock
                phase={phase}
                phaseName={phaseName}
                timeInPhase={timeInPhase}
                phaseEndTime={phaseEndTime}
                waveNumber={waveNumber}
                scenarioStarted={scenarioStarted}
                waitingForLaunchWindow={waitingForLaunchWindow}
                progressValue={phaseProgress}
                progressColor={phaseColor}
                styles={s}
            />

            <ThreatLegendBlock
                threatGroups={threatGroups}
                threatGroupStyles={threatGroupStyles}
                etaStyles={etaStyles}
                identifyConfirmed={identifyConfirmed}
                identifyFocusActive={identifyFocusActive}
                identifyTrackedEntity={identifyTrackedEntity}
                styles={s}
            />

            <ThreatStatsGrid sectionProps={props} styles={s} />
        </Column>
    );
});

ThreatListSection.displayName = "ThreatListSection";
