/**
 * WarRoomWaveCard — the left-column hero: a large circular SVG countdown that reads the current
 * wave phase. During calm/recovery the ring counts toward the next event and the centre shows the
 * time remaining; during an active assault it switches to WAVE ACTIVE and the centre shows the live
 * track count. The headline flips to INCOMING the moment an alert or assault is on the board.
 *
 * All values come from the existing ThreatDto fields already threaded through WarRoomContent — no
 * new binding. Pure presentational reader, flex-only, mono-typed.
 */

import React, { memo, useMemo } from "react";
import { useAccents, useTheme, hexToRgba } from "@themes";
import { useLocale } from "@locales";
import { type ThreatDto } from "@hooks/domain";
import { isActiveAssaultPhase } from "../../../../../types/domainDtos";
import { WarRoomProgressRing } from "./WarRoomProgressRing";

const formatTime = (seconds: number): string => {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, "0")}`;
};

interface WarRoomWaveCardProps {
    phase: ThreatDto["WavePhase"];
    phaseColor: string;
    phaseName: string;
    timeInPhase: number;
    phaseEndTime: number;
    waveNumber: number;
    scenarioStarted: boolean;
    waitingForLaunchWindow: boolean;
    active: number;
}

export const WarRoomWaveCard: React.FC<WarRoomWaveCardProps> = memo(({
    phase,
    phaseColor,
    phaseName,
    timeInPhase,
    phaseEndTime,
    waveNumber,
    scenarioStarted,
    waitingForLaunchWindow,
    active,
}) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const mono = theme.typography.fontFamilyMono;

    const isAssault = isActiveAssaultPhase(phase);
    const incoming = phase === "alert" || isAssault;
    const remaining = Math.max(0, phaseEndTime - timeInPhase);
    const ringPct = phaseEndTime > 0 ? Math.min(100, (timeInPhase / phaseEndTime) * 100) : 0;

    const headline = !scenarioStarted
        ? l.t("UI_WARROOM_WAVE_STANDBY")
        : incoming
            ? l.t("UI_WARROOM_WAVE_INCOMING")
            : phase === "recovery"
                ? l.t("UI_WARROOM_WAVE_RECOVERY")
                : l.t("UI_WARROOM_WAVE_NEXT");

    // Centre of the ring: during an assault the live track count is the number that matters;
    // otherwise it is the countdown to the next event (or a dash while we wait for the window).
    const centerBig = !scenarioStarted
        ? "--"
        : isAssault
            ? `${active}`
            : waitingForLaunchWindow
                ? "--:--"
                : formatTime(remaining);
    const centerCaption = isAssault ? l.t("UI_WARROOM_ACTIVE") : l.t("UI_WARROOM_WAVE_ETA");

    const ringColor = incoming ? accents.crisis.accent : phaseColor;

    const card = useMemo<React.CSSProperties>(() => ({
        display: "flex",
        flexDirection: "column",
        minHeight: "40rem",
        alignItems: "center",
        flexShrink: 0,
        padding: "8rem",
        marginBottom: theme.spacing.sm,
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(ringColor, 0.06),
        border: `1rem solid ${hexToRgba(ringColor, 0.35)}`,
    }), [theme, ringColor]);

    return (
        <div style={card}>
            <span style={{
                fontSize: "12rem",
                fontWeight: theme.typography.weightBold,
                letterSpacing: "2rem",
                textTransform: "uppercase",
                color: ringColor,
                fontFamily: mono,
                marginBottom: "8rem",
                textShadow: incoming ? `0 0 12rem ${hexToRgba(ringColor, 0.5)}` : "none",
            } as React.CSSProperties}>
                {headline}
            </span>

            <WarRoomProgressRing
                id="wave"
                pct={ringPct}
                size={104}
                stroke={7}
                color={ringColor}
                trackColor={hexToRgba(theme.colors.textMuted, 0.18)}
                glow
            >
                <span style={{
                    fontSize: "26rem",
                    fontWeight: theme.typography.weightBold,
                    lineHeight: 1,
                    color: ringColor,
                    fontFamily: mono,
                }}>
                    {centerBig}
                </span>
                <span style={{
                    fontSize: "10rem",
                    letterSpacing: "1rem",
                    textTransform: "uppercase",
                    color: theme.colors.textMuted,
                    fontFamily: mono,
                    marginTop: "3rem",
                } as React.CSSProperties}>
                    {centerCaption}
                </span>
            </WarRoomProgressRing>

            <div style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                width: "100%",
                marginTop: "10rem",
            }}>
                <span style={{
                    fontSize: "11rem",
                    fontWeight: theme.typography.weightBold,
                    letterSpacing: "0.5rem",
                    textTransform: "uppercase",
                    color: phaseColor,
                    fontFamily: mono,
                } as React.CSSProperties}>
                    {phaseName}
                </span>
                <span style={{
                    fontSize: "11rem",
                    fontWeight: theme.typography.weightBold,
                    color: theme.colors.textSecondary,
                    fontFamily: mono,
                }}>
                    {l.t("UI_WARROOM_WAVE_LABEL", waveNumber)}
                </span>
            </div>
        </div>
    );
});

WarRoomWaveCard.displayName = "WarRoomWaveCard";
