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

// 'command' = the friendly SITUATION radar (cyan). 'strike' = the enemy-projection STRIKE
// radar (dark red) — same command-post grid, hostile palette (option C: one ThreatRadar,
// two moods, not two components).
export type RadarThemeName = 'command' | 'strike';

export const radarThemes: Record<RadarThemeName, RadarTheme> = {
    // Command-post cyan — the single friendly radar identity, reusable by the War Room.
    command: {
        bg: '#04101a',
        ring: '#123a4f',
        sweep: '#27e0ff',
        sweepGlow: 'rgba(39, 224, 255, 0.5)',
        compass: '#33586a',
        water: 'rgba(39, 200, 224, 0.15)',
    },
    // Dark-red enemy projection — the STRIKE view of the mirror city. Same grid/reticle as
    // 'command', hostile hue so the offensive board never reads as your own airspace.
    strike: {
        bg: '#180608',
        ring: '#4a1418',
        sweep: '#ff3b3b',
        sweepGlow: 'rgba(255, 59, 59, 0.5)',
        compass: '#7a3038',
        water: 'rgba(224, 60, 60, 0.12)',
    },
};

// Threat colors — command-post palette: cyan tracks on the dark navy map, danger
// (ballistic / hardlock) pops in red/orange, targets in amber.
export const threatColors = {
    shahed: '#27e0ff',            // Cyan - default unidentified air track
    shahedIdentified: '#3ef0a0',  // Green - confirmed by camera tracking
    ballistic: '#ff3b3b',         // Red - danger stands out against cyan
    target: '#ffb020',            // Amber - attacked building
    outbound: '#4d9bff',          // Friendly blue - the player's outbound counter-strike
    outboundTrail: 'rgba(77, 155, 255, 0.5)',
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
