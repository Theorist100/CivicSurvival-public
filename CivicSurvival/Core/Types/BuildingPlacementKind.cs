namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Discriminator for the generic Core building-placement pipeline. Each mod
    /// building that the player places through the shared placement service
    /// (<c>IBuildingPlacementService</c>) owns one value here and registers an
    /// <c>IBuildingPlacementHandler</c> for it. The generic pending / intent
    /// components carry this value so the detector, payment, commit and lifecycle
    /// systems can dispatch the building-specific work to the right handler.
    ///
    /// Serialized as a byte on the persisted <c>BuildingPlacementIntent</c>, so
    /// values are stable — append, never renumber.
    /// </summary>
    public enum BuildingPlacementKind : byte
    {
        None = 0,

        /// <summary>Air-defense installation props (Bofors / Gepard / Patriot / Heritage).</summary>
        AirDefense = 1,

        /// <summary>Propaganda Center — the unique cognitive-defence HQ.</summary>
        PropagandaCenter = 2,

        /// <summary>Drone launcher — field emplacement that outbound counter-strike drones launch from.</summary>
        DroneLauncher = 3
    }

    /// <summary>
    /// Which purse pays a budget-funded placement. Orthogonal to the reserved credit
    /// kind: a credit (Heritage / Donor Patriot) consumes a counter and needs no money;
    /// a paid placement needs money and this says where it comes from — the deduction
    /// and any reject refund both go through the SAME purse. Serialized as a byte on
    /// the persisted <c>BuildingPlacementIntent</c>, so values are stable — append,
    /// never renumber.
    /// </summary>
    public enum PlacementPurse : byte
    {
        /// <summary>City treasury (vanilla budget).</summary>
        CityBudget = 0,

        /// <summary>Shadow wallet (off-books purchases; sanctions markup applies).</summary>
        ShadowWallet = 1
    }
}
