/**
 * Reads the mirror enemy city STRIKE-view binding and parses it once.
 *
 * The payload is the quantized snapshot built C#-side by MirrorCitySnapshotBuilder — the exact
 * numbers below intel level 2 never reach the string (positions snapped, contribution bucketed,
 * hp/rebuild hidden behind the coarse `state`, AA rings inflated). Shape (stable at every level,
 * arrays populated per level):
 *
 *   {
 *     header:  { variantId, mapId, genVersion, intelLevel(0..2) },
 *     signals: [ { axis, x, z } ],                                   // L0 only, else []
 *     targets: [ { id, axis, tier, x, z, state, contrib, hpPct, rebuildPct } ], // [] at L0
 *     aa:      [ { id, x, z, range, state, hpPct } ]                 // [] at L0
 *   }
 *
 * axis   = "physical" | "digital" | "social"
 * tier   = "reserve" | "key" | "regular"  (AA sites live in `aa`, not `targets`)
 * state  = "INTACT" | "DAMAGED" | "DEAD" | "REBUILDING"
 * hpPct / rebuildPct = exact 0..100 at L2, -1 (hidden) below.
 *
 * Published ~once per meaningful change (not per frame), so parsing is memoized by the raw string.
 */

import { useMemo } from "react";
import { useValue } from "cs2/api";
import { type MapBoundsDto } from "types/domainDtos.generated";
import { mirrorCity$ } from "./bindings/domainJsonBindings";
import { safeJsonParse } from "../utils/jsonParse";

export type MirrorAxis = "physical" | "digital" | "social";
export type MirrorTier = "reserve" | "key" | "regular";
export type MirrorTargetState = "INTACT" | "DAMAGED" | "DEAD" | "REBUILDING";

export interface MirrorCityHeader {
    variantId: number;
    mapId: string;
    genVersion: number;
    intelLevel: number; // 0..2 (effective, insider already folded in C#-side)
}

export interface MirrorSignal {
    axis: MirrorAxis;
    x: number;
    z: number;
}

export interface MirrorTarget {
    id: number;
    axis: MirrorAxis;
    tier: MirrorTier;
    x: number;
    z: number;
    state: MirrorTargetState;
    contrib: number;    // L1 bucketed, L2 exact
    hpPct: number;      // L2 exact 0..100, -1 hidden below
    rebuildPct: number; // L2 exact 0..100, -1 hidden below
}

export interface MirrorAaSite {
    id: number;
    x: number;
    z: number;
    range: number; // L1 inflated ring, L2 exact
    state: MirrorTargetState;
    hpPct: number; // L2 exact 0..100, -1 hidden below
}

/**
 * Map-contour underlay (present from intel L1 when the variant's baked mask shipped with the mod):
 * tile bounds + closed water polygons + open coast polylines as flat [x,z,x,z,...] world-metre
 * rings/lines. Water is drawn fill-only; coast is the shoreline stroke laid over it (the same
 * two-pass render the friendly radar contour uses — without the stroke the scanline-run water
 * rectangles read as bare stacked boxes).
 */
export interface MirrorContour {
    bounds: [number, number, number, number]; // minX, minZ, maxX, maxZ
    water: number[][];
    coast: number[][];
}

export interface MirrorCitySnapshot {
    header: MirrorCityHeader;
    signals: MirrorSignal[];
    targets: MirrorTarget[];
    aa: MirrorAaSite[];
    contour?: MirrorContour;
}

/** Empty snapshot — "no city intel" (pre-war, or genVersion reset pending regeneration). */
export const EMPTY_MIRROR_CITY: MirrorCitySnapshot = {
    header: { variantId: -1, mapId: "", genVersion: 0, intelLevel: 0 },
    signals: [],
    targets: [],
    aa: [],
};

const MIRROR_AXES: readonly MirrorAxis[] = ["physical", "digital", "social"];
const MIRROR_TIERS: readonly MirrorTier[] = ["reserve", "key", "regular"];
const MIRROR_STATES: readonly MirrorTargetState[] = ["INTACT", "DAMAGED", "DEAD", "REBUILDING"];

const isRecord = (v: unknown): v is Record<string, unknown> =>
    typeof v === "object" && v !== null;

const num = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);

const isHeader = (v: unknown): v is MirrorCityHeader =>
    isRecord(v) &&
    num(v.variantId) &&
    typeof v.mapId === "string" &&
    num(v.genVersion) &&
    num(v.intelLevel);

const isSignal = (v: unknown): v is MirrorSignal =>
    isRecord(v) &&
    MIRROR_AXES.includes(v.axis as MirrorAxis) &&
    num(v.x) &&
    num(v.z);

const isTarget = (v: unknown): v is MirrorTarget =>
    isRecord(v) &&
    num(v.id) &&
    MIRROR_AXES.includes(v.axis as MirrorAxis) &&
    MIRROR_TIERS.includes(v.tier as MirrorTier) &&
    num(v.x) && num(v.z) &&
    MIRROR_STATES.includes(v.state as MirrorTargetState) &&
    num(v.contrib) && num(v.hpPct) && num(v.rebuildPct);

