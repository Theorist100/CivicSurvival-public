# Mirror City baked maps

Baked land/water contours for the mirror-city generator (Wave 4 — MirrorCityTargets,
phase A). One JSON file per map; the file id (filename without `.json`) is the `mapId`
referenced by `MirrorCityVariantCatalog.MapPool` and loaded by
`Core/Services/MirrorCityMapCatalog.cs`.

## Format

Same coast/water payload the radar publishes (`UI/src/hooks/useMapContour.ts`,
`Core/Adapters/VanillaMapContourAdapter.cs`), plus a `name` and an optional explicit
`bounds`:

```json
{
  "name": "RIVER DELTA",
  "bounds": [minX, minZ, maxX, maxZ],
  "coast": [[x, z, x, z, ...], ...],
  "water": [[x, z, x, z, ...], ...]
}
```

- `name` — display label shown in the STRIKE view header ("MIRROR CITY #037 — RIVER
  DELTA"). Free text.
- `bounds` — the playable tile as `[minX, minZ, maxX, maxZ]` in world metres. Optional:
  when omitted, the loader derives the tile bounding box from the union of all `coast`
  and `water` points. Prefer supplying it explicitly (from the terrain
  `playableOffset` / `playableArea` rectangle) so land beyond the last coast line still
  counts as on-tile.
- `coast` — open polylines of the land↔water boundary (world-space X/Z pairs). Decorative
  for the mask: only used to derive `bounds` when `bounds` is absent. Consumed by the UI
  for the shoreline stroke.
- `water` — closed fill polygons (world-space X/Z pairs, implicitly closed). These ARE the
  land mask: `MirrorCityLandMask.IsLand` returns false inside any water polygon (even-odd
  rule). Keep them simplified — a few hundred points total is plenty.

All coordinates are world-space X/Z metres, matching the live contour payload.

## How to bake a real map

1. Load the chosen vanilla map in a DEBUG build and let the city finish loading (the
   contour is computed once terrain + water are ready — see `VanillaMapContourAdapter`).
2. Run the dev dump: `MirrorCityContourDumper.TryDumpBakedMap(reader, mapId, displayName,
   tileBounds, out path)` (DEBUG-only). `reader` is the `IMapContourReader`; pass the
   playable-area rectangle as `tileBounds` so the mask has exact bounds. It writes
   `{ModData}/CivicSurvival/MirrorCityMaps/{mapId}.json` in the format above.
3. (Optional) Simplify the polygons offline with the `Tools/` simplification script to
   keep the resource small (~2–5 KB) before committing.
4. Copy the file into this folder and add its `mapId` to
   `MirrorCityVariantCatalog.MapPool`.

The build-time catalog validator runs `MirrorCityGenerator.Generate` +
`MirrorCityGenerator.Validate` for every `(mapId, seed)` pair against the real mask and
rejects any seed that fails (targets off land, spacing, tier set, AA coverage).

## `synthetic_test_island.json`

Placeholder synthetic geometry: a rectangular tile (`[-7000,-7000,7000,7000]`) with one
square lake in the `(+X,+Z)` quadrant. Not a real vanilla map — it ships so the pipeline
(catalog load → mask → generate → validate) is exercisable before the real dumps exist.
Replace/extend with real baked maps at the dev-dump stage.
