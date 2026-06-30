namespace CivicSurvival.Core.Components.CrossDomain
{
    /// <summary>
    /// Monotonic readiness level published by the resident-population producer and
    /// read through its views. A single source-of-truth field on
    /// <c>ResidentPopulationModelSystem</c> carries this value; it is never stored
    /// inside a snapshot struct (snapshots hold data only).
    ///
    /// Monotonic by design: <see cref="SelectionReady"/> implies
    /// <see cref="ScalarReady"/> by <c>&gt;=</c> comparison, so consumers gate with
    /// <c>readiness &gt;= ScalarReady</c> / <c>readiness &gt;= SelectionReady</c> rather
    /// than two independent boolean flags that could express the physically
    /// impossible "selection ready but scalar not ready" state.
    /// </summary>
    public enum ResidentPopulationReadiness
    {
        /// <summary>Nothing published yet this session/load.</summary>
        NotReady = 0,

        /// <summary>
        /// Scalar result valid: AliveResidentCitizens + household counts are
        /// published (persist-restorable). Scalar consumers may read.
        /// </summary>
        ScalarReady = 1,

        /// <summary>
        /// Selection result valid: EligibleHouseholds + LiveCitizensPerHousehold are
        /// rebuilt and published. Implies <see cref="ScalarReady"/>.
        /// </summary>
        SelectionReady = 2,
    }
}
