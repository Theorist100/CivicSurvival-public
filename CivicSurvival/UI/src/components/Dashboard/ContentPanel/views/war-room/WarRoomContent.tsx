/**
 * WarRoomContent — the War Room command theatre, rendered inside the fullscreen overlay.
 *
 * Layout (command-theatre, flex-only):
 *   header  — operation title + INCOMING/TRACKS chips (left), three compact enemy-axis chips
 *             (center), OBJECTIVE progress ring + clock + ESC (right)
 *   body    — LEFT: wave countdown card / ACTIVE TRACES (ThreatListSection: threats + telemetry) /
 *             AIR DEFENSE / AA ROSTER; CENTER: SITUATION|STRIKE radar + strike card (unchanged);
 *             RIGHT: LAUNCH CONTROL (operation slots + arsenal) / INTEL / CITY STATUS
 *   bottom  — C2 EVENT BUS single-line ticker
 *
 * Read-and-command: the arsenal buy, operation slots, resupply and intel buttons all fire the
 * existing synchronous triggers (pause-safe, no layout read in the click path). A radar track click
 * focuses the camera and closes the overlay so the focused view is visible behind the surface.
 */

import React, { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAccents, useTheme, hexToRgba } from "@themes";
import { radarThemes } from "@themes/radar";
import { type EnemyTargetPick, ThreatRadar } from "@shared/radar/ThreatRadar";
import { bindingDataOrDefault, type GridWarfareDto, type ThreatDto, useGridWarfareDomain, useThreat } from "@hooks/domain";
import { DEFAULT_GRID_WARFARE_DTO, DEFAULT_THREAT_DTO, isActiveAssaultPhase } from "../../../../../types/domainDtos";
import { type RadarView, useRadar } from "@hooks/useRadar";
import { type MapGeometry, useMapContour } from "@hooks/useMapContour";
import { type MirrorCitySnapshot, mirrorCityBounds, useMirrorCity } from "@hooks/useMirrorCity";
import { useSimulationClock } from "@hooks/useSimulationClock";
import { useGridWarfareActions } from "@hooks/actions";
import { ThreatListSection } from "../../../../war/sections";
import { OperationSlots } from "../../../../GridWarfare";
import { HelpSection } from "../../../../shared/common/HelpSection";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "@hooks/bindingNames.generated";
import { useLocale } from "@locales";
import { createWarRoomStyles } from "./WarRoomContent.styles";
import { useWarRoomEventBus, type C2EventKind } from "./useWarRoomEventBus";
import { axisRawOf } from "./strikeTargetNames";
import { WarRoomStrikeCard } from "./WarRoomStrikeCard";
import { WarRoomAxisChips } from "./WarRoomAxisChips";
import { WarRoomProgressRing } from "./WarRoomProgressRing";
import { WarRoomWaveCard } from "./WarRoomWaveCard";
import { WarRoomAirDefense } from "./WarRoomAirDefense";
import { WarRoomAaRoster } from "./WarRoomAaRoster";
import { WarRoomIntel } from "./WarRoomIntel";
import { WarRoomCityStatus } from "./WarRoomCityStatus";
import { WarRoomTicker } from "./WarRoomTicker";
import { useWavePresentation } from "@hooks/useWavePresentation";
import { navigateDashboard } from "@hooks/useDashboardNav";
import { setDefenseBuildMode } from "@hooks/useDefenseBuildMode";
import { scLog } from "../../../../../utils/logging";

// SITUATION = your city (friendly radar); STRIKE = the enemy projection (mirror-city).
type WarRoomView = "situation" | "strike";

type Phase = ThreatDto["WavePhase"];
type GridWarfareActions = ReturnType<typeof useGridWarfareActions>;

const cmd = radarThemes.command;
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

// EnemyTarget.NoTargetId on the wire — the "no preference" sentinel of the PreferredTarget* readouts.
const NO_TARGET_ID = 65535;

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

// PERF-LOCK: the 1 Hz clock state lives in this leaf so its tick re-renders ONE text span —
// hoisting useUiClock back into WarRoomReady would re-reconcile the whole overlay (radar SVG
// included) every second for a header timestamp.
const WarRoomClock = memo(({ style }: { style: React.CSSProperties }) => {
    const clock = useUiClock();
    return <span style={style}>{clock}</span>;
});
WarRoomClock.displayName = "WarRoomClock";

