/**
 * Testing tab — drone spawn, explode, camera controls.
 */

import React from "react";
import { triggerCivic } from "@hooks/typedTrigger";
import { bindCivicValue } from "../../hooks/typedBinding.generated";
import { B } from "../../hooks/bindingNames.generated";
import { useSafeString } from "../../hooks/useSafeBinding";
import type { TabProps } from "./debugPanelShared";

const personalChronicleStatus$ = bindCivicValue(B.Debug_PersonalChronicleStatus, "");

// Inline newspaper glyph (SVG, never emoji — Coherent UI). Sits on the chronicle button.
const ChronicleIcon: React.FC = () => (
    <svg
        width="11"
        height="11"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        style={{ marginRight: "4rem", verticalAlign: "middle" }}
    >
        <path d="M4 4h13a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H6a2 2 0 0 1-2-2V4z" />
        <path d="M18 8h2a1 1 0 0 1 1 1v9a2 2 0 0 1-2 2" />
        <path d="M8 8h6M8 12h6M8 16h4" />
    </svg>
);

const SPAWN_BTN: React.CSSProperties = {
    flex: 1,
    padding: "6rem",
    fontSize: "10rem",
    backgroundColor: "#ff6600",
    color: "#fff",
    border: "none",
    borderRadius: "3rem",
    cursor: "pointer",
    pointerEvents: "auto" as const,
    marginRight: "4rem",
    marginBottom: "4rem",
};

const DESTROY_BTN: React.CSSProperties = {
    ...SPAWN_BTN,
    backgroundColor: "#cc0000",
};

const EXPLODE_BTN: React.CSSProperties = {
    ...SPAWN_BTN,
    backgroundColor: "#ff0066",
};

const CAMERA_BTN: React.CSSProperties = {
    ...SPAWN_BTN,
    backgroundColor: "#0088cc",
};

const PHASE_BTN: React.CSSProperties = {
    ...SPAWN_BTN,
    backgroundColor: "#0a8754",
};

const CHRONICLE_BTN: React.CSSProperties = {
    ...SPAWN_BTN,
    backgroundColor: "#2563eb",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: "none" as const,
};

