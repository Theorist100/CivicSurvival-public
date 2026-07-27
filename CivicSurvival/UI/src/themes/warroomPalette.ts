/**
 * WAR ROOM command-surface palette — the navy+crimson calm-down approved on the
 * 2026-07-22 mock: surfaces are deep navy, EVERYTHING neutral except one hot colour
 * (the crisis accent) for genuinely critical facts and a point amber for "unknown".
 * Axis/operation identity colours survive only as thin edge/track lines.
 *
 * Deliberately a standalone constant module (not theme.colors): this is a tab-local
 * identity for the War Room command surfaces, not a global reskin — the global
 * navy+crimson candidate is tracked separately (Opinion Board rework note).
 */
export const WARROOM_NAVY = {
    /** Card/row surface. */
    surface: "#111a2b",
    /** Card/row border. */
    border: "#22314e",
    /** Interactive-element border (buttons) — one step brighter than card borders. */
    borderStrong: "#2c3d5e",
    /** Empty bar track. */
    track: "#1a2438",
    /** Section/label headers. */
    label: "#8fa3c4",
    /** De-emphasized captions, locked states. */
    muted: "#66738c",
    /** Primary readout text. */
    text: "#dfe4ec",
    /** Point accent for "unknown" readouts. */
    amber: "#e0b25c",
} as const;