const clampCount = (n: number): number =>
    Math.max(ARSENAL_COUNT_MIN, Math.min(ARSENAL_COUNT_MAX, n));

interface WarRoomReadyProps {
    radar: RadarView;
    threatState: ThreatDto;
    gw: GridWarfareDto;
    mapContour: MapGeometry;
    mirrorCity: MirrorCitySnapshot;
    actions: GridWarfareActions;
    onClose: () => void;
}

const WarRoomReady = memo(({
    radar,
    threatState,
    gw,
    mapContour,
    mirrorCity,
    actions,
    onClose,
}: WarRoomReadyProps) => {
    const theme = useTheme();
    const accents = useAccents();
    const l = useLocale();
    const s = useMemo(() => createWarRoomStyles(theme, accents), [theme, accents]);

    // Both intel levels are the EFFECTIVE one (insider folded C#-side, one shared property), but
    // they ride two bindings with different publish cadence, so each surface gates by the binding
    // that carries ITS data: gw.IntelLevel gates the gw-sourced axis chips; the snapshot's
    // header.intelLevel gates the snapshot-sourced surfaces (target card naming, C2 strike lines) —
    // otherwise an insider purchase briefly renders already-revealed snapshot data behind a
    // stale gate (or vice versa) until the other binding catches up.
    const intelLevel = gw.IntelLevel ?? 0;
    const snapshotIntelLevel = mirrorCity.header.intelLevel;
    const events = useWarRoomEventBus(threatState, radar.outbound.length, gw.ResolvedStrikes, snapshotIntelLevel, mirrorCity);

    // Player-chosen purchase quantity (shared by both BUY buttons).
    const [purchaseCount, setPurchaseCount] = useState(ARSENAL_COUNT_DEFAULT);
    const decCount = () => setPurchaseCount((c) => clampCount(c - 1));
    const incCount = () => setPurchaseCount((c) => clampCount(c + 1));

    // BUILD DRONE LAUNCHER: the overlay covers the map, so the tool cannot be used here.
    // Take the launcher in hand first (sync trigger), then land the Dashboard on the
    // DEFENSE tab in STRIKE build mode and drop the overlay — the placement tool is
    // already live when the map becomes visible.
    const onBuildLauncher = () => {
        triggerCivic(B.PlaceDroneLauncher);
        setDefenseBuildMode("strike");
        navigateDashboard("war", "defense");
        onClose();
    };

    // MANAGE AA deep-link: the overlay hosts read + resupply, but placement / policy live on the
    // full DEFENSE tab. Same navigation contract as the launcher build (no new trigger).
    const onManageAA = useCallback(() => {
        navigateDashboard("war", "defense");
        onClose();
    }, [onClose]);

    const { threats, outbound, targets, defenses, broadcasts, mapBounds, cameraX, cameraZ } = radar;

    const phase: Phase = threatState.WavePhase || "calm";
    // Calm is a prep window, not "all clear" — color/headline reflect inbound wave +
    // grid recovery, shared with the combat radar via the same hook.
    const { phaseColor, phaseName } = useWavePresentation(phase, threatState.ScenarioStarted);

    // Map view: SITUATION (your city radar) vs STRIKE (enemy projection). Local UI state.
    const [view, setView] = useState<WarRoomView>("situation");
    // An inbound wave while the player is on the STRIKE board warrants a nudge back to
    // SITUATION — surfaced as an alert banner only, never a forced switch (and no duplicate
    // switch button: the SITUATION|STRIKE toggle in the same row is the navigation control).
    const incomingWave = phase === "alert" || isActiveAssaultPhase(phase);
    // Vanilla pause/speed — the fullscreen overlay hides the toolbar widget, so we show it here.
    const clock = useSimulationClock();

    // STRIKE view: the picked enemy target (target or AA id). Selecting one sends the per-axis
    // preference to C# (SetStrikeTarget) so the next Execute of that axis aims at it if still valid.
    // The pick is stored TOGETHER with the city generation it was made against, and the derived
    // strikeTargetId is null whenever the current snapshot is a different generation — target ids
    // are small buffer indices reused across regenerations, so a bare id would silently highlight
    // an unrelated target of the next city. Declarative invalidation; no reset effect to forget.
    const [strikePick, setStrikePick] = useState<{ variantId: number; genVersion: number; id: number } | null>(null);
    const strikeTargetId =
        strikePick !== null &&
        strikePick.variantId === mirrorCity.header.variantId &&
        strikePick.genVersion === mirrorCity.header.genVersion
            ? strikePick.id
            : null;
    const enemyBounds = useMemo(() => mirrorCityBounds(mirrorCity), [mirrorCity]);
    const hasCityIntel = mirrorCity.targets.length > 0 || mirrorCity.signals.length > 0 || mirrorCity.aa.length > 0;

    // [StrikeRadarDiag] STRIKE-map collapse investigation: fires on EVERY visit to the STRIKE view
    // (not inside the radar, which never mounts when the snapshot is empty) so the log always says
    // which branch rendered and what the snapshot/bounds actually were at that moment.
    useEffect(() => {
        if (view !== "strike") return;
        scLog(
            `[StrikeRadarDiag] view=strike hasCityIntel=${hasCityIntel}`
            + ` variant=${mirrorCity.header.variantId} gen=${mirrorCity.header.genVersion}`
            + ` intel=${mirrorCity.header.intelLevel} mapId=${mirrorCity.header.mapId || "-"}`
            + ` signals=${mirrorCity.signals.length} targets=${mirrorCity.targets.length}`
            + ` aa=${mirrorCity.aa.length}`
            + ` contour=${mirrorCity.contour ? mirrorCity.contour.water.length : "none"}`
            + ` bounds=[${enemyBounds.MinX.toFixed(0)},${enemyBounds.MinZ.toFixed(0)}..${enemyBounds.MaxX.toFixed(0)},${enemyBounds.MaxZ.toFixed(0)}]`,
        );
    }, [view, hasCityIntel, mirrorCity, enemyBounds]);

    const pickVariantId = mirrorCity.header.variantId;
    const pickGenVersion = mirrorCity.header.genVersion;
    const onSelectEnemyTarget = useCallback((pick: EnemyTargetPick) => {
        setStrikePick({ variantId: pickVariantId, genVersion: pickGenVersion, id: pick.id });
        triggerCivic(B.SetStrikeTarget, axisRawOf(pick.axis), pick.id);
    }, [pickVariantId, pickGenVersion]);

    // Empty-map click dismisses the target card AND disarms the backend preference of the
    // picked target's axis (SetStrikeTarget with NoTargetId → auto-select) — a UI-only clear
    // would leave an invisible preference aiming the next Execute, exactly the hazard the
    // re-seed effect below exists to prevent. AA sites feed the physical axis (pick semantics).
    const onClearEnemyTarget = useCallback(() => {
        if (strikeTargetId !== null) {
            const axis =
                mirrorCity.targets.find((tg) => tg.id === strikeTargetId)?.axis ?? "physical";
            triggerCivic(B.SetStrikeTarget, axisRawOf(axis), NO_TARGET_ID);
        }
        setStrikePick(null);
    }, [strikeTargetId, mirrorCity]);

    // Re-seed the pick from the backend preference readout on (re)mount: the C# preference outlives
    // the overlay unmount and still aims the next Execute — an armed-but-invisible preference would
    // silently strike a target the UI no longer highlights. One-shot; a fresh player click always
    // overrides. First matching axis wins (a player pick sets one target at a time).
    const seededPickRef = useRef(false);
    useEffect(() => {
        if (seededPickRef.current || strikePick !== null || !hasCityIntel) return;
        const preferences = [gw.PreferredTargetPhysical, gw.PreferredTargetDigital, gw.PreferredTargetSocial];
        for (const preference of preferences) {
            const id = preference ?? NO_TARGET_ID;
            if (id < 0 || id === NO_TARGET_ID) continue;
            const known = mirrorCity.targets.some((tg) => tg.id === id) || mirrorCity.aa.some((a) => a.id === id);
            if (!known) continue;
            seededPickRef.current = true;
            setStrikePick({ variantId: pickVariantId, genVersion: pickGenVersion, id });
            return;
        }
        // No live preference to restore — stop probing once a real snapshot was seen.
        seededPickRef.current = true;
    }, [strikePick, hasCityIntel, gw.PreferredTargetPhysical, gw.PreferredTargetDigital, gw.PreferredTargetSocial,
        mirrorCity, pickVariantId, pickGenVersion]);

    const waveNumber = threatState.WaveNumber ?? 0;
    const active = threatState.ThreatsRemaining ?? 0;
    const intercepted = threatState.ThreatsIntercepted ?? 0;
    const hits = threatState.ThreatsHit ?? 0;
    const crashed = threatState.ThreatsCrashed ?? 0;
    const spawned = threatState.ThreatsSpawned ?? 0;
    const waveDataStatus = threatState.WaveDataStatus;
    const waveReady = waveDataStatus === "active" || waveDataStatus === "completed";
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
    const outboundSummaries = useMemo(
        () => outbound.map((t) => ({
            type: (t.Type === "shahed" || t.Type === "ballistic" ? t.Type : "shahed") as "shahed" | "ballistic",
            eta: t.Eta,
        })),
        [outbound],
    );

    const trackChipColor = active > 0 ? accents.crisis.accent : theme.colors.textMuted;

    const objectiveProgress = gw.ObjectiveProgress ?? 0;
    const objectivePct = Math.round(Math.max(0, Math.min(1, objectiveProgress)) * 100);
    const droneStock = gw.DroneStock ?? 0;
    const ballisticStock = gw.BallisticStock ?? 0;
    const launcherCount = gw.DroneLauncherCount ?? 0;

    const eventColor = useCallback((kind: C2EventKind): string => {
        switch (kind) {
            case "intercept": return accents.schemes.accent;
            case "hit": return accents.crisis.accent;
            case "crash": return accents.resilience.accent;
            case "spawn": return theme.colors.textSecondary;
            case "launch": return accents.schemes.accent;
            case "arrival": return accents.resilience.accent;
            case "intercepted": return accents.crisis.accent;
            default: return phaseColor;
        }
    }, [accents, theme, phaseColor]);

    return (
        <div style={s.root}>
            {/* ---- Header: operation / axis chips / objective / clock / close ---- */}
            <div style={s.header}>
                <div style={s.headerLeft}>
                    <span style={s.headerAccent} />
                    <div style={s.headerTitleWrap}>
                        <span style={s.headerLabel}>{l.t("UI_WARROOM_TITLE")}</span>
                        <span style={s.headerOperation}>
                            {waveNumber > 0 ? l.t("UI_WARROOM_OPERATION_WAVE", waveNumber) : l.t("UI_WARROOM_STANDING_WATCH")}
                        </span>
                    </div>
                    <div style={s.chipRow}>
                        <div style={s.chip(phaseColor)}>
                            <span style={s.chipDot(phaseColor, isActiveAssaultPhase(phase))} />
                            <span style={s.chipLabel(phaseColor)}>{phaseName}</span>
                        </div>
                        <div style={s.chip(trackChipColor)}>
                            <span style={s.chipDot(trackChipColor, false)} />
                            <span style={s.chipLabel(trackChipColor)}>{l.t("UI_WARROOM_TRACKS", active)}</span>
                        </div>
                    </div>
                </div>

                {/* Center — three compact enemy-axis chips (replacing the old full-width strip). */}
                <div style={s.headerAxisWrap}>
                    <WarRoomAxisChips gw={gw} intelLevel={intelLevel} />
                </div>

                {/* Right — objective ring + clock + close. */}
                <div style={s.headerRight}>
                    <div style={s.headerObjective}>
                        <WarRoomProgressRing
                            id="obj"
                            pct={objectivePct}
                            size={40}
                            stroke={11}
                            color={cmd.sweep}
                            trackColor={hexToRgba(cmd.ring, 0.6)}
                        >
                            <span style={s.headerObjValue}>{`${objectivePct}%`}</span>
                        </WarRoomProgressRing>
                        <span style={s.headerObjLabel}>{l.t("UI_WARROOM_OBJECTIVE_ALL")}</span>
                    </div>
                    <WarRoomClock style={s.clock} />
                    <div
                        style={s.closeButton}
                        role="button"
                        tabIndex={0}
                        onClick={onClose}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") {
                                e.preventDefault();
                                onClose();
                            }
                        }}
                    >
                        {l.t("UI_WARROOM_CLOSE")}
                    </div>
                </div>
            </div>

            {/* ---- Body: traces / radar / launch console -------------------- */}
            <div style={s.body}>
                {/* Left — wave card + ACTIVE TRACES (threats + telemetry) + AIR DEFENSE + AA ROSTER */}
                <div style={s.sidePanel("left")}>
                    <WarRoomWaveCard
                        phase={phase}
                        phaseColor={phaseColor}
                        phaseName={phaseName}
                        timeInPhase={timeInPhase}
                        phaseEndTime={phaseEndTime}
                        waveNumber={waveNumber}
                        scenarioStarted={threatState.ScenarioStarted}
                        waitingForLaunchWindow={threatState.WaitingForLaunchWindow ?? false}
                        active={active}
                    />

                    <div style={s.panelTitle}>{l.t("UI_WARROOM_ACTIVE_TRACES")}</div>
                    <div style={{ flexShrink: 0 }}>
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

                    <WarRoomAirDefense onManageAA={onManageAA} />
                    <WarRoomAaRoster />
                </div>

                {/* Center — SITUATION|STRIKE toggle over the map. SITUATION reuses the radar
                    (fit-height; a track click focuses camera and closes the overlay); STRIKE
                    switches the SAME radar to enemy mode (mirror-city projection) + a target card. */}
                <div style={s.center}>
                    {/* Toggle is the only in-flow child (row centers it); the clock chip and
                        the INCOMING WAVE pill live in absolute corner slots, so their width
                        changes can never nudge the toggle (cohtml sizes flex zones by content).
                        Row minHeight is pinned — nothing jumps when the pill appears. */}
                    <div style={s.toggleRow}>
                        {/* Left zone — simulation clock chip. The fullscreen War Room covers the
                            vanilla toolbar's pause/speed widget, so the overlay re-surfaces the
                            state: PAUSED (amber, prominent) or the running ×1/×2/×4 (muted). */}
                        <div style={s.toggleSlotLeft}>
                            {/* Glyphs are plain divs, NOT inline <svg>: the pause toggle swaps
                                this subtree every time, and dynamically replacing SVG nodes is
                                the cohtml layout-thread AV class (2026-07-20 22:15 crash dump —
                                null-deref in the layout walk right after this chip shipped with
                                swapped <svg> glyphs). Divs churn through React conditionals all
                                over this codebase without issue. */}
                            <div style={s.clockChip(clock.paused)}>
                                {clock.paused && (
                                    <div style={s.clockPauseBars}>
                                        <div style={s.clockPauseBar} />
                                        <div style={s.clockPauseBar} />
                                    </div>
                                )}
                                <span>
                                    {clock.paused
                                        ? l.t("UI_WARROOM_CLOCK_PAUSED")
                                        : `×${clock.speedMultiplier}`}
                                </span>
                            </div>
                        </div>
                        <div style={s.viewToggle}>
                            <div
                                style={s.viewSeg(view === "situation", "situation")}
                                role="button"
                                tabIndex={0}
                                onClick={() => setView("situation")}
                                onKeyDown={(e) => {
                                    if (e.key === "Enter" || e.key === " ") {
                                        e.preventDefault();
                                        setView("situation");
                                    }
                                }}
                            >
                                {l.t("UI_WARROOM_VIEW_SITUATION")}
                            </div>
                            <div
                                style={s.viewSeg(view === "strike", "strike")}
                                role="button"
                                tabIndex={0}
                                onClick={() => setView("strike")}
                                onKeyDown={(e) => {
                                    if (e.key === "Enter" || e.key === " ") {
                                        e.preventDefault();
                                        setView("strike");
                                    }
                                }}
                            >
                                {l.t("UI_WARROOM_VIEW_STRIKE")}
                            </div>
                        </div>
                        {/* Right zone — INCOMING WAVE alert. Pure signal, no duplicate switch
                            button: the SITUATION|STRIKE toggle in the same row is the single
                            navigation control, and a second door next to a red alert only
                            competed with the alert for attention. */}
                        <div style={s.toggleSlotRight}>
                            {view === "strike" && incomingWave && (
                                <div style={s.incomingBanner}>
                                    <span style={s.incomingText}>{l.t("UI_WARROOM_INCOMING_WAVE")}</span>
                                </div>
                            )}
                        </div>
                    </div>

                    <div style={s.mapArea}>
                        {view === "situation" ? (
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
                                theme="command"
                                showPrediction={true}
                                squareFrom="height"
                                onFocusTrack={onClose}
                            />
                        ) : hasCityIntel ? (
                            <ThreatRadar
                                mapBounds={enemyBounds}
                                theme="strike"
                                squareFrom="height"
                                enemyCity={mirrorCity}
                                selectedEnemyTargetId={strikeTargetId}
                                onSelectEnemyTarget={onSelectEnemyTarget}
                                onClearEnemyTarget={onClearEnemyTarget}
                            />
                        ) : (
                            <span style={s.eventEmpty}>{l.t("UI_WARROOM_NO_CITY_INTEL")}</span>
                        )}

                        {/* STRIKE-only: selected-target readout + per-axis prepare/execute.
                            Overlays the map bottom (see strikeCardOverlay) so mapArea keeps
                            the same height in both views — nothing shifts on switch. */}
                        {view === "strike" && hasCityIntel && (
                            <div style={s.strikeCardOverlay}>
                                <WarRoomStrikeCard
                                    snapshot={mirrorCity}
                                    selectedTargetId={strikeTargetId}
                                    intelLevel={snapshotIntelLevel}
                                    accents={accents}
                                    theme={theme}
                                    onPrepare={actions.prepareOperation}
                                    onExecute={actions.executeOperation}
                                />
                            </div>
                        )}
                    </div>
                </div>

                {/* Right — LAUNCH CONTROL (slots + arsenal) + INTEL + CITY STATUS */}
                <div style={s.sidePanel("right")}>
                    <div style={s.panelTitle}>
                        {l.t("UI_WARROOM_LAUNCH_CONTROL")}
                        <HelpSection id="counterstrike" title={l.t("UI_WARROOM_HELP_TITLE")}>
                            {l.t("HELP_COUNTERSTRIKE")}
                        </HelpSection>
                    </div>
                    <div style={{ flexShrink: 0 }}>
                        <OperationSlots state={gw} actions={actions} />
                    </div>

                    {/* Arsenal procurement — sends PurchaseCounterAttackArsenal (kindRaw, count). */}
                    <div style={s.arsenalBlock}>
                        <div style={s.arsenalTitle}>{l.t("UI_WARROOM_ARSENAL")}</div>
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
                                    -
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

                        {/* Drone launcher — field emplacement outbound drones launch from.
                            The fullscreen overlay covers the map, so placement cannot happen
                            here: the button closes the overlay, lands the Dashboard on the
                            DEFENSE tab with the build section in STRIKE mode, and takes the
                            launcher in hand (B.PlaceDroneLauncher, sync and pause-safe) —
                            the tool is already live when the tab appears. */}
                        <div style={s.counterRow}>
                            <span style={s.counterLabel}>{l.t("UI_WARROOM_LAUNCHERS")}</span>
                            <span style={s.counterValue(accents.schemes.accent)}>{launcherCount}</span>
                        </div>
                        <div
                            style={s.arsenalButton(accents.schemes.accent)}
                            role="button"
                            tabIndex={0}
                            onClick={onBuildLauncher}
                            onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                    e.preventDefault();
                                    onBuildLauncher();
                                }
                            }}
                        >
                            {l.t("UI_WARROOM_BUILD_LAUNCHER")}
                        </div>
                    </div>

                    {/* Intel — next-wave forecast + tension + insider/upgrade (existing triggers). */}
                    <WarRoomIntel />

                    {/* City status — stability / ops discount / shock / casualties / respite. */}
                    <WarRoomCityStatus gw={gw} />
                </div>
            </div>

            {/* ---- Bottom: C2 EVENT BUS single-line ticker ------------------ */}
            <WarRoomTicker events={events} colorOf={eventColor} />
        </div>
    );
});
WarRoomReady.displayName = "WarRoomReady";

export const WarRoomContent = memo(({ onClose }: { onClose: () => void }) => {
    const radar = useRadar();
    const threatData = bindingDataOrDefault(useThreat(), DEFAULT_THREAT_DTO);
    const gridWarfare = bindingDataOrDefault(useGridWarfareDomain(), DEFAULT_GRID_WARFARE_DTO);
    const mapContour = useMapContour();
    const mirrorCity = useMirrorCity();
    const actions = useGridWarfareActions();
    return (
        <WarRoomReady
            radar={radar}
            threatState={threatData}
            gw={gridWarfare}
            mapContour={mapContour}
            mirrorCity={mirrorCity}
            actions={actions}
            onClose={onClose}
        />
    );
});
WarRoomContent.displayName = "WarRoomContent";
