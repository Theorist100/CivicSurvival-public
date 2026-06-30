using System;
using System.Collections.Generic;
using CivicSurvival.Core.Features.CrossDomain.ThreatsAirDefense;
using CivicSurvival.Core.Features.Wellbeing;
using System.Diagnostics;
using System.Threading;
using CivicSurvival.Core.Systems;
using CivicSurvival.Core.UI;
using UnityEngine.Profiling;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Individual PERF.log report sections: memory, ECB, sync points, GC, UI, allocations.
    /// </summary>
    internal static class PerfReportSections
    {
        private const double MS_PER_SECOND = 1000.0;
        private const double BYTES_PER_MB = 1024.0 * 1024.0;
        private const double CHARS_PER_KILO = 1000.0;
        private const double CHARS_PER_MEGA = 1_000_000.0;
        private const float REPORT_INTERVAL_SECONDS = 5.0f;

        // Memory tracking — baseline captured on first report, deltas shown on subsequent reports
        private static long s_BaselineManagedBytes;
        private static long s_BaselineMonoHeapBytes;
        private static long s_BaselineNativeAllocBytes;
        private static long s_BaselineNativeReservedBytes;
        private static long s_BaselineGfxBytes;
        private static long s_BaselineWorkingSetBytes;
        private static volatile bool s_MemoryBaselineCaptured;

        // UI React profiling report (received from JS via trigger, pre-formatted)
        private static string s_LatestUIReactReport = null!;

        /// <summary>
        /// Store pre-formatted UI React profiling report from JS.
        /// Called from MainMenuShellUISystem trigger handler (UiProfileReport
        /// is menu-safe — JS can send reports independently of city load).
        /// </summary>
        public static void SetUIReactReport(string report)
        {
            Interlocked.Exchange(ref s_LatestUIReactReport, report);
        }

        /// <summary>
        /// Reset memory baseline. Call after save load to track fresh deltas.
        /// </summary>
        public static void ResetMemoryBaseline()
        {
            s_MemoryBaselineCaptured = false;
            PerfLogWriter.Write($"\n[{DateTime.Now:HH:mm:ss}] === MEMORY BASELINE RESET ===\n");
        }

        public static void ReportMemory()
        {
            long managed = GC.GetTotalMemory(false);
            long monoHeap = Profiler.GetMonoHeapSizeLong();
            long nativeAlloc = Profiler.GetTotalAllocatedMemoryLong();
            long nativeReserved = Profiler.GetTotalReservedMemoryLong();
            long gfx = Profiler.GetAllocatedMemoryForGraphicsDriver();
            long workingSet;
            try
            {
                using var proc = System.Diagnostics.Process.GetCurrentProcess();
                workingSet = proc.WorkingSet64;
            }
#pragma warning disable CIVIC052 // Process API may fail in sandboxed environments
            catch { workingSet = 0; }
#pragma warning restore CIVIC052

            if (!s_MemoryBaselineCaptured)
            {
                s_BaselineManagedBytes = managed;
                s_BaselineMonoHeapBytes = monoHeap;
                s_BaselineNativeAllocBytes = nativeAlloc;
                s_BaselineNativeReservedBytes = nativeReserved;
                s_BaselineGfxBytes = gfx;
                s_BaselineWorkingSetBytes = workingSet;
                s_MemoryBaselineCaptured = true;

                PerfLogWriter.Write($"");
                PerfLogWriter.Write($"MEMORY BASELINE");
                PerfLogWriter.Write(new string('─', 60));
                PerfLogWriter.Write($"  Managed (GC):     {managed / BYTES_PER_MB,8:F1} MB");
                PerfLogWriter.Write($"  Mono Heap:        {monoHeap / BYTES_PER_MB,8:F1} MB");
                PerfLogWriter.Write($"  Native Alloc:     {nativeAlloc / BYTES_PER_MB,8:F1} MB");
                PerfLogWriter.Write($"  Native Reserved:  {nativeReserved / BYTES_PER_MB,8:F1} MB");
                PerfLogWriter.Write($"  GPU Driver:       {gfx / BYTES_PER_MB,8:F1} MB");
                if (workingSet > 0)
                    PerfLogWriter.Write($"  Process WS:       {workingSet / BYTES_PER_MB,8:F1} MB");
                return;
            }

            long dManaged = managed - s_BaselineManagedBytes;
            long dMonoHeap = monoHeap - s_BaselineMonoHeapBytes;
            long dNativeAlloc = nativeAlloc - s_BaselineNativeAllocBytes;
            long dNativeReserved = nativeReserved - s_BaselineNativeReservedBytes;
            long dGfx = gfx - s_BaselineGfxBytes;
            long dWorkingSet = workingSet - s_BaselineWorkingSetBytes;

            PerfLogWriter.Write($"");
            PerfLogWriter.Write($"MEMORY (delta from baseline)");
            PerfLogWriter.Write(new string('─', 60));
            PerfLogWriter.Write($"  Managed (GC):     {managed / BYTES_PER_MB,8:F1} MB  ({FormatDelta(dManaged)})");
            PerfLogWriter.Write($"  Mono Heap:        {monoHeap / BYTES_PER_MB,8:F1} MB  ({FormatDelta(dMonoHeap)})");
            PerfLogWriter.Write($"  Native Alloc:     {nativeAlloc / BYTES_PER_MB,8:F1} MB  ({FormatDelta(dNativeAlloc)})");
            PerfLogWriter.Write($"  Native Reserved:  {nativeReserved / BYTES_PER_MB,8:F1} MB  ({FormatDelta(dNativeReserved)})");
            PerfLogWriter.Write($"  GPU Driver:       {gfx / BYTES_PER_MB,8:F1} MB  ({FormatDelta(dGfx)})");
            if (workingSet > 0)
                PerfLogWriter.Write($"  Process WS:       {workingSet / BYTES_PER_MB,8:F1} MB  ({FormatDelta(dWorkingSet)})");
        }

        /// <summary>
        /// Entity count snapshot — correlate with FPS to detect entity accumulation.
        /// Uses cached queries from EntityCountProbe (updated once per report).
        /// </summary>
        public static void ReportEntityCounts()
        {
            var counts = EntityCountProbe.Snapshot();
            if (!counts.Valid)
                return;

            if (counts.TotalModEntities == 0 && counts.VanillaOnFire == 0 && counts.VanillaDestroyed == 0
                && counts.PsyStateEntities == 0)
                return;

            PerfLogWriter.Write($"");
            PerfLogWriter.Write($"ENTITY COUNTS");
            PerfLogWriter.Write(new string('─', 60));
            PerfLogWriter.Write($"  Threats alive:     {counts.ThreatsAlive,5}  (flying/arrived)");
            PerfLogWriter.Write($"  Debris falling:    {counts.DebrisFalling,5}");
            PerfLogWriter.Write($"  Vanilla OnFire:    {counts.VanillaOnFire,5}  (buildings burning)");
            PerfLogWriter.Write($"  Vanilla Destroyed: {counts.VanillaDestroyed,5}  (buildings with Destroyed tag)");
            PerfLogWriter.Write($"  Vanilla Buildings: {counts.VanillaTotalEntities,5}  (total non-deleted)");
            PerfLogWriter.Write($"  Mod entities:      {counts.TotalModEntities,5}  (ThreatPosition only)");
            PerfLogWriter.Write($"  PsyState:          {counts.PsyStateEntities,5}  (HouseholdPsyState)");
            PerfLogWriter.Write($"  Spotters:          {counts.SpotterEntities,5}");
            PerfLogWriter.Write($"  BackupPower:       {counts.BackupPowerEntities,5}");
            PerfLogWriter.Write($"  EquipmentWear:     {counts.EquipmentWearEntities,5}");

            // WRS diagnostic: early-exit ratio shows how many entities actually generate ECB work
            int wrsEarly = WellbeingResolverSystem.LastEarlyExits;
            int wrsProcessed = WellbeingResolverSystem.LastProcessed;
            int wrsEcb = WellbeingResolverSystem.LastEcbWrites;
            if (wrsEarly > 0 || wrsProcessed > 0)
            {
                int wrsTotal = wrsEarly + wrsProcessed;
                float earlyPct = wrsTotal > 0 ? (100f * wrsEarly / wrsTotal) : 0f;
                PerfLogWriter.Write($"  WRS last fire:     earlyExit={wrsEarly} ({earlyPct:F0}%)  processed={wrsProcessed}  ecbWrites={wrsEcb}");
            }
        }

        private static string FormatDelta(long bytes)
        {
            double mb = bytes / BYTES_PER_MB;
            return mb >= 0 ? $"+{mb:F1} MB" : $"{mb:F1} MB";
        }

        public static void ReportEcbCounts()
        {
            try
            {
                var counts = new List<(string Name, int Count)>();

                // Threat systems
                AddIfNonZero(counts, "ThreatSpawnSystem", Domains.Waves.Systems.ThreatSpawnSystem.EcbCommandCount);
                AddIfNonZero(counts, "ThreatArrivalSystem", Domains.ThreatDamage.Systems.ThreatArrivalSystem.EcbCommandCount);
                AddIfNonZero(counts, "ThreatDamageSystem", Domains.ThreatDamage.Systems.ThreatDamageSystem.EcbCommandCount);
                AddIfNonZero(counts, "WaveExecutor", Domains.Waves.Systems.WaveExecutor.EcbCommandCount);
                AddIfNonZero(counts, "DebrisSystem", Domains.ThreatDamage.Systems.DebrisSystem.EcbCommandCount);
                int psyBulk = Domains.Cognitive.Threats.Systems.PsyImpactLifecycleSystem.BulkInitCount;

                // C-5 threat-generation drop diagnostics (per PERF cycle — nonzero ⇒ stale/unstamped
                // impacts being dropped; the "missiles don't damage" signal, Axiom 1).
                int aeStale = Domains.ThreatDamage.Systems.ThreatDamageSystem.DroppedStaleImpactCount;
                int aeUnstamped = Domains.ThreatDamage.Systems.ThreatDamageSystem.DroppedUnstampedImpactCount;
                int aeSnap = Domains.AirDefense.Systems.BallisticDefenseSystem.DroppedStaleSnapshotCount;
                int aeDrops = aeStale + aeUnstamped + aeSnap;

                // Crash-guard trips (nonzero ⇒ a release-safe guard refused unsafe work this
                // cycle; never expected in normal play — investigate the named invariant).
                int tbsAbort = Domains.AirDefense.Systems.TargetingBufferStore.AbortedCapacityCount;
                int bdsClamp = Domains.AirDefense.Systems.BallisticDefenseSystem.ClampedSnapshotCount;
                int crashGuards = tbsAbort + bdsClamp;

                // AirDefense systems
                AddIfNonZero(counts, "AirDefenseOrchestrator", Domains.AirDefense.Systems.AirDefenseOrchestrator.EcbCommandCount);
                AddIfNonZero(counts, "AirDefensePolicySystem", Domains.AirDefense.Systems.AirDefensePolicySystem.EcbCommandCount);
                AddIfNonZero(counts, "AACrewReleaseSystem", Domains.AirDefense.Systems.AACrewReleaseSystem.EcbCommandCount);
                AddIfNonZero(counts, "AARequestProcessorSystem", Domains.AirDefense.Systems.AARequestProcessorSystem.EcbCommandCount);
                AddIfNonZero(counts, "IntelPurchaseSystem", Domains.Intel.Systems.IntelPurchaseSystem.EcbCommandCount);
                AddIfNonZero(counts, "SpotterSpawnSystem", Domains.Spotters.Systems.SpotterSpawnSystem.EcbCommandCount);
                AddIfNonZero(counts, "SpotterRequestSystem", Domains.Spotters.Systems.SpotterRequestSystem.EcbCommandCount);
                AddIfNonZero(counts, "AACrewAssignmentSystem", Domains.Mobilization.Systems.AACrewAssignmentSystem.EcbCommandCount);

                // Engineering systems
                AddIfNonZero(counts, "PowerPlantDisasterSystem", Domains.Engineering.Systems.PowerPlantDisasterSystem.EcbCommandCount);
                AddIfNonZero(counts, "GridStressSystem", Domains.Engineering.Systems.GridStressSystem.EcbCommandCount);
                AddIfNonZero(counts, "PlantWearSimulation", Domains.Engineering.Systems.PlantWearSimulation.EcbCommandCount);
                AddIfNonZero(counts, "PlantRepairRequestProcessor", Domains.Engineering.Systems.PlantRepairRequestProcessor.EcbCommandCount);
                AddIfNonZero(counts, "ConstructionDelaySystem", Domains.Engineering.Systems.ConstructionDelaySystem.EcbCommandCount);

                // Core systems
                AddIfNonZero(counts, "InterceptProcessingSystem", Core.Features.CrossDomain.ThreatsAirDefense.InterceptProcessingSystem.EcbCommandCount);

                // WRS ECB diagnostics (written via NativeReference, read after job complete)
                AddIfNonZero(counts, "WellbeingResolverSystem", WellbeingResolverSystem.LastEcbWrites);

                if (counts.Count == 0 && psyBulk == 0 && aeDrops == 0 && crashGuards == 0) return;

                int total = 0;
                foreach (var c in counts) total += c.Count;

                PerfLogWriter.Write($"");
                PerfLogWriter.Write($"ECB COMMANDS (total: {total})");
                PerfLogWriter.Write(new string('─', 40));
                if (psyBulk > 0)
                    PerfLogWriter.Write($"PsyImpactLifecycle Phase1 (households tagged, not ECB)  {psyBulk,6}");

                counts.Sort((a, b) => b.Count.CompareTo(a.Count));
                foreach (var c in counts)
                {
                    PerfLogWriter.Write($"{c.Name,-35} {c.Count,6}");
                }

                if (aeDrops > 0)
                {
                    PerfLogWriter.Write($"");
                    PerfLogWriter.Write($"THREAT GENERATION DROPS (C-5 — nonzero ⇒ stale/unstamped, investigate)");
                    PerfLogWriter.Write(new string('─', 40));
                    PerfLogWriter.Write($"TDS stale impacts                   {aeStale,6}");
                    PerfLogWriter.Write($"TDS unstamped impacts               {aeUnstamped,6}");
                    PerfLogWriter.Write($"BDS stale ballistic snapshots       {aeSnap,6}");
                }

                if (crashGuards > 0)
                {
                    PerfLogWriter.Write($"");
                    PerfLogWriter.Write($"CRASH-GUARD TRIPS (nonzero ⇒ unsafe work refused — investigate invariant)");
                    PerfLogWriter.Write(new string('─', 40));
                    PerfLogWriter.Write($"TBS candidate-capacity aborts       {tbsAbort,6}");
                    PerfLogWriter.Write($"BDS snapshot count/array clamps     {bdsClamp,6}");
                }
            }
            finally
            {
                ResetEcbCounters();
            }
        }

        private static void ResetEcbCounters()
        {
            // Reset all counters via ResetCounters() methods (encapsulated pattern)
            Domains.Waves.Systems.ThreatSpawnSystem.ResetCounters();
            Domains.ThreatDamage.Systems.ThreatArrivalSystem.ResetCounters();
            Domains.ThreatDamage.Systems.ThreatDamageSystem.ResetCounters();
            Domains.Waves.Systems.WaveExecutor.ResetCounters();
            Domains.ThreatDamage.Systems.DebrisSystem.ResetCounters();
            Domains.Cognitive.Threats.Systems.PsyImpactLifecycleSystem.ResetCounters();
            Domains.AirDefense.Systems.AirDefenseOrchestrator.ResetCounters();
            Domains.AirDefense.Systems.AirDefensePolicySystem.ResetCounters();
            Domains.AirDefense.Systems.AACrewReleaseSystem.ResetCounters();
            Domains.AirDefense.Systems.AARequestProcessorSystem.ResetCounters();
            Domains.Intel.Systems.IntelPurchaseSystem.ResetCounters();
            Domains.Spotters.Systems.SpotterSpawnSystem.ResetCounters();
            Domains.Spotters.Systems.SpotterRequestSystem.ResetCounters();
            Domains.Mobilization.Systems.AACrewAssignmentSystem.ResetCounters();
            Domains.Engineering.Systems.PowerPlantDisasterSystem.ResetCounters();
            Domains.Engineering.Systems.GridStressSystem.ResetCounters();
            Domains.Engineering.Systems.PlantWearSimulation.ResetCounters();
            Domains.Engineering.Systems.PlantRepairRequestProcessor.ResetCounters();
            Domains.Engineering.Systems.ConstructionDelaySystem.ResetCounters();
            Core.Features.CrossDomain.ThreatsAirDefense.InterceptProcessingSystem.ResetCounters();
            Domains.AirDefense.Systems.BallisticDefenseSystem.ResetCounters();
            Domains.AirDefense.Systems.TargetingBufferStore.ResetCounters();
        }

        private static void AddIfNonZero(List<(string Name, int Count)> list, string name, int count)
        {
            if (count > 0) list.Add((name, count));
        }

        public static void ReportAllocations(List<KeyValuePair<string, PerformanceProfiler.AllocationData>> allocations)
        {
            if (allocations.Count == 0) return;

            allocations.Sort((a, b) => b.Value.TotalBytes.CompareTo(a.Value.TotalBytes));

            PerfLogWriter.Write($"");
            PerfLogWriter.Write($"NATIVE ALLOCATIONS (per report interval)");
            PerfLogWriter.Write(new string('─', 60));

            const double BYTES_PER_KB = 1024.0;
            foreach (var kvp in allocations)
            {
                double kb = kvp.Value.TotalBytes / BYTES_PER_KB;
                PerfLogWriter.Write($"  {kvp.Key,-40} {kb,8:F1} KB  (x{kvp.Value.CallCount} calls)");
            }
        }

        public static void ReportSyncPoints(List<KeyValuePair<string, PerformanceProfiler.ProfileData>> snapshot)
        {
            const string FULL_PREFIX = "Full:";
            const double MIN_TOTAL_SYNC_MS = 0.5;
            const double PERCENT_FACTOR = 100.0;

            // Build lookup: "X.OnUpdate" → ProfileData (CivicSystemBase entries)
            var codeData = new Dictionary<string, PerformanceProfiler.ProfileData>();
            foreach (var kvp in snapshot)
            {
                if (!kvp.Key.StartsWith(FULL_PREFIX, StringComparison.Ordinal))
                    codeData[kvp.Key] = kvp.Value;
            }

            // Find "Full:X.OnUpdate" entries and compute delta vs "X.OnUpdate"
            var entries = new List<(string System, double TotalSyncMs, int Calls, double SyncPerCall, double SyncPct, double FullPerCall, double CodePerCall)>();

            foreach (var kvp in snapshot)
            {
                if (!kvp.Key.StartsWith(FULL_PREFIX, StringComparison.Ordinal))
                    continue;

                string codeName = kvp.Key.Substring(FULL_PREFIX.Length);
                var full = kvp.Value;

                if (codeData.TryGetValue(codeName, out var code))
                {
                    // Matched pair: Full:X and X both exist
                    double totalSyncMs = full.TotalMs - code.TotalMs;
                    double syncPct = full.TotalMs > 0 ? totalSyncMs / full.TotalMs * PERCENT_FACTOR : 0;
                    double syncPerCall = full.CallCount > 0 ? totalSyncMs / full.CallCount : 0;

                    if (totalSyncMs >= MIN_TOTAL_SYNC_MS)
                        entries.Add((codeName, totalSyncMs, full.CallCount, syncPerCall, syncPct, full.AvgMs, code.AvgMs));
                }
                else
                {
                    // Full:X exists but no X — Update() runs but OnUpdateImpl never measured
                    // (system disabled mid-period, or not CivicSystemBase). Entire cost = overhead.
                    if (full.TotalMs >= MIN_TOTAL_SYNC_MS)
                        entries.Add((codeName + " [full only]", full.TotalMs, full.CallCount, full.AvgMs, PERCENT_FACTOR, full.AvgMs, 0));
                }
            }

            if (entries.Count == 0) return;

            entries.Sort((a, b) => b.TotalSyncMs.CompareTo(a.TotalSyncMs));

            double grandTotalSync = 0;
            foreach (var e in entries) grandTotalSync += e.TotalSyncMs;

            PerfLogWriter.Write($"");
            PerfLogWriter.Write($"SYNC POINT COST (Update overhead beyond OnUpdateImpl) — total: {grandTotalSync:F1}ms / {REPORT_INTERVAL_SECONDS:F0}s");
            PerfLogWriter.Write(new string('─', 100));
            PerfLogWriter.Write($"{"SYSTEM",-40} {"TOTAL",8} {"CALLS",6} {"SYNC/C",8} {"SYNC%",6} {"FULL/C",8} {"CODE/C",8}");

            foreach (var e in entries)
            {
                PerfLogWriter.Write($"{e.System,-40} {e.TotalSyncMs,7:F1}ms {e.Calls,6} {e.SyncPerCall,7:F2}ms {e.SyncPct,5:F0}% {e.FullPerCall,7:F2}ms {e.CodePerCall,7:F2}ms");
            }

            PerfLogWriter.Write($"{"TOTAL",-40} {grandTotalSync,7:F1}ms");
        }

        public static void ReportGcPauses(List<KeyValuePair<string, PerformanceProfiler.ProfileData>> snapshot)
        {
            // Collect systems where Gen2 GC happened during measurement
            List<string> lines = null!;
            foreach (var kvp in snapshot)
            {
                var d = kvp.Value;
                if (d.GcHitCount <= 0) continue;

                lines ??= new List<string>();
#pragma warning disable CIVIC201 // 40 = system name column width, matches table header format
                lines.Add($"  {kvp.Key,-40} {d.GcHitCount}x  spike={d.MaxMs:F1}ms  clean={d.MaxMsNoGC:F1}ms");
#pragma warning restore CIVIC201
            }

            if (lines == null) return;

            PerfLogWriter.Write($"");
            PerfLogWriter.Write($"GC PAUSES (Gen2 during measurement — spike is GC, not code)");
            PerfLogWriter.Write(new string('─', 70));
            foreach (var line in lines) PerfLogWriter.Write(line);
        }

        public static void ReportUIBindings()
        {
            var data = BindingRegistry.DrainProfileData();
            if (data.Count == 0) return;

            int totalUpdates = 0, totalSkips = 0;
            long totalChars = 0, totalTicks = 0, totalBuildTicks = 0;
            foreach (var entry in data)
            {
                totalUpdates += entry.Updates;
                totalSkips += entry.Skips;
                totalChars += entry.Chars;
                totalTicks += entry.Ticks;
                totalBuildTicks += entry.BuildTicks;
            }

            int totalCalls = totalUpdates + totalSkips;
            double skipRate = totalCalls > 0 ? totalSkips * 100.0 / totalCalls : 0;
            double totalMs = totalTicks * MS_PER_SECOND / Stopwatch.Frequency;
            double totalBuildMs = totalBuildTicks * MS_PER_SECOND / Stopwatch.Frequency;

            PerfLogWriter.Write($"");
            PerfLogWriter.Write($"UI BINDINGS ({data.Count} active, {totalUpdates} updates, {totalSkips} skips — {skipRate:F0}% skip rate — build {totalBuildMs:F1}ms / push {totalMs:F1}ms)");
            PerfLogWriter.Write(new string('─', 70));
            PerfLogWriter.Write($"{"BINDING",-32} {"UPDATES",8} {"SKIPPED",8} {"CHARS",8} {"BUILD_MS",9} {"PUSH_MS",8}");

            // Sort by total cost (build + push) descending
            data.Sort((a, b) => (b.BuildTicks + b.Ticks).CompareTo(a.BuildTicks + a.Ticks));

            const double MIN_DISPLAY_MS = 0.01;
            foreach (var entry in data)
            {
                double ms = entry.Ticks * MS_PER_SECOND / Stopwatch.Frequency;
                double buildMs = entry.BuildTicks * MS_PER_SECOND / Stopwatch.Frequency;
                if (ms < MIN_DISPLAY_MS && buildMs < MIN_DISPLAY_MS) continue;
                string charStr = FormatChars(entry.Chars);
                PerfLogWriter.Write($"{entry.Key,-32} {entry.Updates,8} {entry.Skips,8} {charStr,8} {buildMs,9:F2} {ms,8:F2}");
            }

            PerfLogWriter.Write($"{"TOTAL",-32} {totalUpdates,8} {totalSkips,8} {FormatChars(totalChars),8} {totalBuildMs,9:F2} {totalMs,8:F2}");
        }

        public static void ReportUIReact()
        {
            var report = Interlocked.Exchange(ref s_LatestUIReactReport!, null);
            if (report == null) return;

            PerfLogWriter.Write("");
            foreach (var line in report.Split('\n'))
                PerfLogWriter.Write(line);
        }

        private static string FormatChars(long chars)
        {
            if (chars >= CHARS_PER_MEGA) return $"{chars / CHARS_PER_MEGA:F1}M";
            if (chars >= CHARS_PER_KILO) return $"{chars / CHARS_PER_KILO:F1}K";
            return chars.ToString();
        }
    }
}
