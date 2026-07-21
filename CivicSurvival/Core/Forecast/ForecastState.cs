namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Per-run mutable timeline state for the severity Monte-Carlo sweep. A plain helper struct
    /// (NOT an ECS component), passed by <c>ref</c> to the per-domain forecast steps so the
    /// composer's loop drives one shared state with zero copies and zero virtual dispatch.
    ///
    /// The per-plant arrays and the repair queue are <b>references</b> to the composer's reused
    /// scratch (allocated once in <c>OnCreate</c>, never per-run — CIVIC050). This struct only
    /// holds the references, so constructing or passing it allocates nothing on the heap; the
    /// composer resets the scalar fields and the array contents at the start of each run.
    /// </summary>
    public struct ForecastState
    {
        /// <summary>Megawatts of generation currently knocked out (sum of per-plant damage·plantMW).</summary>
        public float LostMW;

        /// <summary>Saturation steady-state factor, stepped statefully with inertia each tick.</summary>
        public float SatFactor;

        /// <summary>Grid stress accumulator (game-hours of deficit, decays toward zero).</summary>
        public float Stress;

        /// <summary>True while the grid is collapsed (blackout); recovery counts down in <see cref="Recov"/>.</summary>
        public bool Collapsed;

        /// <summary>Game-hours remaining in the current recovery window before the grid comes back.</summary>
        public float Recov;

        /// <summary>Day index of the first collapse this run (-1 if the run never collapsed).</summary>
        public int FirstCollapseDay;

        /// <summary>Plants currently under repair (bounded by the concurrent-repair cash gate).</summary>
        public int ActiveRepairs;

        /// <summary>Live plant count this run (only the first NPlants array entries are valid).</summary>
        public int NPlants;

        /// <summary>Per-plant damage fraction 0..1 (reference to the composer's reused scratch).</summary>
        public float[] PlantDamage;

        /// <summary>Per-plant nameplate (MW), reference to the composer's reused scratch — set ONLY in
        /// the Tier-0 live-plant path (real <c>OriginalCapacityKW</c> sizes). <c>null</c> in
        /// archetype-fallback mode, where every plant is the single discretised <c>plantMW</c> and the
        /// damage→MW conversion uses that scalar instead (byte-identical to the pre-Tier-0 model).</summary>
        public float[]? PlantCapMW;

        /// <summary>Per-plant repair-completion game-hour, or <see cref="RepairForecast.REPAIR_NONE"/>
        /// when the plant is not under repair (reference to the composer's reused scratch).</summary>
        public float[] RepairDone;

        /// <summary>Fixed-capacity FIFO ring of damaged plant ids awaiting a free repair slot (reference
        /// to the composer's reused buffer, sized to MAX_PLANTS in OnCreate). A ring + the
        /// <see cref="RepairQueued"/> dedup flag makes both enqueue and dequeue O(1) with NO per-tick
        /// allocation (the old List.RemoveAt(0) was O(n) and an append-only list would reallocate
        /// mid-run — CIVIC050). At most NPlants ids are ever live at once (the flag dedupes), so the ring
        /// never overflows its MAX_PLANTS capacity. Driven via <see cref="RepairHead"/> /
        /// <see cref="RepairCount"/>; reset per run by <see cref="RepairForecast.Reset"/>.</summary>
        public int[] RepairQueue;

        /// <summary>Head index of the repair ring (next id to dispatch). Reset to 0 per run.</summary>
        public int RepairHead;

        /// <summary>Number of ids currently in the repair ring (enqueue advances tail = (head+count) %
        /// capacity). Reset to 0 per run.</summary>
        public int RepairCount;

        /// <summary>Per-plant "already queued for repair" membership flag (reference to the composer's
        /// reused scratch) — O(1) replacement for a Contains scan. True while a plant sits in the ring;
        /// cleared when it is dequeued. Only the first NPlants entries are live; reset per run by
        /// <see cref="RepairForecast.Reset"/>.</summary>
        public bool[] RepairQueued;
    }
}
