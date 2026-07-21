/**
 * AllocSlider — a Coherent-safe 0..1 slider for the broadcast-allocation drill-down.
 *
 * Track/fill/cap-tick/knob are custom-painted layers. The interaction is the ONE
 * drag pattern proven in game — onMouseDown on the element + mousemove/mouseup
 * listeners on window (the Dashboard panel drag, Dashboard.tsx). Both a hand-rolled
 * onPointerMove div and an overlaid native <input type="range"> read as dead in
 * GameFace: pointer-move events are not delivered to plain divs, and the native
 * range input's engine drag never fires either (SliderRow, the assumed reference,
 * only ever ran in the devtools panel).
 */

import React, { memo, useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useTheme } from "../../../../../themes";

interface AllocSliderProps {
    /** Current weight, 0..1 (the fill + knob position). */
    value: number;
    /** Signal-coverage ceiling, 0..1 — drawn as a cap tick; null hides it. */
    cap: number | null;
    /** Fill/knob color. */
    color: string;
    onChange: (value: number) => void;
    onDragStart?: () => void;
    onDragEnd?: () => void;
    disabled?: boolean;
}

const clamp01 = (v: number): number => Math.max(0, Math.min(1, v));

export const AllocSlider = memo(({ value, cap, color, onChange, onDragStart, onDragEnd, disabled = false }: AllocSliderProps) => {
    const theme = useTheme();
    const trackRef = useRef<HTMLDivElement>(null);
    const [dragging, setDragging] = useState(false);

    // Latest callbacks behind refs so the window-listener effect subscribes once per
    // drag instead of resubscribing on every drag-step re-render (the parent passes
    // a fresh arrow per render).
    const onChangeRef = useRef(onChange);
    const onDragEndRef = useRef(onDragEnd);
    useEffect(() => {
        onChangeRef.current = onChange;
        onDragEndRef.current = onDragEnd;
    }, [onChange, onDragEnd]);

    // Cache the track geometry measured in the sanctioned layout phase — reading
    // getBoundingClientRect from a mouse handler races the cohtml layout thread and
    // null-derefs natively (civic/no-layout-read-in-handlers). Re-measured after every
    // commit (the component re-renders on each drag step, so this stays current).
    const rectRef = useRef({ left: 0, width: 1 });
    useLayoutEffect(() => {
        const el = trackRef.current;
        if (el === null) return;
        const r = el.getBoundingClientRect();
        rectRef.current = { left: r.left, width: r.width > 0 ? r.width : 1 };
    });

    const valueFromClientX = useCallback((clientX: number): number => {
        const { left, width } = rectRef.current;
        return clamp01((clientX - left) / width);
    }, []);

    const handleMouseDown = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
        if (disabled) return;
        e.preventDefault();
        setDragging(true);
        onDragStart?.();
        onChange(valueFromClientX(e.clientX));
    }, [disabled, onChange, onDragStart, valueFromClientX]);

    // Window-level listeners live only while a drag is active (the Dashboard drag
    // pattern): the knob keeps following the mouse even when it leaves the track,
    // and the release is caught anywhere on screen.
    useEffect(() => {
        if (!dragging) return;

        const handleMouseMove = (e: MouseEvent) => {
            onChangeRef.current(valueFromClientX(e.clientX));
        };
        const handleMouseUp = () => {
            setDragging(false);
            onDragEndRef.current?.();
        };

        window.addEventListener("mousemove", handleMouseMove);
        window.addEventListener("mouseup", handleMouseUp);
        return () => {
            window.removeEventListener("mousemove", handleMouseMove);
            window.removeEventListener("mouseup", handleMouseUp);
        };
    }, [dragging, valueFromClientX]);

    // Unmount-commit: if the view unmounts mid-drag (owner auto-exit, panel close)
    // the mouseup never reaches the listener above — commit the last dragged value.
    const draggingRef = useRef(false);
    draggingRef.current = dragging;
    useEffect(() => () => {
        if (draggingRef.current) onDragEndRef.current?.();
    }, []);

    const pct = clamp01(value) * 100;
    const capPct = cap === null ? null : clamp01(cap) * 100;

    return (
        <div
            ref={trackRef}
            onMouseDown={handleMouseDown}
            role="slider"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(pct)}
            tabIndex={disabled ? -1 : 0}
            onKeyDown={(e) => {
                if (disabled) return;
                if (e.key === "ArrowLeft") { onChange(clamp01(value - 0.05)); onDragEnd?.(); }
                if (e.key === "ArrowRight") { onChange(clamp01(value + 0.05)); onDragEnd?.(); }
            }}
            style={{
                position: "relative",
                height: "14rem",
                borderRadius: "7rem",
                backgroundColor: theme.colors.surface,
                border: `2rem solid ${theme.colors.border}`,
                cursor: disabled ? "not-allowed" : "pointer",
                opacity: disabled ? 0.55 : 1,
            }}
        >
            {/* Fill = this stratum's weight — clipped to the rounded track in its own overflow
                layer. The cap tick and knob are SIBLINGS outside the clip so they keep their
                deliberate overhang (tick past the track edges, full knob at 0/100%). */}
            <div style={{
                position: "absolute",
                left: 0,
                top: 0,
                right: 0,
                bottom: 0,
                borderRadius: "5rem",
                overflow: "hidden",
            }}>
                <div style={{
                    position: "absolute",
                    left: 0,
                    top: 0,
                    bottom: 0,
                    width: `${pct}%`,
                    backgroundColor: color,
                    opacity: 0.85,
                }} />
            </div>
            {/* Signal-coverage ceiling tick — capacity past it is wasted on this stratum. Drawn
                over the fill so it stays visible whether the weight is under or over the cap. */}
            {capPct !== null && (
                <div style={{
                    position: "absolute",
                    left: `${capPct}%`,
                    top: "-2rem",
                    bottom: "-2rem",
                    width: "3rem",
                    marginLeft: "-1rem",
                    backgroundColor: theme.colors.textSecondary,
                }} />
            )}
            {/* Knob. */}
            <div style={{
                position: "absolute",
                left: `${pct}%`,
                top: "50%",
                width: "12rem",
                height: "12rem",
                marginLeft: "-6rem",
                marginTop: "-6rem",
                borderRadius: "6rem",
                backgroundColor: theme.colors.textPrimary,
                border: `2rem solid ${color}`,
            }} />
        </div>
    );
});

AllocSlider.displayName = "AllocSlider";
