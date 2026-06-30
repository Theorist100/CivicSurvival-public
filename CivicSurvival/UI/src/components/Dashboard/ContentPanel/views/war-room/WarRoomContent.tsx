/**
 * WarRoomContent — War Room command screen (Phase 35, Stage A).
 *
 * Pure read-only dashboard wrapper around the existing combat radar. Layout is
 * the "PRISMA" command room: header (operation / UI clock / phase + track chips)
 * + center radar (reuses <ThreatRadar/>, incl. its projectBox 2.5D target boxes)
 * + side panels (ACTIVE TRACES = ThreatListSection, FLEET STATUS = RadarGauges +
 * wave counters) + bottom C2 EVENT BUS ticker.
 *
 * The C2 EVENT BUS introduces NO new back-channel: it derives events from deltas
 * of the existing ThreatDto snapshot between binding ticks (phase changes,
 * intercept/hit/crash/spawn count increases). Flex-only (COHTML has no grid).
 *
 * Stage A is a reader only — no UI commands (mission planner = Stage B). The
 * camera-focus interactions live inside <ThreatRadar/> and already follow the
 * cohtml click-path contract (layout read only in useEffect).
 */

import React, { memo, useEffect, useMemo, useState } from "react";
import { useAccents, useTheme } from "@themes";
import { ThreatRadar } from "@shared/radar/ThreatRadar";
import { RadarGauges } from "@shared/radar/RadarGauges";
import { bindingDataOrDefault, type ThreatDto, useGridWarfareDomain, useThreat } from "@hooks/domain";
import { DEFAULT_GRID_WARFARE_DTO, DEFAULT_THREAT_DTO } from "../../../../../types/domainDtos";
import { type RadarView, useRadar } from "@hooks/useRadar";
import { type MapGeometry, useMapContour } from "@hooks/useMapContour";
import { ThreatListSection } from "../../../../war/sections";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "@hooks/bindingNames.generated";
import { useLocale } from "@locales";
import { createWarRoomStyles } from "./WarRoomContent.styles";
import { useWarRoomEventBus, type C2Event } from "./useWarRoomEventBus";
import { useWavePresentation } from "@hooks/useWavePresentation";

type Phase = ThreatDto["WavePhase"];

const pad2 = (n: number): string => n.toString().padStart(2, "0");

// Arsenal kind discriminator carried in the PurchaseCounterAttackArsenal payload
// (int kindRaw, int count): 0 = drone, 1 = ballistic — mirrors the C# enum.
const ARSENAL_KIND_DRONE = 0;
const ARSENAL_KIND_BALLISTIC = 1;

// Quantity-stepper bounds. The real clamp lives in C# (ArsenalMaxPurchaseCount);
// these only keep the UI counter sane. Default batch is 5.
const ARSENAL_COUNT_MIN = 1;
const ARSENAL_COUNT_MAX = 50;
const ARSENAL_COUNT_DEFAULT = 5;

const hasWaveData = (status: ThreatDto["WaveDataStatus"]): boolean => {
    switch (status) {
        case "active":
        case "completed":
            return true;
        case "unavailable":
        case "noWave":
        case "preStart":
            return false;
        default: {
            const exhaustive: never = status;
            return exhaustive;
        }
    }
};

// ---- UI clock (real session wall-clock, ticks 1Hz) ------------------------
const useUiClock = (): string => {
    const [now, setNow] = useState(() => new Date());
    useEffect(() => {
        const id = window.setInterval(() => setNow(new Date()), 1000);
        return () => window.clearInterval(id);
    }, []);
    return useMemo(
        () => `${pad2(now.getHours())}:${pad2(now.getMinutes())}:${pad2(now.getSeconds())}`,
        [now],
    );
};

interface WarRoomReadyProps {
    radar: RadarView;
    threatState: ThreatDto;
    mapContour: MapGeometry;
    events: C2Event[];
    droneStock: number;
    ballisticStock: number;
    respitePhysical: boolean;
    respiteDigital: boolean;
    respiteSocial: boolean;
    objectiveProgress: number;
}

const clampCount = (n: number): number =>
    Math.max(ARSENAL_COUNT_MIN, Math.min(ARSENAL_COUNT_MAX, n));

