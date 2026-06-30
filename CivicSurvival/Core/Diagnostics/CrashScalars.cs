namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Last-known native-AV invariant scalars, captured cheaply on the hot path and embedded by the
    /// breadcrumb writer at the marker-write cadence. The "dump substitute" for the spatial-hash
    /// overrun class (fix a57f0e912): if <see cref="SpatialHashCount"/> == <see cref="SpatialHashCapacity"/>
    /// at crash time, the parallel <c>NativeParallelMultiHashMap</c> was exactly full → the
    /// false-exhaustion overrun path. Lets a native crash be classified WITHOUT a dump.
    /// <para/>
    /// Hot-path writers store plain values (a few int stores + one ref) — no allocation, no
    /// <c>DateTime</c>, no string format, no disk (Axiom 15). A torn read on recovery is acceptable
    /// (diagnostic only), so <c>volatile</c> is enough and no lock is taken on the hot path. Lives in
    /// <c>Core/Diagnostics</c> (sibling of <see cref="CrashBreadcrumbIdentity"/>) so the breadcrumb
    /// writer reads it with no dependency on the producing domain.
    /// </summary>
    public static class CrashScalars
    {
        private static volatile int s_SpatialHashCount;
        private static volatile int s_SpatialHashCapacity;
        private static volatile int s_BuildingCacheLength;
        private static volatile string s_LastJob = string.Empty;
        private static volatile string s_LastFilePath = string.Empty;

        /// <summary>Hot path: record the just-scheduled spatial-hash build's element count and allocated capacity.</summary>
        public static void SetSpatialHash(int count, int capacity)
        {
            s_SpatialHashCount = count;
            s_SpatialHashCapacity = capacity;
        }

        /// <summary>Hot path: record the building-cache length backing the spatial hash.</summary>
        public static void SetBuildingCacheLength(int length) => s_BuildingCacheLength = length;

        /// <summary>Name of the last native job dispatched in a risk region (whitelisted identifier, not free text).</summary>
        public static string LastJob
        {
            get => s_LastJob;
            set => s_LastJob = value ?? string.Empty;
        }

        /// <summary>
        /// Last file path our code was about to open for a synchronous write (set by AtomicFileWriter
        /// just before the blocking CreateFile). If the main thread is frozen in CreateFileW at ANR time,
        /// this names the file — the only way to attribute a sync-IO ANR to our write vs vanilla, since
        /// the minidump's managed heap (and the real lpFileName) is not in a DumpWithoutCrash capture.
        /// Torn read on recovery is acceptable (diagnostic only); volatile, no lock on the hot path.
        /// </summary>
        public static string LastFilePath
        {
            get => s_LastFilePath;
            set => s_LastFilePath = value ?? string.Empty;
        }

        public static int SpatialHashCount => s_SpatialHashCount;
        public static int SpatialHashCapacity => s_SpatialHashCapacity;
        public static int BuildingCacheLength => s_BuildingCacheLength;
    }
}
