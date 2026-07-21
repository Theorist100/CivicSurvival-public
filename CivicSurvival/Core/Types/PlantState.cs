namespace CivicSurvival.Core.Types
{
    /// <summary>
    /// Equipment plant operational state.
    /// Computed from EquipmentWear component fields (not stored).
    /// default(PlantState) is intentionally Operational because the value is computed on demand.
    /// Priority: Repairing > Exploded > Critical > Worn > Operational
    /// </summary>
    public enum PlantState : byte
    {
        /// <summary>No/minimal wear - plant operating normally.</summary>
        Operational = 0,

        /// <summary>Has wear, not in danger zone.</summary>
        Worn = 1,

        /// <summary>Wear >= danger threshold, explosion risk while overloaded.</summary>
        Critical = 2,

        /// <summary>Offline for repair, capacity = 0.</summary>
        Repairing = 3,

        /// <summary>Suffered explosion damage, capacity reduced.</summary>
        Exploded = 4,

        /// <summary>Under construction, capacity = 0 until complete.</summary>
        UnderConstruction = 5,

        /// <summary>Disabled by disaster (fire, structural failure), capacity reduced or zero.</summary>
        DisabledByDisaster = 6
    }
}
