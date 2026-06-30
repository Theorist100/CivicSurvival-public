using CivicSurvival.Core.Logic;

namespace CivicSurvival.Core.Forecast
{
    /// <summary>
    /// Forecast-layer manpower model: collapses the <see cref="ManpowerLogic"/> leaf formulas
    /// (base pool · patriotism · morale · war-fatigue · conscription) into the single
    /// recruitable pool the crisis sweep — and later the runtime re-evaluator — reads. The
    /// crew-gate that turns this pool into operational AA lives in
    /// <see cref="AirDefenseForecast.OperationalAa"/>: manpower owns the pool, air defense owns
    /// how many launchers it can crew from it.
    ///
    /// variant D: this calls the SAME ManpowerLogic the runtime uses, so the forecast pool can
    /// never drift from the simulated one. No new balance arithmetic — only the composition of
    /// existing leaves, lifted out of CrisisSweepSystem so the manpower lever lives in one place.
    /// </summary>
    public static class ManpowerForecast
    {
        /// <summary>
        /// Recruitable manpower pool at the given war-day, from the population peak and the player
        /// policy assumptions (patriotism / morale / conscription). Mirrors the inline block the
        /// monolithic CrisisSweepSystem ran in both the invariant verdict and the per-wave severity
        /// timeline — the only difference between the two call sites is the war-day and the
        /// conscription flag they pass in, both explicit parameters here.
        /// </summary>
        public static int Pool(int populationPeak, float patriotism, float morale, int warDay, bool isConscription)
            => ManpowerLogic.CalculateTotalManpower(
                ManpowerLogic.CalculateBasePool(populationPeak),
                patriotism, morale,
                ManpowerLogic.CalculateFatigueFactor(warDay),
                isConscription);
    }
}
