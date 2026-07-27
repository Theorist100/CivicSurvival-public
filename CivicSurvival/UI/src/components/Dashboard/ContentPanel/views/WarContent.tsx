/**
 * WarContent - Combat Radar View
 * HYBRID OPS domain → RADAR view
 * Layout: Left (Threat List) + Right (Big Radar)
 *
 * Uses reusable section components from components/war/sections
 */

import React, { memo, useMemo } from "react";
import { useTheme, useAccents, hexToRgba } from "@themes";
import { ThreatRadar } from "@shared/radar/ThreatRadar";
import { bindingDataOrDefault, type ThreatDto, useThreat } from "@hooks/domain";
import { DEFAULT_THREAT_DTO } from "../../../../types/domainDtos";
import { ThreatListSection } from "../../../war/sections";
import { getIconComponent } from "../../../shared/common/Icons";
import { type RadarView, useRadar } from "@hooks/useRadar";
import { type MapGeometry, useMapContour } from "@hooks/useMapContour";
import { useWavePresentation } from "@hooks/useWavePresentation";
import { useBetaWave } from "@hooks/useBetaWave";
import { openWarRoom } from "@hooks/useWarRoomOverlay";
import { useLocale } from "@locales";

const WarRoomExpandButton = memo(() => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    // Same gate as the War Room overlay: absent until the GridWarfare beta wave opens.
    const gate = useBetaWave("GridWarfare");
    const Icon = getIconComponent("globe");
    if (gate.isLocked) return null;

    // Pinned to the bottom of the left (threat list) column — it used to float
    // absolute over the radar's top-right corner, where it covered the radar's
    // own KINETIC/MENTAL toggle.
    const style: React.CSSProperties = {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        flexShrink: 0,
        margin: `0 ${theme.spacing.md} ${theme.spacing.md}`,
        // Height-matched to the radar's FOCUS THREAT row it now aligns with.
        padding: "4rem 12rem",
        borderRadius: theme.layout.borderRadiusLg,
        background: hexToRgba(accents.crisis.accent, 0.15),
        border: `2rem solid ${hexToRgba(accents.crisis.accent, 0.5)}`,
        cursor: "pointer",
    };

    return (
        <div
            style={style}
            role="button"
            tabIndex={0}
            onClick={openWarRoom}
            onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    openWarRoom();
                }
            }}
        >
            {Icon && (
                <span style={{ display: "flex", alignItems: "center", marginRight: "6rem", color: accents.crisis.accent }}>
                    <Icon />
                </span>
            )}
            <span style={{
                fontSize: "11rem",
                fontWeight: 700,
                textTransform: "uppercase" as const,
                letterSpacing: "0.5rem",
                color: accents.crisis.accent,
                fontFamily: theme.typography.fontFamilyMono,
            }}>
                {l.t("UI_WARROOM_EXPAND")}
            </span>
        </div>
    );
});
WarRoomExpandButton.displayName = "WarRoomExpandButton";

interface WarContentReadyProps {
    radar: RadarView;
    threatState: ThreatDto;
    mapContour: MapGeometry;
}

