using System.Collections.Generic;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Config;
using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure, seeded generator of an enemy "mirror city": the spatial model whose live targets
    /// sum into the enemy's three axes (Physical / Digital / Social). Same discipline as
    /// <see cref="StrikeResolver"/> — a pure function of its inputs, no session RNG, no
    /// <c>DateTime</c>, no ECS, no I/O (Axiom 5: the loader that reads baked contours lives on
    /// the domain/service side and hands this generator a ready <see cref="MirrorCityLandMask"/>).
    ///
    /// Determinism contract: <c>Generate(seed, genVersion, mask)</c> is reproducible for a fixed
    /// balance config WITHIN one platform/runtime. The same (seed, genVersion, mask, config) yields
    /// the identical <see cref="GeneratedCity"/> on every run — tuning is snapshotted ONCE per call
    /// from the globally-synced balance contract (no mid-generation re-reads, so a remote config
    /// swap can never produce a half-and-half city), the build-time catalog validator and the
    /// runtime new-game path both call this one definition, and only the resulting
    /// <c>variantId</c> is persisted (the city is re-derived on load). Cross-platform bit-equality
    /// is NOT promised: placement geometry uses <c>math.sin/cos/sqrt</c>, whose last bit may differ
    /// between runtimes/compilers — the game ships on one platform (Windows x64 Mono), and the
    /// persisted-target model means a save never depends on regenerating identically anyway.
    /// Retuning a catalog-affecting field (target counts / AA geometry) requires re-running the
    /// validator; the runtime enforces coherence by stamping <see cref="ComputeTuningHash"/> on the
    /// generated city and regenerating on a mismatch at load.
    /// <paramref name="genVersion"/> is folded into the RNG seed so a version bump re-rolls the
    /// city wholesale (house pre-release policy: no legacy generators, no damage carry-over).
    ///
    /// Layout (ported 1:1 from the design-session prototype):
    /// <list type="bullet">
    ///   <item>one district centroid per axis (Physical/Digital/Social), mutually separated;</item>
    ///   <item>per axis: exactly one Reserve (contrib = floor share, effectively invulnerable),
    ///     exactly one Key (fat contrib, the permanent-progress target), and 2–4 Regular targets
    ///     (rebuildable), all placed within the axis's district;</item>
    ///   <item>three AA sites on the Physical axis (contrib 0, positive <c>AaRange</c>) seeded near
    ///     the Physical and Digital districts, so a strike lane over those targets is defended.</item>
    /// </list>
    /// All positions are rejection-sampled on land with a minimum separation between targets.
    /// </summary>
    public static class MirrorCityGenerator
    {
        // ── Tuning ──
        // The per-tier composition, contributions, hit points and AA range come from the balance
        // contract's MirrorCity section (Docs/Contracts/balance.contract.yaml → GridWarfareConfig.
        // MirrorCity*). The config is snapshotted ONCE at the top of Generate/Validate and threaded
        // through as a value — never re-dereferenced mid-run — so a concurrent remote-config swap
        // cannot produce a city drawn from two different configs. NOTE: fields that change target
        // COUNT or the AA coverage geometry (RegularMin/MaxPerAxis, AaSiteCount, AaRange) are
        // catalog-affecting — retuning them shifts the placement RNG draw sequence / coverage, so
        // the build-time catalog validator must be re-run; the runtime stamps ComputeTuningHash on
        // each generated city and regenerates on a mismatch at load, making the coherence
        // self-enforcing instead of a comment.

        // Per-axis tier composition. Reserve/Key are structurally exactly one (plan decision 4).
        private const int RESERVE_PER_AXIS = 1;   // invulnerable floor materialised as an object
        private const int KEY_PER_AXIS = 1;       // exactly one; its death permanently trims the cap

        private const float AA_CONTRIB = 0f; // structural: AA sites are positional, never contribute
        private const float NO_AA_RANGE = 0f;

        // Fraction of the smaller tile dimension used to clamp district geometry to the mask.
        private const float HALF_SPAN_FRACTION = 0.4f;

        // Placement geometry (metres) — full-size-tile values; each is clamped to the mask's
        // half-span in Generate so districts AND spacing stay satisfiable on small/odd masks
        // (an unscaled spacing on a tiny tile would exhaust every sample budget and dump the
        // whole city onto overlapping fallback points).
        private const float DISTRICT_RADIUS = 2000f;         // target scatter around a centroid
        private const float DISTRICT_MIN_SEPARATION = 2500f; // between district centroids
        private const float MIN_TARGET_DISTANCE = 350f;      // between any two placed targets
        private const float MIN_TARGET_DISTANCE_FRACTION = 0.175f; // of districtRadius, on small masks

        // Rejection-sampling attempt budgets. Bounded so the generator always terminates; on a
        // pathological mask the fallback still returns a land point (validation then flags it,
        // and the catalog builder discards that seed).
        private const int SAMPLE_ATTEMPTS = 512;

        // The three axes, in a fixed order so the RNG draw sequence is stable across runs.
        // Physical→Kinetic, Digital→Cyber, Social→Psyops (mirror of the city's axes).
        private static readonly AttackCategory[] Axes =
        {
            AttackCategory.Kinetic,
            AttackCategory.Cyber,
            AttackCategory.Psyops,
        };

        /// <summary>
        /// Generate the mirror city for <paramref name="seed"/> / <paramref name="genVersion"/> on
        /// the given land <paramref name="mask"/>. Pure and deterministic per config snapshot. The
        /// returned city always carries a full tier set per axis plus the AA sites; whether every
        /// constraint (on-land, spacing, coverage) is satisfied for THIS seed/mask is reported by
        /// <see cref="Validate"/>.
        /// </summary>
        public static GeneratedCity Generate(uint seed, int genVersion, MirrorCityLandMask mask, out int tuningHash)
        {
            // ONE config snapshot for the whole run (see the tuning note above). The tuning hash is
            // computed from THIS snapshot and returned to the caller: stamping it from a second
            // BalanceConfig.Current dereference could describe a different config than the one the
            // city was actually built under (remote swap between the two reads).
            GridWarfareConfig gw = BalanceConfig.Current.GridWarfare;
            tuningHash = ComputeTuningHash(gw);

            var rng = new Random(MixSeed(seed, genVersion));
            var city = new GeneratedCity();

            // Clamp placement geometry to the tile so districts fit even on small/odd masks —
            // keeps the generator robust across the whole catalog and on synthetic test masks.
            float halfSpan = HALF_SPAN_FRACTION * math.min(mask.Width, mask.Height);
            float districtRadius = math.min(DISTRICT_RADIUS, math.max(1f, halfSpan));
            float minSeparation = math.min(DISTRICT_MIN_SEPARATION, math.max(1f, halfSpan));
            float minTargetDistance = MinTargetDistanceFor(districtRadius);

            // SANE_REGULAR_MAX guards the rng.NextInt(min, max + 1) below: an int.MaxValue config
            // value would overflow the +1 into a max < min range. The persisted-target cap is 512
            // total, so any real config sits far below this ceiling.
            const int SANE_REGULAR_MAX = 128;
            int regularMin = math.clamp(gw.MirrorCityRegularMinPerAxis, 0, SANE_REGULAR_MAX);
            int regularMax = math.clamp(math.max(regularMin, gw.MirrorCityRegularMaxPerAxis), regularMin, SANE_REGULAR_MAX);
            int aaSiteCount = math.max(0, gw.MirrorCityAaSiteCount);

            // One district centroid per axis, inset so a full district disc stays on the tile,
            // and mutually separated so the three axes read as distinct city sectors.
            var centroids = new float2[Axes.Length];
            for (int a = 0; a < Axes.Length; a++)
            {
                centroids[a] = SampleCentroid(ref rng, mask, centroids, a, districtRadius, minSeparation);
            }

            // Per-axis tier population.
            for (int a = 0; a < Axes.Length; a++)
            {
                AttackCategory axis = Axes[a];
                float2 centre = centroids[a];

                PlaceTarget(ref rng, mask, city.Targets, centre, districtRadius, minTargetDistance,
                    axis, MirrorTargetTier.Reserve, gw.MirrorCityReserveContrib, gw.MirrorCityReserveMaxHp, NO_AA_RANGE);

                PlaceTarget(ref rng, mask, city.Targets, centre, districtRadius, minTargetDistance,
                    axis, MirrorTargetTier.Key, gw.MirrorCityKeyContrib, gw.MirrorCityKeyMaxHp, NO_AA_RANGE);

                int regularCount = rng.NextInt(regularMin, regularMax + 1);
                for (int r = 0; r < regularCount; r++)
                {
                    PlaceTarget(ref rng, mask, city.Targets, centre, districtRadius, minTargetDistance,
                        axis, MirrorTargetTier.Regular, gw.MirrorCityRegularContrib, gw.MirrorCityRegularMaxHp, NO_AA_RANGE);
                }
            }

            // AA sites — Physical axis, contrib 0, positive range. Seed them near the Physical and
            // Digital districts (indices 0 and 1) so the defended lanes cover real targets. The AA
            // scatter uses the full district radius so a site can reach targets in its district.
            for (int i = 0; i < aaSiteCount; i++)
            {
                float2 anchor = centroids[i % 2 == 0 ? 0 : 1];
                PlaceTarget(ref rng, mask, city.Targets, anchor, districtRadius, minTargetDistance,
                    AttackCategory.Kinetic, MirrorTargetTier.AaSite, AA_CONTRIB, gw.MirrorCityAaMaxHp, gw.MirrorCityAaRange);
            }

            return city;
        }

        /// <summary>
        /// Minimum inter-target spacing for a mask whose (clamped) district radius is
        /// <paramref name="districtRadius"/>: the full-size value on a normal tile, scaled down
        /// proportionally on a small mask so the spacing stays satisfiable inside the district disc.
        /// One definition for <see cref="Generate"/> and <see cref="Validate"/>.
        /// </summary>
        private static float MinTargetDistanceFor(float districtRadius)
            => math.min(MIN_TARGET_DISTANCE, math.max(1f, districtRadius * MIN_TARGET_DISTANCE_FRACTION));

        /// <summary>
        /// Hash of the catalog-affecting tuning fields (the ones that shift the generator's RNG draw
        /// sequence or the AA coverage geometry: Regular band, AA site count, AA range) under the
        /// given <paramref name="gw"/> snapshot. Stamped on <c>MirrorCityState.TuningHash</c> at
        /// generation and compared at load — a mismatch regenerates the city, so persisted cities
        /// and the balance config can never silently drift apart. FNV-1a over the raw field bits.
        /// </summary>
        public static int ComputeTuningHash(GridWarfareConfig gw)
        {
            const uint FnvOffsetBasis = 2166136261u;
            const uint FnvPrime = 16777619u;
            uint hash = FnvOffsetBasis;
            hash = (hash ^ (uint)gw.MirrorCityRegularMinPerAxis) * FnvPrime;
            hash = (hash ^ (uint)gw.MirrorCityRegularMaxPerAxis) * FnvPrime;
            hash = (hash ^ (uint)gw.MirrorCityAaSiteCount) * FnvPrime;
            hash = (hash ^ math.asuint(gw.MirrorCityAaRange)) * FnvPrime;
            return unchecked((int)hash);
        }

        /// <summary>
        /// Validation predicate for a generated city against its mask. True only when every
        /// invariant the plan requires holds: all targets on land, minimum spacing respected,
        /// a full tier set on each axis, and at least one AA site covering at least one target.
        /// The catalog builder uses this to accept/reject candidate seeds.
        /// </summary>
        public static bool Validate(GeneratedCity city, MirrorCityLandMask mask)
        {
            if (city?.Targets == null || city.Targets.Count == 0)
                return false;

            // ONE config snapshot, mirroring Generate — the two must judge by the same numbers.
            GridWarfareConfig gw = BalanceConfig.Current.GridWarfare;
            int regularMin = gw.MirrorCityRegularMinPerAxis;
            int regularMax = math.max(regularMin, gw.MirrorCityRegularMaxPerAxis);

            // Same mask-clamped spacing Generate used, so a small mask is judged by the spacing
            // it was actually generated with.
            float halfSpan = HALF_SPAN_FRACTION * math.min(mask.Width, mask.Height);
            float districtRadius = math.min(DISTRICT_RADIUS, math.max(1f, halfSpan));
            float minTargetDistance = MinTargetDistanceFor(districtRadius);

            var targets = city.Targets;

            // 1) Every target sits on land.
            for (int i = 0; i < targets.Count; i++)
            {
                if (!mask.IsLand(targets[i].X, targets[i].Z))
                    return false;
            }

            // 2) Minimum separation between every pair of targets.
            for (int i = 0; i < targets.Count; i++)
            {
                for (int j = i + 1; j < targets.Count; j++)
                {
                    if (Dist(targets[i], targets[j]) < minTargetDistance)
                        return false;
                }
            }

            // 3) Full tier set per axis: exactly one Reserve, exactly one Key, 2–4 Regular.
            for (int a = 0; a < Axes.Length; a++)
            {
                AttackCategory axis = Axes[a];
                int reserve = 0, key = 0, regular = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Axis != axis)
                        continue;
                    switch (targets[i].Tier)
                    {
                        case MirrorTargetTier.Reserve: reserve++; break;
                        case MirrorTargetTier.Key: key++; break;
                        case MirrorTargetTier.Regular: regular++; break;
                        case MirrorTargetTier.AaSite: break; // counted separately below
                        default: break; // new tiers do not participate in the per-axis tier tally
                    }
                }
                if (reserve != RESERVE_PER_AXIS || key != KEY_PER_AXIS)
                    return false;
                if (regular < regularMin || regular > regularMax)
                    return false;
            }

            // 4) AA present and covering: at least one AA site, and some AA site covers at least
            //    one non-AA target (a non-AA target lies within that site's AaRange).
            bool anyAa = false;
            bool anyCovered = false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Tier != MirrorTargetTier.AaSite)
                    continue;
                anyAa = true;
                float range = targets[i].AaRange;
                if (range <= 0f)
                    continue;
                for (int j = 0; j < targets.Count; j++)
                {
                    if (targets[j].Tier == MirrorTargetTier.AaSite)
                        continue;
                    if (Dist(targets[i], targets[j]) <= range)
                    {
                        anyCovered = true;
                        break;
                    }
                }
                if (anyCovered)
                    break;
            }
            return anyAa && anyCovered;
        }

        // ── Internals ────────────────────────────────────────────────

        /// <summary>
        /// Fold the caller seed and the generator version into a single non-zero RNG state.
        /// Non-zero is required (a 0 state never advances — same precedent as
        /// <see cref="StrikeResolver"/>), and mixing genVersion guarantees a version bump
        /// re-rolls the whole city.
        /// </summary>
        private static uint MixSeed(uint seed, int genVersion)
        {
            // Golden-ratio odd constant mix; the final OR 1 forces a set bit.
            uint s = seed ^ ((uint)genVersion * 0x9E3779B9u);
            s ^= s >> 16;
            s *= 0x7FEB352Du;
            s ^= s >> 15;
            return s | 1u;
        }

        /// <summary>
        /// Sample a district centroid on land, inset by <paramref name="districtRadius"/> so a full
        /// district disc stays on the tile, and at least <paramref name="minSeparation"/> from every
        /// already-chosen centroid. Falls back to the last sampled in-bounds land point if the
        /// separation budget is exhausted (validation then reflects the crowding).
        /// </summary>
        private static float2 SampleCentroid(
            ref Random rng, MirrorCityLandMask mask, float2[] chosen, int chosenCount,
            float districtRadius, float minSeparation)
        {
            float minX = mask.MinX + districtRadius;
            float maxX = mask.MaxX - districtRadius;
            float minZ = mask.MinZ + districtRadius;
            float maxZ = mask.MaxZ - districtRadius;

            // Guard against a tile smaller than the inset — collapse to the tile centre band.
            if (maxX <= minX) { minX = mask.MinX; maxX = mask.MaxX; }
            if (maxZ <= minZ) { minZ = mask.MinZ; maxZ = mask.MaxZ; }

            float2 last = new float2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            for (int attempt = 0; attempt < SAMPLE_ATTEMPTS; attempt++)
            {
                float2 p = new float2(rng.NextFloat(minX, maxX), rng.NextFloat(minZ, maxZ));
                if (!mask.IsLand(p.x, p.y))
                    continue;
                last = p;

                bool farEnough = true;
                for (int c = 0; c < chosenCount; c++)
                {
                    if (math.distancesq(p, chosen[c]) < minSeparation * minSeparation)
                    {
                        farEnough = false;
                        break;
                    }
                }
                if (farEnough)
                    return p;
            }
            return last;
        }

        /// <summary>
        /// Rejection-sample a point uniformly within the district disc around
        /// <paramref name="centre"/> that is on land and at least <paramref name="minTargetDistance"/>
        /// from every already-placed target, then append the target. Falls back to the last
        /// on-land sample if the spacing budget is exhausted.
        /// </summary>
        private static void PlaceTarget(
            ref Random rng, MirrorCityLandMask mask, List<GeneratedTarget> placed,
            float2 centre, float districtRadius, float minTargetDistance,
            AttackCategory axis, MirrorTargetTier tier, float contrib, float maxHp, float aaRange)
        {
            float2 chosen = centre;
            bool haveLand = false;

            for (int attempt = 0; attempt < SAMPLE_ATTEMPTS; attempt++)
            {
                // Uniform sample inside the disc: sqrt(u) radius removes centre clustering.
                float angle = rng.NextFloat(0f, 2f * math.PI);
                float radius = districtRadius * math.sqrt(rng.NextFloat());
                float2 p = new float2(
                    centre.x + radius * math.cos(angle),
                    centre.y + radius * math.sin(angle));

                if (!mask.IsLand(p.x, p.y))
                    continue;

                if (!haveLand) { chosen = p; haveLand = true; }

                bool farEnough = true;
                for (int i = 0; i < placed.Count; i++)
                {
                    float dx = placed[i].X - p.x;
                    float dz = placed[i].Z - p.y;
                    if (dx * dx + dz * dz < minTargetDistance * minTargetDistance)
                    {
                        farEnough = false;
                        break;
                    }
                }
                if (farEnough)
                {
                    chosen = p;
                    break;
                }
            }

            placed.Add(new GeneratedTarget
            {
                Axis = axis,
                Tier = tier,
                X = chosen.x,
                Z = chosen.y,
                Contrib = contrib,
                MaxHp = maxHp,
                AaRange = aaRange,
            });
        }

        private static float Dist(GeneratedTarget a, GeneratedTarget b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return math.sqrt(dx * dx + dz * dz);
        }
    }
}
