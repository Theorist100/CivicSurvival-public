using CivicSurvival.Core.Types;
using Unity.Mathematics;

namespace CivicSurvival.Core.Services
{
    /// <summary>
    /// City-size scaling for an AA installation's magazine. The wave already scales with the city
    /// (<see cref="WaveScalingService.CalculateThreatCount"/> uses a log2 size factor off
    /// <c>ProductionMW</c>); a flat magazine therefore drifts — too strong in a small city, too weak
    /// in a megacity. This applies the same log2 <i>curve</i> to the per-type base magazine so the
    /// ratio <i>(AA capacity / wave size)</i> stays roughly constant across every city size. Note the
    /// magazine uses the log2 headroom RELATIVE to the base city (factor 1.0 at <c>AmmoBaseMW</c>),
    /// whereas the wave size factor is the ABSOLUTE log2 — same curve, anchored differently.
    ///
    /// SINGLE SCALED-MAGAZINE SITE: the only caller is the AA placement detector, which stamps the
    /// scaled value onto the installation once at birth (the value then persists verbatim — no live
    /// recompute, no <c>MaxAmmo</c>/<c>CurrentAmmo</c> reconciliation). Total city capacity is NOT
    /// scaled here — the player already scales that by building more installations; scaling it again
    /// would double-count.
    ///
    /// BURST↔AMMO COUPLING (<see cref="CivicSurvival.Core.Config.AAParams"/>): <c>BurstRounds</c> is
    /// NEVER an input to this scaler — the signature takes only the base magazine. Scaling
    /// <c>MaxAmmo</c> while <c>BurstRounds</c> is fixed scales the engagement count
    /// (<c>MaxAmmo / BurstRounds</c>) exactly, which is the intent. Do not add a burst parameter.
    /// </summary>
    public static class AAAmmoScaling
    {
        /// <summary>
        /// Scale a type's base magazine by city size.
        /// </summary>
        /// <param name="cfg">Balance config (reads the <c>AAUnits.Ammo*</c> scaling constants).</param>
        /// <param name="baseMaxAmmo">The type's unscaled <c>AAUnits.{Type}MaxAmmo</c> (small-city base).</param>
        /// <param name="productionMW">City size in MW (see <see cref="WaveContextGatherer.ToCitySizeMW"/>).</param>
        /// <returns>
        /// Scaled magazine, always in <c>[baseMaxAmmo, baseMaxAmmo * AmmoMaxScaleCap]</c>: the floor
        /// keeps a small/low-power city at its base, the ceiling bounds growth (mirrors the wave's
        /// <c>MaxThreats</c> clamp). Returns <paramref name="baseMaxAmmo"/> unchanged for a
        /// non-positive base.
        /// </returns>
        public static int ScaleMaxAmmo(RemoteBalanceConfig cfg, int baseMaxAmmo, int productionMW)
        {
            if (baseMaxAmmo <= 0)
                return baseMaxAmmo;

            var aa = cfg.AAUnits;

            // Divisor floor is defensive — the contract already constrains AmmoScaleDiv to >= 1,
            // but a torn hot-reload must never divide by zero.
            float div = math.max(aa.AmmoScaleDiv, 1f);
            float baseMW = math.max(aa.AmmoBaseMW, WaveContextGatherer.MIN_PRODUCTION_MW);
            int mw = math.max(productionMW, WaveContextGatherer.MIN_PRODUCTION_MW);

            // Same log2 shape as the wave size factor; headroom is measured against the reference
            // small city so the factor is exactly 1.0 at AmmoBaseMW (small city = base) and grows
            // logarithmically above it.
            float headroom = math.log2(mw / div + 1f) - math.log2(baseMW / div + 1f);
            float scale = 1f + math.max(aa.AmmoScaleMult, 0f) * headroom;

            int scaled = (int)math.round(baseMaxAmmo * scale);
            int ceiling = (int)math.round(baseMaxAmmo * math.max(aa.AmmoMaxScaleCap, 1f));
            return math.clamp(scaled, baseMaxAmmo, ceiling);
        }
    }
}
