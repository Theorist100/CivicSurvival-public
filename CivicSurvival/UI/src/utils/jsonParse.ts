/**
 * Safe JSON parsing utility for DTO bindings.
 */

import { scLog } from "./logging";
import { PARSE_PROFILING_ENABLED, parseNow, recordParse } from "./parseProfiler";

export function safeJsonParse<T>(
    raw: unknown,
    validate: (value: unknown) => value is T,
    debugName?: string
): T | null {
    if (typeof raw !== "string" || raw === "" || raw === "{}") return null;

    if (!PARSE_PROFILING_ENABLED) {
        try {
            const parsed: unknown = JSON.parse(raw);
            return validate(parsed) ? parsed : null;
        } catch (e) {
            scLog(`[safeJsonParse] PARSE ERROR: ${String(e)} | preview=${raw.slice(0, 200)}`);
            return null;
        }
    }

    const t0 = parseNow();
    let parsed: unknown;
    try {
        parsed = JSON.parse(raw);
    } catch (e) {
        scLog(`[safeJsonParse] PARSE ERROR: ${String(e)} | preview=${raw.slice(0, 200)}`);
        return null;
    }
    const t1 = parseNow();
    const ok = validate(parsed);
    const t2 = parseNow();
    recordParse(debugName ?? "?", t1 - t0, t2 - t1);
    return ok ? (parsed as T) : null;
}
