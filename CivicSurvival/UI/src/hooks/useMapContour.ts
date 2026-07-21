/**
 * Reads the static city map-geometry binding and parses it once.
 *
 * The payload is a single object with two flat-polyline arrays:
 *   { coast: [[x,z,x,z,...],...], water: [[x,z,x,z,...],...] }
 * - coast = open polylines of the land↔water boundary (stroked shoreline).
 * - water = closed fill polygons (run-length rectangles; UI closes each implicitly).
 * All coordinates are world-space X/Z. It is published once per loaded city, so the
 * raw string changes ~once per save — parsing is memoized by the raw string to avoid
 * re-parsing on every radar tick.
 */

import { useMemo } from "react";
import { useValue } from "cs2/api";
import { mapContour$ } from "./bindings/domainJsonBindings";
import { safeJsonParse } from "../utils/jsonParse";

/** Flat [x,z,x,z,...] world-space polylines. */
export type MapContourPolylines = number[][];

export interface MapGeometry {
    coast: MapContourPolylines;
    water: MapContourPolylines;
}

const EMPTY: MapGeometry = { coast: [], water: [] };

const isPolylines = (value: unknown): value is MapContourPolylines =>
    Array.isArray(value) &&
    value.every(
        line =>
            Array.isArray(line) &&
            line.length % 2 === 0 &&
            line.every(n => typeof n === "number" && Number.isFinite(n)),
    );

const isGeometry = (value: unknown): value is MapGeometry =>
    typeof value === "object" &&
    value !== null &&
    isPolylines((value as { coast?: unknown }).coast) &&
    isPolylines((value as { water?: unknown }).water);

export const useMapContour = (): MapGeometry => {
    const raw = useValue(mapContour$);

    return useMemo(() => {
        if (typeof raw !== "string" || raw === "" || raw === "{}") {
            return EMPTY;
        }
        return safeJsonParse(raw, isGeometry, "mapContour") ?? EMPTY;
    }, [raw]);
};
