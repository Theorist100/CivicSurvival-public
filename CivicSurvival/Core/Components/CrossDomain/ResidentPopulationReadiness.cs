namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Readiness of the one thing the resident-population producer publishes: the
    /// household selection. A single source-of-truth field on
    /// <c>ResidentPopulationModelSystem</c> carries this value and it is read through the
    /// views as <c>IsSelectionReady</c>; it is never stored inside a snapshot struct
    /// (snapshots hold data only).
    ///
    /// <para>It was a three-level monotonic scale while the system also published a
    /// persisted scalar count of the city — the middle level, <c>ScalarReady</c>, existed
    /// so that "scalar restored from the save, selection still rebuilding" was expressible
    /// and "selection ready, scalar not" was not. With the scalar gone there is one
    /// published result and nothing left to order, so the scale is a boolean written as an
    /// enum: the name at the read site says what is ready, which a bare <c>bool</c> field
    /// on the producer would not.</para>
    /// </summary>
    public enum ResidentPopulationReadiness
    {
        /// <summary>Nothing published yet this session/load.</summary>
        NotReady = 0,

        /// <summary>
        /// Selection result valid: EligibleHouseholds, LiveCitizensPerHousehold and the
        /// alive-citizen sum over that same selection are rebuilt and published.
        /// </summary>
        SelectionReady = 1,
    }
}
