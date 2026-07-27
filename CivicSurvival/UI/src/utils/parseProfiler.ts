/**
 * Opt-in JSON.parse + type-guard timing for DTO / array bindings.
 *
 * Off by default — when disabled the only cost is one boolean check in the
 * parse path. Enable by setting `globalThis.__CIVIC_DEVTOOLS__ = true` (same
 * flag uiProfiler uses). Aggregated stats flush every 5s to CivicSurvival.log
 * via scLog as a single grep-friendly `[PERF-PARSE]` line per flush.
 *
 * Produces the frontend half of structural-binding parse/guard cost evidence
 * (the backend half is the BUILD_MS / PUSH_MS columns in the PERF.log
 * UI BINDINGS section).
 */

import { scLog } from "./logging";

export const PARSE_PROFILING_ENABLED =
    typeof globalThis !== "undefined"
    && (globalThis as { __CIVIC_DEVTOOLS__?: boolean }).__CIVIC_DEVTOOLS__ === true;

// Coherent GT's performance.now() may have ~1ms precision (integer ms).
export const parseNow: () => number =
    typeof performance !== "undefined" && typeof performance.now === "function"
        ? () => performance.now()
        : () => Date.now();

interface ParseStats {
    count: number;
    parseMs: number;
    guardMs: number;
    maxMs: number;
}

const stats = new Map<string, ParseStats>();
const FLUSH_INTERVAL_MS = 5000;

export function recordParse(name: string, parseMs: number, guardMs: number): void {
    let entry = stats.get(name);
    if (entry === undefined) {
        entry = { count: 0, parseMs: 0, guardMs: 0, maxMs: 0 };
        stats.set(name, entry);
    }
    entry.count++;
    entry.parseMs += parseMs;
    entry.guardMs += guardMs;
    const total = parseMs + guardMs;
    if (total > entry.maxMs) entry.maxMs = total;
}

function flush(): void {
    if (stats.size === 0) return;

    let totalCount = 0;
    let totalParse = 0;
    let totalGuard = 0;
    const rows: string[] = [];
    stats.forEach((v, k) => {
        totalCount += v.count;
        totalParse += v.parseMs;
        totalGuard += v.guardMs;
        const avg = (v.parseMs + v.guardMs) / v.count;
        rows.push(
            `${k} n=${v.count} parse=${v.parseMs.toFixed(2)} guard=${v.guardMs.toFixed(2)} `
            + `avg=${avg.toFixed(3)} max=${v.maxMs.toFixed(2)}`,
        );
    });
    stats.clear();
    rows.sort();

    const parseText = totalParse.toFixed(1) + "ms";
    const guardText = totalGuard.toFixed(1) + "ms";
    scLog(
        `[PERF-PARSE] ${totalCount} parses, parse ${parseText}, `
        + `guard ${guardText} — ${rows.join(" | ")}`,
    );
}

if (PARSE_PROFILING_ENABLED) {
    setInterval(flush, FLUSH_INTERVAL_MS);
}
