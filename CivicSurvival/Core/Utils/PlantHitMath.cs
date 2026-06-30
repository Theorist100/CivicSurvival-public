using Unity.Mathematics;

namespace CivicSurvival.Core.Utils
{
    /// <summary>
    /// Nameplate-scaled missile-hit math, shared by OperationalDamageSystem (damage
    /// accrual) and EquipmentUISystem (hit-count display) so the two can never drift.
    ///
    /// One hit removes a slice of the plant's CURRENT nameplate (PlantBaseCapacity,
    /// re-published by the index system on upgrade — so installed upgrades raise
    /// survivability). The slice = max(<c>Repair.HitDamageMW</c>,
    /// <c>Repair.HitFleetSharePercent</c> × fleet nameplate) — fleet-share scaling keeps
    /// one wave meaningful against over-built grids where the absolute slice would be a
    /// rounding error. The resulting damage fraction stays clamped to
    /// [MinHitLossPercent..MaxHitLossPercent]: small plants still take at least ~2 hits,
    /// giants stay killable. At the calibrated defaults (1200 MW, share 0.1, 0.1..0.5)
    /// and the 14815 MW reference fleet the slice is 1481 MW: a 105 MW wind turbine
    /// takes 2 hits, a 3500 MW gas plant 3, a 7500 MW nuclear 5 — and the nuclear
    /// (share &gt; 1/3 of the fleet) survives a full 3-threat wave at 59% damage.
    /// </summary>
    public static class PlantHitMath
    {
        private const float KW_PER_MW = 1000f;
        // Display guard for degenerate configs (loss clamped to 0): the accrual loop in
        // OperationalDamageSystem compares accumulated damage, so it needs no cap, but a
        // "N/∞ hits" tooltip does.
        private const int MAX_DISPLAY_HITS = 99;
        // Floor for the divisor (a zero-clamped config must not divide by zero) and the
        // ceil() epsilon that keeps exact divisions stable (0.95 / 0.475 must read 2, not 3).
        private const float LOSS_EPSILON = 1e-4f;

        /// <summary>
        /// Per-hit damage slice in MW: the larger of the absolute <paramref name="hitDamageMW"/>
        /// and <paramref name="fleetSharePercent"/> of the fleet's live nameplate
        /// (<paramref name="fleetNameplateKW"/>, knocked-out stations already excluded).
        /// With share ≤ 0.3 and MaxThreatsPerTarget = 3, a station holding more than 1/3
        /// of the fleet still survives a full wave (3 × share/⅓ &lt; DestructionThreshold) —
        /// the one-wave-no-collapse invariant. A non-positive fleet nameplate (no snapshot
        /// yet) falls back to the absolute slice.
        /// </summary>
        public static float EffectiveHitSliceMW(float hitDamageMW, float fleetSharePercent, int fleetNameplateKW)
        {
            if (fleetNameplateKW <= 0)
                return hitDamageMW;
            return math.max(hitDamageMW, fleetSharePercent * fleetNameplateKW / KW_PER_MW);
        }

        /// <summary>Per-hit damage fraction of the given live nameplate (kW).</summary>
        public static float LossPerHit(int nameplateKW, float hitDamageMW, float minLossPercent, float maxLossPercent)
        {
            if (nameplateKW <= 0)
                return maxLossPercent;
            return math.clamp(hitDamageMW * KW_PER_MW / nameplateKW, minLossPercent, maxLossPercent);
        }

        /// <summary>
        /// Per-hit loss fraction in MW-float units — the crisis-sweep path (the forecast works in MW
        /// floats; the runtime path above works in kW ints). Combines the slice + clamp in one call:
        /// slice = max(<paramref name="hitDamageMW"/>, <paramref name="fleetSharePercent"/> ×
        /// <paramref name="fleetMW"/>), fraction = clamp(slice / <paramref name="plantMW"/>,
        /// [min, max]); a non-positive plant nameplate falls back to <paramref name="maxLossPercent"/>.
        /// Lives here so the sweep no longer re-derives the missile-hit math — PlantHitMath is the single
        /// owner. Intentionally NOT routed through the kW-int path: a float↔int kW round-trip would shift
        /// the last bit and break the forecast's byte-identity invariant, so the two unit systems keep
        /// their own byte-exact expressions side by side.
        /// </summary>
        public static float LossPerHitMW(float plantMW, float hitDamageMW, float fleetSharePercent, float fleetMW, float minLossPercent, float maxLossPercent)
        {
            if (plantMW <= 0f)
                return maxLossPercent;
            float sliceMW = hitDamageMW;
            if (fleetMW > 0f)
                sliceMW = math.max(sliceMW, fleetSharePercent * fleetMW);
            return math.clamp(sliceMW / plantMW, minLossPercent, maxLossPercent);
        }

        /// <summary>Hits to reach the destruction threshold at the given per-hit fraction.</summary>
        public static int HitsToDestroy(float lossPerHit, float destructionThreshold)
        {
            if (destructionThreshold <= 0f)
                return 1;
            float loss = math.max(lossPerHit, LOSS_EPSILON);
            return math.clamp((int)math.ceil(destructionThreshold / loss - LOSS_EPSILON), 1, MAX_DISPLAY_HITS);
        }

        /// <summary>Discrete hits sustained, derived from accumulated damage (display only).</summary>
        public static int HitCount(float damagePercent, float lossPerHit, int hitMax)
        {
            float loss = math.max(lossPerHit, LOSS_EPSILON);
            return math.clamp((int)math.round(damagePercent / loss), 0, hitMax);
        }
    }
}
