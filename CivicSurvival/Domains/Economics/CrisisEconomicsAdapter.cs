using Unity.Entities;

namespace CivicSurvival.Domains.Economics
{
    /// <summary>
    /// Static accessor for crisis economics multipliers. Tax remains a direct
    /// per-call world resolve because TaxSystem calls are low-frequency and need
    /// immediate act-state changes. Commerce uses a primitive cache published by
    /// <c>CrisisEconomicsSystem</c> so hot vanilla getter postfixes do not perform
    /// <c>World.GetExistingSystemManaged&lt;T&gt;()</c> on every read. The cache stores
    /// only <c>World.SequenceNumber</c> and floats, never a world/system reference.
    /// </summary>
    public static class CrisisEconomicsAdapter
    {
        private static bool s_HasCommerceCache;
        private static ulong s_CommerceWorldSequence;
        private static float s_CommerceMultiplier = 1f;

        public static float GetTaxMultiplier(World world)
        {
            var system = GetSystem(world);
            return system != null && system.Enabled ? system.TaxMultiplier : 1f;
        }

        public static float GetCommerceMultiplier(World world)
        {
            var system = GetSystem(world);
            if (system == null || !system.Enabled)
            {
                ClearCommerceMultiplier(world);
                return 1f;
            }

            if (TryGetPublishedCommerceMultiplier(world, out float multiplier))
                return multiplier;

            multiplier = system.CommerceMultiplier;
            PublishCommerceMultiplier(world, multiplier);
            return multiplier;
        }

        public static void PublishCommerceMultiplier(World world, float multiplier)
        {
            if (world == null || !world.IsCreated)
                return;

            s_CommerceWorldSequence = GetWorldSequence(world);
            s_CommerceMultiplier = multiplier;
            s_HasCommerceCache = true;
        }

        public static void ClearCommerceMultiplier(World? world = null)
        {
            if (world != null && world.IsCreated && s_HasCommerceCache && s_CommerceWorldSequence != GetWorldSequence(world))
                return;

            s_HasCommerceCache = false;
            s_CommerceWorldSequence = 0;
            s_CommerceMultiplier = 1f;
        }

        private static Systems.CrisisEconomicsSystem? GetSystem(World? world)
        {
            if (world == null || !world.IsCreated) return null;
            return world.GetExistingSystemManaged<Systems.CrisisEconomicsSystem>();
        }

        private static bool TryGetPublishedCommerceMultiplier(World world, out float multiplier)
        {
            if (world != null && world.IsCreated && s_HasCommerceCache && s_CommerceWorldSequence == GetWorldSequence(world))
            {
                multiplier = s_CommerceMultiplier;
                return true;
            }

            multiplier = 1f;
            return false;
        }

        private static ulong GetWorldSequence(World world) => world.SequenceNumber;
    }
}
