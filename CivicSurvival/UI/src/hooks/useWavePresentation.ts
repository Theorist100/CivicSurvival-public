/**
 * useWavePresentation — single source for how a wave phase reads on the radar
 * chrome (color + headline label).
 *
 * The calm phase is NOT a safe-green "all clear": with a scenario running there is
 * always a wave inbound, so calm is a prep window. This hook resolves calm into
 * three distinct readings instead of one green badge:
 *   - genuinely safe (no scenario / between acts) → safe green "CLEAR"
 *   - rebuilding after damage (grid still collapsed) → amber "RECOVERING"
 *   - bracing for the next wave (grid intact) → radar cyan "PREP"
 *
 * Aftermath is derived from the existing power-grid binding (same signal
 * StatusBadges surfaces) — no new C# state. Both the combat radar and the War
 * Room consume this hook so the phase reads identically in both.
 */

import { useLocale, type TranslationKey } from "@locales";
import { getPhaseColors, useAccents, useTheme } from "@themes";
import { radarThemes } from "@themes/radar";
import { bindingDataOrDefault, usePowerGrid } from "@hooks/domain";
import { DEFAULT_POWER_GRID_DTO, type WavePhase } from "../types/domainDtos";

export interface WavePresentation {
    /** Color for the phase indicator dot, name and the headline countdown value. */
    phaseColor: string;
    /** Localized phase headline, already resolved for the active locale. */
    phaseName: string;
}

// Non-calm phases map straight to the canonical phase accents and headlines.
const PHASE_NAME_KEYS: Record<Exclude<WavePhase, "calm">, TranslationKey> = {
    alert: "UI_STATUS_ALERT",
    attack: "THREAT_PHASE_INCOMING",
    recovery: "UI_STATUS_RECOVERY",
};

export function useWavePresentation(phase: WavePhase, scenarioStarted: boolean): WavePresentation {
    const l = useLocale();
    const accents = useAccents();
    const theme = useTheme();
    const grid = bindingDataOrDefault(usePowerGrid(), DEFAULT_POWER_GRID_DTO);

    // Wave phase and grid status are independent: the wave can be over (calm) while
    // the grid is still collapsed from earlier damage. That is the aftermath — the
    // city is rebuilding, not safe.
    const isAftermath = phase === "calm" && (grid.StressZone ?? "normal") === "collapsed";

    let phaseColor: string;
    let phaseNameKey: TranslationKey;
    if (phase === "calm" && !scenarioStarted) {
        // Pure safe-green is reserved for when no wave exists at all.
        phaseColor = accents.schemes.accent;
        phaseNameKey = "THREAT_PHASE_CLEAR";
    } else if (isAftermath) {
        // Amber: rebuilding under a ticking clock toward the next wave.
        phaseColor = accents.resilience.accent;
        phaseNameKey = "THREAT_PHASE_RECOVERING";
    } else if (phase === "calm") {
        // Radar cyan: on the scope, bracing for the next inbound wave (not safe).
        phaseColor = radarThemes.command.sweep;
        phaseNameKey = "THREAT_PHASE_PREP";
    } else {
        phaseColor = getPhaseColors(accents)[phase] || theme.colors.textMuted;
        phaseNameKey = PHASE_NAME_KEYS[phase];
    }

    return { phaseColor, phaseName: l.t(phaseNameKey) };
}
