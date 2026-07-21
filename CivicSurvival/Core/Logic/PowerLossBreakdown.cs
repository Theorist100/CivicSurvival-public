using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Sequential decomposition of the effective-capacity multiplier chain
    /// (<c>damage × saturation × fuel</c>, the same chain
    /// <c>PowerCapacityMath.ComputeEffectiveFactor</c> folds): each term's loss is taken
    /// from what the previous terms left, so the buckets plus <see cref="AllowedKW"/> sum
    /// exactly to the accumulated nameplate. Shared by the resolver job's
    /// <c>[CapacityLoss]</c> debug aggregate and the PowerGrid UI "capacity losses"
    /// breakdown so the two can never drift. Burst-compatible (plain struct + math).
    /// </summary>
    public struct PowerLossBreakdown
    {
        /// <summary>Nameplate of standing ruins (collapse / repair window / fully damaged), kW.</summary>
        public long KnockedOutKW;
        /// <summary>Lost to partial damage (strikes / disaster / explosion), kW.</summary>
        public long DamageLossKW;
        /// <summary>Lost to surplus saturation (fleet КПД), taken from the post-damage residue, kW.</summary>
        public long SaturationLossKW;
        /// <summary>Lost to thermal fuel starvation, taken from the post-saturation residue, kW.</summary>
        public long FuelLossKW;
        /// <summary>What the multiplier chain allows the plant to serve, kW.</summary>
        public long AllowedKW;

        /// <summary>
        /// Accumulates one plant. A knocked-out plant contributes its whole nameplate to
        /// <see cref="KnockedOutKW"/>; otherwise the losses decompose sequentially.
        /// </summary>
        public void Add(int nameplateKW, bool isKnockedOut, float damageMultiplier, float saturationFactor, float fuelFactor)
        {
            if (isKnockedOut)
            {
                KnockedOutKW += nameplateKW;
                return;
            }

            long np = nameplateKW;
            DamageLossKW += (long)math.round(np * (1f - damageMultiplier));
            SaturationLossKW += (long)math.round(np * damageMultiplier * (1f - saturationFactor));
            FuelLossKW += (long)math.round(np * damageMultiplier * saturationFactor * (1f - fuelFactor));
            AllowedKW += (long)math.round(np * damageMultiplier * saturationFactor * fuelFactor);
        }
    }
}