const WarContentReady = memo(({ radar, threatState, mapContour }: WarContentReadyProps) => {
    const theme = useTheme();
    const { threats, outbound, targets, defenses, broadcasts, mapBounds, cameraX, cameraZ } = radar;
    const threatSummaries = useMemo(
        () => threats.map((t) => ({
            type: (t.Type === "shahed" || t.Type === "ballistic" ? t.Type : "shahed") as "shahed" | "ballistic",
            eta: t.Eta,
        })),
        [threats],
    );
    const outboundSummaries = useMemo(
        () => outbound.map((t) => ({
            type: (t.Type === "shahed" || t.Type === "ballistic" ? t.Type : "shahed") as "shahed" | "ballistic",
            eta: t.Eta,
        })),
        [outbound],
    );
    const phase = threatState.WavePhase || "calm";
    // Map keeps the command-post cyan identity across all phases. Danger is signalled
    // by the chrome (left panel + gauges) and by per-track colors (ballistic/hardlock),
    // not by repainting the whole map red.
    const radarTheme = "command" as const;

    // Calm is a prep window, not "all clear": the color/headline reflect whether a
    // wave is actually inbound and whether the grid is still rebuilding.
    const { phaseColor, phaseName } = useWavePresentation(phase, threatState.ScenarioStarted);

    const waveNumber = threatState.WaveNumber ?? 0;
    const active = threatState.ThreatsRemaining ?? 0;
    const intercepted = threatState.ThreatsIntercepted ?? 0;
    const hits = threatState.ThreatsHit ?? 0;
    const crashed = threatState.ThreatsCrashed ?? 0;
    const spawned = threatState.ThreatsSpawned ?? 0;
    const waveDataStatus = threatState.WaveDataStatus;
    const hasWaveData = (() => {
        switch (waveDataStatus) {
            case "active":
            case "completed":
                return true;
            case "unavailable":
            case "noWave":
            case "preStart":
                return false;
            default: {
                const exhaustive: never = waveDataStatus;
                return exhaustive;
            }
        }
    })();
    const interceptRate = hasWaveData && spawned > 0 ? Math.round((intercepted / spawned) * 100) : 0;
    const timeInPhase = threatState.TimeInPhase ?? 0;
    const phaseEndTime = threatState.PhaseEndTime;

    return (
        <div style={{
            position: "relative" as const,
            width: "100%",
            height: "100%",
            overflow: "hidden" as const,
        }}>
            {/* Left Column - THREAT LIST (absolute for height). Flex column: the
                threat list scrolls in its own zone, the OPEN WAR ROOM button stays
                pinned at the very bottom (it used to overlap the radar's toggle). */}
            <div style={{
                position: "absolute" as const,
                top: 0,
                left: 0,
                bottom: 0,
                width: "210rem",
                borderRight: `2rem solid ${theme.colors.border}`,
                display: "flex" as const,
                flexDirection: "column" as const,
                minHeight: 0,
            }}>
                <div style={{
                    flex: 1,
                    minHeight: 0,
                    overflowY: "auto" as const,
                    overflowX: "hidden" as const,
                    padding: theme.spacing.md,
                }}>
                <ThreatListSection
                    threats={threatSummaries}
                    outbound={outboundSummaries}
                    phase={phase}
                    phaseColor={phaseColor}
                    phaseName={phaseName}
                    timeInPhase={timeInPhase}
                    phaseEndTime={phaseEndTime}
                    waveNumber={waveNumber}
                    spawned={spawned}
                    active={active}
                    intercepted={intercepted}
                    hits={hits}
                    crashed={crashed}
                    interceptRate={interceptRate}
                    scenarioStarted={threatState.ScenarioStarted}
                    waveDataStatus={waveDataStatus}
                    waitingForLaunchWindow={threatState.WaitingForLaunchWindow ?? false}
                    identifyProgress={threatState.IdentifyProgress ?? 0}
                    identifyConfirmed={threatState.IdentifyConfirmed ?? false}
                    identifyFocusActive={threatState.IdentifyFocusActive ?? false}
                    identifyTrackedEntity={threatState.IdentifyTrackedEntity ?? -1}
                />
                </div>
                <WarRoomExpandButton />
            </div>

            {/* Right Column - BIG RADAR */}
            <div style={{
                position: "absolute" as const,
                top: 0,
                left: "210rem",
                right: 0,
                bottom: 0,
                display: "flex" as const,
                // flex-end (not center): pins the radar block — and its FOCUS
                // THREAT row — to the same 12rem bottom line the left column's
                // OPEN WAR ROOM button sits on, so the two read as one row.
                alignItems: "flex-end" as const,
                justifyContent: "center" as const,
                minHeight: 0,
                padding: theme.spacing.md,
            }}>
                <ThreatRadar
                    threats={threats}
                    outbound={outbound}
                    targets={targets}
                    defenses={defenses}
                    broadcasts={broadcasts}
                    interceptions={threatState.RadarInterceptions}
                    mapBounds={mapBounds}
                    mapContour={mapContour}
                    cameraX={cameraX}
                    cameraZ={cameraZ}
                    theme={radarTheme}
                    showPrediction={true}
                />
            </div>
        </div>
    );
});
WarContentReady.displayName = "WarContentReady";

export const WarContent = memo(() => {
    const radar = useRadar();
    const threatData = bindingDataOrDefault(useThreat(), DEFAULT_THREAT_DTO);
    const mapContour = useMapContour();
    return <WarContentReady radar={radar} threatState={threatData} mapContour={mapContour} />;
});
WarContent.displayName = "WarContent";
