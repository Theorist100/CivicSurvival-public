namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Lock-free, allocation-free native-buffer footprint registry for the memory-pressure hunt.
    ///
    /// Owner systems publish the byte size (<c>Capacity × element size</c>) of their persistent
    /// native containers via the <c>Report*</c> setters from their own update — a single 64-bit
    /// store. This is deliberately NOT routed through a dictionary or event: a plain static field
    /// write is allocation-free and lock-free, and reading <c>NativeList.Capacity</c> /
    /// <c>NativeParallelMultiHashMap.Capacity</c> is a main-thread allocation-metadata read that
    /// does NOT force a job to complete (unlike <c>.Length</c> or <c>.ToArray()</c>) — so it adds
    /// no sync point.
    ///
    /// <see cref="CivicSurvival.Core.Systems.DiagnosticReportSystem"/> reads these in its
    /// <c>[ACCUM]</c> line to attribute native growth that entity counts cannot see: a
    /// <c>Clear()</c>-reused <c>NativeList</c> keeps its <c>Capacity</c>, so a buffer grown to a
    /// peak-wave size holds that native memory until disposed — invisible to any entity query.
    ///
    /// Diagnostic only. Cross-update staleness is irrelevant at the report cadence; 64-bit
    /// aligned field writes are atomic on x86-64, so no torn reads. Setters are static (writes
    /// never happen from the owning instance directly) to keep instance/static separation clean.
    /// </summary>
    public static class NativeFootprintTracker
    {
        private const long BytesPerMb = 1024 * 1024;

        // ThreatFlight.ThreatMovementSystem — building obstacle cache (scales with city size).
        private static long s_ThreatBuildingCacheBytes;
        private static long s_ThreatBuildingGridBytes;

        // AirDefense.TargetingBufferStore — engagement buffers (scale with threats × AA).
        private static long s_TargetingThreatBytes;
        private static long s_TargetingCandidateBytes;

        // Waves.ThreatTargetCacheSystem — target lists (scale with city building count).
        private static long s_TargetCacheBytes;

        public static void ReportThreatBuildingCache(long bytes) => s_ThreatBuildingCacheBytes = bytes;
        public static void ReportThreatBuildingGrid(long bytes) => s_ThreatBuildingGridBytes = bytes;
        public static void ReportTargetingThreat(long bytes) => s_TargetingThreatBytes = bytes;
        public static void ReportTargetingCandidate(long bytes) => s_TargetingCandidateBytes = bytes;
        public static void ReportTargetCache(long bytes) => s_TargetCacheBytes = bytes;

        /// <summary>Compact MB breakdown for the <c>[ACCUM]</c> diagnostic line.</summary>
        public static string Format()
        {
            long total = s_ThreatBuildingCacheBytes + s_ThreatBuildingGridBytes
                + s_TargetingThreatBytes + s_TargetingCandidateBytes + s_TargetCacheBytes;
            return $"ourBufMB={total / BytesPerMb} "
                + $"(bcache={s_ThreatBuildingCacheBytes / BytesPerMb} grid={s_ThreatBuildingGridBytes / BytesPerMb} "
                + $"tgtThreat={s_TargetingThreatBytes / BytesPerMb} tgtCand={s_TargetingCandidateBytes / BytesPerMb} "
                + $"tgtCache={s_TargetCacheBytes / BytesPerMb})";
        }
    }
}
