using Unity.Entities;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Static accessor for the aggregate <b>resource-run</b> lever (rumor
    /// resource-run; shared with the deferred Fuel Crisis). A resource run is a
    /// city-level, O(1) scalar — <b>never</b> per-household/per-vehicle state — that a
    /// live rumor raises on a real tradeable good so the panic-buying pressure becomes
    /// visible. v1 targets <c>Resource.Food</c> only.
    ///
    /// The lever is <b>derived, not serialized</b>: the single writer (the Cognitive
    /// PSYOPS launcher) recomputes it every tick from the live <c>PsyOpsAttack</c>
    /// entities — which vanilla already persists — and republishes it here, so a reload
    /// mid-rumor re-derives the same value with no leak into the save. This mirrors
    /// <c>CrisisEconomicsAdapter</c> exactly: a process-local primitive cache keyed by
    /// <c>World.SequenceNumber</c>, storing only floats and the world sequence, never a
    /// world/system reference. Hot vanilla patch postfixes read it per call without a
    /// <c>GetExistingSystemManaged&lt;T&gt;()</c> lookup.
    ///
    /// Lives in <c>Core</c> (Axiom 5): Cognitive (writer), the demand patch (reader) and
    /// the economy bite (reader) reach it through Core; neither the Cognitive nor the
    /// future Fuel domain imports the other. One writer-owner (CIVIC175): the Cognitive
    /// launcher publishes; every other party only reads.
    /// </summary>
    public static class ResourceRunAdapter
    {
        private static bool s_HasFoodCache;
        private static ulong s_FoodWorldSequence;
        // Demand-up multiplier (>= 1) applied to the Food demand element — the visible
        // confirmation tell. 1 = no run.
        private static float s_FoodDemandRunMult = 1f;
        // Aggregate run intensity (0-1) — drives the economy commerce bite, the confirmation
        // top-up and the UI tell. 0 = no run.
        private static float s_FoodRunIntensity;

        /// <summary>
        /// Publish the derived Food run for <paramref name="world"/>. Called by the single
        /// Cognitive owner each tick; the values are recomputed from the live rumor attacks,
        /// so this is inherently save-clean (nothing here touches the save file).
        /// </summary>
        public static void PublishFoodRun(World world, float demandRunMult, float runIntensity)
        {
            if (world == null || !world.IsCreated)
                return;

            float intensity = runIntensity;
            if (intensity < 0f)
                intensity = 0f;
            else if (intensity > 1f)
                intensity = 1f;

            s_FoodWorldSequence = GetWorldSequence(world);
            s_FoodDemandRunMult = demandRunMult >= 1f ? demandRunMult : 1f;
            s_FoodRunIntensity = intensity;
            s_HasFoodCache = true;
        }

        /// <summary>Demand-up multiplier for the Food element (>= 1). Defaults to 1 (no run) for a fresh/other world.</summary>
        public static float GetFoodDemandRun(World world)
            => TryGetFood(world, out float mult, out _) ? mult : 1f;

        /// <summary>Aggregate Food run intensity (0-1). Defaults to 0 (no run) for a fresh/other world.</summary>
        public static float GetFoodRunIntensity(World world)
            => TryGetFood(world, out _, out float intensity) ? intensity : 0f;

        public static void ClearFoodRun(World? world = null)
        {
            if (world != null && world.IsCreated && s_HasFoodCache && s_FoodWorldSequence != GetWorldSequence(world))
                return;

            s_HasFoodCache = false;
            s_FoodWorldSequence = 0;
            s_FoodDemandRunMult = 1f;
            s_FoodRunIntensity = 0f;
        }

        private static bool TryGetFood(World? world, out float demandMult, out float intensity)
        {
            if (world != null && world.IsCreated && s_HasFoodCache && s_FoodWorldSequence == GetWorldSequence(world))
            {
                demandMult = s_FoodDemandRunMult;
                intensity = s_FoodRunIntensity;
                return true;
            }

            demandMult = 1f;
            intensity = 0f;
            return false;
        }

        private static ulong GetWorldSequence(World world) => world.SequenceNumber;
    }
}
