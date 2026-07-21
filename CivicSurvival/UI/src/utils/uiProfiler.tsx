/**
 * UI Performance Profiler
 *
 * Measures React component render-to-commit time using useLayoutEffect.
 * Aggregates stats per component and reports to C# via trigger every 5s.
 * C# writes the report to PERF.log as "UI REACT" section.
 *
 * Usage:
 *   const ProfiledDashboard = withProfiler(Dashboard, "Dashboard");
 *   moduleRegistry.append("Game", ProfiledDashboard);
 */

import React, { useLayoutEffect, useRef } from "react";
import { triggerCivic } from "@hooks/typedTrigger";
import { B } from "../hooks/bindingNames.generated";

/**
 * Master switch. When false, withProfiler/Profiled are zero-cost passthroughs.
 * Explicitly enable via the devtools flag; production roots must not schedule
 * profiler timers or send UiProfileReport traffic by default.
 */
const PROFILING_ENABLED =
    typeof globalThis !== "undefined"
    && (globalThis as { __CIVIC_DEVTOOLS__?: boolean }).__CIVIC_DEVTOOLS__ === true;

// Coherent GT's performance.now() may have ~1ms precision (integer ms).
const now: () => number =
    typeof performance !== "undefined" && typeof performance.now === "function"
        ? () => performance.now()
        : () => Date.now();

// ═══════════════════════════════════════════════════════════════════
// Stats accumulation
// ═══════════════════════════════════════════════════════════════════

interface RenderStats {
    renders: number;
    totalMs: number;
    maxMs: number;
}

const stats = new Map<string, RenderStats>();

function recordRender(id: string, ms: number): void {
    let entry = stats.get(id);
    if (!entry) {
        entry = { renders: 0, totalMs: 0, maxMs: 0 };
        stats.set(id, entry);
    }
    entry.renders++;
    entry.totalMs += ms;
    if (ms > entry.maxMs) entry.maxMs = ms;
}

// ═══════════════════════════════════════════════════════════════════
// Report formatting + dispatch
// ═══════════════════════════════════════════════════════════════════

const REPORT_INTERVAL_MS = 5000;
const SEP = "\u2500".repeat(70);

function drain(): string | null {
    if (stats.size === 0) return null;

    let totalRenders = 0;
    let totalMs = 0;
    const entries: Array<{ id: string } & RenderStats> = [];

    stats.forEach((v, k) => {
        entries.push({ id: k, ...v });
        totalRenders += v.renders;
        totalMs += v.totalMs;
    });
    stats.clear();

    // Sort by total time descending
    entries.sort((a, b) => b.totalMs - a.totalMs);

    const lines: string[] = [];
    const totalMsText = totalMs.toFixed(3);
    lines.push(
        `UI REACT (${entries.length} components, ${totalRenders} renders \u2014 ${totalMsText}ms total)`
    );
    lines.push(SEP);
    lines.push(
        `${"COMPONENT".padEnd(24)} ${"RENDERS".padStart(8)} ${"TOTAL_MS".padStart(9)} ${"AVG_MS".padStart(8)} ${"MAX_MS".padStart(8)}`
    );

    for (const e of entries) {
        const avg = e.renders > 0 ? e.totalMs / e.renders : 0;
        const totalText = e.totalMs.toFixed(3).padStart(9);
        const avgText = avg.toFixed(2).padStart(8);
        const maxText = e.maxMs.toFixed(3).padStart(8);
        lines.push(
            `${e.id.padEnd(24)} ${String(e.renders).padStart(8)} ${totalText} ${avgText} ${maxText}`
        );
    }

    lines.push(
        `${"TOTAL".padEnd(24)} ${String(totalRenders).padStart(8)} ${totalMs.toFixed(3).padStart(9)}`
    );

    return lines.join("\n");
}

// Start reporting timer
if (PROFILING_ENABLED) {
    setInterval(() => {
        const report = drain();
        if (report) {
            try {
                triggerCivic(B.UiProfileReport, report);
            } catch {
                // Ignore if trigger fails during early initialization
            }
        }
    }, REPORT_INTERVAL_MS);
}

// ═══════════════════════════════════════════════════════════════════
// HOC: withProfiler
// ═══════════════════════════════════════════════════════════════════

/**
 * Wrap a component with performance profiling.
 * Measures render-to-commit time using useLayoutEffect.
 * When PROFILING_ENABLED is false, returns the original component (zero cost).
 */
export function withProfiler<P extends object>(
    Component: React.ComponentType<P>,
    id: string,
): React.FC<P> {
    if (!PROFILING_ENABLED) return Component as React.FC<P>;

    const Wrapper: React.FC<P> = (props) => {
        const startRef = useRef(0);
        startRef.current = now();

        useLayoutEffect(() => {
            recordRender(id, now() - startRef.current);
        });

        return <Component {...props} />;
    };
    Wrapper.displayName = `Profiled(${id})`;
    return Wrapper;
}

// ═══════════════════════════════════════════════════════════════════
// Inline wrapper: <Profiled id="name">...</Profiled>
// ═══════════════════════════════════════════════════════════════════

/**
 * Inline profiling wrapper for sub-components (tabs, views, sections).
 * When PROFILING_ENABLED is false, renders children directly (zero cost).
 */
export const Profiled: React.FC<{ id: string; children: React.ReactNode }> = PROFILING_ENABLED
    ? ({ id, children }) => {
        const startRef = useRef(0);
        startRef.current = now();

        useLayoutEffect(() => {
            recordRender(id, now() - startRef.current);
        });

        return <>{children}</>;
    }
    : ({ children }) => <>{children}</>;
