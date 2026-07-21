/**
 * WarRoomProgressRing — a single SVG progress ring, shared by the header OBJECTIVE readout
 * (small) and the left-column wave card (large countdown). Pure presentational reader.
 *
 * The arc is stroke-dashoffset driven (same technique as RadarGauges); the center content is
 * an absolutely-centered overlay so callers can stack a value + label without fighting SVG
 * text metrics. viewBox is a fixed 100x100 unit box so `size` (in rem) scales it uniformly.
 *
 * The `glow` halo is a second, wider, translucent circle UNDER the arc — the same cohtml-safe
 * technique every radar layer uses. No SVG `<filter>`/`feGaussianBlur` here: Gameface renders
 * only SVG Tiny and drops filter elements, so a filter-based glow either vanishes or takes the
 * arc down with an unresolvable `url(#...)` reference.
 */

import React from "react";
import { hexToRgba } from "@themes";

interface WarRoomProgressRingProps {
    /** Unique id per instance (kept for caller stability; no defs are emitted). */
    id: string;
    /** 0..100 — fraction of the ring to fill. */
    pct: number;
    /** Outer diameter, in rem. */
    size: number;
    /** Stroke width in viewBox units (100-unit box). */
    stroke: number;
    color: string;
    trackColor: string;
    glow?: boolean;
    children?: React.ReactNode;
}

export const WarRoomProgressRing: React.FC<WarRoomProgressRingProps> = ({
    pct,
    size,
    stroke,
    color,
    trackColor,
    glow = false,
    children,
}) => {
    // Input normalization at the public boundary: `pct`/`stroke`/`size` are plain numbers with
    // no caller-side guarantee — a NaN pct would put NaN into strokeDashoffset, a negative or
    // non-finite stroke would emit an invalid stroke-width (arc/track/halo vanish), a NaN size
    // would collapse the container to "NaNrem". EVERY raw input is folded to a safe value here
    // instead of trusting call sites; the SAME sanitized value feeds every formula so no output
    // ever mixes a sanitized radius with a raw stroke.
    const safePct = Number.isFinite(pct) ? pct : 0;
    const clamped = Math.max(0, Math.min(100, safePct));
    const safeStroke = Number.isFinite(stroke) ? Math.max(0, stroke) : 0;
    const safeSize = Number.isFinite(size) && size > 0 ? size : 1;
    const radius = Math.max(0.5, 50 - safeStroke / 2);
    const circumference = 2 * Math.PI * radius;
    const offset = circumference * (1 - clamped / 100);
    // The halo is a wider stroke on a SMALLER radius so its outer edge stays inside the 100-unit
    // viewBox (outer SVG clips overflow: a halo centred on the arc radius would get its outer
    // half shaved off flat instead of fading out).
    const glowStroke = safeStroke * 2.2;
    const glowRadius = Math.max(0.5, radius - (glowStroke - safeStroke) / 2);
    const glowCircumference = 2 * Math.PI * glowRadius;
    const glowOffset = glowCircumference * (1 - clamped / 100);

    return (
        <div style={{ position: "relative", width: `${safeSize}rem`, height: `${safeSize}rem`, flexShrink: 0 } as React.CSSProperties}>
            <svg style={{ width: "100%", height: "100%" }} viewBox="0 0 100 100">
                <circle cx="50" cy="50" r={radius} fill="none" stroke={trackColor} strokeWidth={safeStroke} />
                {glow && (
                    <circle
                        cx="50"
                        cy="50"
                        r={glowRadius}
                        fill="none"
                        stroke={hexToRgba(color, 0.28)}
                        strokeWidth={glowStroke}
                        strokeLinecap="round"
                        strokeDasharray={glowCircumference}
                        strokeDashoffset={glowOffset}
                        transform="rotate(-90 50 50)"
                    />
                )}
                <circle
                    cx="50"
                    cy="50"
                    r={radius}
                    fill="none"
                    stroke={color}
                    strokeWidth={safeStroke}
                    strokeLinecap="round"
                    strokeDasharray={circumference}
                    strokeDashoffset={offset}
                    transform="rotate(-90 50 50)"
                />
            </svg>
            <div
                style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    right: 0,
                    bottom: 0,
                    display: "flex",
                    flexDirection: "column",
                    minHeight: `${safeSize}rem`,
                    alignItems: "center",
                    justifyContent: "center",
                    minWidth: 0,
                } as React.CSSProperties}
            >
                {children}
            </div>
        </div>
    );
};
