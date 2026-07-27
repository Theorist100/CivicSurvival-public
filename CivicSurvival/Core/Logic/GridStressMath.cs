using Unity.Mathematics;

namespace CivicSurvival.Core.Logic
{
    /// <summary>
    /// Pure deficit-detection rule shared by the runtime (<c>GridStressSystem</c>) and the
    /// forecast (<c>GridStressForecast.Step</c>). The two sides legitimately feed DIFFERENT
    /// inputs but must apply the SAME decision: runtime supplies the signed grid balance from
    /// vanilla (<c>PowerGridSingleton.RawBalance</c> = delivered production flow − active load)
    /// in kW; the forecast supplies its own <c>productionFull − active</c> in MW. The rule itself
    /// is unit-agnostic — the dead-zone floor and the active load are expressed in whatever unit
    /// the caller already works in, as long as they are consistent within one call.
    ///
    /// Before this unit each side re-derived the dead-zone clamp and the deficit comparison
    /// inline, so the two could drift independently (the H1 ✗ — two deficit-detection machines).
    /// Pulling the decision here makes it one rule with caller-chosen inputs.
    ///
    /// Pure over blittable <c>float</c>, side-effect-free, Burst-compatible (only
    /// <see cref="Unity.Mathematics.math"/> intrinsics). Byte-identical to the prior inline forms.
    /// </summary>
    public static class GridStressMath
    {
        /// <summary>
        /// Dead-zone half-width that the signed balance must fall below (negatively) before the
        /// tick counts as a deficit: <c>max(minFloor, fraction · activeLoad)</c>. Scales with the
        /// active (post-shedding) load so a shed — which lowers active load — also narrows the zone,
        /// and the bare demand-following production noise around zero never integrates into stress.
        ///
        /// Unit-agnostic: <paramref name="minFloor"/> and <paramref name="activeLoad"/> must share
        /// the caller's unit (runtime: kW; forecast: MW). The result is in that same unit.
        /// </summary>
        public static float DeadZone(float minFloor, float fraction, float activeLoad)
            => math.max(minFloor, fraction * activeLoad);

        /// <summary>
        /// The deficit decision: <c>signedBalance &lt; -DeadZone(minFloor, fraction, activeLoad)</c>.
        /// <paramref name="signedBalance"/> is delivered production minus active load (runtime:
        /// vanilla <c>RawBalance</c> in kW; forecast: <c>productionFull − active</c> in MW). A bare
        /// <c>signedBalance &lt; 0</c> would integrate sub-percent demand-following noise into
        /// permanent stress; the dead-zone is what keeps a city with surplus capacity out of the
        /// Red zone while still firing on honest 5%+ shortfalls.
        ///
        /// All three load/balance arguments must share one unit within the call.
        /// </summary>
        public static bool IsDeficit(float signedBalance, float minFloor, float fraction, float activeLoad)
            => signedBalance < -DeadZone(minFloor, fraction, activeLoad);
    }
}
