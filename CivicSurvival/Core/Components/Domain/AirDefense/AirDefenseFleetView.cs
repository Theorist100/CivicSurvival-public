namespace CivicSurvival.Core.Components.Domain.AirDefense
{
    /// <summary>
    /// Core-side projection of the live air-defense fleet for the crisis-sweep forecast: the total
    /// number of placed installations plus the per-type breakdown (Heritage Bofors, Bofors 40mm,
    /// Gepard, Patriot SAM). It is a read-only snapshot of the counts AirDefenseStateSystem already
    /// maintains for the UI (<c>AirDefenseUiStatsSnapshot</c>), surfaced through
    /// <see cref="CivicSurvival.Core.Interfaces.Domain.AirDefense.IAirDefenseStatsReader"/> so the
    /// Core/Forecast layer never imports AirDefense.Systems (Axiom 5).
    ///
    /// Fail-closed contract: <c>default(AirDefenseFleetView)</c> is the all-zero fleet (no AA of any
    /// type). The forecast treats <c>TotalAa == 0</c> as "no live data", which keeps the verdict
    /// byte-identical to the archetype model (the FREE Heritage grant + the Heritage crew/range
    /// approximation) when AirDefense is closed or the city has not been loaded.
    /// </summary>
    public readonly struct AirDefenseFleetView
    {
        /// <summary>Total live installations = Heritage + Bofors + Gepard + Patriot.</summary>
        public readonly int TotalAa;
        public readonly int HeritageCount;
        public readonly int BoforsCount;
        public readonly int GepardCount;
        public readonly int PatriotCount;

        public AirDefenseFleetView(int heritageCount, int boforsCount, int gepardCount, int patriotCount)
        {
            HeritageCount = heritageCount;
            BoforsCount = boforsCount;
            GepardCount = gepardCount;
            PatriotCount = patriotCount;
            TotalAa = heritageCount + boforsCount + gepardCount + patriotCount;
        }
    }
}
