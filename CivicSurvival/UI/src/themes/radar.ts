/**
 * Radar color themes - single source of truth.
 *
 * Only the command-post cyan theme survives. The old phase-driven themes
 * (classic green / techNoir blue / warning red) repainted the whole map by
 * wave phase — that radar was replaced by the command-post look and is gone.
 */

export interface RadarTheme {
    bg: string;
    ring: string;
    sweep: string;
    sweepGlow: string;
    compass: string;
    // Semi-transparent fill for water bodies on the map layer (drawn under the grid).
    water: string;
}

export type RadarThemeName = 'command';

export const radarThemes: Record<RadarThemeName, RadarTheme> = {
    // Command-post cyan — the single radar identity, reusable by the War Room.
    command: {
        bg: '#04101a',
        ring: '#123a4f',
        sweep: '#27e0ff',
        sweepGlow: 'rgba(39, 224, 255, 0.5)',
        compass: '#33586a',
        water: 'rgba(39, 200, 224, 0.15)',
    },
};

// Threat colors — command-post palette: cyan tracks on the dark navy map, danger
// (ballistic / hardlock) pops in red/orange, targets in amber.
export const threatColors = {
    shahed: '#27e0ff',            // Cyan - default unidentified air track
    shahedIdentified: '#3ef0a0',  // Green - confirmed by camera tracking
    ballistic: '#ff3b3b',         // Red - danger stands out against cyan
    target: '#ffb020',            // Amber - attacked building
    interceptionSuccess: '#3ef0a0',
    interceptionFail: '#ff7a18',
    prediction: 'rgba(39, 224, 255, 0.28)',
    trail: 'rgba(39, 224, 255, 0.5)',
};

// Evasion status colors - command post style (no percentages, just status)
// targeted = normal tracking (cyan - the calm command color)
// evasive = maneuvering, AA having trouble (yellow, blinking)
// hardlock = critical evasion, very hard to hit (orange, fast blink)
export const evasionStatusColors = {
    targeted: '#27e0ff',   // Cyan - normal track
    evasive: '#ffd23f',    // Yellow - warning
    hardlock: '#ff7a18',   // Orange - critical
};