const WarRoomReady = memo(({
    radar,
    threatState,
    mapContour,
    events,
    droneStock,
    ballisticStock,
    respitePhysical,
    respiteDigital,
    respiteSocial,
    objectiveProgress,
}: WarRoomReadyProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarRoomStyles(theme, accents), [theme, accents]);
    const clock = useUiClock();

    // Player-chosen purchase quantity (shared by both BUY buttons).
    const [purchaseCount, setPurchaseCount] = useState(ARSENAL_COUNT_DEFAULT);
    const decCount = () => setPurchaseCount((c) => clampCount(c - 1));
    const incCount = () => setPurchaseCount((c) => clampCount(c + 1));

    // Per-axis suppression mapped onto the three mirror axes (KINETIC/CYBER/PSYOPS).
    const objectivePct = Math.round(Math.max(0, Math.min(1, objectiveProgress)) * 100);

    const { threats, targets, defenses, mapBounds, cameraX, cameraZ } = radar;

    const phase: Phase = threatState.WavePhase || "calm";
    // Calm is a prep window, not "all clear" — color/headline reflect inbound wave +
    // grid recovery, shared with the combat radar via the same hook.
    const { phaseColor, phaseName } = useWavePresentation(phase, threatState.ScenarioStarted);

    const waveNumber = threatState.WaveNumber ?? 0;
    const active = threatState.ThreatsRemaining ?? 0;
    const intercepted = threatState.ThreatsIntercepted ?? 0;
    const hits = threatState.ThreatsHit ?? 0;
    const crashed = threatState.ThreatsCrashed ?? 0;
    const spawned = threatState.ThreatsSpawned ?? 0;
    const waveDataStatus = threatState.WaveDataStatus;
    const waveReady = hasWaveData(waveDataStatus);
    const interceptRate = waveReady && spawned > 0 ? Math.round((intercepted / spawned) * 100) : 0;
    const timeInPhase = threatState.TimeInPhase ?? 0;
    const phaseEndTime = threatState.PhaseEndTime;

    const threatSummaries = useMemo(
        () => threats.map((t) => ({
            type: (t.Type === "shahed" || t.Type === "ballistic" ? t.Type : "shahed") as "shahed" | "ballistic",
            eta: t.Eta,
        })),
        [threats],
    );

    const trackChipColor = active > 0 ? accents.crisis.accent : theme.colors.textMuted;

    return (
        <div style={s.root}>
            {/* ---- Header: operation / chips / clock ------------------------ */}
            <div style={s.header}>
                <span style={s.headerAccent} />
                <div style={s.headerTitleWrap}>
                    <span style={s.headerLabel}>{l.t("UI_WARROOM_TITLE")}</span>
                    <span style={s.headerOperation}>
                        {waveNumber > 0 ? l.t("UI_WARROOM_OPERATION_WAVE", waveNumber) : l.t("UI_WARROOM_STANDING_WATCH")}
                    </span>
                </div>
                <div style={s.chipRow}>
                    <div style={s.chip(phaseColor)}>
                        <span style={s.chipDot(phaseColor, phase === "attack")} />
                        <span style={s.chipLabel(phaseColor)}>{phaseName}</span>
                    </div>
                    <div style={s.chip(trackChipColor)}>
                        <span style={s.chipDot(trackChipColor, false)} />
                        <span style={s.chipLabel(trackChipColor)}>{l.t("UI_WARROOM_TRACKS", active)}</span>
                    </div>
                    <span style={s.clock}>{clock}</span>
                </div>
            </div>

            {/* ---- Body: traces / radar / fleet ----------------------------- */}
            <div style={s.body}>
                {/* Left — ACTIVE TRACES */}
                <div style={s.sidePanel("left")}>
                    <div style={s.panelTitle}>{l.t("UI_WARROOM_ACTIVE_TRACES")}</div>
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

                {/* Center — radar (reused, projectBox 2.5D boxes inside) */}
                <div style={s.center}>
                    <ThreatRadar
                        threats={threats}
                        targets={targets}
                        defenses={defenses}
                        interceptions={threatState.RadarInterceptions}
                        mapBounds={mapBounds}
                        mapContour={mapContour}
                        cameraX={cameraX}
                        cameraZ={cameraZ}
                        theme="command"
                        showPrediction={true}
                    />
                </div>

                {/* Right — FLEET STATUS (gauges + wave counters) */}
                <div style={s.sidePanel("right")}>
                    <div style={s.panelTitle}>{l.t("UI_WARROOM_FLEET_STATUS")}</div>
                    <RadarGauges
                        interceptRate={interceptRate}
                        active={active}
                        spawned={spawned}
                        hasWaveData={waveReady}
                    />
                    <div style={s.statGrid}>
                        <div style={s.statCell}>
                            <span style={s.statCellLabel}>{l.t("UI_WARROOM_SPAWNED")}</span>
                            <span style={s.statCellValue(theme.colors.textPrimary)}>{spawned}</span>
                        </div>
                        <div style={s.statCell}>
                            <span style={s.statCellLabel}>{l.t("UI_WARROOM_INTERCEPTED")}</span>
                            <span style={s.statCellValue(accents.schemes.accent)}>{intercepted}</span>
                        </div>
                        <div style={s.statCell}>
                            <span style={s.statCellLabel}>{l.t("UI_WARROOM_HIT")}</span>
                            <span style={s.statCellValue(accents.crisis.accent)}>{hits}</span>
                        </div>
                        <div style={s.statCell}>
                            <span style={s.statCellLabel}>{l.t("UI_WARROOM_CRASHED")}</span>
                            <span style={s.statCellValue(accents.resilience.accent)}>{crashed}</span>
                        </div>
                    </div>

                    {/* Arsenal procurement — sends PurchaseCounterAttackArsenal
                        (kindRaw, count). Current stock is read from GridWarfareDto
                        (throttled push); the buttons fire the trigger (a plain API
                        call, no layout read in the click path — cohtml-safe). */}
                    <div style={s.arsenalBlock}>
                        <div style={s.arsenalTitle}>{l.t("UI_WARROOM_ARSENAL_PROCUREMENT")}</div>
                        <div style={s.counterRow}>
                            <span style={s.counterLabel}>{l.t("UI_WARROOM_STOCK_DRONES")}</span>
                            <span style={s.counterValue(accents.resilience.accent)}>{droneStock}</span>
                        </div>
                        <div style={s.counterRow}>
                            <span style={s.counterLabel}>{l.t("UI_WARROOM_STOCK_ROCKETS")}</span>
                            <span style={s.counterValue(accents.crisis.accent)}>{ballisticStock}</span>
                        </div>

                        {/* Quantity stepper — shared count for both BUY buttons. */}
                        <div style={s.stepperRow}>
                            <span style={s.stepperLabel}>{l.t("UI_WARROOM_QUANTITY")}</span>
                            <div style={s.stepperControls}>
                                <div
                                    style={s.stepperButton(purchaseCount > ARSENAL_COUNT_MIN)}
                                    role="button"
                                    tabIndex={0}
                                    onClick={decCount}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter" || e.key === " ") {
                                            e.preventDefault();
                                            decCount();
                                        }
                                    }}
                                >
                                    −
                                </div>
                                <span style={s.stepperValue}>{purchaseCount}</span>
                                <div
                                    style={s.stepperButton(purchaseCount < ARSENAL_COUNT_MAX)}
                                    role="button"
                                    tabIndex={0}
                                    onClick={incCount}
                                    onKeyDown={(e) => {
                                        if (e.key === "Enter" || e.key === " ") {
                                            e.preventDefault();
                                            incCount();
                                        }
                                    }}
                                >
                                    +
                                </div>
                            </div>
                        </div>

                        <div
                            style={s.arsenalButton(accents.resilience.accent)}
                            role="button"
                            tabIndex={0}
                            onClick={() => triggerCivic(B.PurchaseCounterAttackArsenal, ARSENAL_KIND_DRONE, purchaseCount)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                    e.preventDefault();
                                    triggerCivic(B.PurchaseCounterAttackArsenal, ARSENAL_KIND_DRONE, purchaseCount);
                                }
                            }}
                        >
                            {l.t("UI_WARROOM_BUY_DRONE", purchaseCount)}
                        </div>
                        <div
                            style={s.arsenalButton(accents.crisis.accent)}
                            role="button"
                            tabIndex={0}
                            onClick={() => triggerCivic(B.PurchaseCounterAttackArsenal, ARSENAL_KIND_BALLISTIC, purchaseCount)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                    e.preventDefault();
                                    triggerCivic(B.PurchaseCounterAttackArsenal, ARSENAL_KIND_BALLISTIC, purchaseCount);
                                }
                            }}
                        >
                            {l.t("UI_WARROOM_BUY_BALLISTIC", purchaseCount)}
                        </div>
                    </div>

                    {/* Enemy suppression — per-axis SUPPRESSED badge + act-objective bar. */}
                    <div style={s.suppressionBlock}>
                        <div style={s.suppressionTitle}>{l.t("UI_WARROOM_ENEMY_SUPPRESSION")}</div>
                        <div style={s.axisRow(accents.crisis.accent)}>
                            <span style={s.axisLabel(accents.crisis.accent)}>{l.t("UI_WARROOM_AXIS_PHYSICAL")}</span>
                            <span style={s.suppressBadge(respitePhysical)}>
                                {respitePhysical ? l.t("UI_WARROOM_SUPPRESSED") : l.t("UI_WARROOM_ACTIVE")}
                            </span>
                        </div>
                        <div style={s.axisRow(accents.operations.accent)}>
                            <span style={s.axisLabel(accents.operations.accent)}>{l.t("UI_WARROOM_AXIS_DIGITAL")}</span>
                            <span style={s.suppressBadge(respiteDigital)}>
                                {respiteDigital ? l.t("UI_WARROOM_SUPPRESSED") : l.t("UI_WARROOM_ACTIVE")}
                            </span>
                        </div>
                        <div style={s.axisRow(accents.resilience.accent)}>
                            <span style={s.axisLabel(accents.resilience.accent)}>{l.t("UI_WARROOM_AXIS_SOCIAL")}</span>
                            <span style={s.suppressBadge(respiteSocial)}>
                                {respiteSocial ? l.t("UI_WARROOM_SUPPRESSED") : l.t("UI_WARROOM_ACTIVE")}
                            </span>
                        </div>
                        <div style={s.objectiveLabelRow}>
                            <span style={s.objectiveLabel}>{l.t("UI_WARROOM_OBJECTIVE")}</span>
                            <span style={s.objectiveValue}>{objectivePct}%</span>
                        </div>
                        <div style={s.objectiveTrack}>
                            <div style={s.objectiveFill(objectiveProgress)} />
                        </div>
                    </div>
                </div>
            </div>

            {/* ---- Bottom: C2 EVENT BUS ticker ------------------------------ */}
            <div style={s.eventBus}>
                <div style={s.eventBusHeader}>
                    <span style={s.eventBusTitle}>{l.t("UI_WARROOM_C2_EVENT_BUS")}</span>
                </div>
                <div style={s.eventBusFeed}>
                    {events.length === 0 ? (
                        <span style={s.eventEmpty}>{l.t("UI_WARROOM_AWAITING")}</span>
                    ) : (
                        events.map((ev) => (
                            <div key={ev.id} style={s.eventLine}>
                                <span style={s.eventTime}>{ev.time}</span>
                                <span style={s.eventText(
                                    ev.kind === "intercept" ? accents.schemes.accent
                                        : ev.kind === "hit" ? accents.crisis.accent
                                        : ev.kind === "crash" ? accents.resilience.accent
                                        : ev.kind === "spawn" ? theme.colors.textSecondary
                                        : phaseColor,
                                )}>
                                    {ev.text}
                                </span>
                            </div>
                        ))
                    )}
                </div>
            </div>
        </div>
    );
});
WarRoomReady.displayName = "WarRoomReady";

export const WarRoomContent = memo(() => {
    const radar = useRadar();
    const threatData = bindingDataOrDefault(useThreat(), DEFAULT_THREAT_DTO);
    const gridWarfare = bindingDataOrDefault(useGridWarfareDomain(), DEFAULT_GRID_WARFARE_DTO);
    const mapContour = useMapContour();
    const events = useWarRoomEventBus(threatData);
    return (
        <WarRoomReady
            radar={radar}
            threatState={threatData}
            mapContour={mapContour}
            events={events}
            droneStock={gridWarfare.DroneStock ?? 0}
            ballisticStock={gridWarfare.BallisticStock ?? 0}
            respitePhysical={gridWarfare.RespitePhysicalActive ?? false}
            respiteDigital={gridWarfare.RespiteDigitalActive ?? false}
            respiteSocial={gridWarfare.RespiteSocialActive ?? false}
            objectiveProgress={gridWarfare.ObjectiveProgress ?? 0}
        />
    );
});
WarRoomContent.displayName = "WarRoomContent";
