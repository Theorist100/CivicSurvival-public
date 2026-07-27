/**
 * useSquareFit — measures a container and returns the largest square side (px) that fits.
 *
 * cohtml (Gameface) has no `aspect-ratio` support, so a square built from the container
 * HEIGHT (rather than the `padding-bottom: 100%` width trick) has to be sized in JS. The
 * measurement runs in a layout effect (before paint, so no first-frame flash) and re-reads
 * on every render — the War Room enemy strip can grow/shrink a row, which changes the map
 * area's height without a window resize. The change guard keeps the setState from looping.
 *
 * The hook must NOT depend on outside renders to converge: cohtml lays out on a separate
 * thread, so a freshly-mounted element reads clientWidth/clientHeight = 0 here, and a
 * memoized host (the STRIKE-mode ThreatRadar, whose props are all stable references) may
 * never render again to retrigger the read. A failed measure therefore schedules a retry
 * RENDER (a state bump — the timer callback itself never touches layout, per
 * civic/no-layout-read-in-handlers), and the re-run of this layout effect re-reads. The
 * loop stops at the first non-zero result — the caller's contract is "returns the fitting
 * side", not "returns it if something else re-renders you". The resize listener bumps the
 * same tick for the same reason.
 *
 * Layout reads live ONLY in the layout-effect body, never in an event handler or timer
 * callback (see civic/no-layout-read-in-handlers) — the same shape Dashboard's reclamp uses.
 */

import { useLayoutEffect, useRef, useState } from "react";

// Retry cadence while the container still measures 0 (cohtml layout thread not done yet).
const MEASURE_RETRY_MS = 50;
// A fresh mount measures non-zero within a frame or two; a container that still reads 0
// after this many fast retries is not "layout in flight" but hidden (display:none — e.g.
// the radar view of an inactive WAR sub-tab stays mounted hidden). Keep pumping so the
// contract "converges without outside renders" holds, but at a watchdog cadence instead
// of 20 Hz — the hidden case would otherwise self-re-render forever at full speed.
const MEASURE_RETRY_BURST = 10;
const MEASURE_RETRY_IDLE_MS = 1000;

export interface SquareFit {
    ref: React.MutableRefObject<HTMLDivElement | null>;
    /** Square side in px (0 until first measured). */
    side: number;
}

export function useSquareFit(enabled: boolean): SquareFit {
    const ref = useRef<HTMLDivElement | null>(null);
    const [side, setSide] = useState(0);
    // Render pump: bumped by the retry timer / resize so the layout effect below re-runs.
    const [, setMeasureTick] = useState(0);
    // Consecutive zero measures — switches the pump from burst to watchdog cadence.
    const zeroStreakRef = useRef(0);

    // eslint-disable-next-line react-hooks/exhaustive-deps -- deliberately dep-less: re-measures on every render (container height can change without a resize event); the same-value guard in setSide breaks the update chain
    useLayoutEffect(() => {
        if (!enabled) return;
        let retryId: number | null = null;
        const el = ref.current;
        const next = el ? Math.min(el.clientWidth, el.clientHeight) : 0;
        if (next > 0) {
            zeroStreakRef.current = 0;
            setSide((prev) => (Math.abs(prev - next) < 1 ? prev : next));
        } else {
            zeroStreakRef.current += 1;
            const delay = zeroStreakRef.current <= MEASURE_RETRY_BURST ? MEASURE_RETRY_MS : MEASURE_RETRY_IDLE_MS;
            retryId = window.setTimeout(() => setMeasureTick((t) => t + 1), delay);
        }
        const onResize = () => {
            // Re-arm the fast burst: a resize can turn a long-zero container non-zero.
            zeroStreakRef.current = 0;
            setMeasureTick((t) => t + 1);
        };
        window.addEventListener("resize", onResize);
        return () => {
            if (retryId !== null) window.clearTimeout(retryId);
            window.removeEventListener("resize", onResize);
        };
    });

    return { ref, side };
}
