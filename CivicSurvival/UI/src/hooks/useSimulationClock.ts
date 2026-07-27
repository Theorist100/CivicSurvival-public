import { bindValue, useValue } from "cs2/api";

/**
 * Vanilla simulation clock: paused flag + selected speed. The fullscreen War Room hides the
 * vanilla toolbar (with its pause/speed widget), so overlays that cover it re-surface this
 * state themselves.
 *
 * Source bindings are vanilla `Game.UI.InGame.TimeUISystem` (decompile-verified):
 *   ("time", "simulationPaused") — bool, selectedSpeed == 0;
 *   ("time", "simulationSpeed")  — int INDEX 0..2, multiplier = 2^index (×1/×2/×4). While
 *   paused it reports the speed the game will resume at, not 0.
 * No mod-side publisher exists — these are engine bindings, hence raw bindValue on the
 * vanilla group instead of the civic binding manifest.
 */
const VANILLA_TIME_GROUP = "time";
const SIMULATION_PAUSED_BINDING = "simulationPaused";
const SIMULATION_SPEED_BINDING = "simulationSpeed";

const simulationPaused$ = bindValue<boolean>(VANILLA_TIME_GROUP, SIMULATION_PAUSED_BINDING, false);
const simulationSpeedIndex$ = bindValue<number>(VANILLA_TIME_GROUP, SIMULATION_SPEED_BINDING, 0);

export interface SimulationClock {
    paused: boolean;
    // Human-facing multiplier (1 | 2 | 4) — already decoded from the vanilla speed index.
    speedMultiplier: number;
}

export const useSimulationClock = (): SimulationClock => {
    // useValue widens to `T | {}` until the binding delivers — narrow by shape, defaulting
    // to "running at ×1" so the chip never claims PAUSED before real data arrives.
    const pausedRaw = useValue(simulationPaused$);
    const speedRaw = useValue(simulationSpeedIndex$);
    const paused = pausedRaw === true;
    const speedIndex = typeof speedRaw === "number" ? speedRaw : 0;
    const clamped = Math.max(0, Math.min(2, Math.round(speedIndex)));
    return { paused, speedMultiplier: 2 ** clamped };
};
