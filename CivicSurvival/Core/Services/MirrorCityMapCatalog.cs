using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Utils;
using Unity.Mathematics;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Loads baked map contours from JSON resources and turns each into a pure
    /// <see cref="MirrorCityLandMask"/> for the generator. This is the I/O boundary the generator
    /// deliberately does NOT cross (Axiom 5 / plan phase A: contour loading is on the service side,
    /// Core/Logic stays a pure function fed a ready mask).
    ///
    /// Resource layout: one file per map at
    /// <c>{ModInstall}/Resources/MirrorCityMaps/{mapId}.json</c>, in the same coast/water payload
    /// the radar publishes plus a <c>name</c> field and an optional explicit <c>bounds</c>:
    /// <code>{ "name": "...", "bounds": [minX,minZ,maxX,maxZ], "coast": [[x,z,...],...], "water": [[x,z,...],...] }</code>
    /// The WATER polygons become the mask's water regions; <c>coast</c> is only used to derive the
    /// tile bounding box when <c>bounds</c> is absent. Parsed masks are cached per mapId (the baked
    /// geometry is static for the life of the process).
    /// </summary>
    public sealed class MirrorCityMapCatalog
    {
        private static readonly LogContext Log = new("MirrorCityMapCatalog");

        /// <summary>
        /// Process-wide shared instance. The parse cache is keyed by mapId and the baked geometry is
        /// static for the life of the process, so every consumer (sim generation, UI contour builds)
        /// shares ONE cache: the OnStartRunning pre-warm in MirrorCitySystem then covers the first
        /// War Room STRIKE publish too, instead of each system paying its own cold disk read.
        /// </summary>
        public static MirrorCityMapCatalog Shared { get; } = new();

        // Sub-directory (under the mod install root) holding the baked map JSON resources.
        private const string ResourceSubDir = "MirrorCityMaps";
        private const string ResourceRoot = "Resources";

        // Upper bound on a baked-map resource (bytes). A simplified coast/water payload is a few KB;
        // this cap refuses a pathological/corrupt file before it is read whole into memory.
        private const long MaxResourceBytes = 4L * 1024 * 1024;

        // Upper bound on the TOTAL vertices a baked map may carry across all its polygons. The byte
        // cap alone does not bound work: a degenerate file of tiny coordinates inside 4 MB could
        // still yield hundreds of thousands of vertices, making every IsLand O(n) probe of the
        // rejection sampler pathologically slow. A simplified contour is a few hundred vertices.
        private const int MaxTotalVertices = 20_000;

        private readonly Dictionary<string, MirrorCityLandMask?> m_Cache = new(StringComparer.Ordinal);
        private readonly object m_Lock = new();

        /// <summary>
        /// Return the land mask for <paramref name="mapId"/>, loading and parsing the JSON resource
        /// on first request and caching the result (including a null cache entry for a
        /// missing/malformed resource, so a broken map is not re-read every call). Returns null when
        /// the resource cannot be loaded or parsed — the caller decides the fallback (e.g. skip the
        /// variant, or use a default mask).
        ///
        /// The disk read happens OUTSIDE the cache lock (main-thread ANR discipline: never hold a
        /// lock across file I/O); the cache is then filled under the lock with a lost-race dropping
        /// the duplicate parse. Both callers are main-thread ECS systems, and MirrorCitySystem
        /// pre-warms the current variant's mask at OnStartRunning, so gameplay ticks normally hit
        /// only the cached entry.
        /// </summary>
        public MirrorCityLandMask? GetLandMask(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return null;

            lock (m_Lock)
            {
                if (m_Cache.TryGetValue(mapId, out var cached))
                    return cached;
            }

            var mask = LoadMask(mapId);

            lock (m_Lock)
            {
                // Lost race (another caller parsed while we read): keep the first entry.
                if (m_Cache.TryGetValue(mapId, out var winner))
                    return winner;
                m_Cache[mapId] = mask;
                return mask;
            }
        }

        /// <summary>Clear the cache so subsequent lookups re-read from disk (dev-loop convenience).</summary>
        public void InvalidateCache()
        {
            lock (m_Lock)
            {
                m_Cache.Clear();
            }
        }

        private static string ResourcePath(string mapId)
            => Path.Combine(ModPaths.ModInstallDirectory, ResourceRoot, ResourceSubDir, mapId + ".json");

        private static MirrorCityLandMask? LoadMask(string mapId)
        {
            string json;
            try
            {
                // Path.Combine sits INSIDE the try: on some runtimes it throws ArgumentException for
                // invalid path characters, and the "nothing here may ever propagate into an ECS
                // OnUpdate" guarantee below must not depend on every future MapId staying a clean literal.
                string path = ResourcePath(mapId);
                if (!File.Exists(path))
                {
                    Log.Warn($"Baked map resource not found for '{mapId}' at {ModPaths.SanitizePathTail(path)}.");
                    return null;
                }

                using var reader = new StreamReader(path);
                if (reader.BaseStream.Length > MaxResourceBytes)
                {
                    Log.Warn($"Baked map '{mapId}' is {reader.BaseStream.Length} bytes (> {MaxResourceBytes} cap) — refusing to load.");
                    return null;
                }
                json = reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                // Loading a baked content resource is best-effort by contract: ANY failure to open
                // or read (IOException, UnauthorizedAccessException from an AV/cloud-sync lock or an
                // ACL race, SecurityException, …) means "this map is unavailable" — the caller falls
                // back to a working mask. Nothing here may ever propagate into an ECS OnUpdate.
                Log.Warn($"Failed to read baked map '{mapId}': {ex}");
                return null;
            }

            var mask = ParseMask(json);
            if (mask == null)
                Log.Warn($"Baked map '{mapId}' is malformed or has no usable geometry.");
            else
                Log.Info($"Loaded baked map '{mapId}': {mask.WaterPolygonCount} water polygon(s), " +
                         $"bounds [{mask.MinX:0}, {mask.MinZ:0}]..[{mask.MaxX:0}, {mask.MaxZ:0}].");
            return mask;
        }

        /// <summary>
        /// Parse the baked-map payload into a land mask. Pure string → mask (no I/O), separated so
        /// it can be unit-exercised without touching the filesystem. Returns null on malformed JSON
        /// or when neither explicit bounds nor any geometry are present to define a tile.
        /// Public so tools and unit checks can build a mask from a payload without filesystem I/O.
        /// </summary>
        public static MirrorCityLandMask? ParseMask(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var water = new List<float2[]>();
                var coast = new List<float2[]>();
                float[]? bounds = null;
                int totalVertices = 0; // shared across water AND coast so the cap is truly total

                int p = 0;
                SkipWs(json, ref p);
                if (p >= json.Length || json[p] != '{')
                    return null;
                p++;

                while (true)
                {
                    SkipWs(json, ref p);
                    if (p >= json.Length) return null;
                    if (json[p] == '}') break;

                    if (json[p] != '"') return null;
                    string key = JsonStream.ReadJsonString(json, ref p);
                    SkipWs(json, ref p);
                    if (p >= json.Length || json[p] != ':') return null;
                    p++;
                    SkipWs(json, ref p);

                    switch (key)
                    {
                        case "water":
                            ParsePolylineCollection(json, ref p, water, ref totalVertices);
                            break;
                        case "coast":
                            ParsePolylineCollection(json, ref p, coast, ref totalVertices);
                            break;
                        case "bounds":
                            bounds = ParseFlatNumberArray(json, ref p);
                            break;
                        default:
                            // Unknown field (e.g. "name") — skip its value.
                            if (!JsonStream.SkipJsonValue(json, ref p)) return null;
                            break;
                    }

                    SkipWs(json, ref p);
                    if (p < json.Length && json[p] == ',') p++;
                }

                return BuildMask(bounds, coast, water);
            }
            catch (FormatException ex)
            {
                Log.Warn($"ParseMask ignored malformed baked-map JSON: {ex}");
                return null;
            }
        }

        private static MirrorCityLandMask? BuildMask(float[]? bounds, List<float2[]> coast, List<float2[]> water)
        {
            if (bounds != null && bounds.Length >= 4)
                return new MirrorCityLandMask(bounds[0], bounds[1], bounds[2], bounds[3], water, coast);

            // No explicit bounds: derive the tile bounding box from all geometry (coast + water).
            float minX = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            bool any = false;

            void Accumulate(List<float2[]> polys)
            {
                foreach (var poly in polys)
                {
                    foreach (var v in poly)
                    {
                        any = true;
                        minX = math.min(minX, v.x); maxX = math.max(maxX, v.x);
                        minZ = math.min(minZ, v.y); maxZ = math.max(maxZ, v.y);
                    }
                }
            }
            Accumulate(coast);
            Accumulate(water);

            if (!any)
                return null; // no bounds and no geometry — nothing to build a tile from.

            return new MirrorCityLandMask(minX, minZ, maxX, maxZ, water, coast);
        }

        // ── Minimal nested-array JSON parsing (JsonStream only covers flat scalar/string fields) ──

        private static void SkipWs(string json, ref int p)
            => JsonStream.SkipWhitespace(json, ref p);

        /// <summary>
        /// Parse an array of flat point arrays: <c>[[x,z,x,z,...], ...]</c> into a list of
        /// <see cref="float2"/> polygons (each inner flat array collapses X/Z pairs into vertices).
        /// Odd trailing coordinates are dropped. Advances past the closing bracket.
        /// </summary>
        private static void ParsePolylineCollection(string json, ref int p, List<float2[]> into, ref int totalVertices)
        {
            SkipWs(json, ref p);
            if (p >= json.Length || json[p] != '[')
                throw new FormatException("Expected '[' for polyline collection");
            p++;
            SkipWs(json, ref p);
            if (p < json.Length && json[p] == ']') { p++; return; } // empty collection

            while (true)
            {
                float[] flat = ParseFlatNumberArray(json, ref p);
                int vertexCount = flat.Length / 2;
                if (vertexCount > 0)
                {
                    totalVertices += vertexCount;
                    if (totalVertices > MaxTotalVertices)
                        throw new FormatException($"Baked map exceeds {MaxTotalVertices} total vertices");
                    var poly = new float2[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                        poly[i] = new float2(flat[i * 2], flat[i * 2 + 1]);
                    into.Add(poly);
                }

                SkipWs(json, ref p);
                if (p >= json.Length) throw new FormatException("Unterminated polyline collection");
                if (json[p] == ',') { p++; SkipWs(json, ref p); continue; }
                if (json[p] == ']') { p++; return; }
                throw new FormatException("Expected ',' or ']' in polyline collection");
            }
        }

        /// <summary>
        /// Parse a flat numeric array <c>[n, n, ...]</c> into a float array. Advances past the
        /// closing bracket. Used for both the polyline inner arrays and the optional bounds array.
        /// </summary>
        private static float[] ParseFlatNumberArray(string json, ref int p)
        {
            SkipWs(json, ref p);
            if (p >= json.Length || json[p] != '[')
                throw new FormatException("Expected '[' for number array");
            p++;

            var values = new List<float>();
            while (true)
            {
                SkipWs(json, ref p);
                if (p >= json.Length) throw new FormatException("Unterminated number array");
                if (json[p] == ']') { p++; break; }

                int start = p;
                while (p < json.Length)
                {
                    char c = json[p];
                    if (c == ',' || c == ']' || char.IsWhiteSpace(c)) break;
                    p++;
                }
                if (p == start) throw new FormatException("Empty number token");
                string token = json.Substring(start, p - start);
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    throw new FormatException($"Invalid number '{token}'");
                // net48 float.TryParse accepts "NaN"/"Infinity" — a non-finite coordinate or bound
                // would poison IsLand (every comparison against NaN is false, so the bounds test
                // stops rejecting anything). Geometry must be finite.
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new FormatException($"Non-finite number '{token}'");
                values.Add(value);

                SkipWs(json, ref p);
                if (p < json.Length && json[p] == ',') { p++; continue; }
                if (p < json.Length && json[p] == ']') { p++; break; }
                throw new FormatException("Expected ',' or ']' in number array");
            }
            return values.ToArray();
        }
    }
}
