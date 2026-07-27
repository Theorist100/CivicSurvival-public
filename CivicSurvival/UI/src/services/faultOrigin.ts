/**
 * Fault-origin classification for crash reports.
 *
 * Encodes the rule triage already follows by hand (`Docs/Diagnostics/CRASH_TRIAGE.md` §8.1):
 * authorship is decided by the TOP frame — the throw site — not by whether our bundle appears
 * somewhere in the stack. A vanilla component that faults while our UI is mounted carries our
 * file in lower frames and is still a vanilla fault.
 *
 * Why this lives in code rather than in a reviewer's head: a wheel-handler fault inside vanilla's
 * own `item-grid` reached prod on 0.3.30 looking like ours, and resolving it took reading minified
 * offsets by hand. Tagged at send time, the same row is one dashboard filter away.
 *
 * Nothing is dropped here. A vanilla fault triggered by mod state is a real finding — the point is
 * to make authorship filterable, not to hide half the reports.
 */

/** Deployed bundle filename — the only marker that identifies a frame as ours. */
const MOD_BUNDLE = "CivicSurvival.mjs";

export type FaultOrigin = "mod" | "vanilla" | "unknown";

export interface FaultAttribution {
    /** Authorship of the throw site. */
    origin: FaultOrigin;
    /** Whether our bundle appears anywhere in the stack — context, never authorship. */
    modInStack: boolean;
}

/** Classify a single script URL (e.g. `ErrorEvent.filename`, which is already the throw site). */
export function originOfFile(filename: string | null | undefined): FaultOrigin {
    if (!filename) return "unknown";
    return filename.includes(MOD_BUNDLE) ? "mod" : "vanilla";
}

/**
 * Classify a Sentry stack-frame list. Sentry orders frames oldest-first, so the throw site is the
 * LAST frame carrying a filename — reading `frames[0]` would name the entry point instead, which
 * inverts the verdict on exactly the cases this exists for.
 */
export function classifyFrames(
    frames: readonly { filename?: string }[] | undefined,
): FaultAttribution {
    if (!frames || frames.length === 0) return { origin: "unknown", modInStack: false };

    let origin: FaultOrigin = "unknown";
    let modInStack = false;

    for (const frame of frames) {
        const filename = frame.filename;
        if (!filename) continue;
        if (filename.includes(MOD_BUNDLE)) modInStack = true;
        origin = originOfFile(filename);
    }

    return { origin, modInStack };
}
