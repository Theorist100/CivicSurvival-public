namespace CivicSurvival.Core.Adapters
{
#if DEBUG
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using CivicSurvival.Core.Config;
    using CivicSurvival.Core.Interfaces.Services;
    using CivicSurvival.Core.Utils;
    using Unity.Mathematics;
#endif

    /// <summary>
    /// DEBUG-only dev helper that snapshots the live map contour into a baked-map JSON resource for
    /// the mirror-city catalog. It reads the same coast/water payload the radar publishes (through
    /// <see cref="IMapContourReader"/>, filled once per loaded city by
    /// <see cref="VanillaMapContourAdapter"/>) and wraps it with a <c>name</c> and an explicit
    /// <c>bounds</c> array — the exact format <see cref="Services.MirrorCityMapCatalog"/> consumes.
    ///
    /// The plan's data step is "dump the current mapContour payload to JSON, once per chosen vanilla
    /// map"; this is that dump. Output lands in writable ModData (mod-install resources are read-only
    /// for Paradox subscribers); a single call on a loaded vanilla map produces a file the developer
    /// copies into <c>Resources/MirrorCityMaps/</c> (after optional offline polygon simplification).
    ///
    /// Compiled only in DEBUG builds — it is a developer tool, never shipped to players.
    /// </summary>
    public static class MirrorCityContourDumper
    {
#if DEBUG
        private static readonly LogContext Log = new("MirrorCityContourDumper");

        // Sub-directory under ModData where dumped baked-map candidates are written.
        private const string DumpSubDir = "MirrorCityMaps";

        // StringBuilder slack for the injected "name"/"bounds" prefix ahead of the original contour.
        private const int BakedMapHeaderReserve = 96;

        /// <summary>
        /// Dump the current contour to <c>{ModData}/MirrorCityMaps/{mapId}.json</c> in baked-map
        /// format. <paramref name="tileBounds"/> is the playable-tile bounding box
        /// (minX, minZ, maxX, maxZ) — pass the terrain <c>playableOffset</c>/<c>playableArea</c>
        /// rectangle so the mask has exact bounds; when null the loader falls back to the geometry
        /// bounding box. Returns false (and logs) when the contour is not yet computed.
        /// </summary>
        public static bool TryDumpBakedMap(
            IMapContourReader reader, string mapId, string displayName, float4? tileBounds, out string outputPath)
        {
            outputPath = string.Empty;
            if (reader == null || string.IsNullOrEmpty(mapId))
            {
                Log.Warn("Dump skipped: null reader or empty mapId.");
                return false;
            }

            if (!reader.TryGetContourJson(out string contourJson) || string.IsNullOrEmpty(contourJson))
            {
                Log.Warn($"Dump skipped for '{mapId}': contour not ready (terrain/water not sampled yet).");
                return false;
            }

            string? baked = WrapAsBakedMap(contourJson, displayName, tileBounds);
            if (baked == null)
            {
                Log.Warn($"Dump skipped for '{mapId}': contour payload was not a JSON object.");
                return false;
            }

            string dir = Path.Combine(ModPaths.ModDataDirectory, DumpSubDir);
            outputPath = Path.Combine(dir, mapId + ".json");
            try
            {
                Directory.CreateDirectory(dir);
                AtomicFileWriter.WriteAllText(outputPath, baked);
            }
            catch (Exception ex)
            {
                // Best-effort dev tool: any write failure (IOException, UnauthorizedAccessException
                // from an AV/cloud-sync lock, …) just means "no dump this run" — it must never
                // escape into the adapter's compute path.
                Log.Warn($"Dump failed for '{mapId}': {ex}");
                return false;
            }

            Log.Info($"Dumped baked map '{mapId}' ({baked.Length} bytes) to {ModPaths.SanitizePathTail(outputPath)}. " +
                     "Copy into Resources/MirrorCityMaps/ (simplify offline if needed).");
            return true;
        }

        /// <summary>
        /// Inject a <c>name</c> (and optional <c>bounds</c>) into the contour object, which is
        /// <c>{"coast":[...],"water":[...]}</c>. Returns null if the payload is not a JSON object.
        /// </summary>
        private static string? WrapAsBakedMap(string contourJson, string displayName, float4? tileBounds)
        {
            // Ordinal scan for the opening brace (a structural ASCII char — culture-invariant, and
            // net48 has no IndexOf(char, StringComparison) overload to satisfy CA1307 directly).
            int brace = -1;
            for (int i = 0; i < contourJson.Length; i++)
            {
                if (contourJson[i] == '{') { brace = i; break; }
            }
            if (brace < 0)
                return null;

            var sb = new StringBuilder(contourJson.Length + BakedMapHeaderReserve);
            sb.Append("{\"name\":\"").Append(EscapeJson(displayName ?? string.Empty)).Append('"');
            if (tileBounds.HasValue)
            {
                float4 b = tileBounds.Value;
                sb.Append(",\"bounds\":[")
                  .Append(Num(b.x)).Append(',').Append(Num(b.y)).Append(',')
                  .Append(Num(b.z)).Append(',').Append(Num(b.w)).Append(']');
            }
            // Append the rest of the original object (its "coast"/"water" fields), starting right
            // after the opening brace, prefixed with a comma.
            sb.Append(',').Append(contourJson, brace + 1, contourJson.Length - brace - 1);
            return sb.ToString();
        }

        private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string EscapeJson(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
#endif
    }
}
