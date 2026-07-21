import { useMemo } from "react";
import { useValue } from "cs2/api";
import { B } from "./bindingNames.generated";
import { bindCivicValue } from "./typedBinding.generated";
import { type FeatureId } from "../types/semantic";
import { safeJsonParse } from "../utils/jsonParse";
import { isWaveManifest, type WaveManifest } from "./useBetaWaveManifest";

/**
 * Beta wave manifest published by MainUISystem.
 *
 * Format: { current: int, waves: { featureId: int } }
 * - `current` is the active beta wave (1-based).
 * - `waves` maps feature ids to the wave that unlocks them. Wave 99 = ships in
 *   v2 of the mod, not in v1 at all.
 * - Features missing from `waves` default to wave 1 (always open).
 *
 * Source: Docs/Project/Beta/WAVE_ROADMAP.md → balance_config.json.
 */
const PENDING_MANIFEST = "__CIVIC_FEATURE_WAVE_MANIFEST_PENDING__";
const WAVE_SENTINEL_UNAVAILABLE = 99;

const waveManifest$ = bindCivicValue(B.FeatureWaveManifest, PENDING_MANIFEST);

type ManifestState =
    | { received: false }
    | { received: true; manifest: WaveManifest };

export function useWaveManifest(): ManifestState {
    const raw = useValue(waveManifest$);
    return useMemo(() => {
        if (raw === PENDING_MANIFEST || typeof raw !== "string" || raw.length === 0) return { received: false };
        const parsed = safeJsonParse(raw, isWaveManifest);
        if (parsed == null) return { received: false };
        return { received: true, manifest: parsed };
    }, [raw]);
}

export type BetaWaveState =
    | { status: "pending"; isLocked: true }
    | { status: "open"; isLocked: false; wave: number; current: number }
    | { status: "preview"; isLocked: true; wave: number; current: number }
    | { status: "unavailable"; isLocked: true; current: number };

export function useBetaWave(featureId: FeatureId): BetaWaveState {
    const manifestState = useWaveManifest();

    if (!manifestState.received) {
        return { status: "pending", isLocked: true };
    }

    const { manifest } = manifestState;

    // Sentinel wave = not shipped in this build -> later version.
    const wave = manifest.waves[featureId] ?? 1;
    if (wave >= WAVE_SENTINEL_UNAVAILABLE) {
        return { status: "unavailable", isLocked: true, current: manifest.current };
    }

    // Missing entry = wave 1 (always reached), mirroring backend FeatureManifest.WaveOf.
    if (wave > manifest.current) {
        return { status: "preview", isLocked: true, wave, current: manifest.current };
    }

    return { status: "open", isLocked: false, wave, current: manifest.current };
}
