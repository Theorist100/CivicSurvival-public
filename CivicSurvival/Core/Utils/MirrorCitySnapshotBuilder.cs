using System.Globalization;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using CivicSurvival.Core.Components.Domain.GridWarfare;
using CivicSurvival.Core.Logic;
using CivicSurvival.Core.Types;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Pure (ECS-free) builder that turns a mirror-city target list into the War Room STRIKE-view JSON
    /// snapshot, quantized to the player's <em>effective</em> intel level. Sibling in spirit to
    /// <see cref="MapContourBuilder"/>: the owning UI system reads ECS data and pushes the string to a
    /// raw-string binding, while ALL construction and — critically — all intel quantization lives here,
    /// dependency-free, so exact numbers are stripped before serialization and can be reasoned about /
    /// reused in isolation (e.g. a future PvP mirror of an enemy city).
    ///
    /// Quantization is C#-side by design (plan decision 7): below level 2 the exact contributions, hp,
    /// positions and AA radii never enter the string, so no UI or wire inspection can recover them.
    ///
    /// Payload schema (stable shape at every level; arrays populated per level):
    /// <code>
    /// {
    ///   "header":  { "variantId": int, "mapId": string, "genVersion": int, "intelLevel": int(0..2) },
    ///   "signals": [ { "axis": "physical|digital|social", "x": num, "z": num } ], // L0 only, else []
    ///   "targets": [ {
    ///       "id": int, "axis": "physical|digital|social", "tier": "reserve|key|regular",
    ///       "x": num, "z": num,                 // L1 snapped to a coarse grid, L2 exact
    ///       "state": "INTACT|DAMAGED|DEAD|REBUILDING",
    ///       "contrib": num,                     // L1 bucketed to a coarse step, L2 exact
    ///       "hpPct": num,                       // L1 = -1 (hidden; state conveys), L2 exact 0..100
    ///       "rebuildPct": num                   // L1 = -1 (hidden), L2 exact 0..100
    ///   } ],                                    // [] at L0
    ///   "aa": [ {
    ///       "id": int, "x": num, "z": num,      // L1 snapped, L2 exact
    ///       "range": num,                       // L1 INFLATED (×1.25, rounded), L2 exact
    ///       "state": "INTACT|DAMAGED|DEAD|REBUILDING",
    ///       "hpPct": num                        // L1 = -1, L2 exact 0..100
    ///   } ]                                     // [] at L0
    /// }
    /// </code>
    /// Levels: L0 = map presence only (one centroid "signal" per axis); L1 = targets + approximate AA
    /// (coarse contribution, coarse state, snapped positions, inflated AA rings); L2 = exact everything.
    /// </summary>
    public static class MirrorCitySnapshotBuilder
    {
        // L1 buckets contribution to the nearest multiple of this before it reaches the DTO ("~20").
        private const float ContribStepL1 = 10f;

        // L1 snaps X/Z to this world-metre grid (district-scale cell) — rough layout, no pinpoint coords.
        private const float CoordGridL1 = 250f;

        // L1 draws AA coverage larger than it really is: true radius ×this, then rounded to the step below.
        private const float AaRangeInflateL1 = 1.25f;
        private const float AaRangeRoundL1 = 25f;

        // A standing target below this fraction of full hp reads as DAMAGED rather than INTACT.
        private const float IntactHpFraction = 0.95f;

        // -1 marks a numeric field the current intel level withholds (UI shows the coarse state instead).
        private const float HiddenValue = -1f;

        /// <summary>
        /// Serialize the quantized snapshot. <paramref name="intelLevel"/> is the effective level (0..2);
        /// the caller computes it via <c>IntelStateSingleton.EffectiveIntelLevel</c> and passes it in.
        /// </summary>
        public static string Build(in MirrorCityState state, NativeArray<EnemyTarget> targets, int intelLevel)
            => Build(in state, targets, intelLevel, null);

        /// <summary>
        /// Serialize the quantized snapshot including the variant's map contour underlay when
        /// <paramref name="contourFragment"/> (a pre-serialized <see cref="BuildContourFragment"/>
        /// result) is available. The contour reveals from intel L1 — at L0 the enemy map is
        /// "unknown terrain": only the axis signals ship, and the header's <c>mapId</c> is withheld
        /// too (naming the vanilla map would identify terrain the level is supposed to hide; the
        /// <c>variantId</c> stays as an opaque number). The contour is static per variant, which is
        /// exactly why it arrives pre-serialized: at L2 the snapshot republishes every tick (exact
        /// values genuinely move), and re-serializing hundreds of static vertices each time was pure
        /// waste — the owning system caches the fragment per variant.
        /// </summary>
        public static string Build(in MirrorCityState state, NativeArray<EnemyTarget> targets, int intelLevel, string? contourFragment)
        {
            int level = math.clamp(intelLevel, 0, 2);
            var sb = new StringBuilder(1024);

            sb.Append("{\"header\":{");
            sb.Append("\"variantId\":").Append(state.VariantId);
            sb.Append(",\"mapId\":\"");
            if (level >= 1)
                AppendEscaped(sb, ResolveMapId(state.VariantId));
            sb.Append("\",\"genVersion\":").Append(state.GenVersion);
            sb.Append(",\"intelLevel\":").Append(level);
            sb.Append('}');

            sb.Append(",\"signals\":");
            if (level <= 0)
                AppendSignals(sb, targets);
            else
                sb.Append("[]");

            sb.Append(",\"targets\":");
            if (level >= 1)
                AppendTargets(sb, targets, level);
            else
                sb.Append("[]");

            if (level >= 1 && !string.IsNullOrEmpty(contourFragment))
            {
                sb.Append(",\"contour\":");
                sb.Append(contourFragment);
            }

            sb.Append(",\"aa\":");
            if (level >= 1)
                AppendAaSites(sb, targets, level);
            else
                sb.Append("[]");

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Serialize the map-contour underlay once per variant: tile bounds and the water polygons,
        /// world metres rounded to integers (the baked masks are already offline-simplified to a few
        /// hundred vertices, so pass-through with rounding keeps the payload in single-digit KB).
        /// <c>{"bounds":[minX,minZ,maxX,maxZ],"water":[[x,z,...],...],"coast":[[x,z,...],...]}</c>
        /// The owning UI system caches the returned string per variant and hands it to
        /// <see cref="Build"/> — the geometry is static, so it must not be re-serialized per publish.
        /// </summary>
        public static string BuildContourFragment(MirrorCityLandMask mask)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"bounds\":[");
            sb.Append((int)mask.MinX).Append(',').Append((int)mask.MinZ).Append(',');
            sb.Append((int)mask.MaxX).Append(',').Append((int)mask.MaxZ);
            sb.Append("],\"water\":[");
            for (int p = 0; p < mask.WaterPolygonCount; p++)
            {
                if (p > 0) sb.Append(',');
                sb.Append('[');
                var poly = mask.GetWaterPolygon(p);
                for (int v = 0; v < poly.Length; v++)
                {
                    if (v > 0) sb.Append(',');
                    sb.Append((int)poly[v].x).Append(',').Append((int)poly[v].y);
                }
                sb.Append(']');
            }
            // Coast polylines: the shoreline stroke the UI draws OVER the fill-only water
            // polygons (mirroring the friendly radar's contour layer). Without it the
            // scanline-run water rectangles have no organic outline to hide behind.
            sb.Append("],\"coast\":[");
            for (int p = 0; p < mask.CoastPolylineCount; p++)
            {
                if (p > 0) sb.Append(',');
                sb.Append('[');
                var line = mask.GetCoastPolyline(p);
                for (int v = 0; v < line.Length; v++)
                {
                    if (v > 0) sb.Append(',');
                    sb.Append((int)line[v].x).Append(',').Append((int)line[v].y);
                }
                sb.Append(']');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// Fold the already-quantized snapshot content into a change signature. The owning system rebuilds
        /// and re-pushes the JSON only when this value changes, so a repair tick that moves nothing the
        /// player would see (below the intel resolution) costs only this cheap fold. At L2 exact values
        /// change continuously, which is correct — the precise view genuinely updates every tick.
        /// </summary>
        public static long ComputeSignature(in MirrorCityState state, NativeArray<EnemyTarget> targets, int intelLevel)
        {
            int level = math.clamp(intelLevel, 0, 2);
            long h = unchecked((long)14695981039346656037UL); // FNV-1a 64-bit offset basis
            h = Fold(h, level);
            h = Fold(h, state.VariantId);
            h = Fold(h, state.GenVersion);

            if (level <= 0)
            {
                for (int a = 0; a <= 2; a++)
                {
                    var axis = (AttackCategory)a;
                    if (!TryAxisCentroid(targets, axis, out float cx, out float cz, out int count))
                    {
                        h = Fold(h, 0);
                        continue;
                    }
                    h = Fold(h, count);
                    h = Fold(h, math.asint(cx));
                    h = Fold(h, math.asint(cz));
                }
                return h;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                bool isAa = t.Tier == MirrorTargetTier.AaSite;
                h = Fold(h, t.Id);
                h = Fold(h, (int)t.Tier);
                h = Fold(h, (int)t.Axis);
                h = Fold(h, StateCode(t));
                h = Fold(h, math.asint(PublishX(t.X, level)));
                h = Fold(h, math.asint(PublishZ(t.Z, level)));
                if (isAa)
                {
                    h = Fold(h, math.asint(PublishAaRange(t.AaRange, level)));
                    h = Fold(h, math.asint(PublishHpPct(t, level)));
                }
                else
                {
                    h = Fold(h, math.asint(PublishContrib(t.Contrib, level)));
                    h = Fold(h, math.asint(PublishHpPct(t, level)));
                    h = Fold(h, math.asint(PublishRebuildPct(t, level)));
                }
            }
            return h;
        }

        // ── Sections ─────────────────────────────────────────────────────────────────────────────

        private static void AppendSignals(StringBuilder sb, NativeArray<EnemyTarget> targets)
        {
            sb.Append('[');
            bool first = true;
            // One centroid per axis over that axis's contributing (non-AA) targets — the enemy's rough
            // location without exposing any individual target. Positions are static, so snapping to the
            // L1 grid keeps even the coarsest view from leaking exact placement.
            for (int a = 0; a <= 2; a++)
            {
                var axis = (AttackCategory)a;
                if (!TryAxisCentroid(targets, axis, out float cx, out float cz, out _))
                    continue;

                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"axis\":\"").Append(AxisName(axis)).Append("\",\"x\":");
                AppendNum(sb, cx);
                sb.Append(",\"z\":");
                AppendNum(sb, cz);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void AppendTargets(StringBuilder sb, NativeArray<EnemyTarget> targets, int level)
        {
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t.Tier == MirrorTargetTier.AaSite)
                    continue; // AA sites carry no axis contribution — emitted in the "aa" array

                if (!first) sb.Append(',');
                first = false;

                sb.Append("{\"id\":").Append(t.Id);
                sb.Append(",\"axis\":\"").Append(AxisName(t.Axis));
                sb.Append("\",\"tier\":\"").Append(TierName(t.Tier));
                sb.Append("\",\"x\":");
                AppendNum(sb, PublishX(t.X, level));
                sb.Append(",\"z\":");
                AppendNum(sb, PublishZ(t.Z, level));
                sb.Append(",\"state\":\"").Append(StateName(t)).Append('"');
                sb.Append(",\"contrib\":");
                AppendNum(sb, PublishContrib(t.Contrib, level));
                sb.Append(",\"hpPct\":");
                AppendNum(sb, PublishHpPct(t, level));
                sb.Append(",\"rebuildPct\":");
                AppendNum(sb, PublishRebuildPct(t, level));
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void AppendAaSites(StringBuilder sb, NativeArray<EnemyTarget> targets, int level)
        {
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t.Tier != MirrorTargetTier.AaSite)
                    continue;

                if (!first) sb.Append(',');
                first = false;

                sb.Append("{\"id\":").Append(t.Id);
                sb.Append(",\"x\":");
                AppendNum(sb, PublishX(t.X, level));
                sb.Append(",\"z\":");
                AppendNum(sb, PublishZ(t.Z, level));
                sb.Append(",\"range\":");
                AppendNum(sb, PublishAaRange(t.AaRange, level));
                sb.Append(",\"state\":\"").Append(StateName(t)).Append('"');
                sb.Append(",\"hpPct\":");
                AppendNum(sb, PublishHpPct(t, level));
                sb.Append('}');
            }
            sb.Append(']');
        }

        // ── Quantization (below L2 the exact numbers never enter the payload) ──────────────────────

        // L1 buckets contribution to a coarse step; L2 emits the exact value.
        private static float PublishContrib(float contrib, int level)
            => level >= 2 ? contrib : math.round(contrib / ContribStepL1) * ContribStepL1;

        // Exact hp fraction is L2-only; L1 hides it behind the coarse state string.
        private static float PublishHpPct(in EnemyTarget t, int level)
        {
            if (level < 2)
                return HiddenValue;
            if (t.MaxHp <= 0f)
                return 0f;
            return math.clamp(t.Hp / t.MaxHp, 0f, 1f) * 100f;
        }

        // Exact rebuild progress is L2-only; L1 shows only that the site is REBUILDING via state.
        private static float PublishRebuildPct(in EnemyTarget t, int level)
            => level >= 2 ? math.saturate(t.RebuildProgress) * 100f : HiddenValue;

        private static float PublishX(float x, int level) => level >= 2 ? x : Snap(x, CoordGridL1);
        private static float PublishZ(float z, int level) => level >= 2 ? z : Snap(z, CoordGridL1);

        // L1 over-states AA reach (inflated, rounded ring); L2 shows the true radius.
        private static float PublishAaRange(float range, int level)
            => level >= 2 ? range : math.round(range * AaRangeInflateL1 / AaRangeRoundL1) * AaRangeRoundL1;

        private static float Snap(float value, float grid)
        {
            if (grid <= 0f)
                return value;
            return math.round(value / grid) * grid;
        }

        /// <summary>
        /// Centroid of an axis's contributing (non-AA) targets, snapped to the L1 grid. False when the
        /// axis has no such targets. Shared by the L0 signal serializer and its signature fold so the two
        /// can never diverge.
        /// </summary>
        private static bool TryAxisCentroid(NativeArray<EnemyTarget> targets, AttackCategory axis, out float x, out float z, out int count)
        {
            float sumX = 0f, sumZ = 0f;
            count = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                if (t.Axis != axis || t.Tier == MirrorTargetTier.AaSite)
                    continue;
                sumX += t.X;
                sumZ += t.Z;
                count++;
            }
            if (count == 0)
            {
                x = 0f;
                z = 0f;
                return false;
            }
            x = Snap(sumX / count, CoordGridL1);
            z = Snap(sumZ / count, CoordGridL1);
            return true;
        }

        /// <summary>
        /// Coarse life-cycle state shared by both levels (it never leaks an exact number): DEAD
        /// (permanently killed key, or destroyed and not yet rebuilding), REBUILDING (construction site
        /// ramping back), DAMAGED (standing but below full hp), INTACT (full).
        /// </summary>
        private static string StateName(in EnemyTarget t) => StateCode(t) switch
        {
            0 => "DEAD",
            1 => "REBUILDING",
            2 => "DAMAGED",
            _ => "INTACT"
        };

        // Stable numeric mirror of the state (no per-process hash randomization) for the signature fold.
        private static int StateCode(in EnemyTarget t)
        {
            if (t.DestroyedForever || t.Hp <= 0f)
                return 0; // DEAD
            if (t.RebuildProgress < 1f)
                return 1; // REBUILDING
            if (t.MaxHp > 0f && t.Hp < t.MaxHp * IntactHpFraction)
                return 2; // DAMAGED
            return 3;     // INTACT
        }

        // FNV-1a 64-bit prime (pairs with the 14695981039346656037 offset basis seeded in the folds).
        private const long FnvPrime = 1099511628211L;

        private static long Fold(long h, int value) => unchecked((h ^ (uint)value) * FnvPrime);

        // ── Formatting helpers ────────────────────────────────────────────────────────────────────

        // The persisted variant id is valid by construction (bounded at the codec boundary and
        // rolled inside [0, Count) at generation), so the lookup is a plain Get.
        private static string ResolveMapId(int variantId)
            => MirrorCityVariantCatalog.Get(variantId).MapId;

        private static string AxisName(AttackCategory axis) => axis switch
        {
            AttackCategory.Kinetic => "physical",
            AttackCategory.Cyber => "digital",
            AttackCategory.Psyops => "social",
            _ => "physical"
        };

        private static string TierName(MirrorTargetTier tier) => tier switch
        {
            MirrorTargetTier.Reserve => "reserve",
            MirrorTargetTier.Key => "key",
            MirrorTargetTier.Regular => "regular",
            MirrorTargetTier.AaSite => "aa", // AA sites emit in the "aa" array, not here — total for exhaustiveness
            _ => "regular"
        };

        private static void AppendNum(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                sb.Append('0');
                return;
            }
            sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static void AppendEscaped(StringBuilder sb, string value)
        {
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
        }
    }
}
