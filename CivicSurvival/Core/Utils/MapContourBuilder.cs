using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Pure (ECS-free) helper that turns a boolean land/water grid into simplified
    /// world-space geometry for the radar map.
    ///
    /// Produces two things from the same grid:
    /// <list type="bullet">
    /// <item><b>coast</b> — the land↔water boundary as open polylines (marching-
    /// squares + Douglas–Peucker), stroked as the city's real shoreline.</item>
    /// <item><b>water</b> — the water region as closed fill polygons. These are
    /// built as run-length rectangles (greedy horizontal runs per grid row, merged
    /// vertically across identical adjacent rows). Run-length rectangles are always
    /// closed and naturally clamp to the map edge (a run simply ends at the boundary
    /// cell where the sea leaves the playable area), so the fill is well-defined even
    /// where the sea runs off the map. The coast stroke is drawn on top and visually
    /// masks the rectangular stair-stepping along diagonal shores.</item>
    /// </list>
    ///
    /// Sampling and the boolean grid are produced by the caller
    /// (<see cref="CivicSurvival.Core.Adapters.VanillaMapContourAdapter"/>); this
    /// helper is intentionally dependency-free so it stays trivially reusable
    /// (e.g. a future PvP "enemy city" radar) and analyzable in isolation.
    ///
    /// Output JSON is a single object with two flat-polyline arrays:
    /// <c>{"coast":[[x,z,x,z,...],...],"water":[[x,z,x,z,...],...]}</c>. Each inner
    /// array is a flat list of world X/Z pairs; water polygons are implicitly closed
    /// (the UI does not repeat the first point). World coordinates let the UI
    /// normalize the geometry with the same normalizePosition() used for
    /// threats/targets, guaranteeing alignment with the markers.
    /// </summary>
    public static class MapContourBuilder
    {
        // Marching-squares corner bit flags (which corners are "water").
        private const int CORNER_BOTTOM_LEFT = 1;
        private const int CORNER_BOTTOM_RIGHT = 2;
        private const int CORNER_TOP_RIGHT = 4;
        private const int CORNER_TOP_LEFT = 8;
        // All-land (0) and all-water (BL|BR|TR|TL) cells carry no boundary.
        private const int CASE_ALL_LAND = 0;
        private const int CASE_ALL_WATER =
            CORNER_BOTTOM_LEFT | CORNER_BOTTOM_RIGHT | CORNER_TOP_RIGHT | CORNER_TOP_LEFT;

        private const int INITIAL_JSON_CAPACITY = 4096;
        // Endpoints come from a fixed grid, so coincident points match within a tiny epsilon.
        private const float STITCH_EPSILON = 0.01f;
        private const float STITCH_EPSILON_SQ = STITCH_EPSILON * STITCH_EPSILON;
        // Degenerate-segment guard for the perpendicular-distance projection.
        private const float MIN_SEGMENT_LENGTH_SQ = 1e-6f;

        /// <summary>
        /// Builds the radar map geometry (coast polylines + water fill polygons) from
        /// a boolean water grid and returns it as the <c>{"coast":[...],"water":[...]}</c>
        /// JSON described above. Empty/degenerate grid returns
        /// <c>{"coast":[],"water":[]}</c>.
        /// </summary>
        /// <param name="isWater">Row-major grid (length = cols*rows); true = water.</param>
        /// <param name="cols">Grid columns (X axis sample count).</param>
        /// <param name="rows">Grid rows (Z axis sample count).</param>
        /// <param name="origin">World position of grid cell (0,0).</param>
        /// <param name="cellSize">World distance between adjacent grid samples (x = X step, y = Z step).</param>
        /// <param name="simplifyTolerance">Douglas–Peucker tolerance in world units (larger = fewer points).</param>
        /// <param name="maxTotalPoints">Hard ceiling on emitted points across coast + water combined; geometry is dropped if exceeded.</param>
        /// <param name="totalPoints">Total points actually emitted (coast + water, post-simplification).</param>
        public static string BuildMapGeometryJson(
            bool[] isWater,
            int cols,
            int rows,
            float3 origin,
            float2 cellSize,
            float simplifyTolerance,
            int maxTotalPoints,
            out int totalPoints)
        {
            totalPoints = 0;
            if (isWater == null || cols < 2 || rows < 2 || isWater.Length < cols * rows)
                return "{\"coast\":[],\"water\":[]}";

            var coastLines = BuildCoastlines(isWater, cols, rows, origin, cellSize, simplifyTolerance);
            var waterPolys = BuildWaterPolygons(isWater, cols, rows, origin, cellSize);

            // Combined point budget across both layers; on overflow, drop everything
            // (UI keeps the empty cyan grid) rather than shipping a heavy payload.
            int emitted = 0;
            foreach (var line in coastLines) emitted += line.Count;
            foreach (var poly in waterPolys) emitted += poly.Count;
            if (emitted > maxTotalPoints)
            {
                totalPoints = emitted;
                return "{\"coast\":[],\"water\":[]}";
            }

            var sb = new StringBuilder(INITIAL_JSON_CAPACITY);
            sb.Append("{\"coast\":");
            AppendPolylineArray(sb, coastLines);
            sb.Append(",\"water\":");
            AppendPolylineArray(sb, waterPolys);
            sb.Append('}');

            totalPoints = emitted;
            return sb.ToString();
        }

        // Marching-squares coastline (land↔water boundary) → stitched, simplified
        // open polylines. Endpoints sit at cell-edge midpoints in world space.
        private static List<List<float2>> BuildCoastlines(
            bool[] isWater,
            int cols,
            int rows,
            float3 origin,
            float2 cellSize,
            float simplifyTolerance)
        {
            var segments = new List<float4>(); // (ax, az, bx, bz)

            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    bool bl = isWater[r * cols + c];
                    bool br = isWater[r * cols + c + 1];
                    bool tl = isWater[(r + 1) * cols + c];
                    bool tr = isWater[(r + 1) * cols + c + 1];

                    int caseIndex =
                        (bl ? CORNER_BOTTOM_LEFT : 0) |
                        (br ? CORNER_BOTTOM_RIGHT : 0) |
                        (tr ? CORNER_TOP_RIGHT : 0) |
                        (tl ? CORNER_TOP_LEFT : 0);
                    if (caseIndex == CASE_ALL_LAND || caseIndex == CASE_ALL_WATER)
                        continue;

                    // Edge midpoints in world space.
                    float x0 = origin.x + c * cellSize.x;
                    float x1 = origin.x + (c + 1) * cellSize.x;
                    float z0 = origin.z + r * cellSize.y;
                    float z1 = origin.z + (r + 1) * cellSize.y;
                    float xm = (x0 + x1) * 0.5f;
                    float zm = (z0 + z1) * 0.5f;

                    var bottom = new float2(xm, z0);
                    var top = new float2(xm, z1);
                    var left = new float2(x0, zm);
                    var right = new float2(x1, zm);

                    AppendCaseSegments(segments, caseIndex, bottom, top, left, right);
                }
            }

            var result = new List<List<float2>>();
            if (segments.Count == 0)
                return result;

            // Stitch segments into connected polylines so Douglas–Peucker can run on
            // continuous boundaries rather than thousands of unordered fragments.
            foreach (var line in StitchSegments(segments))
            {
                if (line.Count < 2)
                    continue;
                var simplified = DouglasPeucker(line, simplifyTolerance);
                if (simplified.Count >= 2)
                    result.Add(simplified);
            }

            return result;
        }

        // Closed water fill as run-length rectangles. Each grid row is scanned into
        // maximal horizontal runs of water cells (a row's runs are an ordered list of
        // [startCol, endCol] spans). A run that reaches the last column simply ends at
        // the map edge — the rectangle closes there with no special case, which is how
        // the sea running off the playable area gets a well-defined fill boundary.
        //
        // Vertical merge: if a row's full run list is identical to the run list of the
        // currently-open band, the band extends downward; otherwise the open band is
        // flushed (one rectangle per run, spanning the rows it covered) and a new band
        // opens for this row. Rectangle corners are cell-center world positions grown
        // by half a cell so adjacent water cells tile seamlessly. This stair-steps
        // along diagonal coasts; the coast stroke drawn on top hides it. Each output
        // rectangle is a 4-point polygon (UI closes it implicitly).
        private static List<List<float2>> BuildWaterPolygons(
            bool[] isWater,
            int cols,
            int rows,
            float3 origin,
            float2 cellSize)
        {
            var result = new List<List<float2>>();
            float halfX = cellSize.x * 0.5f;
            float halfZ = cellSize.y * 0.5f;

            // (start,end) col spans of the open band's runs, plus the row it opened on.
            var openRuns = new List<int2>();
            int openStartRow = -1;

            void Flush(int endRowExclusive)
            {
                if (openRuns.Count == 0)
                    return;
                float wz0 = origin.z + openStartRow * cellSize.y - halfZ;
                float wz1 = origin.z + (endRowExclusive - 1) * cellSize.y + halfZ;
                foreach (var run in openRuns)
                {
                    float wx0 = origin.x + run.x * cellSize.x - halfX;
                    float wx1 = origin.x + run.y * cellSize.x + halfX;
                    result.Add(new List<float2>(4)
                    {
                        new float2(wx0, wz0),
                        new float2(wx1, wz0),
                        new float2(wx1, wz1),
                        new float2(wx0, wz1),
                    });
                }
                openRuns.Clear();
                openStartRow = -1;
            }

            var rowRuns = new List<int2>();
            for (int r = 0; r < rows; r++)
            {
                ScanRowRuns(isWater, cols, r, rowRuns);

                if (RunsEqual(rowRuns, openRuns))
                    continue; // identical band shape — extend the open rectangles down

                // Different shape: close the open band (ending at the previous row),
                // then open a new band for this row's runs (empty list = no open band).
                Flush(r);
                if (rowRuns.Count > 0)
                {
                    openRuns.AddRange(rowRuns);
                    openStartRow = r;
                }
            }

            Flush(rows);
            return result;
        }

        // Fills `runs` with the ordered maximal water spans of row r (cleared first).
        private static void ScanRowRuns(bool[] isWater, int cols, int r, List<int2> runs)
        {
            runs.Clear();
            int rowBase = r * cols;
            int c = 0;
            while (c < cols)
            {
                if (!isWater[rowBase + c]) { c++; continue; }
                int start = c;
                int end = c;
                for (int d = c + 1; d < cols && isWater[rowBase + d]; d++)
                    end = d;
                runs.Add(new int2(start, end));
                c = end + 1;
            }
        }

        private static bool RunsEqual(List<int2> a, List<int2> b)
        {
            if (a.Count == 0 || a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].x != b[i].x || a[i].y != b[i].y)
                    return false;
            }
            return true;
        }

        // Serializes a list of flat-XZ polylines as a JSON array of number arrays.
        private static void AppendPolylineArray(StringBuilder sb, List<List<float2>> lines)
        {
            sb.Append('[');
            bool firstLine = true;
            foreach (var line in lines)
            {
                if (line.Count < 2)
                    continue;
                if (!firstLine) sb.Append(',');
                firstLine = false;
                sb.Append('[');
                for (int i = 0; i < line.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendNumber(sb, line[i].x);
                    sb.Append(',');
                    AppendNumber(sb, line[i].y);
                }
                sb.Append(']');
            }
            sb.Append(']');
        }

        // Marching-squares segment table keyed by the corner bit flags above. Saddle
        // cases (BL|TR, BR|TL) emit both diagonals — acceptable since the output is
        // decorative and later simplified.
        private static void AppendCaseSegments(
            List<float4> segments,
            int caseIndex,
            float2 bottom,
            float2 top,
            float2 left,
            float2 right)
        {
            switch (caseIndex)
            {
                case CORNER_BOTTOM_LEFT:
                case CASE_ALL_WATER & ~CORNER_BOTTOM_LEFT:
                    AddSeg(segments, left, bottom);
                    break;
                case CORNER_BOTTOM_RIGHT:
                case CASE_ALL_WATER & ~CORNER_BOTTOM_RIGHT:
                    AddSeg(segments, bottom, right);
                    break;
                case CORNER_BOTTOM_LEFT | CORNER_BOTTOM_RIGHT:
                case CORNER_TOP_RIGHT | CORNER_TOP_LEFT:
                    AddSeg(segments, left, right);
                    break;
                case CORNER_TOP_RIGHT:
                case CASE_ALL_WATER & ~CORNER_TOP_RIGHT:
                    AddSeg(segments, right, top);
                    break;
                case CORNER_BOTTOM_LEFT | CORNER_TOP_RIGHT: // saddle
                    AddSeg(segments, left, top);
                    AddSeg(segments, bottom, right);
                    break;
                case CORNER_BOTTOM_RIGHT | CORNER_TOP_RIGHT:
                case CORNER_BOTTOM_LEFT | CORNER_TOP_LEFT:
                    AddSeg(segments, bottom, top);
                    break;
                case CORNER_BOTTOM_LEFT | CORNER_BOTTOM_RIGHT | CORNER_TOP_RIGHT:
                case CORNER_TOP_LEFT:
                    AddSeg(segments, left, top);
                    break;
                case CORNER_BOTTOM_RIGHT | CORNER_TOP_LEFT: // saddle
                    AddSeg(segments, left, bottom);
                    AddSeg(segments, right, top);
                    break;
                default:
                    break;
            }
        }

        private static void AddSeg(List<float4> segments, float2 a, float2 b)
            => segments.Add(new float4(a.x, a.y, b.x, b.y));

        // Greedy stitching: chains segments whose endpoints coincide (within a small
        // epsilon, since all endpoints come from a fixed grid). Not topologically
        // perfect at saddle points, but ample for a decorative outline.
        private static List<List<float2>> StitchSegments(List<float4> segments)
        {
            var result = new List<List<float2>>();
            var used = new bool[segments.Count];

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;

                var seg = segments[i];
                var line = new List<float2>
                {
                    new float2(seg.x, seg.y),
                    new float2(seg.z, seg.w),
                };

                // Extend forward from the tail until no connecting segment remains.
                bool extended = true;
                while (extended)
                {
                    extended = false;
                    var tail = line[line.Count - 1];
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (used[j]) continue;
                        var s = segments[j];
                        var a = new float2(s.x, s.y);
                        var b = new float2(s.z, s.w);
                        if (math.distancesq(tail, a) <= STITCH_EPSILON_SQ)
                        {
                            line.Add(b);
                            used[j] = true;
                            extended = true;
                            break;
                        }
                        if (math.distancesq(tail, b) <= STITCH_EPSILON_SQ)
                        {
                            line.Add(a);
                            used[j] = true;
                            extended = true;
                            break;
                        }
                    }
                }

                result.Add(line);
            }

            return result;
        }

        // Standard iterative Douglas–Peucker line simplification.
        private static List<float2> DouglasPeucker(List<float2> points, float tolerance)
        {
            int count = points.Count;
            if (count < 3)
                return points;

            var keep = new bool[count];
            keep[0] = true;
            keep[count - 1] = true;

            // Manual stack via a List (Stack<T> is ambiguous between System and mscorlib
            // under the CS2 reference set): push = Add, pop = RemoveAt(last).
            var stack = new List<int2> { new int2(0, count - 1) };

            while (stack.Count > 0)
            {
                int top = stack.Count - 1;
                var range = stack[top];
                stack.RemoveAt(top);
                int first = range.x;
                int last = range.y;
                float maxDist = 0f;
                int index = -1;

                for (int i = first + 1; i < last; i++)
                {
                    float dist = PerpendicularDistance(points[i], points[first], points[last]);
                    if (dist > maxDist)
                    {
                        maxDist = dist;
                        index = i;
                    }
                }

                if (index != -1 && maxDist > tolerance)
                {
                    keep[index] = true;
                    stack.Add(new int2(first, index));
                    stack.Add(new int2(index, last));
                }
            }

            var simplified = new List<float2>(count);
            for (int i = 0; i < count; i++)
            {
                if (keep[i])
                    simplified.Add(points[i]);
            }

            return simplified;
        }

        private static float PerpendicularDistance(float2 point, float2 lineStart, float2 lineEnd)
        {
            float2 dir = lineEnd - lineStart;
            float lenSq = math.lengthsq(dir);
            if (lenSq < MIN_SEGMENT_LENGTH_SQ)
                return math.distance(point, lineStart);

            // Divisor floored at the degenerate threshold (the early-return above already
            // handles that case) so the division is provably never by zero.
            float t = math.dot(point - lineStart, dir) / math.max(lenSq, MIN_SEGMENT_LENGTH_SQ);
            t = math.clamp(t, 0f, 1f);
            float2 projection = lineStart + t * dir;
            return math.distance(point, projection);
        }

        private static void AppendNumber(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                sb.Append('0');
                return;
            }
            // F1 keeps the payload tiny — sub-metre precision is irrelevant at radar scale.
            sb.Append(value.ToString("F1", CultureInfo.InvariantCulture));
        }
    }
}