const isAaSite = (v: unknown): v is MirrorAaSite =>
    isRecord(v) &&
    num(v.id) &&
    num(v.x) && num(v.z) &&
    num(v.range) &&
    MIRROR_STATES.includes(v.state as MirrorTargetState) &&
    num(v.hpPct);

const isPolylineSet = (v: unknown): v is number[][] =>
    Array.isArray(v) && v.every(ring => Array.isArray(ring) && ring.length % 2 === 0 && ring.every(num));

const isContour = (v: unknown): v is MirrorContour =>
    isRecord(v) &&
    Array.isArray(v.bounds) && v.bounds.length === 4 && v.bounds.every(num) &&
    isPolylineSet(v.water) &&
    isPolylineSet(v.coast);

const isSnapshot = (v: unknown): v is MirrorCitySnapshot =>
    isRecord(v) &&
    isHeader(v.header) &&
    Array.isArray(v.signals) && v.signals.every(isSignal) &&
    Array.isArray(v.targets) && v.targets.every(isTarget) &&
    Array.isArray(v.aa) && v.aa.every(isAaSite) &&
    (v.contour === undefined || isContour(v.contour));

export const useMirrorCity = (): MirrorCitySnapshot => {
    const raw = useValue(mirrorCity$);

    return useMemo(() => {
        if (typeof raw !== "string" || raw === "" || raw === "{}") {
            return EMPTY_MIRROR_CITY;
        }
        return safeJsonParse(raw, isSnapshot, "mirrorCity") ?? EMPTY_MIRROR_CITY;
    }, [raw]);
};

// Fallback half-extent (world metres) when the snapshot has a single point (or none) to bound —
// keeps the derived square non-degenerate so normalizePosition never divides by ~0.
const MIN_HALF_SPAN = 512;
// Fraction of the content span added as margin on each side, so markers/AA rings never clip the edge.
const BOUNDS_PAD_FRACTION = 0.12;

/**
 * Derive radar bounds for the enemy projection from the snapshot itself. Unlike the friendly radar
 * (which gets the city's real MapBounds), the mirror city ships no separate bounds DTO:
 *   - with a contour underlay (intel L1+ and a shipped baked mask) the frame IS the map tile
 *     (`contour.bounds`), edge to edge: the tile is the geographic context the underlay exists
 *     for, so it is neither shrunk to fit AA coverage discs nor padded — folding the discs in
 *     inflated the frame past the tile and the map floated as a ~80% inset with dead grid
 *     around it. An AA ring poking past the tile edge clips at the radar rim like any map
 *     annotation; targets/signals always sit on-tile, so nothing else can be cropped;
 *   - otherwise the frame derives from the target/AA/signal points, where an AA site folds its
 *     whole coverage disc (centre ± range) — a ring is drawn geometry, and padding only the centre
 *     point let rim sites clip their domes at the radar edge. Padded, and squared so world X/Z
 *     map to the radar face without axis distortion.
 * Memoize on the snapshot in the caller. Empty snapshot → a symmetric default box.
 */
export const mirrorCityBounds = (snap: MirrorCitySnapshot): MapBoundsDto => {
    if (snap.contour) {
        const [bMinX, bMinZ, bMaxX, bMaxZ] = snap.contour.bounds;
        const cx = (bMinX + bMaxX) / 2;
        const cz = (bMinZ + bMaxZ) / 2;
        // Baked tiles are square; the larger-axis pick + MIN_HALF_SPAN only guard a degenerate
        // or non-square bounds entry so normalizePosition never divides by ~0.
        const half = Math.max((bMaxX - bMinX) / 2, (bMaxZ - bMinZ) / 2, MIN_HALF_SPAN);
        return { MinX: cx - half, MaxX: cx + half, MinZ: cz - half, MaxZ: cz + half };
    }

    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    let any = false;

    const fold = (x: number, z: number) => {
        any = true;
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (z < minZ) minZ = z;
        if (z > maxZ) maxZ = z;
    };

    for (const t of snap.targets) fold(t.x, t.z);
    for (const a of snap.aa) {
        const r = Math.max(0, a.range);
        fold(a.x - r, a.z - r);
        fold(a.x + r, a.z + r);
    }
    for (const s of snap.signals) fold(s.x, s.z);

    if (!any) {
        return { MinX: -MIN_HALF_SPAN, MaxX: MIN_HALF_SPAN, MinZ: -MIN_HALF_SPAN, MaxZ: MIN_HALF_SPAN };
    }

    const cx = (minX + maxX) / 2;
    const cz = (minZ + maxZ) / 2;
    // Square the frame on the larger axis so the world circle of an AA ring stays circular on-radar.
    let half = Math.max((maxX - minX) / 2, (maxZ - minZ) / 2, MIN_HALF_SPAN);
    half *= 1 + BOUNDS_PAD_FRACTION;

    return { MinX: cx - half, MaxX: cx + half, MinZ: cz - half, MaxZ: cz + half };
};
