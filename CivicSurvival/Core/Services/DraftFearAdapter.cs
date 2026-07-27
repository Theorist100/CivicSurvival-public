using Unity.Entities;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// Static accessor for the aggregate <b>draft-fear</b> lever — the standing pressure the
    /// landed enemy Propaganda raids exert on the draft strata (poor/middle). A city-level,
    /// O(1) scalar, never per-household state: the Mobilization side integrates it into its
    /// persisted draft-evasion level ("ухилянти"), which eats into total manpower.
    ///
    /// The lever is <b>derived, not serialized</b>: the single writer (the Cognitive PSYOPS
    /// launcher) recomputes it every tick from the live <c>PsyOpsAttack</c> entities — which
    /// vanilla already persists — and republishes it here, so a reload mid-raid re-derives the
    /// same value with no leak into the save. Mirrors <see cref="ResourceRunAdapter"/> exactly:
    /// a process-local primitive cache keyed by <c>World.SequenceNumber</c>, storing only floats
    /// and the world sequence, never a world/system reference.
    ///
    /// Lives in <c>Core</c> (Axiom 5): Cognitive (writer) and Mobilization (reader) reach it
    /// through Core; neither domain imports the other. One writer-owner (CIVIC175): the
    /// Cognitive launcher publishes; every other party only reads.
    /// </summary>
    public static class DraftFearAdapter
    {
        private static bool s_HasCache;
        private static ulong s_WorldSequence;
        // Aggregate hero-countered pressure (0+) of the live landed Propaganda raids on the
        // draft strata. 0 = no live raid pressure.
        private static float s_PropagandaFear;

        /// <summary>
        /// Publish the derived draft-fear pressure for <paramref name="world"/>. Called by the
        /// single Cognitive owner each tick; the value is recomputed from the live Propaganda
        /// attacks, so this is inherently save-clean (nothing here touches the save file).
        /// </summary>
        public static void PublishPropagandaFear(World world, float pressure)
        {
            if (world == null || !world.IsCreated)
                return;

            s_WorldSequence = GetWorldSequence(world);
            s_PropagandaFear = pressure > 0f ? pressure : 0f;
            s_HasCache = true;
        }

        /// <summary>Aggregate draft-fear pressure (0+). Defaults to 0 (no pressure) for a fresh/other world.</summary>
        public static float GetPropagandaFear(World world)
        {
            if (world != null && world.IsCreated && s_HasCache && s_WorldSequence == GetWorldSequence(world))
                return s_PropagandaFear;
            return 0f;
        }

        public static void ClearPropagandaFear(World? world = null)
        {
            if (world != null && world.IsCreated && s_HasCache && s_WorldSequence != GetWorldSequence(world))
                return;

            s_HasCache = false;
            s_WorldSequence = 0;
            s_PropagandaFear = 0f;
        }

        private static ulong GetWorldSequence(World world) => world.SequenceNumber;
    }
}