export const PerfTab: React.FC<TabProps> = ({ styles: _styles, theme: _theme, accents: _accents }) => {
    const chronicleStatus = useSafeString(personalChronicleStatus$, "");

    return (
    <>
        {/* Spawn Controls */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>SPAWN:</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.DebugSetDusk)} style={{...SPAWN_BTN, backgroundColor: "#7c3aed"}}>Set Dusk</button>
                <button onClick={() => triggerCivic(B.DebugSetMidnight)} style={{...SPAWN_BTN, backgroundColor: "#1e3a8a"}}>Set Midnight</button>
                <button onClick={() => triggerCivic(B.DebugSpawnWaveDusk)} style={{...SPAWN_BTN, backgroundColor: "#5b21b6"}}>Wave (scaled)</button>
                <button onClick={() => triggerCivic(B.DebugSpawn1Drone)} style={SPAWN_BTN}>1 Drone</button>
                <button onClick={() => triggerCivic(B.DebugSpawn1Ballistic)} style={SPAWN_BTN}>1 Ballistic</button>
                <button onClick={() => triggerCivic(B.DebugSpawn25Drones)} style={SPAWN_BTN}>25 Drones</button>
                <button onClick={() => triggerCivic(B.DebugSpawn10Ballistics)} style={SPAWN_BTN}>10 Ballistics</button>
                {/* Same inbound ballistic as "1 Ballistic", but aimed at the building selected in
                    the vanilla info panel instead of at the wave target selector's own pick — the
                    round still spawns at the perimeter, flies the arc and can be shot down. Select
                    a building first; refusals (nothing selected, not a building, paused) are logged. */}
                <button onClick={() => triggerCivic(B.DebugStrikeSelectedBallistic)} style={{...SPAWN_BTN, backgroundColor: "#9f1239"}}>Ballistic → Selected</button>
            </div>
        </div>

        {/* Outbound strike — the player's own counter-strike, kept apart from the enemy SPAWN row
            above so the two directions never get mixed up. Each button runs a REAL operation of that
            axis through the ordinary Prepare/Execute path: shadow cash, munition, damage roll, target
            pick and arrival resolution against the mirror city are all the live ones. First press
            prepares (slot idle), second press executes once the timer is done; every refusal — no
            launcher, empty arsenal, empty wallet, war not started — is logged with its reason. */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>OUTBOUND STRIKE (real operation):</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.DebugStrikeDrone)} style={{...SPAWN_BTN, backgroundColor: "#047857"}}>Strike: Drone</button>
                <button onClick={() => triggerCivic(B.DebugStrikeCyber)} style={{...SPAWN_BTN, backgroundColor: "#065f46"}}>Strike: Cyber</button>
                <button onClick={() => triggerCivic(B.DebugStrikePsyops)} style={{...SPAWN_BTN, backgroundColor: "#064e3b"}}>Strike: Psyops</button>
            </div>
        </div>

        {/* Actions */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>ACTIONS:</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.DebugExplodeDrone)} style={EXPLODE_BTN}>Explode Drone</button>
                <button onClick={() => triggerCivic(B.DebugTestExplosion)} style={EXPLODE_BTN}>Explode at Camera</button>
                {/* Fires a synthetic AA burst (Gepard = 5 tracers) ~40m in front of the camera,
                    straight up, so tracers spawn in-frame for a visibility check. */}
                <button onClick={() => triggerCivic(B.DebugTestTracer)} style={{...SPAWN_BTN, backgroundColor: "#ca8a04"}}>Test Tracer @Cam</button>
                {/* Toggle: forces one live drone into the pre-fix avoidance ping-pong (twitching
                    in place) to eyeball the movement watchdog; press again to release it. */}
                <button onClick={() => triggerCivic(B.DebugGlitchDrone)} style={{...SPAWN_BTN, backgroundColor: "#b45309"}}>Glitch Drone (toggle)</button>
                <button onClick={() => triggerCivic(B.DebugDestroyAllDrones)} style={DESTROY_BTN}>Destroy All</button>
                {/* Kills 500 real citizens through vanilla deathcare (HealthProblem -> hearse) —
                    the casualty kill path, exercised on demand. */}
                <button onClick={() => triggerCivic(B.DebugKill500CiviliansNative)} style={{...DESTROY_BTN, backgroundColor: "#7f1d1d"}}>Kill 500 Civilians</button>
                {/* Demolish random live buildings through the production Destroy path (vanilla
                    Destroy event, not DestroyEntity). Each demolish bumps the building order
                    version → spatial grid/cache rebuild + swap, the suspected wave-crash race.
                    One-shot vs continuous toggle (one building per tick while active). */}
                <button onClick={() => triggerCivic(B.DebugDemolish1Building)} style={{...DESTROY_BTN, backgroundColor: "#9a3412"}}>Demolish 1 Building</button>
                <button onClick={() => triggerCivic(B.DebugDemolishBuildingsToggle)} style={{...DESTROY_BTN, backgroundColor: "#b45309"}}>Demolish (toggle)</button>
            </div>
        </div>

        {/* Crash pipeline test — deliberately kills the process with a native Burst AV to
            validate the breadcrumb → next-launch crash telemetry. DEBUG build only. */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>CRASH PIPELINE TEST:</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.DebugForceCrash)} style={{...DESTROY_BTN, backgroundColor: "#000", border: "1rem solid #cc0000"}}>Force Native Crash</button>
            </div>
        </div>

        {/* Update-restart notice — fires BOTH real channels (vanilla MessageDialog +
            React ModUpdatedRestart modal) exactly like a live ModInstanceWatch folder-
            deletion detection, without touching its once-per-process latch. */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>UPDATE NOTICE TEST:</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.DebugShowModUpdatedRestart)} style={{...SPAWN_BTN, backgroundColor: "#0e7490"}}>Restart Notice (both channels)</button>
            </div>
        </div>

        {/* Wave phase — advance the Calm→Alert→Attack→Recovery state machine. */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>WAVE PHASE:</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.DebugSkipPhase)} style={PHASE_BTN}>Skip Phase</button>
            </div>
        </div>

        {/* Camera Tracking */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>CAMERA:</div>
            <div style={{ display: "flex", flexWrap: "wrap" }}>
                <button onClick={() => triggerCivic(B.FocusNextThreat)} style={CAMERA_BTN}>Track Drone</button>
            </div>
        </div>

        {/* Personal AI Chronicle — skip the ~30 min server worker and generate
            THIS player's digest now. Idempotent per window (repeat clicks re-bill
            nothing); the feed auto-refreshes once it lands. */}
        <div style={{ marginBottom: "6rem" }}>
            <div style={{ fontSize: "10rem", color: "#888", marginBottom: "3rem" }}>PERSONAL CHRONICLE:</div>
            <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
                <button onClick={() => triggerCivic(B.DebugGeneratePersonalChronicle)} style={CHRONICLE_BTN}>
                    <ChronicleIcon />Generate My Chronicle Now
                </button>
            </div>
            {chronicleStatus
                ? <div style={{ fontSize: "10rem", color: "#9ca3af", marginTop: "2rem" }}>{chronicleStatus}</div>
                : null}
        </div>
    </>
    );
};
