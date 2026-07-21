using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.GridWarfare;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// One catalog entry: the persisted identity of a mirror-city variant. Only
    /// <see cref="VariantId"/> is written to the save (plan decision 3); the map, seed and
    /// generator version are re-derived from the catalog on load and fed to
    /// <see cref="MirrorCityGenerator.Generate"/>.
    /// </summary>
    public readonly struct MirrorCityVariant
    {
        /// <summary>Stable index into the catalog (0..<see cref="MirrorCityVariantCatalog.Count"/>-1). This is the ONLY field persisted.</summary>
        public readonly int VariantId;

        /// <summary>Id of the baked map this variant is built on (resolved to a land mask by the map catalog).</summary>
        public readonly string MapId;

        /// <summary>Deterministic generator seed for this variant.</summary>
        public readonly uint Seed;

        /// <summary>Generator version this variant was authored against.</summary>
        public readonly int GenVersion;

        public MirrorCityVariant(int variantId, string mapId, uint seed, int genVersion)
        {
            VariantId = variantId;
            MapId = mapId;
            Seed = seed;
            GenVersion = genVersion;
        }
    }

    /// <summary>
    /// The fixed 100-entry catalog of (mapId, seed) variants (plan decision 3). Seeds are DATA:
    /// derived once by a deterministic formula from the entry index, never rolled at runtime, so
    /// the list is identical on every machine and every launch. A new game picks a variant index
    /// at random and stores only that index; everything else is looked up here.
    ///
    /// The seed formula is a fixed integer hash of the index (splitmix-style avalanche), giving a
    /// well-spread, non-zero seed per entry without any RNG or clock. Maps are round-robined over
    /// <see cref="MapPool"/> so the pool of baked maps is evenly represented across the catalog.
    ///
    /// Note: this catalog lists candidate pairs. Whether a given (map, seed) actually produces a
    /// valid city is decided by <see cref="MirrorCityGenerator.Validate"/> at build time against
    /// the real land mask; the plan's build step is expected to swap out any seed that fails. Until
    /// that offline validation pass runs, these are the deterministic starting seeds.
    /// </summary>
    public static class MirrorCityVariantCatalog
    {
        /// <summary>Number of variants in the catalog.</summary>
        public const int Count = 100;

        /// <summary>
        /// Generator version stamped on every catalog variant. Single-sourced from
        /// <see cref="MirrorCityState.CurrentGenVersion"/> so the catalog and the persisted-state
        /// header never drift. Bumping it (with a fresh validation pass) re-rolls every city — house
        /// pre-release policy (plan decision 12): a version mismatch regenerates rather than migrating.
        /// </summary>
        public const int GenVersion = MirrorCityState.CurrentGenVersion;

        /// <summary>
        /// Ids of the baked maps the catalog draws from — STRICTLY one per JSON resource that
        /// actually ships under <c>Resources/MirrorCityMaps/</c>. An id listed here without its
        /// resource would silently send every variant on that map to the all-land fallback mask —
        /// a city geometrically unrelated to what the offline validator approved — so a map enters
        /// this pool only TOGETHER with its baked file (and a <c>genVersion</c> bump, since the
        /// round-robin re-maps existing variants). To add a map: load it in a DEBUG build so
        /// <c>MirrorCityContourDumper</c> writes
        /// <c>{ModData}/CivicSurvival/MirrorCityMaps/{name}.json</c>, copy that file into
        /// <c>Resources/MirrorCityMaps/</c> (csproj deploys it to the mod), add its name here, and
        /// bump <c>MirrorCityState.CurrentGenVersion</c>.
        /// </summary>
        public static readonly IReadOnlyList<string> MapPool = new[]
        {
            "yeavering",
            "archipelago_haven",
            "barrier_island",
            "corral_riches",
            "great_highlands",
            "mountain_village",
            "river_delta",
            "sunshine_peninsula",
            "sweeping_plains",
            "twin_mountain",
            "waterway_pass",
            "windy_fjords",
        };

        private static readonly MirrorCityVariant[] s_Variants = BuildVariants();

        /// <summary>
        /// Look up a variant by id. Throws <see cref="System.ArgumentOutOfRangeException"/> for an
        /// id outside [0, <see cref="Count"/>) — an out-of-range persisted id is a corruption/
        /// migration bug the caller must handle explicitly, not silently clamp.
        /// </summary>
        public static MirrorCityVariant Get(int variantId)
        {
            if (variantId < 0 || variantId >= s_Variants.Length)
                throw new System.ArgumentOutOfRangeException(
                    nameof(variantId), variantId, $"Variant id must be in [0, {Count}).");
            return s_Variants[variantId];
        }

        /// <summary>All variants, in id order. Read-only view for the build-time validator.</summary>
        public static IReadOnlyList<MirrorCityVariant> All => s_Variants;

        private static MirrorCityVariant[] BuildVariants()
        {
            var variants = new MirrorCityVariant[Count];
            for (int i = 0; i < Count; i++)
            {
                string mapId = MapPool[i % MapPool.Count];
                uint seed = SeedForIndex(i);
                variants[i] = new MirrorCityVariant(i, mapId, seed, GenVersion);
            }
            return variants;
        }

        /// <summary>
        /// Deterministic per-index seed: a splitmix64-style avalanche of the index folded to 32
        /// bits, forced non-zero. Pure data — the same index always yields the same seed, with no
        /// RNG state or clock involved.
        /// </summary>
        private static uint SeedForIndex(int index)
        {
            // Advance a 64-bit splitmix state by (index+1) so entry 0 is non-trivial, then mix.
            ulong z = unchecked(0x9E3779B97F4A7C15UL * (ulong)(index + 1));
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            uint seed = (uint)(z ^ (z >> 32));
            return seed == 0u ? 1u : seed;
        }
    }
}
