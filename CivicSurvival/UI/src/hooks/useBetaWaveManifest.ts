import { isRecord } from "../utils/typeGuards";

export interface WaveManifest {
    current: number;
    waves: Record<string, number>;
}

export function isWaveManifest(value: unknown): value is WaveManifest {
    if (!isRecord(value)) return false;
    if (typeof value.current !== "number" || !Number.isInteger(value.current) || value.current < 1) return false;
    if (!isRecord(value.waves)) return false;
    for (const v of Object.values(value.waves)) {
        if (typeof v !== "number" || !Number.isInteger(v) || v < 1) return false;
    }
    return true;
}
