/**
 * WarContent - Combat Radar View
 * HYBRID OPS domain → RADAR view
 * Layout: Left (Threat List) + Right (Big Radar)
 *
 * Uses reusable section components from components/war/sections
 */

import React, { memo, useMemo } from "react";
import { useTheme } from "@themes";
import { ThreatRadar } from "@shared/radar/ThreatRadar";
import { bindingDataOrDefault, type ThreatDto, useThreat } from "@hooks/domain";
import { DEFAULT_THREAT_DTO } from "../../../../types/domainDtos";
import { ThreatListSection } from "../../../war/sections";
import { type RadarView, useRadar } from "@hooks/useRadar";
import { type MapGeometry, useMapContour } from "@hooks/useMapContour";
import { useWavePresentation } from "@hooks/useWavePresentation";

interface WarContentReadyProps {
    radar: RadarView;
    threatState: ThreatDto;
    mapContour: MapGeometry;
}

const WarContentReady = memo(({ radar, threatState, mapContour }: WarContentReadyProps) => {
    const theme = useTheme();
    const { threats, targets, defenses, mapBounds, cameraX, cameraZ } = radar;
    const threatSummaries = useMemo(
        () => threats.map((t) => ({
            type: (t.Type === "shahed" || t.Type === "ballistic" ? t.Type : "shahed") as "shahed" | "ballistic",
            eta: t.Eta,
        })),
        [threats],
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
            {/* Left Column - THREAT LIST (absolute for height) */}
            <div style={{
                position: "absolute" as const,
                top: 0,
                left: 0,
                bottom: 0,
                width: "210rem",
                borderRight: `2rem solid ${theme.colors.border}`,
                overflowY: "auto" as const,
                overflowX: "hidden" as const,
                padding: theme.spacing.md,
            }}>
                <ThreatListSection
                    threats={threatSummaries}
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

            {/* Right Column - BIG RADAR */}
            <div style={{
                position: "absolute" as const,
                top: 0,
                left: "210rem",
                right: 0,
                bottom: 0,
                display: "flex" as const,
                alignItems: "center" as const,
                justifyContent: "center" as const,
                minHeight: 0,
                padding: theme.spacing.md,
            }}>
                <ThreatRadar
                    threats={threats}
                    targets={targets}
                    defenses={defenses}
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
