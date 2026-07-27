using System.Collections.Generic;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure, immutable land/water oracle for the mirror-city generator. It holds the
    /// axis-aligned tile bounds plus the set of closed water polygons (world-space X/Z),
    /// and answers the single question the generator needs: "is this world point on land?".
    ///
    /// The payload is the same coast/water geometry the radar publishes
    /// (see <c>UI/src/hooks/useMapContour.ts</c> and
    /// <see cref="CivicSurvival.Core.Adapters.VanillaMapContourAdapter"/>): the WATER array
    /// carries the closed fill polygons, which are exactly the point-in-polygon regions that
    /// mark "not land". The coast polylines never affect <see cref="IsLand"/> — the mask
    /// carries them only so the STRIKE-radar underlay can stroke the shoreline the same way
    /// the friendly radar does (fill-only water + coast stroke on top; without the stroke the
    /// scanline-run water rectangles read as bare stacked boxes).
    ///
    /// Determinism / purity: this type performs NO I/O and holds no engine state. The JSON
    /// resource loading lives one layer up in <c>Core/Services</c> (Axiom 5: Core/Logic stays
    /// pure and never reads resources); the mask is constructed from already-parsed geometry.
    /// A land point is one inside the tile bounds AND outside every water polygon, tested with
    /// the standard even-odd (ray-casting) rule so islands-in-lakes and lakes-in-islands both
    /// resolve correctly.
    /// </summary>
    public sealed class MirrorCityLandMask
    {
        // Axis-aligned tile bounds in world metres. A point outside this rectangle is never
        // land — it is off the playable tile.
        private readonly float m_MinX;
        private readonly float m_MinZ;
        private readonly float m_MaxX;
        private readonly float m_MaxZ;

        // Closed water polygons (world-space X/Z vertices). Each polygon is treated as
        // implicitly closed (last vertex connects back to the first). A point inside an
        // odd number of these polygons is water.
        private readonly float2[][] m_WaterPolygons;

        // Open land↔water boundary polylines (world-space X/Z vertices). Decorative only:
        // never consulted by IsLand, carried for the STRIKE-radar shoreline stroke.
        private readonly float2[][] m_CoastPolylines;

        /// <summary>Tile minimum X (world metres).</summary>
        public float MinX => m_MinX;

        /// <summary>Tile minimum Z (world metres).</summary>
        public float MinZ => m_MinZ;

        /// <summary>Tile maximum X (world metres).</summary>
        public float MaxX => m_MaxX;

        /// <summary>Tile maximum Z (world metres).</summary>
        public float MaxZ => m_MaxZ;

        /// <summary>Tile width (world metres).</summary>
        public float Width => m_MaxX - m_MinX;

        /// <summary>Tile height/depth (world metres).</summary>
        public float Height => m_MaxZ - m_MinZ;

        /// <summary>Number of water polygons the mask carries.</summary>
        public int WaterPolygonCount => m_WaterPolygons.Length;

        /// <summary>
        /// Read-only view of one water polygon's vertices (world-space X/Z, implicitly closed).
        /// Span view keeps the mask immutable (no caller-visible array reference) and alloc-free —
        /// used by the snapshot builder to serialize the contour for the STRIKE radar underlay.
        /// </summary>
        public System.ReadOnlySpan<float2> GetWaterPolygon(int index) => m_WaterPolygons[index];

        /// <summary>Number of coast polylines the mask carries (decorative shoreline stroke).</summary>
        public int CoastPolylineCount => m_CoastPolylines.Length;

        /// <summary>
        /// Read-only view of one coast polyline's vertices (world-space X/Z, open line).
        /// Same span discipline as <see cref="GetWaterPolygon"/>.
        /// </summary>
        public System.ReadOnlySpan<float2> GetCoastPolyline(int index) => m_CoastPolylines[index];

        /// <summary>
        /// The all-land default playable tile (the ~14.3 km CS2 playable area centred at origin,
        /// no water). The explicit content-pipeline fallback: when a variant's baked map resource
        /// fails to load, generation proceeds on THIS mask through the one real
        /// <c>MirrorCityGenerator.Generate</c> entry point — the mask is the only thing that
        /// differs, never the code path.
        /// </summary>
        public static readonly MirrorCityLandMask AllLandDefaultTile =
            new(-7150f, -7150f, 7150f, 7150f, null, null);

        /// <summary>
        /// Construct from parsed geometry: the tile bounds, the closed water polygons and the
        /// decorative coast polylines. Bounds are normalised so <c>Min</c> ≤ <c>Max</c>
        /// regardless of argument order. A null polygon set is treated as "no water" (the
        /// whole tile is land); a null coast set as "no shoreline stroke".
        /// </summary>
        public MirrorCityLandMask(
            float minX, float minZ, float maxX, float maxZ,
            IReadOnlyList<float2[]>? waterPolygons,
            IReadOnlyList<float2[]>? coastPolylines)
        {
            m_MinX = math.min(minX, maxX);
            m_MaxX = math.max(minX, maxX);
            m_MinZ = math.min(minZ, maxZ);
            m_MaxZ = math.max(minZ, maxZ);

            if (waterPolygons == null || waterPolygons.Count == 0)
            {
                m_WaterPolygons = System.Array.Empty<float2[]>();
            }
            else
            {
                // Copy defensively so the caller cannot mutate the mask after construction —
                // the mask is passed to a pure generator and must stay immutable for determinism.
                var polys = new List<float2[]>(waterPolygons.Count);
                foreach (var poly in waterPolygons)
                {
                    // A polygon with fewer than 3 vertices encloses no area — skip it rather
                    // than let a degenerate ring skew the even-odd count.
                    if (poly != null && poly.Length >= 3)
                        polys.Add((float2[])poly.Clone());
                }
                m_WaterPolygons = polys.ToArray();
            }

            if (coastPolylines == null || coastPolylines.Count == 0)
            {
                m_CoastPolylines = System.Array.Empty<float2[]>();
            }
            else
            {
                var lines = new List<float2[]>(coastPolylines.Count);
                foreach (var line in coastPolylines)
                {
                    // A polyline needs at least 2 points to stroke anything.
                    if (line != null && line.Length >= 2)
                        lines.Add((float2[])line.Clone());
                }
                m_CoastPolylines = lines.ToArray();
            }
        }

        /// <summary>
        /// True when the world point (<paramref name="x"/>, <paramref name="z"/>) is on land:
        /// inside the tile bounds and outside every water polygon. Points exactly on the tile
        /// boundary count as in-bounds; the even-odd rule decides polygon edges consistently.
        /// </summary>
        public bool IsLand(float x, float z)
        {
            if (x < m_MinX || x > m_MaxX || z < m_MinZ || z > m_MaxZ)
                return false;

            // Land = in-bounds and not inside any water body.
            for (int i = 0; i < m_WaterPolygons.Length; i++)
            {
                if (PointInPolygon(m_WaterPolygons[i], x, z))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Standard even-odd (ray-casting) point-in-polygon test against an implicitly closed
        /// ring of world-space X/Z vertices. Casts a ray along +X and counts edge crossings;
        /// an odd count means the point is inside.
        /// </summary>
        private static bool PointInPolygon(float2[] polygon, float x, float z)
        {
            bool inside = false;
            int n = polygon.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = polygon[i].x, zi = polygon[i].y;
                float xj = polygon[j].x, zj = polygon[j].y;

                // Does the edge (j → i) straddle the horizontal line at z? If so, count the
                // crossing when it lies to the right of x. Division-free even-odd test:
                // x < crossX ⟺ num/den > 0 ⟺ num*den > 0, where den = zj - zi is guaranteed
                // non-zero whenever the edge straddles z — this sidesteps the divide entirely.
                bool straddles = (zi > z) != (zj > z);
                if (straddles)
                {
                    float den = zj - zi;
                    float num = (xi - x) * den + (z - zi) * (xj - xi);
                    if (num * den > 0f)
                        inside = !inside;
                }
            }
            return inside;
        }
    }
}
